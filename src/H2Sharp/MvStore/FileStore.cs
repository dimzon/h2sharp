using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using H2Sharp.Engine;
using H2Sharp.MvStore.Cache;
using H2Sharp.MvStore.Type;
using H2Sharp.Store.FileSystem;
using H2Sharp.Util;
using BackgroundExecutor = H2Sharp.Util.Utils.BackgroundExecutor;

namespace H2Sharp.MvStore;

/// <summary>
/// A page without its key and value types, for the file to serialize, cache and account for.
/// </summary>
/// <remarks>
/// The store collects changed roots as <c>object</c> because every map has its own type
/// arguments, and the file only ever asks a page for its identity, its size, and its bytes.
/// </remarks>
internal interface IPage
{
    /// <summary>
    /// The number of this page inside its chunk, or -1 when it has none.
    /// </summary>
    int PageNo { get; }

    /// <summary>
    /// The id of the map this page belongs to.
    /// </summary>
    int GetMapId();

    /// <summary>
    /// How many entries live under this page.
    /// </summary>
    long GetTotalCount();

    /// <summary>
    /// Where this page lives, or 0 when it was never saved.
    /// </summary>
    long GetPos();

    /// <summary>
    /// Whether this page holds values rather than children.
    /// </summary>
    bool IsLeaf();

    /// <summary>
    /// How much memory this page costs, for the page cache.
    /// </summary>
    int GetMemory();

    /// <summary>
    /// Writes every unsaved page under this one.
    /// </summary>
    void WriteUnsavedRecursive(FileStore.PageSerializationManager manager);

    /// <summary>
    /// Releases every saved page under this one.
    /// </summary>
    void ReleaseSavedPages();
}

/// <summary>
/// A reentrant mutual-exclusion lock, the two locks the file serializes on.
/// </summary>
/// <remarks>
/// The reference uses <c>ReentrantLock</c>: the serialization pass unlocks and relocks around
/// the actual store, and the chunk move unlocks the save lock around the same call. A C#
/// <c>lock</c> statement cannot do the unlock-relock dance, and a semaphore is not reentrant,
/// so this wraps a <see cref="Monitor"/>, which is.
/// </remarks>
internal sealed class ReentrantLock
{
    private readonly object sync = new();

    /// <summary>
    /// Takes the lock, blocking until it is free. Reentrant.
    /// </summary>
    public void Lock() => Monitor.Enter(sync);

    /// <summary>
    /// Releases one hold on the lock.
    /// </summary>
    public void Unlock() => Monitor.Exit(sync);

    /// <summary>
    /// Whether the current thread holds the lock at least once.
    /// </summary>
    public bool IsHeldByCurrentThread() => Monitor.IsEntered(sync);
}

/// <summary>
/// The persistent file behind a store.
/// </summary>
/// <remarks>
/// The manager lives on the non-generic base because a page does not know the chunk type: the
/// reference spells it <c>FileStore.PageSerializationManager</c> off the raw file type, and this
/// says the same thing.
/// </remarks>
public abstract class FileStore
{
    /// <summary>
    /// The key for the entry within "layout" map, which contains id of "meta" map.
    /// </summary>
    public const string MetaIdKey = "meta.id";

    /// <summary>
    /// The block size (physical sector size) of the disk. The store header is written twice, one
    /// copy in each block, to ensure it survives a crash.
    /// </summary>
    internal const int BlockSize = 4 * 1024;

    /// <summary>
    /// How many chunks may sit between serialization and saving.
    /// </summary>
    public const int PipeLength = 3;

    internal const string HdrH = "H";
    internal const string HdrBlockSize = "blockSize";
    internal const string HdrFormat = "format";
    internal const string HdrCreated = "created";
    internal const string HdrFormatRead = "formatRead";
    internal const string HdrChunk = "chunk";
    internal const string HdrBlock = "block";
    internal const string HdrVersion = "version";
    internal const string HdrClean = "clean";
    internal const string HdrFletcher = "fletcher";

    protected const int FormatWriteMin = 3;
    protected const int FormatWriteMax = 3;
    protected const int FormatReadMin = 3;
    protected const int FormatReadMax = 3;

    /// <summary>
    /// Reads bytes at an absolute position. The chunk owns the data; the file owns the channel.
    /// </summary>
    /// <param name="chunk">the chunk that owns the data, or null for the store header</param>
    /// <param name="pos">where to read from</param>
    /// <param name="len">how many bytes to read</param>
    /// <returns>the bytes read</returns>
    internal abstract RentedBuffer ReadFully(Chunk? chunk, long pos, int len);

    /// <summary>
    /// What the serialization manager needs from the file it cannot name: the page cache, the
    /// removed-page queue, the table of contents, and the new-page counters.
    /// </summary>
    internal interface ISerializationHost
    {
        /// <summary>
        /// Puts a freshly serialized page in the cache.
        /// </summary>
        void CachePage(IPage page);

        /// <summary>
        /// Queues a deleted page for occupancy accounting.
        /// </summary>
        void AccountForRemovedPage(long pos, long version, bool pinned, int pageNo);

        /// <summary>
        /// Caches a chunk's table of contents.
        /// </summary>
        void CacheToc(Chunk chunk, long[] toc);

        /// <summary>
        /// Counts a page written into the table of contents.
        /// </summary>
        void CountNewPage(bool leaf);
    }

    /// <summary>
    /// Writes pages into a chunk, numbering them as they go.
    /// </summary>
    public sealed class PageSerializationManager
    {
        private readonly Chunk chunk;
        private readonly WriteBuffer buff;
        private readonly ISerializationHost host;
        private readonly List<long> toc = new();

        internal PageSerializationManager(Chunk chunk, WriteBuffer buff, ISerializationHost host)
        {
            this.chunk = chunk;
            this.buff = buff;
            this.host = host;
        }

        /// <summary>
        /// The buffer the pages are serialized into.
        /// </summary>
        public WriteBuffer GetBuffer() => buff;

        /// <summary>
        /// The sequential number the next serialized page will have within the chunk.
        /// </summary>
        public int GetPageNo() => toc.Count;

        /// <summary>
        /// Assigns a position to a serialized page: records its table-of-contents element and
        /// patches its length and check value over the length placeholder the page left.
        /// </summary>
        public long GetPagePosition(int mapId, int offset, int pageLength, int type)
        {
            long tocElement = DataUtils.ComposeTocElement(mapId, offset, pageLength, type);
            toc.Add(tocElement);
            long pagePos = DataUtils.ComposePagePos(chunk.Id, tocElement);
            int chunkId = chunk.Id;
            int check = DataUtils.GetCheckValue(chunkId)
                    ^ DataUtils.GetCheckValue(offset)
                    ^ DataUtils.GetCheckValue(pageLength);
            _ = buff.PutInt(offset, pageLength).PutShort(offset + 4, (short)check);
            return pagePos;
        }

        /// <summary>
        /// Accounts for a serialized page: it stays cached, its chunk counts the bytes, and a
        /// deleted page is queued for occupancy accounting in its chunk's version.
        /// </summary>
        public void OnPageSerialized<K, V>(Page<K, V> page, bool isDeleted, int diskSpaceUsed, bool isPinned)
        {
            host.CachePage(page);
            if (!page.IsLeaf())
            {
                // cache again - this will make sure nodes stays in the cache
                // for a longer time
                host.CachePage(page);
            }

            chunk.AccountForWrittenPage(diskSpaceUsed, isPinned);
            if (isDeleted)
            {
                host.AccountForRemovedPage(page.GetPos(), chunk.Version + 1, isPinned, page.pageNo);
            }
        }

        /// <summary>
        /// Appends the table of contents to the buffer and caches it.
        /// </summary>
        public void SerializeToC()
        {
            long[] tocArray = new long[toc.Count];
            int index = 0;
            foreach (long tocElement in toc)
            {
                tocArray[index++] = tocElement;
                _ = buff.PutLong(tocElement);
                host.CountNewPage(DataUtils.IsLeafPosition(tocElement));
            }

            host.CacheToc(chunk, tocArray);
        }
    }
}

/// <summary>
/// The file, over a chunk type: chunk metadata, the serialization pipeline, recovery, page
/// reads, and compaction. The channel itself stays in the subclass.
/// </summary>
/// <typeparam name="C">the chunk type</typeparam>
public abstract class FileStore<C> : FileStore, IFileStore, FileStore.ISerializationHost
    where C : Chunk
{
    private MVStore? mvStore;

    private bool closed;

    /// <summary>
    /// The number of read operations.
    /// </summary>
    internal readonly AtomicLong readCount = new();

    /// <summary>
    /// The number of read bytes.
    /// </summary>
    internal readonly AtomicLong readBytes = new();

    /// <summary>
    /// The number of write operations.
    /// </summary>
    internal readonly AtomicLong writeCount = new();

    /// <summary>
    /// The number of written bytes.
    /// </summary>
    internal readonly AtomicLong writeBytes = new();

    private string? fileName;

    /// <summary>
    /// For how long (in milliseconds) to retain a persisted chunk after it becomes irrelevant.
    /// </summary>
    private int retentionTime = GetDefaultRetentionTime();

    private readonly int maxPageSize;

    private long size;

    private bool readOnly;

    /// <summary>
    /// Guards submission to the serialization executor: chunk creation and serialization.
    /// </summary>
    private readonly ReentrantLock serializationLock = new();

    /// <summary>
    /// Single-threaded executor for serialization of the store snapshot into a buffer.
    /// </summary>
    private BackgroundExecutor? serializationExecutor;

    /// <summary>
    /// Single-threaded executor for saving a buffer as a new chunk.
    /// </summary>
    private BackgroundExecutor? bufferSaveExecutor;

    /// <summary>
    /// The page cache. Null when the store runs without one.
    /// </summary>
    private readonly CacheLongKeyLIRS<object>? cache;

    /// <summary>
    /// Cache for chunks "Table of Content", keyed by chunk id.
    /// </summary>
    private readonly CacheLongKeyLIRS<long[]> chunksToC;

    private readonly PriorityQueue<RemovedPageInfo, long> removedPages = new();

    /// <summary>
    /// The newest chunk. Null when nothing was stored yet.
    /// </summary>
    /// <remarks>
    /// The reference declares this <c>volatile</c>. Every mutation happens under the save lock
    /// and every read that decides anything happens under one of the two file locks; the one
    /// lock-free read only asks whether anything was ever stored. A volatile field of a generic
    /// type parameter is not available, and nothing here needs its ordering.
    /// </remarks>
    protected C? lastChunk;

    /// <summary>
    /// Chunks recently allocated and saved, whose metadata is not in the layout map yet.
    /// </summary>
    private readonly ConcurrentQueue<C> recentlySaved = new();

    /// <summary>
    /// Identifier of the last created chunk. Protected by <see cref="serializationLock"/>.
    /// </summary>
    private int lastChunkId;

    /// <summary>
    /// Guards space allocation and saving: chunk position, the free space, the file size.
    /// </summary>
    internal readonly ReentrantLock saveChunkLock = new();

    /// <summary>
    /// The map of chunks.
    /// </summary>
    private readonly ConcurrentDictionary<int, C> chunks = new();

    /// <summary>
    /// The store header attributes.
    /// </summary>
    protected readonly Dictionary<string, object?> storeHeader = new();

    private long creationTime;

    private readonly ConcurrentQueue<WriteBuffer> writeBufferPool = new();

    /// <summary>
    /// The layout map: chunk metadata and root locations for all maps.
    /// </summary>
    private MVMap<string, string>? layout;

    private readonly LinkedList<C> deadChunks = new();

    private readonly object deadChunksLock = new();

    /// <summary>
    /// The background writer thread, when one runs.
    /// </summary>
    private BackgroundWriterThread? backgroundWriterThread;

    private readonly int autoCompactFillRate;

    private int autoCommitDelay;

    private long autoCompactLastFileOpCount;

    private long lastCommitTime;

    /// <summary>
    /// Whether the store opens in recovery mode, reading what it can.
    /// </summary>
    protected readonly bool recoveryMode;

    /// <summary>
    /// A file over the given configuration.
    /// </summary>
    protected FileStore(IDictionary<string, object?> config)
    {
        recoveryMode = config.ContainsKey("recoveryMode");
        autoCompactFillRate = DataUtils.GetConfigParam(config, "autoCompactFillRate", 90);
        CacheLongKeyLIRS<object>? pageCache = null;
        int mb = DataUtils.GetConfigParam(config, "cacheSize", 16);
        if (mb > 0)
        {
            var cc = new CacheLongKeyLIRS<object>.Config
            {
                MaxMemory = mb * 1024L * 1024L,
            };
            if (config.TryGetValue("cacheConcurrency", out object? o) && o is int concurrency)
            {
                cc.SegmentCount = concurrency;
            }

            pageCache = new CacheLongKeyLIRS<object>(cc);
        }

        cache = pageCache;

        var cc2 = new CacheLongKeyLIRS<long[]>.Config
        {
            MaxMemory = 1024L * 1024L,
        };
        chunksToC = new CacheLongKeyLIRS<long[]>(cc2);

        int maxPageSize = DataUtils.GetConfigParam(config, "pageSplitSize",
            cache == null ? int.MaxValue : Constants.DefaultPageSize);
        // Make sure pages will fit into cache
        if (cache != null)
        {
            int maxCacheableSize = (int)(cache.GetMaxItemSize() >> 4);
            if (maxPageSize > maxCacheableSize)
            {
                maxPageSize = maxCacheableSize;
            }
        }

        this.maxPageSize = maxPageSize;
    }

    /// <inheritdoc/>
    public abstract void Open(string fileName, bool readOnly, char[]? encryptionKey);

    /// <inheritdoc/>
    public abstract IFileStore Open(string fileName, bool readOnly);

    /// <summary>
    /// Remembers the file name and the read-only flag. The subclass opens the channel.
    /// </summary>
    protected void Init(string fileName, bool readOnly)
    {
        this.fileName = fileName;
        this.readOnly = readOnly;
    }

    /// <inheritdoc/>
    public void Bind(MVStore store)
    {
        if (!ReferenceEquals(mvStore, store))
        {
            long pos = layout == null ? 0L : layout.GetRootPage().GetPos();
            layout = new MVMap<string, string>(store, 0, StringDataType.Instance, StringDataType.Instance);
            layout.SetRootPos(pos, store.GetCurrentVersion());
            mvStore = store;
            store.ResetLastMapId(lastChunk == null ? 0 : lastChunk.MapId);
            store.SetCurrentVersion(LastChunkVersion());
        }
    }

    /// <inheritdoc/>
    public MVMap<string, string> Start()
    {
        // locking is not strictly necessary here in startup flow, just to make assertions happy
        serializationLock.Lock();
        try
        {
            if (Size() == 0)
            {
                InitializeCommonHeaderAttributes(GetMvStore().GetTimeAbsolute());
                InitializeStoreHeader(GetMvStore().GetTimeAbsolute());
            }
            else
            {
                saveChunkLock.Lock();
                try
                {
                    ReadStoreHeader(recoveryMode);
                }
                finally
                {
                    saveChunkLock.Unlock();
                }
            }

            lastCommitTime = GetTimeSinceCreation();
            GetMvStore().ResetLastMapId(LastMapId());
            GetMvStore().SetCurrentVersion(LastChunkVersion());
            MVMap<string, string> metaMap = GetMvStore().OpenMetaMap();
            ScrubLayoutMap(metaMap);
            return metaMap;
        }
        finally
        {
            serializationLock.Unlock();
        }
    }

    /// <inheritdoc/>
    public void Stop(long allowedCompactionTime)
    {
        if (allowedCompactionTime > 0)
        {
            CompactStore(allowedCompactionTime);
        }

        serializationLock.Lock();
        try
        {
            // if there are no outstanding changes in data maps,
            // but some recently saved chunk(s), apart from the latest one,
            // are not reflected in layout map yet,
            // we need to force new chunk creation by modifying layout map here
            SaveRecentChunksInLayout(GetMvStore().GetCurrentVersion());
        }
        finally
        {
            serializationLock.Unlock();
        }

        GetMvStore().Commit();
        Debug.Assert(VerifyOrphanedChunksAbsence());
        WriteCleanShutdown();
        ClearCaches();
    }

    /// <inheritdoc/>
    public virtual void Close()
    {
        layout?.Close();
        closed = true;
        chunks.Clear();
    }

    /// <inheritdoc/>
    public int GetMetaMapId(Func<int> nextIdSupplier)
    {
        string? metaIdStr = layout!.Get(MetaIdKey);
        int metaId;
        if (metaIdStr == null)
        {
            metaId = nextIdSupplier();
            layout.Put(MetaIdKey, metaId.ToString("x", CultureInfo.InvariantCulture));
        }
        else
        {
            metaId = DataUtils.ParseHexInt(metaIdStr);
        }

        return metaId;
    }

    /// <inheritdoc/>
    public IDictionary<string, string>? GetLayoutMap()
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in layout!)
        {
            result[entry.Key] = entry.Value;
        }

        return result;
    }

    /// <inheritdoc/>
    public bool IsRegularMap(IAnyMap map) => !ReferenceEquals(map, layout);

    /// <inheritdoc/>
    public long GetRootPos(int mapId)
    {
        string? root = layout!.Get(MVMap<string, string>.GetMapRootKey(mapId));
        return root == null ? 0 : DataUtils.ParseHexLong(root);
    }

    /// <inheritdoc/>
    public bool DeregisterMapRoot(int mapId) => layout!.Remove(MVMap<string, string>.GetMapRootKey(mapId)) != null;

    /// <inheritdoc/>
    public bool HasChangesSince(long lastStoredVersion)
        => layout!.HasChangesSince(lastStoredVersion) && lastStoredVersion > MVStore.InitialVersion;

    /// <summary>
    /// The version of the newest chunk, or the initial version when nothing was stored yet.
    /// </summary>
    public long LastChunkVersion()
    {
        C? chunk = lastChunk;
        return chunk == null ? MVStore.InitialVersion + 1 : chunk.Version;
    }

    /// <inheritdoc/>
    public long GetMaxPageSize() => maxPageSize;

    /// <inheritdoc/>
    public int GetRetentionTime() => retentionTime;

    /// <inheritdoc/>
    public void SetRetentionTime(int ms) => retentionTime = ms;

    /// <inheritdoc/>
    public abstract bool ShouldSaveNow(int unsavedMemory, int autoCommitMemory);

    /// <inheritdoc/>
    public int GetAutoCommitDelay() => autoCommitDelay;

    /// <inheritdoc/>
    public void SetAutoCommitDelay(int millis)
    {
        if (autoCommitDelay != millis)
        {
            autoCommitDelay = millis;
            if (!IsReadOnly())
            {
                StopBackgroundThread(millis >= 0);
                // start the background thread if needed
                if (millis > 0 && GetMvStore().IsOpen())
                {
                    int sleep = Math.Max(10, millis / 3);
                    var t = new BackgroundWriterThread(this, sleep, ToString());
                    if (Interlocked.CompareExchange(ref backgroundWriterThread, t, null) == null)
                    {
                        t.Start();
                        serializationExecutor = Utils.CreateSingleThreadExecutor("H2-serialization");
                        bufferSaveExecutor = Utils.CreateSingleThreadExecutor("H2-save");
                    }
                }
            }
        }
    }

    /// <inheritdoc/>
    public bool IsKnownVersion(long version)
    {
        if (chunks.IsEmpty)
        {
            // no stored data
            return true;
        }

        // need to check if a chunk for this version exists
        C? c = GetChunkForVersion(version);
        if (c == null)
        {
            return false;
        }

        try
        {
            // also, all chunks referenced by this version
            // need to be available in the file
            MVMap<string, string> oldLayoutMap = GetLayoutMap(version);
            foreach (C chunk in GetChunksFromLayoutMap(oldLayoutMap))
            {
                string chunkKey = Chunk.GetMetaKey(chunk.Id);
                // if current layout map does not have it - verify its existence
                if (!layout!.ContainsKey(chunkKey) && !IsValidChunk(chunk))
                {
                    return false;
                }
            }
        }
        catch (MvStoreException)
        {
            // the chunk missing where the metadata is stored
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public void RollbackTo(long version)
    {
        if (version == 0)
        {
            // special case: remove all data
            string? metaId = layout!.Get(MetaIdKey);
            layout.SetInitialRoot(layout.CreateEmptyLeaf(), MVStore.InitialVersion);
            layout.Put(MetaIdKey, metaId!);
        }
        else
        {
            if (!layout!.RollbackRoot(version))
            {
                MVMap<string, string> layoutMap = GetLayoutMap(version);
                layout.SetInitialRoot(layoutMap.GetRootPage(), version);
            }
        }

        serializationLock.Lock();
        try
        {
            C? keep = GetChunkForVersion(version);
            if (keep != null)
            {
                saveChunkLock.Lock();
                try
                {
                    lock (deadChunksLock)
                    {
                        deadChunks.Clear();
                    }

                    lock (removedPages)
                    {
                        removedPages.Clear();
                    }

                    recentlySaved.Clear();
                    SetLastChunk(keep);
                    AdjustStoreToLastChunk();
                }
                finally
                {
                    saveChunkLock.Unlock();
                }
            }
        }
        finally
        {
            serializationLock.Unlock();
        }

        lock (removedPages)
        {
            removedPages.Clear();
        }

        ClearCaches();
    }

    /// <summary>
    /// Sets the header every new store starts from. The subclass writes the blocks.
    /// </summary>
    protected void InitializeCommonHeaderAttributes(long time)
    {
        SetLastChunk(null);
        creationTime = time;
        storeHeader[HdrH] = 2;
        storeHeader[HdrBlockSize] = BlockSize;
        storeHeader[HdrFormat] = FormatWriteMax;
        storeHeader[HdrCreated] = creationTime;
    }

    /// <summary>
    /// Reads the common header attributes after opening, refusing formats this port cannot read
    /// or, unless read-only, cannot write.
    /// </summary>
    protected void ProcessCommonHeaderAttributes()
    {
        creationTime = DataUtils.ReadHexLong(storeHeader, HdrCreated, 0);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // calculate the year (doesn't have to be exact;
        // we assume 365.25 days per year, * 4 = 1461)
        int year = 1970 + (int)(now / (1000L * 60 * 60 * 6 * 1461));
        if (year < 2014)
        {
            // if the year is before 2014,
            // we assume the system doesn't have a real-time clock,
            // and we set the creationTime to the past, so that
            // existing chunks are overwritten
            creationTime = now - GetRetentionTime();
        }
        else if (now < creationTime)
        {
            // the system time was set to the past:
            // we change the creation time
            creationTime = now;
            storeHeader[HdrCreated] = creationTime;
        }

        int blockSize = DataUtils.ReadHexInt(storeHeader, HdrBlockSize, BlockSize);
        if (blockSize != BlockSize)
        {
            throw DataUtils.NewMvStoreException(
                DataUtils.ErrorUnsupportedFormat,
                "Block size {0} is currently not supported",
                blockSize);
        }

        long format = DataUtils.ReadHexLong(storeHeader, HdrFormat, 1);
        if (!IsReadOnly())
        {
            if (format > FormatWriteMax)
            {
                throw GetUnsupportedWriteFormatException(format, FormatWriteMax,
                    "The write format {0} is larger than the supported format {1}");
            }
            else if (format < FormatWriteMin)
            {
                throw GetUnsupportedWriteFormatException(format, FormatWriteMin,
                    "The write format {0} is smaller than the supported format {1}");
            }
        }

        format = DataUtils.ReadHexLong(storeHeader, HdrFormatRead, format);
        if (format > FormatReadMax)
        {
            throw DataUtils.NewMvStoreException(
                DataUtils.ErrorUnsupportedFormat,
                "The read format {0} is larger than the supported format {1}",
                format, FormatReadMax);
        }
        else if (format < FormatReadMin)
        {
            throw DataUtils.NewMvStoreException(
                DataUtils.ErrorUnsupportedFormat,
                "The read format {0} is smaller than the supported format {1}",
                format, FormatReadMin);
        }
    }

    private long GetTimeSinceCreation() => Math.Max(0, GetMvStore().GetTimeAbsolute() - creationTime);

    private MVMap<string, string> GetLayoutMap(long version)
    {
        C? chunk = GetChunkForVersion(version);
        DataUtils.CheckArgument(chunk != null, "Unknown version {0}", version);
        return layout!.OpenReadOnly(chunk!.LayoutRootPos, version);
    }

    private C? GetChunkForVersion(long version)
    {
        C? newest = null;
        foreach (C c in chunks.Values)
        {
            if (c.Version <= version)
            {
                if (newest == null || c.Id > newest.Id)
                {
                    newest = c;
                }
            }
        }

        return newest;
    }

    private void ScrubLayoutMap(MVMap<string, string> meta)
    {
        var keysToRemove = new HashSet<string>(StringComparer.Ordinal);

        // split meta map off layout map
        foreach (string prefix in new[] { DataUtils.MetaName, DataUtils.MetaMap })
        {
            IEnumerator<string> it = layout!.KeyIterator(prefix);
            while (it.MoveNext())
            {
                string key = it.Current;
                if (!key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    break;
                }

                _ = meta.PutIfAbsent(key, layout.Get(key)!);
                GetMvStore().MarkMetaChanged();
                _ = keysToRemove.Add(key);
            }
        }

        // remove roots of non-existent maps (leftover after unfinished map removal)
        IEnumerator<string> roots = layout!.KeyIterator(DataUtils.LayoutRoot);
        while (roots.MoveNext())
        {
            string key = roots.Current;
            if (!key.StartsWith(DataUtils.LayoutRoot, StringComparison.Ordinal))
            {
                break;
            }

            string mapIdStr = key.Substring(key.LastIndexOf('.') + 1);
            if (!meta.ContainsKey(DataUtils.MetaMap + mapIdStr)
                && DataUtils.ParseHexInt(mapIdStr) != meta.GetId())
            {
                _ = keysToRemove.Add(key);
            }
        }

        foreach (string key in keysToRemove)
        {
            _ = layout.Remove(key);
        }
    }

    /// <summary>
    /// Whether anything was ever stored.
    /// </summary>
    protected bool HasPersistentData() => lastChunk != null;

    /// <summary>
    /// Whether fewer than ten file operations happened since the last housekeeping.
    /// </summary>
    protected bool IsIdle() => autoCompactLastFileOpCount >= GetWriteCount() + GetReadCount();

    /// <summary>
    /// Makes a chunk the newest one, forgetting every other chunk. The layout root moves to it.
    /// </summary>
    protected void SetLastChunk(C? last)
    {
        Debug.Assert(serializationLock.IsHeldByCurrentThread());
        lastChunk = last;
        chunks.Clear();
        lastChunkId = 0;
        long layoutRootPos = 0;
        if (last != null)
        {
            // there is a valid chunk
            lastChunkId = last.Id;
            layoutRootPos = last.LayoutRootPos;
            chunks[last.Id] = last;
        }

        layout!.SetRootPos(layoutRootPos, LastChunkVersion());
    }

    /// <summary>
    /// Queues a chunk that holds no live data for eventual removal.
    /// </summary>
    protected void RegisterDeadChunk(C chunk)
    {
        lock (deadChunksLock)
        {
            deadChunks.AddLast(chunk);
        }
    }

    /// <inheritdoc/>
    public void DropUnusedChunks()
    {
        Debug.Assert(GetMvStore().IsLockedByCurrentThread());
        bool hasDead;
        lock (deadChunksLock)
        {
            hasDead = deadChunks.Count != 0;
        }

        if (!hasDead)
        {
            return;
        }

        long oldestVersionToKeep = GetMvStore().GetOldestVersionToKeep();
        long time = GetTimeSinceCreation();
        var toBeFreed = new List<C>();
        lock (deadChunksLock)
        {
            while (deadChunks.Count != 0)
            {
                C chunk = deadChunks.First!.Value;
                if (!(chunk.IsAllocated() && IsSeasonedChunk(chunk, time)
                    && CanOverwriteChunk(chunk, oldestVersionToKeep)))
                {
                    // if chunk is not ready yet, leave it at the head and exit
                    break;
                }

                deadChunks.RemoveFirst();
                if (chunks.TryRemove(chunk.Id, out _))
                {
                    // purge dead pages from cache
                    long[]? toc = CleanToCCache(chunk);
                    if (toc != null && cache != null)
                    {
                        foreach (long tocElement in toc)
                        {
                            long pagePos = DataUtils.ComposePagePos(chunk.Id, tocElement);
                            _ = cache.Remove(pagePos);
                        }
                    }

                    if (layout!.Remove(Chunk.GetMetaKey(chunk.Id)) != null)
                    {
                        GetMvStore().MarkMetaChanged();
                    }

                    toBeFreed.Add(chunk);
                }
            }
        }

        if (toBeFreed.Count != 0)
        {
            saveChunkLock.Lock();
            try
            {
                FreeChunkSpace(toBeFreed);
            }
            finally
            {
                saveChunkLock.Unlock();
            }
        }
    }

    private static bool CanOverwriteChunk(C c, long oldestVersionToKeep)
        => !c.IsLive() && c.UnusedAtVersion < oldestVersionToKeep;

    private bool IsSeasonedChunk(C chunk, long time)
    {
        int retentionTime = GetRetentionTime();
        return retentionTime < 0 || chunk.Time + retentionTime <= time;
    }

    private bool IsRewritable(C chunk, long time)
        => chunk.IsRewritable() && IsSeasonedChunk(chunk, time)
            // to prevent last saved chunk from being re-written as it may cause "endless" re-write loop
            && chunk.Version < GetMvStore().GetCurrentVersion() - 1;

    /// <summary>
    /// Writes bytes at an absolute position.
    /// </summary>
    protected abstract void WriteFully(C? chunk, long pos, ReadOnlyMemory<byte> src);

    /// <summary>
    /// Reads bytes at an absolute position through a channel, counting the read.
    /// </summary>
    /// <remarks>
    /// Rented, not allocated: a page per miss is what this is, and zeroing a fresh array
    /// per page is memset for nothing — the read fills it completely or throws.
    /// </remarks>
    protected RentedBuffer ReadFully(FileChannel file, long pos, int len)
    {
        RentedBuffer rented = RentedBuffer.Rent(len);
        try
        {
            DataUtils.ReadFully(file, pos, rented.Memory);
        }
        catch (Exception e) when (e is not MvStoreException)
        {
            rented.Dispose();
            throw DataUtils.NewMvStoreException(
                DataUtils.ErrorReadingFailed,
                "Reading from file {0} failed at position {1}, length {2}",
                file, pos, len, e);
        }

        _ = readCount.IncrementAndGet();
        _ = readBytes.AddAndGet(len);
        return rented;
    }

    /// <summary>
    /// Gives a buffer its position in the file.
    /// </summary>
    protected abstract void AllocateChunkSpace(C chunk, WriteBuffer buff);

    /// <summary>
    /// Writes a buffer at its chunk's allocated position.
    /// </summary>
    protected abstract void WriteChunk(C chunk, WriteBuffer buffer);

    /// <inheritdoc/>
    public virtual void Sync()
    {
    }

    /// <inheritdoc/>
    public abstract int GetFillRate();

    /// <summary>
    /// Shrinks the file when at least the given percentage of it would be saved.
    /// </summary>
    protected abstract void ShrinkStoreIfPossible(int minPercent);

    /// <inheritdoc/>
    public long Size() => size;

    /// <summary>
    /// Remembers the file size. The subclass owns the channel; this is its cache of it.
    /// </summary>
    protected void SetSize(long size) => this.size = size;

    /// <inheritdoc/>
    public long GetWriteCount() => writeCount.Get();

    private long GetWriteBytes() => writeBytes.Get();

    /// <inheritdoc/>
    public long GetReadCount() => readCount.Get();

    private long GetReadBytes() => readBytes.Get();

    /// <inheritdoc/>
    public bool IsReadOnly() => readOnly;

    /// <summary>
    /// The default retention time for this store in milliseconds.
    /// </summary>
    public static int GetDefaultRetentionTime() => 45000;

    /// <summary>
    /// Forgets chunks, pages, caches and counters. The subclass drops the free space.
    /// </summary>
    public virtual void Clear()
    {
        saveChunkLock.Lock();
        try
        {
            lock (deadChunksLock)
            {
                deadChunks.Clear();
            }

            lastChunk = null;
            readCount.Set(0);
            readBytes.Set(0);
            writeCount.Set(0);
            writeBytes.Set(0);
        }
        finally
        {
            saveChunkLock.Unlock();
        }
    }

    /// <inheritdoc/>
    public string? GetFileName() => fileName;

    /// <summary>
    /// The store this file belongs to.
    /// </summary>
    protected MVStore GetMvStore() => mvStore!;

    /// <summary>
    /// Marks bytes as in use. The subclass owns the free space.
    /// </summary>
    protected abstract void MarkUsed(long pos, int length);

    /// <summary>
    /// The live chunks, by id.
    /// </summary>
    protected ConcurrentDictionary<int, C> GetChunks() => chunks;

    /// <summary>
    /// Whether freed space is reused for new chunks. The base store always does.
    /// </summary>
    public virtual bool IsSpaceReused() => true;

    /// <summary>
    /// Sets whether freed space is reused for new chunks. The base store ignores this.
    /// </summary>
    public virtual void SetReuseSpace(bool reuseSpace)
    {
    }

    /// <summary>
    /// The chunks to consider for rewriting, or null for all of them.
    /// </summary>
    protected virtual IEnumerable<C>? GetRewriteCandidates() => null;

    /// <summary>
    /// Stores now, releasing the serialization lock while the store lock does its work.
    /// Only the chunk move calls this: it already holds the save lock.
    /// </summary>
    protected void Store()
    {
        serializationLock.Unlock();
        try
        {
            GetMvStore().StoreNow();
        }
        finally
        {
            serializationLock.Lock();
        }
    }

    /// <inheritdoc/>
    public void StoreIt(List<object> changed, long version, bool syncWrite)
    {
        lastCommitTime = GetTimeSinceCreation();
        GetMvStore().OnVersionChange(version);
        SubmitOrRun(serializationExecutor,
            () => SerializeAndStore(syncWrite, changed, lastCommitTime, version),
            syncWrite, PipeLength);
    }

    internal static void SubmitOrRun(BackgroundExecutor? executor, Action action, bool syncRun, int threshold)
    {
        if (executor != null)
        {
            try
            {
                Task future = executor.Submit(action);
                if (syncRun || executor.PendingCount > threshold)
                {
                    try
                    {
                        future.GetAwaiter().GetResult();
                    }
                    catch (ThreadInterruptedException)
                    {
                    }
                }

                return;
            }
            catch (InvalidOperationException)
            {
                Utils.ShutdownExecutor(executor);
            }
        }

        action();
    }

    private void SerializeAndStore(bool syncRun, List<object> changed, long time, long version)
    {
        serializationLock.Lock();
        try
        {
            int chunkId = lastChunkId;
            if (chunkId != 0)
            {
                chunkId &= Chunk.MaxId;
                bool found = chunks.TryGetValue(chunkId, out C? lastChunk);
                Debug.Assert(found && lastChunk != null, chunkId + " (" + chunkId + ") " + chunks.Count);
                // never go backward in time
                time = Math.Max(lastChunk!.Time, time);
            }

            C c;
            WriteBuffer buff;
            try
            {
                c = CreateChunk(time, version);
                chunks[c.Id] = c;
                buff = GetWriteBuffer();
                SerializeToBuffer(buff, changed, c);
            }
            catch (Exception)
            {
                lastChunkId = chunkId;
                throw;
            }

            if (c.IsLive())
            {
                SubmitOrRun(bufferSaveExecutor, () => StoreBuffer(c, buff), syncRun, 5);

                foreach (object p in changed)
                {
                    ((IPage)p).ReleaseSavedPages();
                }
            }
            else
            {
                // If there were no updates, chunk is empty, and we don't need to store it.
                // Even more, it would refer to the layout map root page within the previous chunk,
                // and this will cause ChunkNotFoundException on reopen,
                // if store was closed right after that operation. We just skip it instead.
                _ = chunks.TryRemove(c.Id, out _);
                lastChunkId = chunkId;
                ReleaseWriteBuffer(buff);
            }
        }
        catch (MvStoreException e)
        {
            GetMvStore().Panic(e);
        }
        catch (Exception e)
        {
            GetMvStore().Panic(e);
        }
        finally
        {
            serializationLock.Unlock();
        }
    }

    private void SerializeToBuffer(WriteBuffer buff, List<object> changed, C c)
    {
        // need to patch the header later
        int headerLength = c.EstimateHeaderSize();
        _ = buff.Position(headerLength);
        c.Next = headerLength;

        long version = c.Version;
        var pageSerializationManager = new PageSerializationManager(c, buff, this);
        foreach (object p in changed)
        {
            IPage page = (IPage)p;
            string key = MVMap<string, string>.GetMapRootKey(page.GetMapId());
            if (page.GetTotalCount() == 0)
            {
                _ = layout!.Remove(key);
            }
            else
            {
                page.WriteUnsavedRecursive(pageSerializationManager);
                long root = page.GetPos();
                layout!.Put(key, root.ToString("x", CultureInfo.InvariantCulture));
            }
        }

        AcceptChunkOccupancyChanges(c.Time, version);

        // metadata of some recent chunks has not been saved in the layout map yet,
        // just was embedded into the chunk itself, and this need to be done now
        // (it's better not to update right after storing,
        // because that would modify the layout map again)
        SaveRecentChunksInLayout(version);

        RootReference<string, string>? layoutRootReference = layout!.SetWriteVersion(version);
        Debug.Assert(layoutRootReference != null);
        Debug.Assert(layoutRootReference!.version == version,
            layoutRootReference.version.ToString(CultureInfo.InvariantCulture) + " != "
            + version.ToString(CultureInfo.InvariantCulture));

        Page<string, string> layoutRoot = layoutRootReference.root;
        layoutRoot.WriteUnsavedRecursive(pageSerializationManager);
        c.LayoutRootPos = layoutRoot.GetPos();
        changed.Add(layoutRoot);

        // last allocated map id should be captured after the meta map was saved, because
        // this will ensure that concurrently created map, which made it into meta before save,
        // will have its id reflected in "map" header field of the currently written chunk
        c.MapId = GetMvStore().GetLastMapId();

        c.TocPos = buff.Position();
        pageSerializationManager.SerializeToC();
        int chunkLength = buff.Position();

        // add the store header and round to the next block
        int length = MathUtils.RoundUpInt(chunkLength + Chunk.FooterLength, BlockSize);
        _ = buff.Limit(length);
        c.Len = buff.Limit() / BlockSize;
        c.Buffer = buff.GetBuffer();
    }

    private void SaveRecentChunksInLayout(long currentVersion)
    {
        Debug.Assert(serializationLock.IsHeldByCurrentThread());
        while (recentlySaved.TryDequeue(out C? recentlySavedChunk))
        {
            // if it's not a leftover after store rollback
            if (recentlySavedChunk.Version < currentVersion)
            {
                SaveChunkMetadataChanges(recentlySavedChunk);
            }
        }
    }

    private void StoreBuffer(C c, WriteBuffer buff)
    {
        saveChunkLock.Lock();
        try
        {
            if (closed || GetMvStore().GetPanicException() != null)
            {
                throw DataUtils.NewMvStoreException(DataUtils.ErrorWritingFailed, "This fileStore is closed");
            }

            int headerLength = (int)c.Next;

            AllocateChunkSpace(c, buff);
            recentlySaved.Enqueue(c);

            _ = buff.Position(0);
            c.WriteChunkHeader(buff, headerLength);
            _ = buff.Position(buff.Limit() - Chunk.FooterLength);
            _ = buff.Put(c.GetFooterBytes());
            _ = buff.Position(0);

            WriteChunk(c, buff);
            lastChunk = c;
        }
        catch (MvStoreException e)
        {
            GetMvStore().Panic(e);
        }
        catch (Exception e)
        {
            GetMvStore().Panic(e);
        }
        finally
        {
            saveChunkLock.Unlock();
            c.Buffer = null;
            ReleaseWriteBuffer(buff);
        }
    }

    /// <summary>
    /// Applies the freed space to the chunk metadata. Completely free chunks are not removed
    /// here; they are queued and wait until their usage is over.
    /// </summary>
    private void AcceptChunkOccupancyChanges(long time, long version)
    {
        Debug.Assert(serializationLock.IsHeldByCurrentThread());
        if (HasPersistentData())
        {
            var modifiedChunks = new HashSet<C>();
            var unallocatedChunksRpi = new List<RemovedPageInfo>();
            while (true)
            {
                RemovedPageInfo? rpi;
                lock (removedPages)
                {
                    while (removedPages.TryPeek(out rpi, out _) && rpi.Version < version)
                    {
                        // could be different from the peeked one, but nobody else retrieves
                        _ = removedPages.TryDequeue(out rpi, out _);
                        Debug.Assert(rpi != null);
                        Debug.Assert(rpi!.Version < version, rpi + " < " + version);
                        int chunkId = rpi.GetPageChunkId();
                        bool found = chunks.TryGetValue(chunkId, out C? chunk);
                        Debug.Assert(!GetMvStore().IsOpen() || found, chunkId.ToString(CultureInfo.InvariantCulture));
                        if (found && chunk != null)
                        {
                            if (chunk.IsAllocated())
                            {
                                _ = modifiedChunks.Add(chunk);
                                if (chunk.AccountForRemovedPage(rpi.GetPageNo(), rpi.GetPageLength(),
                                    rpi.IsPinned(), time, rpi.Version))
                                {
                                    RegisterDeadChunk(chunk);
                                }
                            }
                            else
                            {
                                unallocatedChunksRpi.Add(rpi);
                            }
                        }
                    }
                }

                if (modifiedChunks.Count == 0)
                {
                    lock (removedPages)
                    {
                        foreach (RemovedPageInfo unallocated in unallocatedChunksRpi)
                        {
                            removedPages.Enqueue(unallocated, unallocated.Version);
                        }
                    }

                    return;
                }

                foreach (C chunk in modifiedChunks)
                {
                    SaveChunkMetadataChanges(chunk);
                }

                modifiedChunks.Clear();
            }
        }
    }

    /// <summary>
    /// The fill rate of all chunks combined: live bytes over all bytes. Young chunks count as
    /// live.
    /// </summary>
    public int GetChunksFillRate() => GetChunksFillRate(true);

    internal int GetRewritableChunksFillRate() => GetChunksFillRate(false);

    private int GetChunksFillRate(bool all)
    {
        long maxLengthSum = 1;
        long maxLengthLiveSum = 1;
        long time = GetTimeSinceCreation();
        foreach (C c in chunks.Values)
        {
            if (all || IsRewritable(c, time))
            {
                Debug.Assert(c.MaxLen >= 0);
                maxLengthSum += c.MaxLen;
                maxLengthLiveSum += c.MaxLenLive;
            }
        }

        // the fill rate of all chunks combined
        return (int)(100 * maxLengthLiveSum / maxLengthSum);
    }

    private int GetChunkCount() => chunks.Count;

    private int GetPageCount()
    {
        int count = 0;
        foreach (C chunk in chunks.Values)
        {
            count += chunk.PageCount;
        }

        return count;
    }

    private int GetLivePageCount()
    {
        int count = 0;
        foreach (C chunk in chunks.Values)
        {
            count += chunk.PageCountLive;
        }

        return count;
    }

    /// <summary>
    /// Puts a page in the cache.
    /// </summary>
    internal void CachePage<K, V>(Page<K, V> page)
    {
        cache?.Put(page.GetPos(), page, page.GetMemory());
    }

    void ISerializationHost.CachePage(IPage page)
    {
        cache?.Put(page.GetPos(), (object)page, page.GetMemory());
    }

    /// <inheritdoc/>
    public int GetCacheSize() => cache == null ? 0 : (int)(cache.GetMaxMemory() >> 20);

    /// <inheritdoc/>
    public int GetCacheSizeUsed() => cache == null ? 0 : (int)(cache.GetUsedMemory() >> 20);

    /// <inheritdoc/>
    public void SetCacheSize(int mb)
    {
        long bytes = mb * 1024L * 1024L;
        if (cache != null)
        {
            cache.SetMaxMemory(bytes);
            cache.Clear();
        }
    }

    internal void CacheToC(C chunk, long[] toc)
        => chunksToC.Put(chunk.Id, toc, toc.Length * 8L + Constants.MemoryArray);

    void ISerializationHost.CacheToc(Chunk chunk, long[] toc) => CacheToC((C)chunk, toc);

    void ISerializationHost.CountNewPage(bool leaf) => GetMvStore().CountNewPage(leaf);

    void ISerializationHost.AccountForRemovedPage(long pos, long version, bool pinned, int pageNo)
        => AccountForRemovedPage(pos, version, pinned, pageNo);

    private long[]? CleanToCCache(C chunk) => chunksToC.Remove(chunk.Id);

    /// <inheritdoc/>
    public void PopulateInfo(Action<string, string> consumer)
    {
        consumer("info.FILE_WRITE", GetWriteCount().ToString(CultureInfo.InvariantCulture));
        consumer("info.FILE_WRITE_BYTES", GetWriteBytes().ToString(CultureInfo.InvariantCulture));
        consumer("info.FILE_READ", GetReadCount().ToString(CultureInfo.InvariantCulture));
        consumer("info.FILE_READ_BYTES", GetReadBytes().ToString(CultureInfo.InvariantCulture));
        consumer("info.FILL_RATE", GetFillRate().ToString(CultureInfo.InvariantCulture));
        consumer("info.CHUNKS_FILL_RATE", GetChunksFillRate().ToString(CultureInfo.InvariantCulture));
        consumer("info.CHUNKS_FILL_RATE_RW", GetRewritableChunksFillRate().ToString(CultureInfo.InvariantCulture));
        consumer("info.FILE_SIZE", Size().ToString(CultureInfo.InvariantCulture));
        consumer("info.CHUNK_COUNT", GetChunkCount().ToString(CultureInfo.InvariantCulture));
        consumer("info.PAGE_COUNT", GetPageCount().ToString(CultureInfo.InvariantCulture));
        consumer("info.PAGE_COUNT_LIVE", GetLivePageCount().ToString(CultureInfo.InvariantCulture));
        consumer("info.PAGE_SIZE", GetMaxPageSize().ToString(CultureInfo.InvariantCulture));
        consumer("info.CACHE_MAX_SIZE", GetCacheSize().ToString(CultureInfo.InvariantCulture));
        consumer("info.CACHE_SIZE", GetCacheSizeUsed().ToString(CultureInfo.InvariantCulture));
        consumer("info.CACHE_HIT_RATIO", GetCacheHitRatio().ToString(CultureInfo.InvariantCulture));
        consumer("info.TOC_CACHE_HIT_RATIO", GetTocCacheHitRatio().ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc/>
    public int GetCacheHitRatio() => GetCacheHitRatio(cache);

    /// <inheritdoc/>
    public int GetTocCacheHitRatio() => GetCacheHitRatio(chunksToC);

    private static int GetCacheHitRatio(CacheLongKeyLIRS<object>? cache)
    {
        if (cache == null)
        {
            return 0;
        }

        long hits = cache.GetHits();
        return (int)(100 * hits / (hits + cache.GetMisses() + 1));
    }

    private static int GetCacheHitRatio(CacheLongKeyLIRS<long[]> cache)
    {
        long hits = cache.GetHits();
        return (int)(100 * hits / (hits + cache.GetMisses() + 1));
    }

    internal bool IsBackgroundThread()
    {
        BackgroundWriterThread? t = backgroundWriterThread;
        return t != null && Thread.CurrentThread == t.Thread;
    }

    private void StopBackgroundThread(bool waitForIt)
    {
        // Loop here is not strictly necessary, except for case of a spurious failure,
        // which should not happen with non-weak flavour of CAS operation,
        // but I've seen it, so just to be safe...
        BackgroundWriterThread? t;
        while ((t = backgroundWriterThread) != null)
        {
            if (Interlocked.CompareExchange(ref backgroundWriterThread, null, t) == t)
            {
                // if called from within the thread itself - can not join
                if (t.Thread != Thread.CurrentThread)
                {
                    lock (t.Sync)
                    {
                        Monitor.PulseAll(t.Sync);
                    }

                    if (waitForIt)
                    {
                        try
                        {
                            _ = t.Thread.Join(TimeSpan.FromDays(1));
                        }
                        catch (Exception)
                        {
                            // ignore
                        }
                    }
                }

                ShutdownExecutors();
                break;
            }
        }
    }

    private void ShutdownExecutors()
    {
        Utils.ShutdownExecutor(serializationExecutor);
        serializationExecutor = null;
        Utils.ShutdownExecutor(bufferSaveExecutor);
        bufferSaveExecutor = null;
    }

    private List<C>? FindOldChunks(int writeLimit, int targetFillRate)
    {
        Debug.Assert(HasPersistentData());
        long time = GetTimeSinceCreation();

        // the queue will contain chunks we want to free up
        // the smaller the collectionPriority, the more desirable this chunk's re-write is
        // the heap dequeues the least desirable chunk first, so priorities are negated
        var queue = new PriorityQueue<C, (int, long)>();

        long totalSize = 0;
        long latestVersion = LastChunkVersion() + 1;

        IEnumerable<C>? candidates = GetRewriteCandidates();
        if (candidates == null)
        {
            candidates = chunks.Values;
        }

        foreach (C chunk in candidates)
        {
            // only look at chunk older than the retention time
            // (it's possible to compact chunks earlier, but right
            // now we don't do that)
            int fillRate = chunk.GetFillRate();
            if (IsRewritable(chunk, time) && fillRate <= targetFillRate)
            {
                long age = Math.Max(1, latestVersion - chunk.Version);
                chunk.CollectPriority = (int)(fillRate * 1000 / age);
                totalSize += chunk.MaxLenLive;
                queue.Enqueue(chunk, (-chunk.CollectPriority, -chunk.MaxLenLive));
                while (totalSize > writeLimit)
                {
                    if (!queue.TryDequeue(out C? removed, out _))
                    {
                        break;
                    }

                    totalSize -= removed.MaxLenLive;
                }
            }
        }

        if (queue.Count == 0)
        {
            return null;
        }

        var result = new List<C>();
        while (queue.TryDequeue(out C? chunk, out _))
        {
            result.Add(chunk);
        }

        return result;
    }

    /// <summary>
    /// Commits and saves all changes, if there are any, and compacts the store if needed.
    /// </summary>
    internal void WriteInBackground()
    {
        try
        {
            if (GetMvStore().IsOpen() && GetMvStore().GetPanicException() == null && !IsReadOnly())
            {
                // could also commit when there are many unsaved pages,
                // but according to a test it doesn't really help
                long time = GetTimeSinceCreation();
                if (time > lastCommitTime + autoCommitDelay)
                {
                    _ = GetMvStore().TryCommit();
                }

                DoHousekeeping(GetMvStore());
                // less than 10 I/O operations will still count as "idle"
                autoCompactLastFileOpCount = GetWriteCount() + GetReadCount() + 10;
            }
        }
        catch (MvStoreException e)
        {
            GetMvStore().Panic(e);
        }
        catch (Exception e)
        {
            GetMvStore().Panic(e);
        }
    }

    /// <summary>
    /// Tries to increase the fill rate by re-writing partially full chunks.
    /// </summary>
    protected bool RewriteChunks(int writeLimit, int targetFillRate)
    {
        serializationLock.Lock();
        try
        {
            MVStore.TxCounter txCounter = GetMvStore().RegisterVersionUsage();
            try
            {
                AcceptChunkOccupancyChanges(GetTimeSinceCreation(), GetMvStore().GetCurrentVersion());
                List<C>? old = FindOldChunks(writeLimit, targetFillRate);
                if (old != null)
                {
                    HashSet<int> idSet = CreateIdSet(old);
                    return idSet.Count != 0 && CompactRewrite(idSet) > 0;
                }
            }
            finally
            {
                GetMvStore().DeregisterVersionUsage(txCounter);
            }

            return false;
        }
        finally
        {
            serializationLock.Unlock();
        }
    }

    private static HashSet<int> CreateIdSet(IEnumerable<C> toCompact)
    {
        var set = new HashSet<int>();
        foreach (C c in toCompact)
        {
            _ = set.Add(c.Id);
        }

        return set;
    }

    /// <inheritdoc/>
    public void ExecuteFileStoreOperation(Action operation)
    {
        // because serializationExecutor is a single-threaded one and
        // all task submissions to it are done under storeLock,
        // it is guaranteed, that upon this dummy task completion
        // there are no pending / in-progress task here
        Utils.FlushExecutor(serializationExecutor);
        serializationLock.Lock();
        try
        {
            // similarly, all task submissions to bufferSaveExecutor
            // are done under serializationLock, and upon this dummy task completion
            // it will be no pending / in-progress task here
            Utils.FlushExecutor(bufferSaveExecutor);
            operation();
        }
        finally
        {
            serializationLock.Unlock();
        }
    }

    private int CompactRewrite(ISet<int> set)
    {
        AcceptChunkOccupancyChanges(GetTimeSinceCreation(), GetMvStore().GetCurrentVersion());
        int rewrittenPageCount = RewriteChunks(set, false);
        AcceptChunkOccupancyChanges(GetTimeSinceCreation(), GetMvStore().GetCurrentVersion());
        rewrittenPageCount += RewriteChunks(set, true);
        return rewrittenPageCount;
    }

    private int RewriteChunks(ISet<int> set, bool secondPass)
    {
        int rewrittenPageCount = 0;
        foreach (int chunkId in set)
        {
            if (chunks.TryGetValue(chunkId, out C? chunk) && chunk != null)
            {
                // there is a chance for a chunk to be dropped after set of chunks to be rewritten
                // has been determined
                long[]? toc = GetToC(chunk);
                if (toc != null)
                {
                    for (int pageNo = 0; (pageNo = chunk.Occupancy!.NextClearBit(pageNo)) < chunk.PageCount; ++pageNo)
                    {
                        long tocElement = toc[pageNo];
                        int mapId = DataUtils.GetPageMapId(tocElement);
                        MVMap<string, string> metaMap = GetMvStore().GetMetaMap();
                        IAnyMap? map = mapId == layout!.GetId() ? layout
                                : mapId == metaMap.GetId() ? metaMap : GetMvStore().GetAnyMap(mapId);
                        if (map != null && !map.MapIsClosed)
                        {
                            Debug.Assert(!map.MapIsSingleWriter);
                            if (secondPass || DataUtils.IsLeafPosition(tocElement))
                            {
                                long pagePos = DataUtils.ComposePagePos(chunkId, tocElement);
                                serializationLock.Unlock();
                                try
                                {
                                    if (map.MapRewritePage(pagePos))
                                    {
                                        ++rewrittenPageCount;
                                        if (mapId == metaMap.GetId())
                                        {
                                            GetMvStore().MarkMetaChanged();
                                        }
                                    }
                                }
                                finally
                                {
                                    serializationLock.Lock();
                                }
                            }
                        }
                    }
                }
            }
        }

        return rewrittenPageCount;
    }

    /// <inheritdoc/>
    public Page<K, V> ReadPage<K, V>(MVMap<K, V> map, long pos)
    {
        try
        {
            if (!DataUtils.IsPageSaved(pos))
            {
                throw DataUtils.NewMvStoreException(
                    DataUtils.ErrorFileCorrupt, "Position 0");
            }

            Page<K, V>? page = ReadPageFromCache<K, V>(pos);
            if (page == null)
            {
                C chunk = GetChunk(pos);
                int pageOffset = DataUtils.GetPageOffset(pos);
                using RentedBuffer buff = chunk.ReadBufferForPage(this, pageOffset, pos);
                try
                {
                    int position = 0;
                    page = Page<K, V>.Read(buff.Memory, ref position, pos, map);
                }
                catch (MvStoreException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    throw DataUtils.NewMvStoreException(DataUtils.ErrorFileCorrupt,
                            "Unable to read the page at position 0x{0}, chunk {1}, offset 0x{3}",
                            pos.ToString("x", CultureInfo.InvariantCulture), chunk,
                            pageOffset.ToString("x", CultureInfo.InvariantCulture), e);
                }

                CachePage(page);
            }

            return page;
        }
        catch (MvStoreException)
        {
            if (recoveryMode)
            {
                return map.CreateEmptyLeaf();
            }

            throw;
        }
    }

    private C GetChunk(long pos)
    {
        int chunkId = DataUtils.GetPageChunkId(pos);
        if (!chunks.TryGetValue(chunkId, out C? c) || c == null)
        {
            string? s = layout!.Get(Chunk.GetMetaKey(chunkId));
            if (s == null)
            {
                throw DataUtils.NewMvStoreException(
                    DataUtils.ErrorChunkNotFound,
                    "Chunk {0} not found", chunkId);
            }

            c = CreateChunk(s);
            if (!c.IsSaved())
            {
                throw DataUtils.NewMvStoreException(
                    DataUtils.ErrorFileCorrupt,
                    "Chunk {0} is invalid", chunkId);
            }

            chunks[c.Id] = c;
        }

        return c;
    }

    private int CalculatePageNo(long pos)
    {
        int pageNo = -1;
        C chunk = GetChunk(pos);
        long[]? toC = GetToC(chunk);
        if (toC != null)
        {
            int offset = DataUtils.GetPageOffset(pos);
            int low = 0;
            int high = toC.Length - 1;
            while (low <= high)
            {
                int mid = (low + high) >> 1;
                long midVal = DataUtils.GetPageOffset(toC[mid]);
                if (midVal < offset)
                {
                    low = mid + 1;
                }
                else if (midVal > offset)
                {
                    high = mid - 1;
                }
                else
                {
                    pageNo = mid;
                    break;
                }
            }
        }

        return pageNo;
    }

    private void ClearCaches()
    {
        cache?.Clear();
        chunksToC.Clear();
        lock (removedPages)
        {
            removedPages.Clear();
        }
    }

    private long[]? GetToC(C chunk)
    {
        if (chunk.TocPos == 0)
        {
            // legacy chunk without table of content
            return null;
        }

        long[]? toc = chunksToC.Get(chunk.Id);
        if (toc == null)
        {
            toc = chunk.ReadToC(this);
            CacheToC(chunk, toc);
        }

        Debug.Assert(toc.Length == chunk.PageCount, toc.Length + " != " + chunk.PageCount);
        return toc;
    }

    private Page<K, V>? ReadPageFromCache<K, V>(long pos)
        => cache == null ? null : (Page<K, V>?)cache.Get(pos);

    /// <inheritdoc/>
    public void AccountForRemovedPage(long pos, long version, bool pinned, int pageNo)
    {
        Debug.Assert(DataUtils.IsPageSaved(pos));
        if (pageNo < 0)
        {
            pageNo = CalculatePageNo(pos);
        }

        var rpi = new RemovedPageInfo(pos, pinned, version, pageNo);
        lock (removedPages)
        {
            removedPages.Enqueue(rpi, rpi.Version);
        }
    }

    private bool VerifyOrphanedChunksAbsence()
    {
        foreach (C chunk in chunks.Values)
        {
            Debug.Assert(ReferenceEquals(chunk, lastChunk)
                || layout!.ContainsKey(Chunk.GetMetaKey(chunk.Id)), chunk.ToString());
        }

        foreach (KeyValuePair<string, string> entry in layout!)
        {
            if (entry.Key.StartsWith(DataUtils.LayoutChunk, StringComparison.Ordinal))
            {
                int chunkId = DataUtils.ParseHexInt(entry.Key.Substring(DataUtils.LayoutChunk.Length));
                Debug.Assert(chunks.ContainsKey(chunkId),
                    "Layout map contains metadata for non-existent chunk " + chunkId
                    + ": " + layout.Get(entry.Key) + " " + layout);
            }
        }

        return true;
    }

    private C CreateChunk(long time, long version)
    {
        int newChunkId = FindNewChunkId();
        C c = CreateChunk(newChunkId);
        c.Time = time;
        c.Version = version;
        c.Occupancy = new LongBitSet();
        return c;
    }

    /// <summary>
    /// A new empty chunk with the given id.
    /// </summary>
    protected abstract C CreateChunk(int id);

    /// <inheritdoc/>
    public abstract C CreateChunk(string s);

    Chunk IFileStore.CreateChunk(string s) => CreateChunk(s);

    /// <summary>
    /// A chunk described by its parsed header or footer.
    /// </summary>
    protected abstract C CreateChunk(Dictionary<string, string> map);

    private int FindNewChunkId()
    {
        int newChunkId;
        while (true)
        {
            newChunkId = ++lastChunkId & Chunk.MaxId;
            if (newChunkId == lastChunkId)
            {
                break;
            }

            if (!chunks.TryGetValue(newChunkId, out C? old) || old == null)
            {
                break;
            }

            if (!old.IsSaved())
            {
                throw DataUtils.NewMvStoreException(
                        DataUtils.ErrorInternal,
                        "Last block {0} not stored, possibly due to out-of-memory", old);
            }
        }

        return newChunkId;
    }

    /// <summary>
    /// Writes the clean-shutdown mark and syncs, unless read-only.
    /// </summary>
    protected void WriteCleanShutdown()
    {
        if (!IsReadOnly())
        {
            saveChunkLock.Lock();
            try
            {
                WriteCleanShutdownMark();
                Sync();
                Debug.Assert(ValidateFileLength("on close"));
            }
            finally
            {
                saveChunkLock.Unlock();
            }
        }
    }

    /// <summary>
    /// Performs the final preparation before the store is closed normally.
    /// </summary>
    protected abstract void WriteCleanShutdownMark();

    /// <summary>
    /// Makes persistent changes after the last chunk was reset.
    /// </summary>
    protected abstract void AdjustStoreToLastChunk();

    /// <summary>
    /// Saves a chunk's serialized metadata into its layout map entry.
    /// </summary>
    protected void SaveChunkMetadataChanges(C chunk)
    {
        Debug.Assert(serializationLock.IsHeldByCurrentThread());

        // chunk's location need to be determined before it's metadata can be is serialized
        if (chunk.IsAllocated())
        {
            int chunkId = chunk.Id;
            _ = layout!.Operate(Chunk.GetMetaKey(chunkId), chunk.AsString(),
                new LayoutDecisionMaker(chunks, chunkId));
        }
    }

    /// <summary>
    /// Forgets a chunk that holds no live data and whose space is already occupied by another
    /// chunk: a leftover of an incompletely persisted chunk removal, referencing no reachable
    /// page.
    /// </summary>
    protected void DropOrphanedChunk(C chunk)
    {
        Debug.Assert(!chunk.IsLive(), chunk.ToString());
        _ = chunks.TryRemove(chunk.Id, out _);
        _ = CleanToCCache(chunk);
        if (!IsReadOnly() && layout!.Remove(Chunk.GetMetaKey(chunk.Id)) != null)
        {
            GetMvStore().MarkMetaChanged();
        }
    }

    /// <summary>
    /// Marks the space occupied by the given chunks as free.
    /// </summary>
    protected abstract void FreeChunkSpace(IEnumerable<C> chunks);

    /// <summary>
    /// Asserts the file length matches the space in use. Debug only, answers true.
    /// </summary>
    protected abstract bool ValidateFileLength(string msg);

    /// <inheritdoc/>
    public bool Compact(int targetFillRate, int write)
    {
        if (HasPersistentData())
        {
            if (targetFillRate > 0 && GetChunksFillRate() < targetFillRate)
            {
                // We can't wait forever for the lock here,
                // because if called from the background thread,
                // it might go into deadlock with concurrent database closure
                // and attempt to stop this thread.
                bool? result = GetMvStore().TryExecuteUnderStoreLock(() => RewriteChunks(write, 100));
                return result != null && result.Value;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public void CompactStore(long maxCompactTime)
        => CompactStore(autoCompactFillRate, maxCompactTime, 16 * 1024 * 1024, GetMvStore());

    /// <summary>
    /// Compacts blocks with a low fill rate and moves chunks next to each other.
    /// </summary>
    protected abstract void CompactStore(int thresholdFillRate, long maxCompactTime, int maxWriteSize,
        MVStore mvStore);

    /// <summary>
    /// The background pass: auto-commit and compaction housekeeping.
    /// </summary>
    protected abstract void DoHousekeeping(MVStore mvStore);

    /// <summary>
    /// Writes the store header blocks of a new store.
    /// </summary>
    protected abstract void InitializeStoreHeader(long time);

    /// <summary>
    /// Reads the store header and the chunk set of an existing store.
    /// </summary>
    protected abstract void ReadStoreHeader(bool recoveryMode);

    private int LastMapId()
    {
        C? chunk = lastChunk;
        return chunk == null ? 0 : chunk.MapId;
    }

    private MvStoreException GetUnsupportedWriteFormatException(long format, int expectedFormat, string s)
    {
        format = DataUtils.ReadHexLong(storeHeader, HdrFormatRead, format);
        if (format >= FormatReadMin && format <= FormatReadMax)
        {
            s += ", and the file was not opened in read-only mode";
        }

        return DataUtils.NewMvStoreException(DataUtils.ErrorUnsupportedFormat, s, format, expectedFormat);
    }

    /// <summary>
    /// Discovers a valid chunk, searching the file backwards from the given block.
    /// </summary>
    protected C? DiscoverChunk(long block)
    {
        long candidateLocation = long.MaxValue;
        C? candidate = null;
        while (true)
        {
            if (block == candidateLocation)
            {
                return candidate;
            }

            if (block == 2)
            {
                // number of blocks occupied by headers
                return null;
            }

            C? test = ReadChunkFooter(block);
            if (test != null)
            {
                // if we encounter chunk footer (with or without corresponding header)
                // in the middle of prospective chunk, stop considering it
                candidateLocation = long.MaxValue;
                test = ReadChunkHeaderOptionally(test.Block, test.Id);
                if (test != null)
                {
                    // if that footer has a corresponding header,
                    // consider them as a new candidate for a valid chunk
                    candidate = test;
                    candidateLocation = test.Block;
                }
            }

            // if we encounter chunk header without corresponding footer
            // (due to incomplete write?) in the middle of prospective
            // chunk, stop considering it
            if (--block > candidateLocation && ReadChunkHeaderOptionally(block) != null)
            {
                candidateLocation = long.MaxValue;
            }
        }
    }

    /// <summary>
    /// Tries the discovered chunks newest-first until one has a layout map whose chunk
    /// references all point at valid locations.
    /// </summary>
    protected bool FindLastChunkWithCompleteValidChunkSet(IComparer<C> chunkComparator,
        Dictionary<long, C> validChunksByLocation, bool afterFullScan)
    {
        // this collection will hold potential candidates for lastChunk to fall back to,
        // in order from the most to the least likely
        var collected = new List<C>(validChunksByLocation.Values);
        C[] lastChunkCandidates = collected.ToArray();
        Array.Sort(lastChunkCandidates, chunkComparator);
        var validChunksById = new Dictionary<int, C>();
        foreach (C chunk in lastChunkCandidates)
        {
            validChunksById[chunk.Id] = chunk;
        }

        // Try candidates for "last chunk" in order from newest to oldest
        // until suitable is found. Suitable one should have meta map
        // where all chunk references point to valid locations.
        foreach (C chunk in lastChunkCandidates)
        {
            bool verified = true;
            try
            {
                SetLastChunk(chunk);
                // load the chunk metadata: although meta's root page resides in the lastChunk,
                // traversing meta map might recursively load another chunk(s)
                foreach (C c in GetChunksFromLayoutMap())
                {
                    C? test;
                    if (!validChunksByLocation.TryGetValue(c.Block, out test) || test.Id != c.Id)
                    {
                        if (validChunksById.TryGetValue(c.Id, out test) && test != null)
                        {
                            // We do not have a valid chunk at that location,
                            // but there is a copy of same chunk from original
                            // location.
                            // Chunk header at original location does not have
                            // any dynamic (occupancy) metadata, so it can't be
                            // used here as is, re-point our chunk to original
                            // location instead.
                            c.SetBlock(test.Block);
                        }
                        else if (c.IsLive() && (afterFullScan || ReadChunkHeaderAndFooter(c.Block, c.Id) == null))
                        {
                            // chunk reference is invalid
                            // this "last chunk" candidate is not suitable
                            verified = false;
                            break;
                        }
                    }

                    if (!c.IsLive() && !validChunksById.ContainsKey(c.Id) &&
                            (afterFullScan || ReadChunkHeaderAndFooter(c.Block, c.Id) == null))
                    {
                        // chunk reference is invalid but chunk is not live anymore:
                        // we can just remove entry from meta, referencing to this chunk,
                        // but store maybe R/O, and it's not properly started yet,
                        // so lets make this chunk "dead" and taking no space,
                        // and it will be automatically removed later.
                        c.SetBlock(0);
                        c.Len = 0;
                        if (c.Unused == 0)
                        {
                            c.Unused = creationTime;
                        }

                        if (c.UnusedAtVersion == 0)
                        {
                            c.UnusedAtVersion = MVStore.InitialVersion;
                        }
                    }
                }
            }
            catch (Exception)
            {
                verified = false;
            }

            if (verified)
            {
                return true;
            }
        }

        return false;
    }

    private C ReadChunkHeader(long block)
    {
        long p = block * BlockSize;
        using RentedBuffer buff = ReadFully((Chunk?)null, p, Chunk.MaxHeaderLength);
        Exception? exception = null;
        try
        {
            int position = 0;
            C chunk = CreateChunk(Chunk.ReadChunkHeader(buff.Span, ref position));
            if (chunk.Block == 0)
            {
                chunk.SetBlock(block);
            }

            if (chunk.Block == block)
            {
                return chunk;
            }
        }
        catch (MvStoreException e)
        {
            exception = e.InnerException ?? e;
        }
        catch (Exception e)
        {
            // there could be various reasons
            exception = e;
        }

        throw DataUtils.NewMvStoreException(
                DataUtils.ErrorFileCorrupt,
                "File corrupt reading chunk at position {0}", p, exception!);
    }

    /// <summary>
    /// The chunks named by the layout map, newest metadata first.
    /// </summary>
    protected IEnumerable<C> GetChunksFromLayoutMap() => GetChunksFromLayoutMap(layout!);

    private IEnumerable<C> GetChunksFromLayoutMap(MVMap<string, string> layoutMap)
    {
        // It is "chicken and egg" problem on store opening - in order to figure out existing chunks
        // we need to read layout map and for that we need metadata about chunks map's pages resides in,
        // so StackOverflowException is always looming here, and good solution is due.
        // For now, we just reverse iterator so latest chunks will come first (mostly).
        Cursor<string, string> cursor = layoutMap.Cursor(
            DataUtils.LayoutChunk + char.MaxValue, null, true);
        while (cursor.HasNext())
        {
            string key = cursor.Next();
            if (key.StartsWith(DataUtils.LayoutChunk, StringComparison.Ordinal))
            {
                C nextChunk = CreateChunk(cursor.GetValue()!);
                // might be there already, due to layout traversal
                // see readPage() ... getChunkIfFound(),
                // then take existing one instead
                C existingChunk = chunks.GetOrAdd(nextChunk.Id, nextChunk);
                yield return ReferenceEquals(existingChunk, nextChunk) ? nextChunk : existingChunk;
            }
        }
    }

    private bool IsValidChunk(C chunk) => ReadChunkHeaderAndFooter(chunk.Block, chunk.Id) != null;

    /// <summary>
    /// Reads a chunk header and footer, verifying the stored data is consistent.
    /// </summary>
    protected C? ReadChunkHeaderAndFooter(long block, int expectedId)
    {
        C? header = ReadChunkHeaderOptionally(block, expectedId);
        if (header != null)
        {
            C? footer = ReadChunkFooter(block + header.Len);
            if (footer == null || footer.Id != expectedId || footer.Block != header.Block)
            {
                return null;
            }
        }

        return header;
    }

    /// <summary>
    /// Reads a chunk header, answering null when it is missing or names another chunk.
    /// </summary>
    protected C? ReadChunkHeaderOptionally(long block, int expectedId)
    {
        C? chunk = ReadChunkHeaderOptionally(block);
        return chunk == null || chunk.Id != expectedId ? null : chunk;
    }

    /// <summary>
    /// Reads a chunk header, answering null when it is missing or lives elsewhere.
    /// </summary>
    protected C? ReadChunkHeaderOptionally(long block)
    {
        try
        {
            C chunk = ReadChunkHeader(block);
            return chunk.Block != block ? null : chunk;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Tries to read a chunk footer: the chunk ending right before the given block.
    /// </summary>
    protected C? ReadChunkFooter(long block)
    {
        // the following can fail for various reasons
        try
        {
            // read the chunk footer of the last block of the file
            long pos = block * BlockSize - Chunk.FooterLength;
            if (pos < 0)
            {
                return null;
            }

            using RentedBuffer buff = ReadFully((Chunk?)null, pos, Chunk.FooterLength);
            Dictionary<string, string>? m = DataUtils.ParseChecksummedMap(buff.Span);
            if (m != null)
            {
                C chunk = CreateChunk(m);
                if (chunk.Block == 0)
                {
                    chunk.SetBlock(block - chunk.Len);
                }

                return chunk;
            }
        }
        catch (Exception)
        {
            // ignore
        }

        return null;
    }

    /// <summary>
    /// A buffer for writing. The caller holds the store lock from here until the buffer is
    /// released.
    /// </summary>
    public WriteBuffer GetWriteBuffer()
    {
        if (writeBufferPool.TryDequeue(out WriteBuffer? buff) && buff != null)
        {
            _ = buff.Clear();
        }
        else
        {
            buff = new WriteBuffer();
        }

        return buff;
    }

    /// <summary>
    /// Releases a buffer for writing.
    /// </summary>
    public void ReleaseWriteBuffer(WriteBuffer buff)
    {
        if (buff.Capacity() <= 4 * 1024 * 1024 && writeBufferPool.Count <= PipeLength)
        {
            writeBufferPool.Enqueue(buff);
        }
    }

    /// <summary>
    /// When the store was created, in milliseconds since 1970.
    /// </summary>
    public long GetCreationTime() => creationTime;

    /// <summary>
    /// The fill rate limit for automatic compaction.
    /// </summary>
    protected int GetAutoCompactFillRate() => autoCompactFillRate;

    /// <inheritdoc/>
    public IDictionary<string, object?> GetStoreHeader() => storeHeader;

    /// <summary>
    /// A removed page: its chunk, its number within the chunk, its length, and the version that
    /// removed it. Ordered by version, so occupancy is accounted oldest-first.
    /// </summary>
    private sealed class RemovedPageInfo : IComparable<RemovedPageInfo>
    {
        internal readonly long Version;
        private readonly long removedPageInfo;

        internal RemovedPageInfo(long pagePos, bool pinned, long version, int pageNo)
        {
            removedPageInfo = CreateRemovedPageInfo(pagePos, pinned, pageNo);
            Version = version;
        }

        public int CompareTo(RemovedPageInfo? other) => Version.CompareTo(other!.Version);

        internal int GetPageChunkId() => DataUtils.GetPageChunkId(removedPageInfo);

        internal int GetPageNo() => DataUtils.GetPageOffset(removedPageInfo);

        internal int GetPageLength() => DataUtils.GetPageMaxLength(removedPageInfo);

        /// <summary>
        /// Whether the removed page was pinned and can not be evacuated to a new chunk.
        /// </summary>
        internal bool IsPinned() => (removedPageInfo & 1) == 1;

        /// <summary>
        /// Transforms a saved page position into removed page info by replacing "page offset"
        /// with "page sequential number" and the "page type" bit with the "pinned page" flag.
        /// </summary>
        private static long CreateRemovedPageInfo(long pagePos, bool isPinned, int pageNo)
        {
            Debug.Assert(pageNo >= 0);
            Debug.Assert(pageNo >> 26 == 0);

            long result = (pagePos & ~((0xFFFFFFFFL << 6) | 1)) | ((long)pageNo << 6);
            if (isPinned)
            {
                result |= 1;
            }

            return result;
        }

        public override string ToString() => "RemovedPageInfo{version=" + Version
            + ", chunk=" + GetPageChunkId()
            + ", pageNo=" + GetPageNo()
            + ", len=" + GetPageLength()
            + (IsPinned() ? ", pinned" : "") + '}';
    }

    /// <summary>
    /// A background writer thread to automatically store changes from time to time.
    /// </summary>
    private sealed class BackgroundWriterThread
    {
        internal readonly object Sync = new();
        internal readonly Thread Thread;
        private readonly FileStore<C> store;
        private readonly int sleep;

        internal BackgroundWriterThread(FileStore<C> store, int sleep, string? fileStoreName)
        {
            this.store = store;
            this.sleep = sleep;
            Thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "MVStore background writer " + fileStoreName,
            };
        }

        internal void Start() => Thread.Start();

        private void Run()
        {
            while (store.IsBackgroundThread())
            {
                lock (Sync)
                {
                    try
                    {
                        _ = Monitor.Wait(Sync, sleep);
                    }
                    catch (ThreadInterruptedException)
                    {
                    }
                }

                if (!store.IsBackgroundThread())
                {
                    break;
                }

                store.WriteInBackground();
            }
        }
    }

    /// <summary>
    /// New chunk metadata is saved when the entry is already there, or the chunk is still
    /// registered: removal from the chunk table happens before layout entry removal, so a
    /// missing layout entry with a live chunk entry means a new chunk, not a dropped one.
    /// </summary>
    private sealed class LayoutDecisionMaker : DecisionMaker<string>
    {
        private readonly ConcurrentDictionary<int, C> chunks;
        private readonly int chunkId;

        public LayoutDecisionMaker(ConcurrentDictionary<int, C> chunks, int chunkId)
        {
            this.chunks = chunks;
            this.chunkId = chunkId;
        }

        public override Decision Decide(string? existingValue, string? providedValue)
            => existingValue != null || chunks.ContainsKey(chunkId) ? Decision.Put : Decision.Abort;
    }
}
