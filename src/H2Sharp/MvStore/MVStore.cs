using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using H2Sharp.Compression;
using H2Sharp.Engine;
using H2Sharp.MvStore.Type;
using H2Sharp.Store.FileSystem;
using H2Sharp.Util;

namespace H2Sharp.MvStore;

/// <summary>
/// One versioned root, seen without knowing the key and value types.
/// </summary>
/// <remarks>
/// C# has no wildcard capture: where the reference says <c>RootReference&lt;?, ?&gt;</c>, the store
/// says this. Only what the store asks of any root is here — its version and its root page.
/// Public because it flows through public methods like <see cref="MVStore.RemoveMap(IAnyMap)"/>.
/// </remarks>
public interface IRootView
{
    /// <summary>
    /// Which version this root belongs to.
    /// </summary>
    long Version { get; }

    /// <summary>
    /// The root page.
    /// </summary>
    object RootPage { get; }
}

/// <summary>
/// One map, seen without knowing the key and value types.
/// </summary>
/// <remarks>
/// C# has no wildcard capture: where the reference says <c>MVMap&lt;?, ?&gt;</c>, the store says
/// this. Only what the store asks of any map is here; everything typed stays on
/// <see cref="MVMap{K, V}"/>. Public because it flows through public methods like
/// <see cref="MVStore.RemoveMap(IAnyMap)"/>.
/// </remarks>
public interface IAnyMap
{
    /// <summary>
    /// The map id.
    /// </summary>
    int MapId { get; }

    /// <summary>
    /// The version this map was created in.
    /// </summary>
    long MapCreateVersion { get; }

    /// <summary>
    /// Whether the map is closed.
    /// </summary>
    bool MapIsClosed { get; }

    /// <summary>
    /// Whether the map is volatile.
    /// </summary>
    bool MapIsVolatile { get; }

    /// <summary>
    /// Whether appends go through the single-writer buffer.
    /// </summary>
    bool MapIsSingleWriter { get; }

    /// <summary>
    /// Closes the map.
    /// </summary>
    void MapClose();

    /// <summary>
    /// Moves the root to a write version, or null when the map is gone.
    /// </summary>
    IRootView? MapSetWriteVersion(long version);

    /// <summary>
    /// Whether anything changed since a version.
    /// </summary>
    bool MapHasChangesSince(long version);

    /// <summary>
    /// Walks the root chain back to a version.
    /// </summary>
    bool MapRollbackRoot(long version);

    /// <summary>
    /// Takes a root read from the layout.
    /// </summary>
    void MapSetRootPos(long rootPos, long version);

    /// <summary>
    /// The current root page.
    /// </summary>
    object MapGetRootPage();

    /// <summary>
    /// The metadata line for the map.
    /// </summary>
    string MapAsString(string? name);

    /// <summary>
    /// Drops everything, answering the root counters the store accounts for.
    /// </summary>
    (long updateCounter, long updateAttemptCounter) MapClearIt();

    /// <summary>
    /// The root counters the store accounts for.
    /// </summary>
    (long updateCounter, long updateAttemptCounter) MapGetRootCounters();

    /// <summary>
    /// Whether the current thread holds the root.
    /// </summary>
    bool MapRootIsLockedByCurrentThread();

    /// <summary>
    /// Rewrites one saved page into the current version, for chunk compaction.
    /// </summary>
    bool MapRewritePage(long pagePos);
}

/// <summary>
/// The file behind a store, seen without knowing the chunk type.
/// </summary>
/// <remarks>
/// C# generics are invariant: where the reference says <c>FileStore&lt;?&gt;</c>, the store says
/// this. Only what the store asks of any file is here — the chunk type never leaves the file.
/// Public because it flows through public methods like <see cref="MVStore.GetFileStore"/>.
/// </remarks>
public interface IFileStore
{
    /// <summary>
    /// Opens a file.
    /// </summary>
    void Open(string fileName, bool readOnly, char[]? encryptionKey);

    /// <summary>
    /// Opens another file of the same kind.
    /// </summary>
    IFileStore Open(string fileName, bool readOnly);

    /// <summary>
    /// Binds the store.
    /// </summary>
    void Bind(MVStore store);

    /// <summary>
    /// Reads the metadata and answers the meta map.
    /// </summary>
    MVMap<string, string> Start();

    /// <summary>
    /// Stops the background thread, compacting first when asked.
    /// </summary>
    void Stop(long allowedCompactionTime);

    /// <summary>
    /// Closes the file.
    /// </summary>
    void Close();

    /// <summary>
    /// The id the meta map takes, from the store's sequence.
    /// </summary>
    int GetMetaMapId(Func<int> nextIdSupplier);

    /// <summary>
    /// The layout, or null when there is none.
    /// </summary>
    IDictionary<string, string>? GetLayoutMap();

    /// <summary>
    /// Whether a map is a regular stored map rather than metadata.
    /// </summary>
    bool IsRegularMap(IAnyMap map);

    /// <summary>
    /// Where a map's root lives.
    /// </summary>
    long GetRootPos(int mapId);

    /// <summary>
    /// Forgets a map's root.
    /// </summary>
    bool DeregisterMapRoot(int mapId);

    /// <summary>
    /// Whether anything changed since a version.
    /// </summary>
    bool HasChangesSince(long lastStoredVersion);

    /// <summary>
    /// The largest page that may be written.
    /// </summary>
    long GetMaxPageSize();

    /// <summary>
    /// How long unused versions are kept, in milliseconds.
    /// </summary>
    int GetRetentionTime();

    /// <summary>
    /// Sets how long unused versions are kept.
    /// </summary>
    void SetRetentionTime(int ms);

    /// <summary>
    /// Whether the unsaved memory calls for a store.
    /// </summary>
    bool ShouldSaveNow(int unsavedMemory, int autoCommitMemory);

    /// <summary>
    /// The background write delay, in milliseconds.
    /// </summary>
    int GetAutoCommitDelay();

    /// <summary>
    /// Sets the background write delay. Negative stops the thread, zero stores without waiting.
    /// </summary>
    void SetAutoCommitDelay(int millis);

    /// <summary>
    /// Whether a version is still known.
    /// </summary>
    bool IsKnownVersion(long version);

    /// <summary>
    /// Rolls the file back to a version.
    /// </summary>
    void RollbackTo(long version);

    /// <summary>
    /// Drops chunks nobody needs.
    /// </summary>
    void DropUnusedChunks();

    /// <summary>
    /// Writes the changed root pages.
    /// </summary>
    void StoreIt(List<object> changed, long version, bool syncWrite);

    /// <summary>
    /// Runs an operation under the store lock, panicking on failure.
    /// </summary>
    void ExecuteFileStoreOperation(Action operation);

    /// <summary>
    /// Syncs the file.
    /// </summary>
    void Sync();

    /// <summary>
    /// Compacts the file, for at most so long.
    /// </summary>
    void CompactStore(long maxCompactTime);

    /// <summary>
    /// Compacts the file.
    /// </summary>
    bool Compact(int targetFillRate, int write);

    /// <summary>
    /// How full the file is, in percent.
    /// </summary>
    int GetFillRate();

    /// <summary>
    /// How many bytes were read.
    /// </summary>
    long GetReadCount();

    /// <summary>
    /// How many bytes were written.
    /// </summary>
    long GetWriteCount();

    /// <summary>
    /// The file size in bytes.
    /// </summary>
    long Size();

    /// <summary>
    /// Reads a chunk header back.
    /// </summary>
    Chunk CreateChunk(string s);

    /// <summary>
    /// Reads a page back.
    /// </summary>
    Page<K, V> ReadPage<K, V>(MVMap<K, V> map, long pos);

    /// <summary>
    /// The cache size, in kilobytes.
    /// </summary>
    int GetCacheSize();

    /// <summary>
    /// The cache size in use, in kilobytes.
    /// </summary>
    int GetCacheSizeUsed();

    /// <summary>
    /// Sets the cache size, in megabytes.
    /// </summary>
    void SetCacheSize(int mb);

    /// <summary>
    /// Whether freed space is reused.
    /// </summary>
    bool IsSpaceReused();

    /// <summary>
    /// Sets whether freed space is reused.
    /// </summary>
    void SetReuseSpace(bool reuseSpace);

    /// <summary>
    /// Describes the file.
    /// </summary>
    void PopulateInfo(Action<string, string> consumer);

    /// <summary>
    /// Accounts for a removed page.
    /// </summary>
    void AccountForRemovedPage(long pos, long version, bool pinned, int pageNo);

    /// <summary>
    /// The store header.
    /// </summary>
    IDictionary<string, object?> GetStoreHeader();

    /// <summary>
    /// Whether the file is read-only.
    /// </summary>
    bool IsReadOnly();

    /// <summary>
    /// The file name.
    /// </summary>
    string? GetFileName();
}

/// <summary>
/// A versioned store of maps: an in-memory tree of roots over an optional file.
/// </summary>
/// <remarks>
/// Versions only move forward. A commit takes the store lock, bumps the version, and either moves
/// every map's root (in memory) or writes the changed roots to the file; readers holding an older
/// version keep reading it until the store stops keeping it. The lock is a reader-writer lock held
/// for writing throughout, which stands in for the reference's fair reentrant lock — reentrant and
/// exclusive, but not fair.
/// <para>
/// The background thread belongs to the file, which is why an in-memory store never starts one:
/// <see cref="SetAutoCommitDelay"/> only reaches the file.
/// </para>
/// </remarks>
public sealed class MVStore : IDisposable
{
    private const int StateOpen = 0;

    private const int StateStopping = 1;

    private const int StateClosed = 2;

    /// <summary>
    /// The version before any version.
    /// </summary>
    internal const long InitialVersion = -1;

    // The transaction layer is out of scope; the prefix is quoted here because compaction has to
    // recognize its single-writer maps. The value is TransactionStore.UNDO_LOG_NAME_PREFIX.
    private const string UndoLogNamePrefix = "undoLog";

    private readonly ReaderWriterLockSlim storeLock = new(LockRecursionPolicy.SupportsRecursion);

    private int storeOperationInProgress;

    private volatile int state;

    // Volatile long is not allowed in C#, so these are plain fields read and written with
    // Volatile.Read and Volatile.Write wherever another thread may be watching.
    private long closingThreadId;

    private readonly IFileStore? fileStore;

    private readonly bool fileStoreShallBeClosed;

    private readonly int keysPerPage;

    private long updateCounter;

    private long updateAttemptCounter;

    private readonly MVMap<string, string> meta;

    private readonly ConcurrentDictionary<int, IAnyMap> maps = new();

    private int lastMapId;

    private int versionsToKeep = 5;

    private readonly int compressionLevel;

    private Compressor? compressorFast;

    private Compressor? compressorHigh;

    /// <summary>
    /// What a background failure is reported to, if anything.
    /// </summary>
    public readonly Action<Thread, Exception>? BackgroundExceptionHandler;

    // Plain, because volatile long is not allowed: every read or write another thread may be
    // watching goes through Volatile.Read or Volatile.Write.
    private long currentVersion;

    private readonly AtomicLong oldestVersionToKeep = new();

    private readonly LinkedList<TxCounter> versions = new();

    private volatile TxCounter currentTxCounter;

    private int unsavedMemory;

    private readonly int autoCommitMemory;

    private volatile bool saveNeeded;

    private volatile bool metaChanged;

    private MvStoreException? panicException;

    private long lastTimeAbsolute;

    private long leafCount;

    private long nonLeafCount;

    private volatile Action<long>? oldestVersionTracker;

    /// <summary>
    /// A store from a configuration, opening the file when one is named.
    /// </summary>
    internal MVStore(IDictionary<string, object?> config)
    {
        currentTxCounter = new TxCounter(currentVersion);
        compressionLevel = DataUtils.GetConfigParam(config, "compress", 0);
        config.TryGetValue("fileName", out object? fileNameObj);
        string? fileName = (string?)fileNameObj;
        config.TryGetValue("fileStore", out object? fileStoreObj);
        IFileStore? store = (IFileStore?)fileStoreObj;
        bool fileStoreShallBeOpen = false;
        if (store is null)
        {
            if (fileName is not null)
            {
                store = new SingleFileStore(config);
                fileStoreShallBeOpen = true;
            }

            fileStoreShallBeClosed = true;
        }
        else
        {
            if (fileName is not null)
            {
                throw new ArgumentException("fileName && fileStore");
            }

            fileStoreShallBeClosed = config.TryGetValue("fileStoreIsAdopted", out object? adopted)
                && adopted is true;
        }

        fileStore = store;
        keysPerPage = DataUtils.GetConfigParam(config, "keysPerPage", 48);
        config.TryGetValue("backgroundExceptionHandler", out object? handlerObj);
        BackgroundExceptionHandler = (Action<Thread, Exception>?)handlerObj;
        storeLock.EnterWriteLock();
        try
        {
            if (fileStore is not null)
            {
                // 19 KB memory is about 1 KB storage
                int kb = Math.Max(1, Math.Min(19, Utils.ScaleForAvailableMemory(64))) * 1024;
                kb = DataUtils.GetConfigParam(config, "autoCommitBufferSize", kb);
                autoCommitMemory = kb * 1024;
                config.TryGetValue("encryptionKey", out object? encryptionKeyObj);
                config.Remove("encryptionKey");
                char[]? encryptionKey = (char[]?)encryptionKeyObj;
                MVMap<string, string>? metaMap = null;
                try
                {
                    if (fileStoreShallBeOpen)
                    {
                        bool readOnly = config.ContainsKey("readOnly");
                        fileStore.Open(fileName!, readOnly, encryptionKey);
                    }

                    fileStore.Bind(this);
                    metaMap = fileStore.Start();
                }
                catch (MvStoreException e)
                {
                    Panic(e);
                }
                finally
                {
                    if (encryptionKey is not null)
                    {
                        Array.Fill(encryptionKey, (char)0);
                    }
                }

                meta = metaMap!;
                ScrubMetaMap();

                // setAutoCommitDelay starts the thread, but only if
                // the parameter is different from the old value
                int delay = DataUtils.GetConfigParam(config, "autoCommitDelay", 1000);
                SetAutoCommitDelay(delay);
            }
            else
            {
                autoCommitMemory = 0;
                meta = OpenMetaMap();
            }

            OnVersionChange(currentVersion);
        }
        finally
        {
            UnlockAndCheckPanicCondition();
        }
    }

    /// <summary>
    /// The meta map, opened over the file's root or an empty root.
    /// </summary>
    public MVMap<string, string> OpenMetaMap()
    {
        int metaId = fileStore is not null ? fileStore.GetMetaMapId(GetNextMapId) : GetNextMapId();
        var map = new MVMap<string, string>(this, metaId, StringDataType.Instance, StringDataType.Instance);
        map.SetRootPos(GetRootPos(map.GetId()), Volatile.Read(ref currentVersion));
        return map;
    }

    /// <summary>
    /// Compacts a file into itself.
    /// </summary>
    public static void Compact(string fileName, bool compress, IFileStore? fileStore)
    {
        string tempName = fileName + Constants.SuffixMvStoreTempFile;
        Compact(fileName, tempName, compress, fileStore);
        MVStoreTool.MoveAtomicReplace(tempName, fileName);
    }

    /// <summary>
    /// Compacts one file into another.
    /// </summary>
    public static void Compact(string sourceFileName, string targetFileName, bool compress,
        IFileStore? fileStore)
    {
        try
        {
            FilePath.Get(targetFileName).Delete();
            var targetBuilder = new Builder();
            if (fileStore is null)
            {
                targetBuilder.FileName(targetFileName);
            }
            else
            {
                targetBuilder.AdoptFileStore(fileStore.Open(targetFileName, false));
            }

            if (compress)
            {
                targetBuilder.Compress();
            }

            using (MVStore target = targetBuilder.Open())
            {
                if (fileStore is not null)
                {
                    fileStore.Close();
                    fileStore = null;
                }

                Builder sourceBuilder = new Builder().ReadOnly()
                    .AdoptFileStore(target.GetFileStore()!.Open(sourceFileName, true));
                using (MVStore source = sourceBuilder.Open())
                {
                    Compact(source, target);
                }
            }
        }
        finally
        {
            fileStore?.Close();
        }
    }

    private static void Compact(MVStore source, MVStore target)
    {
        target.SetCurrentVersion(source.GetCurrentVersion());
        target.AdjustLastMapId(source.GetLastMapId());
        int autoCommitDelay = target.GetAutoCommitDelay();
        bool reuseSpace = target.IsSpaceReused();
        try
        {
            target.SetReuseSpace(false);
            target.SetAutoCommitDelay(0);
            MVMap<string, string> sourceMeta = source.GetMetaMap();
            MVMap<string, string> targetMeta = target.GetMetaMap();
            foreach (KeyValuePair<string, string> m in sourceMeta.EntrySet())
            {
                string key = m.Key;
                if (key.StartsWith(DataUtils.MetaMap, StringComparison.Ordinal))
                {
                    // ignore
                }
                else if (key.StartsWith(DataUtils.MetaName, StringComparison.Ordinal))
                {
                    // ignore
                }
                else
                {
                    targetMeta.Put(key, m.Value);
                }
            }

            string? threads = Environment.GetEnvironmentVariable("h2.compactThreads");
            if (!int.TryParse(threads, out int poolSize))
            {
                poolSize = Math.Max(1, Environment.ProcessorCount / 4);
            }

            // We are going to cheat a little bit in the copyFrom() by employing "incomplete" pages,
            // which would be spared of saving, but save completed pages underneath,
            // and those may appear as dead (non-reachable).
            // That's why it is important to preserve all chunks
            // created in the process, especially if retention time
            // is set to a lower value, or even 0.
            Parallel.ForEach(source.GetMapNames(),
                new ParallelOptions { MaxDegreeOfParallelism = poolSize },
                mapName =>
                {
                    MVMap<object?, object?>.Builder<object?, object?> mp = CreateGenericMapBuilder(mapName);
                    MVMap<object?, object?> sourceMap = source.OpenMap(mapName, mp);
                    MVMap<object?, object?> targetMap = target.OpenMap(mapName, mp);
                    targetMap.CopyFrom(sourceMap);
                    targetMeta.Put(MVMap<object?, object?>.GetMapKey(targetMap.GetId()),
                        sourceMeta.Get(MVMap<object?, object?>.GetMapKey(sourceMap.GetId()))!);
                });

            // this will end hacky mode of operation with incomplete pages
            // end ensure that all pages are saved
            target.Commit();
        }
        finally
        {
            target.SetAutoCommitDelay(autoCommitDelay);
            target.SetReuseSpace(reuseSpace);
        }
    }

    private static MVMap<object?, object?>.Builder<object?, object?> CreateGenericMapBuilder(string mapName)
    {
        MVMap<object?, object?>.Builder<object?, object?> mp = MVStoreTool.GetGenericMapBuilder();
        // This is a hack to preserve chunks occupancy rate accounting.
        // It exposes design deficiency flaw in MVStore related to lack of
        // map's type metadata.
        // TODO: Introduce type metadata which will allow to open any store
        // TODO: without prior knowledge of keys / values types and map implementation
        // TODO: (MVMap vs MVRTreeMap, regular vs. singleWriter etc.)
        if (mapName.StartsWith(UndoLogNamePrefix, StringComparison.Ordinal))
        {
            mp.SingleWriter();
        }

        return mp;
    }

    private void ScrubMetaMap()
    {
        var keysToRemove = new HashSet<string>();

        // ensure that there is only one name mapped to each id
        // this could be a leftover of an unfinished map rename
        IEnumerator<string> it = meta.KeyIterator(DataUtils.MetaName);
        while (it.MoveNext())
        {
            string key = it.Current;
            if (!key.StartsWith(DataUtils.MetaName, StringComparison.Ordinal))
            {
                break;
            }

            string mapName = key.Substring(DataUtils.MetaName.Length);
            int mapId = DataUtils.ParseHexInt(meta.Get(key)!);
            string? realMapName = GetMapName(mapId);
            if (!mapName.Equals(realMapName, StringComparison.Ordinal))
            {
                keysToRemove.Add(key);
            }
        }

        foreach (string key in keysToRemove)
        {
            meta.Remove(key);
            MarkMetaChanged();
        }

        it = meta.KeyIterator(DataUtils.MetaMap);
        while (it.MoveNext())
        {
            string key = it.Current;
            if (!key.StartsWith(DataUtils.MetaMap, StringComparison.Ordinal))
            {
                break;
            }

            string mapName = DataUtils.GetMapName(meta.Get(key)!)!;
            string mapIdStr = key.Substring(DataUtils.MetaMap.Length);
            // ensure that last map id is not smaller than max of any existing map ids
            AdjustLastMapId(DataUtils.ParseHexInt(mapIdStr));
            // each map should have a proper name
            if (!mapIdStr.Equals(meta.Get(DataUtils.MetaName + mapName), StringComparison.Ordinal))
            {
                meta.Put(DataUtils.MetaName + mapName, mapIdStr);
                MarkMetaChanged();
            }
        }
    }

    private void UnlockAndCheckPanicCondition()
    {
        storeLock.ExitWriteLock();
        MvStoreException? exception = GetPanicException();
        if (exception is not null && !Utils.BackgroundExecutor.IsBackgroundThread())
        {
            HandleException(exception);
            if (IsOpen())
            {
                CloseImmediately();
            }

            throw exception;
        }
    }

    /// <summary>
    /// Panics with a fresh internal error.
    /// </summary>
    public void Panic(Exception e)
        => Panic(DataUtils.NewMvStoreException(DataUtils.ErrorInternal, "{0}", e.ToString(), e));

    /// <summary>
    /// Panics: the store closes and every guarded operation throws this from now on.
    /// </summary>
    [DoesNotReturn]
    public void Panic(MvStoreException exception)
    {
        if (Interlocked.CompareExchange(ref panicException, exception, null) is null)
        {
            if (IsOpen())
            {
                HandleException(exception);
                if (!Utils.BackgroundExecutor.IsBackgroundThread())
                {
                    CloseImmediately();
                }
            }
        }

        throw exception;
    }

    /// <summary>
    /// The panic that closed the store, if any.
    /// </summary>
    public MvStoreException? GetPanicException() => Volatile.Read(ref panicException);

    /// <summary>
    /// Opens a persistent store in a file.
    /// </summary>
    public static MVStore Open(string fileName)
    {
        var config = new Dictionary<string, object?>
        {
            ["fileName"] = fileName,
        };
        return new MVStore(config);
    }

    /// <summary>
    /// Opens a map by name, creating it when it is not there.
    /// </summary>
    public MVMap<K, V> OpenMap<K, V>(string name) => OpenMap(name, new MVMap<K, V>.Builder<K, V>());

    /// <summary>
    /// Opens a map by name with a builder, creating it when it is not there.
    /// </summary>
    public M OpenMap<M, K, V>(string name, MVMap<K, V>.IMapBuilder<M, K, V> builder)
        where M : MVMap<K, V>
    {
        int id = GetMapId(name);
        if (id >= 0)
        {
            M? map = GetMap<K, V>(id) as M;
            if (map is null)
            {
                map = OpenMap(id, builder);
            }

            Debug.Assert(builder.GetKeyType() is null
                || map.GetKeyType().GetType() == builder.GetKeyType()!.GetType());
            Debug.Assert(builder.GetValueType() is null
                || map.GetValueType().GetType() == builder.GetValueType()!.GetType());
            return map;
        }

        var c = new Dictionary<string, object?>();
        id = GetNextMapId();
        Debug.Assert(GetMap<K, V>(id) is null);
        c["id"] = id;
        long curVersion = Volatile.Read(ref currentVersion);
        c["createVersion"] = curVersion;
        M created = builder.Create(this, c);
        string x = id.ToString("x", CultureInfo.InvariantCulture);
        meta.Put(MVMap<K, V>.GetMapKey(id), created.AsString(name));
        string? existing = meta.PutIfAbsent(DataUtils.MetaName + name, x);
        if (existing is not null)
        {
            // looks like map was created concurrently, cleanup and re-start
            meta.Remove(MVMap<K, V>.GetMapKey(id));
            return OpenMap(name, builder);
        }

        created.SetRootPos(0, curVersion);
        MarkMetaChanged();
        IAnyMap raced = maps.GetOrAdd(id, created);
        if (!ReferenceEquals(raced, created))
        {
            created = (M)(object)raced;
        }

        return created;
    }

    /// <summary>
    /// Opens a map by id, creating it when it is not there.
    /// </summary>
    public M OpenMap<M, K, V>(int id, MVMap<K, V>.IMapBuilder<M, K, V> builder)
        where M : MVMap<K, V>
    {
        M? map;
        while ((map = (M)(object)GetMap<K, V>(id)!) is null)
        {
            string? configAsString = meta.Get(MVMap<K, V>.GetMapKey(id));
            DataUtils.CheckArgument(configAsString is not null, "Missing map with id {0}", id);
            var config = new Dictionary<string, object?>();
            foreach (KeyValuePair<string, string> entry in DataUtils.ParseMap(configAsString!))
            {
                config[entry.Key] = entry.Value;
            }

            config["id"] = id;
            map = builder.Create(this, config);
            long root = GetRootPos(id);
            map.SetRootPos(root, Volatile.Read(ref currentVersion));
            if (maps.TryAdd(id, map))
            {
                break;
            }
            // looks like map has been concurrently created already, re-start
        }

        return map;
    }

    /// <summary>
    /// A map by id, or null when it is not open.
    /// </summary>
    public MVMap<K, V>? GetMap<K, V>(int id)
    {
        CheckNotClosed();
        return maps.TryGetValue(id, out IAnyMap? map) ? (MVMap<K, V>)map : null;
    }

    /// <summary>
    /// A map by id without knowing its key and value types, or null when it is not open.
    /// The file's compaction pass uses this: it only rewrites pages, never reads keys.
    /// </summary>
    internal IAnyMap? GetAnyMap(int id)
    {
        CheckNotClosed();
        return maps.TryGetValue(id, out IAnyMap? map) ? map : null;
    }

    /// <summary>
    /// The names of all maps.
    /// </summary>
    public ISet<string> GetMapNames()
    {
        var set = new HashSet<string>();
        CheckNotClosed();
        IEnumerator<string> it = meta.KeyIterator(DataUtils.MetaName);
        while (it.MoveNext())
        {
            string x = it.Current;
            if (!x.StartsWith(DataUtils.MetaName, StringComparison.Ordinal))
            {
                break;
            }

            set.Add(x.Substring(DataUtils.MetaName.Length));
        }

        return set;
    }

    /// <summary>
    /// The layout, or null for an in-memory store.
    /// </summary>
    public IDictionary<string, string>? GetLayoutMap() => fileStore?.GetLayoutMap();

    private bool IsRegularMap(IAnyMap map)
        => !ReferenceEquals(map, meta) && (fileStore is null || fileStore.IsRegularMap(map));

    /// <summary>
    /// The meta map.
    /// </summary>
    public MVMap<string, string> GetMetaMap()
    {
        CheckNotClosed();
        return meta;
    }

    /// <summary>
    /// Whether a map with a name exists.
    /// </summary>
    public bool HasMap(string name) => meta.ContainsKey(DataUtils.MetaName + name);

    /// <summary>
    /// Whether a map exists and holds data.
    /// </summary>
    public bool HasData(string name) => HasMap(name) && GetRootPos(GetMapId(name)) != 0;

    /// <summary>
    /// Marks the metadata dirty. Changes in the metadata alone are usually not detected, as the
    /// meta map is changed after storing.
    /// </summary>
    internal void MarkMetaChanged() => metaChanged = true;

    /// <summary>
    /// The last map id handed out.
    /// </summary>
    internal int GetLastMapId() => Volatile.Read(ref lastMapId);

    private int GetNextMapId() => Interlocked.Increment(ref lastMapId);

    /// <summary>
    /// Raises the last map id past anything already stored.
    /// </summary>
    internal void AdjustLastMapId(int mapId)
    {
        if (mapId > Volatile.Read(ref lastMapId))
        {
            Volatile.Write(ref lastMapId, mapId);
        }
    }

    /// <summary>
    /// Sets the last map id.
    /// </summary>
    internal void ResetLastMapId(int mapId) => Volatile.Write(ref lastMapId, mapId);

    /// <inheritdoc/>
    public void Close() => CloseStore(true, 0);

    /// <summary>
    /// Closes the store, compacting first for up to so long.
    /// </summary>
    public void Close(int allowedCompactionTime)
    {
        if (!IsClosed())
        {
            CloseStore(true, allowedCompactionTime);
        }
    }

    /// <summary>
    /// Closes the store at once, without storing.
    /// </summary>
    public void CloseImmediately() => CloseStore(false, 0);

    /// <inheritdoc/>
    public void Dispose() => Close();

    private void CloseStore(bool normalShutdown, int allowedCompactionTime)
    {
        // If any other thread have already initiated closure procedure,
        // isClosed() would wait until closure is done, and then we jump out of the loop.
        // This is a subtle difference between !isClosed() and isOpen().
        while (!IsClosed())
        {
            SetAutoCommitDelay(normalShutdown ? -1 : 0);
            SetOldestVersionTracker(null);
            storeLock.EnterWriteLock();
            try
            {
                Debug.Assert(state == StateOpen, state.ToString(CultureInfo.InvariantCulture));
                state = StateStopping;
                Volatile.Write(ref closingThreadId, Environment.CurrentManagedThreadId);

                try
                {
                    bool compactFully = false;
                    try
                    {
                        if (normalShutdown && fileStore is not null && !fileStore.IsReadOnly())
                        {
                            compactFully = allowedCompactionTime == -1 && fileStoreShallBeClosed;
                            foreach (IAnyMap map in maps.Values)
                            {
                                if (map.MapIsClosed)
                                {
                                    fileStore.DeregisterMapRoot(map.MapId);
                                }
                            }

                            Commit();
                            SetRetentionTime(0);
                            fileStore.Stop(compactFully ? 0 : allowedCompactionTime);
                            Debug.Assert(oldestVersionToKeep.Get() == currentVersion,
                                oldestVersionToKeep.Get().ToString(CultureInfo.InvariantCulture) + " != "
                                + currentVersion.ToString(CultureInfo.InvariantCulture));
                        }

                        ((IAnyMap)meta).MapClose();
                        foreach (IAnyMap m in maps.Values.ToList())
                        {
                            m.MapClose();
                        }

                        maps.Clear();

                        if (compactFully)
                        {
                            string? fileName = fileStore!.GetFileName();
                            if (fileName is not null && FileUtils.Exists(fileName))
                            {
                                // the file could have been deleted concurrently,
                                // so only compact if the file still exists
                                Compact(fileName, true, fileStore);
                                // fileStore has been closed within the call above
                            }
                        }
                    }
                    finally
                    {
                        if (fileStore is not null && fileStoreShallBeClosed)
                        {
                            fileStore.Close();
                        }
                    }
                }
                finally
                {
                    state = StateClosed;
                }
            }
            finally
            {
                storeLock.ExitWriteLock();
            }
        }
    }

    /// <summary>
    /// Whether anything is persistent. An in-memory store never is.
    /// </summary>
    public bool IsPersistent() => fileStore is not null;

    /// <summary>
    /// Stores when needed, without waiting for the lock.
    /// </summary>
    public long TryCommit() => TryCommit(x => true);

    private long TryCommit(Func<MVStore, bool> check)
    {
        if (CanStartStoreOperation() && storeLock.TryEnterWriteLock(0))
        {
            try
            {
                if (check(this))
                {
                    return Store(false);
                }
            }
            finally
            {
                UnlockAndCheckPanicCondition();
            }
        }

        return InitialVersion;
    }

    /// <summary>
    /// Stores, waiting for the lock.
    /// </summary>
    public long Commit() => Commit(x => true);

    private long Commit(Func<MVStore, bool> check)
    {
        if (CanStartStoreOperation())
        {
            long versionAtStart = Volatile.Read(ref currentVersion);
            storeLock.EnterWriteLock();
            try
            {
                if (currentVersion == versionAtStart && check(this))
                {
                    return Store(true);
                }
            }
            finally
            {
                UnlockAndCheckPanicCondition();
            }
        }

        return InitialVersion;
    }

    private bool CanStartStoreOperation()
    {
        // we need to prevent re-entrance, which may be possible,
        // because meta map is modified within storeNow() and that
        // causes beforeWrite() call with possibility of going back here
        return !IsLockedByCurrentThread() || Volatile.Read(ref storeOperationInProgress) == 0;
    }

    private long Store(bool syncWrite)
    {
        Debug.Assert(IsLockedByCurrentThread());
        if (IsOpenOrStopping() && HasUnsavedChanges()
            && Interlocked.CompareExchange(ref storeOperationInProgress, 1, 0) == 0)
        {
            try
            {
                // it is ok, since that path suppose to be single-threaded under storeLock
                long result = ++currentVersion;
                if (fileStore is null)
                {
                    SetWriteVersion(result);
                }
                else
                {
                    if (fileStore.IsReadOnly())
                    {
                        throw DataUtils.NewMvStoreException(DataUtils.ErrorWritingFailed,
                            "This store is read-only");
                    }

                    fileStore.DropUnusedChunks();
                    StoreNow(syncWrite, result);
                }

                return result;
            }
            finally
            {
                Volatile.Write(ref storeOperationInProgress, 0);
            }
        }

        return InitialVersion;
    }

    private void SetWriteVersion(long version)
    {
        foreach (var entry in maps)
        {
            IAnyMap map = entry.Value;
            Debug.Assert(IsRegularMap(map));
            if (map.MapSetWriteVersion(version) is null)
            {
                maps.TryRemove(entry.Key, out _);
            }
        }

        meta.SetWriteVersion(version);
        OnVersionChange(version);
    }

    /// <summary>
    /// Stores at once, in a new version.
    /// </summary>
    internal void StoreNow()
    {
        // it is ok, since that path suppose to be single-threaded under storeLock
        StoreNow(true, ++currentVersion);
    }

    private void StoreNow(bool syncWrite, long version)
    {
        try
        {
            int currentUnsavedMemory = unsavedMemory;
            Debug.Assert(IsLockedByCurrentThread());
            fileStore!.StoreIt(CollectChangedMapRoots(version), version, syncWrite);
            // some pages might have been changed in the meantime (in the newest
            // version)
            saveNeeded = false;
            unsavedMemory = Math.Max(0, unsavedMemory - currentUnsavedMemory);
        }
        catch (MvStoreException e)
        {
            Panic(e);
        }
        catch (Exception e)
        {
            Panic(e);
        }
    }

    private List<object> CollectChangedMapRoots(long version)
    {
        long lastStoredVersion = version - 2;
        var changed = new List<object>();
        foreach (var entry in maps)
        {
            IAnyMap map = entry.Value;
            IRootView? rootReference = map.MapSetWriteVersion(version);
            if (rootReference is null)
            {
                maps.TryRemove(entry.Key, out _);
            }
            else if (map.MapCreateVersion < version
                && !map.MapIsVolatile
                && map.MapHasChangesSince(lastStoredVersion))
            {
                Debug.Assert(rootReference.Version <= version,
                    rootReference.Version.ToString(CultureInfo.InvariantCulture) + " > "
                    + version.ToString(CultureInfo.InvariantCulture));
                // simply checking rootPage.isSaved() won't work here because
                // after deletion previously saved page
                // may pop up as a root, but we still need
                // to save new root pos in meta
                changed.Add(rootReference.RootPage);
            }
        }

        IRootView? metaRoot = meta.SetWriteVersion(version);
        if (meta.HasChangesSince(lastStoredVersion) || metaChanged)
        {
            Debug.Assert(metaRoot is not null && metaRoot.Version <= version,
                metaRoot is null ? "null" : metaRoot.Version.ToString(CultureInfo.InvariantCulture) + " > "
                + version.ToString(CultureInfo.InvariantCulture));
            changed.Add(metaRoot!.RootPage);
        }

        return changed;
    }

    /// <summary>
    /// The absolute time, never running backwards.
    /// </summary>
    public long GetTimeAbsolute()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (lastTimeAbsolute != 0 && now < lastTimeAbsolute)
        {
            // time seems to have run backwards - this can happen
            // when the system time is adjusted, for example
            // on a leap second
            now = lastTimeAbsolute;
        }
        else
        {
            lastTimeAbsolute = now;
        }

        return now;
    }

    /// <summary>
    /// Whether anything waits to be stored.
    /// </summary>
    public bool HasUnsavedChanges()
    {
        if (metaChanged)
        {
            return true;
        }

        long lastStoredVersion = Volatile.Read(ref currentVersion) - 1;
        foreach (IAnyMap m in maps.Values)
        {
            if (!m.MapIsClosed && m.MapHasChangesSince(lastStoredVersion))
            {
                return true;
            }
        }

        return fileStore is not null && fileStore.HasChangesSince(lastStoredVersion);
    }

    /// <summary>
    /// Runs an operation under the store lock, panicking on failure.
    /// </summary>
    public void ExecuteFilestoreOperation(Action operation)
    {
        storeLock.EnterWriteLock();
        try
        {
            CheckNotClosed();
            fileStore!.ExecuteFileStoreOperation(operation);
        }
        catch (MvStoreException e)
        {
            Panic(e);
        }
        catch (Exception e)
        {
            Panic(e);
        }
        finally
        {
            UnlockAndCheckPanicCondition();
        }
    }

    /// <summary>
    /// Runs an operation under the store lock when it frees up within ten milliseconds.
    /// </summary>
    internal R? TryExecuteUnderStoreLock<R>(Func<R> operation)
    {
        R? result = default;
        if (storeLock.TryEnterWriteLock(10))
        {
            try
            {
                result = operation();
            }
            catch (MvStoreException e)
            {
                Panic(e);
            }
            catch (Exception e)
            {
                Panic(e);
            }
            finally
            {
                UnlockAndCheckPanicCondition();
            }
        }

        return result;
    }

    /// <summary>
    /// Syncs the file, if any.
    /// </summary>
    public void Sync()
    {
        CheckOpen();
        fileStore?.Sync();
    }

    /// <summary>
    /// Compacts the file, for at most so long.
    /// </summary>
    public void CompactFile(int maxCompactTime)
    {
        if (fileStore is not null)
        {
            SetRetentionTime(0);
            storeLock.EnterWriteLock();
            try
            {
                fileStore.CompactStore(maxCompactTime);
            }
            finally
            {
                UnlockAndCheckPanicCondition();
            }
        }
    }

    /// <summary>
    /// Compacts the file.
    /// </summary>
    public bool Compact(int targetFillRate, int write)
    {
        CheckOpen();
        return fileStore is not null && fileStore.Compact(targetFillRate, write);
    }

    /// <summary>
    /// How full the file is, in percent.
    /// </summary>
    public int GetFillRate() => fileStore!.GetFillRate();

    /// <summary>
    /// Reads a page back from the file.
    /// </summary>
    internal Page<K, V> ReadPage<K, V>(MVMap<K, V> map, long pos)
    {
        CheckNotClosed();
        return fileStore!.ReadPage(map, pos);
    }

    /// <summary>
    /// Accounts for a removed page.
    /// </summary>
    internal void AccountForRemovedPage(long pos, long version, bool pinned, int pageNo)
        => fileStore!.AccountForRemovedPage(pos, version, pinned, pageNo);

    /// <summary>
    /// The fast compressor, made on first use.
    /// </summary>
    internal Compressor GetCompressorFast() => compressorFast ??= new CompressLZF();

    /// <summary>
    /// The thorough compressor, made on first use.
    /// </summary>
    internal Compressor GetCompressorHigh() => compressorHigh ??= new CompressDeflate();

    /// <summary>
    /// The compression level: 0 for none, 1 for fast, 2 for thorough.
    /// </summary>
    internal int GetCompressionLevel() => compressionLevel;

    /// <summary>
    /// How many keys a page holds before it splits.
    /// </summary>
    public int GetKeysPerPage() => keysPerPage;

    /// <summary>
    /// The largest page that may be written.
    /// </summary>
    public long GetMaxPageSize() => fileStore is null ? long.MaxValue : fileStore.GetMaxPageSize();

    /// <summary>
    /// The cache size, in kilobytes.
    /// </summary>
    public int GetCacheSize() => fileStore is null ? 0 : fileStore.GetCacheSize();

    /// <summary>
    /// The cache size in use, in kilobytes.
    /// </summary>
    public int GetCacheSizeUsed() => fileStore is null ? 0 : fileStore.GetCacheSizeUsed();

    /// <summary>
    /// Sets the cache size, in kilobytes.
    /// </summary>
    public void SetCacheSize(int kb)
    {
        if (fileStore is not null)
        {
            fileStore.SetCacheSize(Math.Max(1, kb / 1024));
        }
    }

    /// <summary>
    /// Whether freed space is reused.
    /// </summary>
    public bool IsSpaceReused() => fileStore!.IsSpaceReused();

    /// <summary>
    /// Sets whether freed space is reused.
    /// </summary>
    public void SetReuseSpace(bool reuseSpace) => fileStore!.SetReuseSpace(reuseSpace);

    /// <summary>
    /// How long unused versions are kept, in milliseconds.
    /// </summary>
    public int GetRetentionTime() => fileStore is null ? 0 : fileStore.GetRetentionTime();

    /// <summary>
    /// Sets how long unused versions are kept.
    /// </summary>
    public void SetRetentionTime(int ms)
    {
        if (fileStore is not null)
        {
            fileStore.SetRetentionTime(ms);
        }
    }

    /// <summary>
    /// Whether old versions are kept at all.
    /// </summary>
    public bool IsVersioningRequired() => fileStore is not null && !fileStore.IsReadOnly() || versionsToKeep > 0;

    /// <summary>
    /// Sets how many versions to keep.
    /// </summary>
    public void SetVersionsToKeep(int count) => versionsToKeep = count;

    /// <summary>
    /// How many versions to keep.
    /// </summary>
    public int GetVersionsToKeep() => versionsToKeep;

    /// <summary>
    /// The oldest version anyone still needs.
    /// </summary>
    internal long GetOldestVersionToKeep()
        => Math.Min(oldestVersionToKeep.Get(),
            Math.Max(Volatile.Read(ref currentVersion) - versionsToKeep, InitialVersion));

    private void SetOldestVersionToKeep(long version)
    {
        bool success;
        do
        {
            long current = oldestVersionToKeep.Get();
            // Oldest version may only advance, never goes back
            success = version <= current || oldestVersionToKeep.CompareAndSet(current, version);
        } while (!success);

        Debug.Assert(version <= Volatile.Read(ref currentVersion),
            version.ToString(CultureInfo.InvariantCulture) + " <= "
            + Volatile.Read(ref currentVersion).ToString(CultureInfo.InvariantCulture));
        Action<long>? versionTracker = oldestVersionTracker;
        versionTracker?.Invoke(version);
    }

    /// <summary>
    /// Calls back with the oldest version to keep whenever it advances.
    /// </summary>
    public void SetOldestVersionTracker(Action<long>? callback) => oldestVersionTracker = callback;

    private bool IsKnownVersion(long version)
    {
        long curVersion = GetCurrentVersion();
        if (version > curVersion || version < 0)
        {
            return false;
        }

        if (version == curVersion)
        {
            // no stored data
            return true;
        }

        return fileStore is null || fileStore.IsKnownVersion(version);
    }

    /// <summary>
    /// Accounts for changed memory, asking for a store when it piles up.
    /// </summary>
    /// <remarks>
    /// The counter is deliberately racy, as in the reference: updates are aggregated upstream,
    /// and an atomic counter measured slower.
    /// </remarks>
    public void RegisterUnsavedMemory(int memory)
    {
        Debug.Assert(fileStore is not null);
        // this counter was intentionally left unprotected against race
        // condition for performance reasons
        // TODO: evaluate performance impact of atomic implementation,
        //       since updates to unsavedMemory are largely aggregated now
        unsavedMemory += memory;
        if (NeedStore())
        {
            saveNeeded = true;
        }
    }

    /// <summary>
    /// Accounts for changed memory, storing at once when it is due.
    /// </summary>
    internal void RegisterUnsavedMemoryAndCommitIfNeeded(int memory)
    {
        RegisterUnsavedMemory(memory);
        if (saveNeeded)
        {
            Commit();
        }
    }

    /// <summary>
    /// Stores when due, unless that would deadlock against a held root or recurse into a store.
    /// </summary>
    internal void BeforeWrite(IAnyMap map)
    {
        if (saveNeeded && IsOpenOrStopping()
            // condition below is to prevent potential deadlock,
            // because we should never seek storeLock while holding
            // map root lock
            && (IsLockedByCurrentThread() || !map.MapRootIsLockedByCurrentThread())
            // to avoid infinite recursion via store() -> dropUnusedChunks() -> layout.remove()
            && fileStore!.IsRegularMap(map))
        {
            saveNeeded = false;
            // check again, because it could have been written by now
            if (NeedStore())
            {
                // if unsaved memory creation rate is too high,
                // some back pressure need to be applied
                // to slow things down and avoid OOME
                if (RequireStore() && !map.MapIsSingleWriter)
                {
                    Commit(s => s.RequireStore());
                }
                else
                {
                    TryCommit(s => s.NeedStore());
                }
            }
        }
    }

    private bool RequireStore() => 3 * unsavedMemory > 4 * autoCommitMemory;

    private bool NeedStore() => autoCommitMemory > 0 && fileStore!.ShouldSaveNow(unsavedMemory, autoCommitMemory);

    /// <summary>
    /// The store version from the metadata, or zero.
    /// </summary>
    public int GetStoreVersion()
    {
        CheckOpen();
        string? x = meta.Get("setting.storeVersion");
        return x is null ? 0 : DataUtils.ParseHexInt(x);
    }

    /// <summary>
    /// Sets the store version in the metadata.
    /// </summary>
    public void SetStoreVersion(int version)
    {
        storeLock.EnterWriteLock();
        try
        {
            CheckOpen();
            MarkMetaChanged();
            meta.Put("setting.storeVersion", version.ToString("x", CultureInfo.InvariantCulture));
        }
        finally
        {
            storeLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Rolls back to the current version, dropping everything after it.
    /// </summary>
    public void Rollback() => RollbackTo(Volatile.Read(ref currentVersion));

    /// <summary>
    /// Rolls the store back to a version.
    /// </summary>
    public void RollbackTo(long version)
    {
        storeLock.EnterWriteLock();
        try
        {
            currentVersion = version;
            CheckOpen();
            DataUtils.CheckArgument(IsKnownVersion(version), "Unknown version {0}", version);

            TxCounter? txCounter;
            while ((txCounter = versions.Last?.Value) is not null && txCounter.version >= version)
            {
                versions.RemoveLast();
            }

            currentTxCounter = new TxCounter(version);
            if (oldestVersionToKeep.Get() > version)
            {
                oldestVersionToKeep.Set(version);
            }

            fileStore?.RollbackTo(version);
            if (!meta.RollbackRoot(version))
            {
                meta.SetRootPos(GetRootPos(meta.GetId()), version - 1);
            }

            metaChanged = false;

            foreach (IAnyMap m in new List<IAnyMap>(maps.Values))
            {
                int id = m.MapId;
                if (m.MapCreateVersion >= version)
                {
                    m.MapClose();
                    maps.TryRemove(id, out _);
                }
                else if (!m.MapRollbackRoot(version))
                {
                    m.MapSetRootPos(GetRootPos(id), version);
                }
            }

            OnVersionChange(currentVersion);
            Debug.Assert(!HasUnsavedChanges());
        }
        finally
        {
            UnlockAndCheckPanicCondition();
        }
    }

    private long GetRootPos(int mapId) => fileStore is null ? 0 : fileStore.GetRootPos(mapId);

    /// <summary>
    /// The current version.
    /// </summary>
    public long GetCurrentVersion() => Volatile.Read(ref currentVersion);

    /// <summary>
    /// Sets the current version.
    /// </summary>
    internal void SetCurrentVersion(long curVersion) => Volatile.Write(ref currentVersion, curVersion);

    /// <summary>
    /// The file, or null for an in-memory store.
    /// </summary>
    public IFileStore? GetFileStore() => fileStore;

    /// <summary>
    /// The store header.
    /// </summary>
    public IDictionary<string, object?> GetStoreHeader() => fileStore!.GetStoreHeader();

    private void CheckOpen()
    {
        if (!IsOpen())
        {
            throw DataUtils.NewMvStoreException(DataUtils.ErrorClosed, "This store is closed", GetPanicException());
        }
    }

    private void CheckNotClosed()
    {
        if (!IsOpenOrStopping())
        {
            throw DataUtils.NewMvStoreException(DataUtils.ErrorClosed, "This store is closed", GetPanicException());
        }
    }

    /// <summary>
    /// Renames a map.
    /// </summary>
    public void RenameMap(IAnyMap map, string newName)
    {
        CheckOpen();
        DataUtils.CheckArgument(IsRegularMap(map), "Renaming the meta map is not allowed");
        int id = map.MapId;
        string? oldName = GetMapName(id);
        if (oldName is not null && !oldName.Equals(newName, StringComparison.Ordinal))
        {
            string idHexStr = id.ToString("x", CultureInfo.InvariantCulture);
            // at first create a new name as an "alias"
            string? existingIdHexStr = meta.PutIfAbsent(DataUtils.MetaName + newName, idHexStr);
            // we need to cope with the case of previously unfinished rename
            DataUtils.CheckArgument(existingIdHexStr is null || existingIdHexStr.Equals(idHexStr,
                StringComparison.Ordinal), "A map named {0} already exists", newName);
            // switch roles of a new and old names - old one is an alias now
            meta.Put(MVMap<string, string>.GetMapKey(id), map.MapAsString(newName));
            // get rid of the old name completely
            meta.Remove(DataUtils.MetaName + oldName);
            MarkMetaChanged();
        }
    }

    /// <summary>
    /// Removes a map.
    /// </summary>
    public void RemoveMap<K, V>(MVMap<K, V> map) => RemoveMap((IAnyMap)map);

    /// <summary>
    /// Removes a map.
    /// </summary>
    public void RemoveMap(IAnyMap map)
    {
        storeLock.EnterWriteLock();
        try
        {
            CheckOpen();
            DataUtils.CheckArgument(IsRegularMap(map), "Removing the meta map is not allowed");
            (long uc, long uac) = map.MapClearIt();
            map.MapClose();

            updateCounter += uc;
            updateAttemptCounter += uac;

            int id = map.MapId;
            string? name = GetMapName(id);
            if (meta.Remove(MVMap<string, string>.GetMapKey(id)) is not null)
            {
                MarkMetaChanged();
            }

            if (meta.Remove(DataUtils.MetaName + name) is not null)
            {
                MarkMetaChanged();
            }

            // normally actual map removal is delayed, up until this current version go out os scope,
            // but for in-memory case, when versions rolling is turned off, do it now
            if (!IsVersioningRequired())
            {
                maps.TryRemove(id, out _);
            }
        }
        finally
        {
            storeLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Forgets a map's root in the file.
    /// </summary>
    internal void DeregisterMapRoot(int mapId)
    {
        if (fileStore is not null && fileStore.DeregisterMapRoot(mapId))
        {
            MarkMetaChanged();
        }
    }

    /// <summary>
    /// Removes a map by name.
    /// </summary>
    public void RemoveMap(string name)
    {
        int id = GetMapId(name);
        if (id > 0)
        {
            IAnyMap? map = maps.TryGetValue(id, out IAnyMap? found) ? found : null;
            map ??= OpenMap(name, CreateGenericMapBuilder(name));
            RemoveMap(map);
        }
    }

    /// <summary>
    /// The name of a map, or null.
    /// </summary>
    public string? GetMapName(int id)
    {
        string? m = meta.Get(MVMap<string, string>.GetMapKey(id));
        return m is null ? null : DataUtils.GetMapName(m);
    }

    private int GetMapId(string name)
    {
        string? m = meta.Get(DataUtils.MetaName + name);
        return m is null ? -1 : DataUtils.ParseHexInt(m);
    }

    /// <summary>
    /// Describes the store and the file.
    /// </summary>
    public void PopulateInfo(Action<string, string> consumer)
    {
        consumer("info.UPDATE_FAILURE_PERCENT",
            (100 * GetUpdateFailureRatio()).ToString("F2", CultureInfo.InvariantCulture) + "%");
        consumer("info.LEAF_RATIO", GetLeafRatio().ToString(CultureInfo.InvariantCulture));

        if (IsVersioningRequired())
        {
            consumer("info.VERSIONS_TO_KEEP", GetVersionsToKeep().ToString(CultureInfo.InvariantCulture));
            consumer("info.OLDEST_VERS_TO_KEEP", GetOldestVersionToKeep().ToString(CultureInfo.InvariantCulture));
            consumer("info.CURRENT_VERSION", GetCurrentVersion().ToString(CultureInfo.InvariantCulture));
        }

        fileStore?.PopulateInfo(consumer);
    }

    /// <summary>
    /// Reports an exception to the background handler, answering whether there is one.
    /// </summary>
    internal bool HandleException(Exception ex)
    {
        Action<Thread, Exception>? handler = BackgroundExceptionHandler;
        if (handler is not null)
        {
            try
            {
                handler(Thread.CurrentThread, ex);
            }
            catch (Exception e)
            {
                if (!ReferenceEquals(ex, e))
                {
                    ex.Data["Suppressed"] = e;
                }
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the store is open.
    /// </summary>
    internal bool IsOpen() => state == StateOpen;

    /// <summary>
    /// Whether the store is closed, waiting for an ongoing closure when asked from another thread.
    /// </summary>
    public bool IsClosed()
    {
        if (IsOpen())
        {
            return false;
        }

        if (Volatile.Read(ref closingThreadId) != Environment.CurrentManagedThreadId)
        {
            int millis = 1;
            while (state != StateClosed)
            {
                try
                {
                    Thread.Sleep(millis++);
                }
                catch (ThreadInterruptedException)
                {
                    Thread.CurrentThread.Interrupt();
                    break;
                }
            }
        }

        return true;
    }

    private bool IsOpenOrStopping() => state <= StateStopping;

    /// <summary>
    /// Sets the background write delay. Negative stops the thread, zero stores without waiting.
    /// </summary>
    public void SetAutoCommitDelay(int millis) => fileStore?.SetAutoCommitDelay(millis);

    /// <summary>
    /// The background write delay, in milliseconds.
    /// </summary>
    public int GetAutoCommitDelay() => fileStore is null ? 0 : fileStore.GetAutoCommitDelay();

    /// <summary>
    /// How much unsaved memory triggers a store.
    /// </summary>
    public int GetAutoCommitMemory() => autoCommitMemory;

    /// <summary>
    /// How much memory waits to be stored.
    /// </summary>
    public int GetUnsavedMemory() => unsavedMemory;

    /// <summary>
    /// Whether the file is read-only.
    /// </summary>
    public bool IsReadOnly() => fileStore is not null && fileStore.IsReadOnly();

    private int GetLeafRatio() => (int)(leafCount * 100 / Math.Max(1, leafCount + nonLeafCount));

    private double GetUpdateFailureRatio()
    {
        long uc = updateCounter;
        long uac = updateAttemptCounter;
        RootReference<string, string> rootReference = meta.GetRoot();
        uc += rootReference.updateCounter;
        uac += rootReference.updateAttemptCounter;
        foreach (IAnyMap map in maps.Values)
        {
            (long muc, long muac) = map.MapGetRootCounters();
            uc += muc;
            uac += muac;
        }

        return uac == 0 ? 0 : 1 - (double)uc / uac;
    }

    /// <summary>
    /// Holds a version while it is in use.
    /// </summary>
    public TxCounter RegisterVersionUsage()
    {
        TxCounter txCounter;
        while (true)
        {
            txCounter = currentTxCounter;
            if (txCounter.IncrementAndGet() > 0)
            {
                return txCounter;
            }

            // The only way for counter to be negative
            // if it was retrieved right before onVersionChange()
            // and now onVersionChange() is done.
            // This version is eligible for reclamation now
            // and should not be used here, so restore count
            // not to upset accounting and try again with a new
            // version (currentTxCounter should have changed).
            Debug.Assert(!ReferenceEquals(txCounter, currentTxCounter), txCounter.ToString());
            txCounter.DecrementAndGet();
        }
    }

    /// <summary>
    /// Releases a held version, dropping what nobody holds.
    /// </summary>
    public void DeregisterVersionUsage(TxCounter txCounter)
    {
        if (DecrementVersionUsageCounter(txCounter))
        {
            if (IsLockedByCurrentThread())
            {
                DropUnusedVersions();
            }
            else if (storeLock.TryEnterWriteLock(0))
            {
                try
                {
                    DropUnusedVersions();
                }
                finally
                {
                    storeLock.ExitWriteLock();
                }
            }
        }
    }

    /// <summary>
    /// Releases one hold, answering whether nobody holds it anymore.
    /// </summary>
    public bool DecrementVersionUsageCounter(TxCounter? txCounter)
        => txCounter is not null && txCounter.DecrementAndGet() <= 0;

    /// <summary>
    /// Whether the current thread holds the store lock.
    /// </summary>
    internal bool IsLockedByCurrentThread() => storeLock.IsWriteLockHeld;

    /// <summary>
    /// Moves to a version: the old counter retires, a fresh one starts.
    /// </summary>
    internal void OnVersionChange(long version)
    {
        Debug.Assert(IsLockedByCurrentThread());
        metaChanged = false;
        TxCounter txCounter = currentTxCounter;
        Debug.Assert(txCounter.Get() >= 0);
        versions.AddLast(txCounter);
        currentTxCounter = new TxCounter(version);
        txCounter.DecrementAndGet();
        DropUnusedVersions();
    }

    private void DropUnusedVersions()
    {
        TxCounter? txCounter;
        while ((txCounter = versions.First?.Value) is not null && txCounter.Get() < 0)
        {
            versions.RemoveFirst();
        }

        long oldestToKeep = (txCounter is not null ? txCounter : currentTxCounter).version;
        SetOldestVersionToKeep(oldestToKeep);
    }

    /// <summary>
    /// Counts a new page, for the leaf ratio.
    /// </summary>
    public void CountNewPage(bool leaf)
    {
        if (leaf)
        {
            ++leafCount;
        }
        else
        {
            ++nonLeafCount;
        }
    }

    /// <summary>
    /// How many threads hold a version.
    /// </summary>
    public sealed class TxCounter
    {
        /// <summary>
        /// The version held.
        /// </summary>
        public readonly long version;

        private int counter;

        /// <summary>
        /// A counter for a version.
        /// </summary>
        internal TxCounter(long version) => this.version = version;

        /// <summary>
        /// The current count.
        /// </summary>
        internal int Get() => Volatile.Read(ref counter);

        /// <summary>
        /// Holds once more.
        /// </summary>
        internal int IncrementAndGet() => Interlocked.Increment(ref counter);

        /// <summary>
        /// Releases one hold.
        /// </summary>
        internal int DecrementAndGet() => Interlocked.Decrement(ref counter);

        /// <inheritdoc/>
        public override string ToString() => "v=" + version + " / cnt=" + Volatile.Read(ref counter);
    }

    /// <summary>
    /// Builds a store from options.
    /// </summary>
    public sealed class Builder
    {
        private readonly Dictionary<string, object?> config;

        private Builder(Dictionary<string, object?> config) => this.config = config;

        /// <summary>
        /// An empty builder.
        /// </summary>
        public Builder() => config = new Dictionary<string, object?>();

        private Builder Set(string key, object? value)
        {
            config[key] = value;
            return this;
        }

        /// <summary>
        /// Turns off the background thread. The thread only starts for a non-zero delay anyway,
        /// which is why this is a separate option rather than a delay of zero.
        /// </summary>
        public Builder AutoCommitDisabled() => Set("autoCommitDelay", 0);

        /// <summary>
        /// How much unsaved memory piles up before storing, in kilobytes.
        /// </summary>
        public Builder AutoCommitBufferSize(int kb) => Set("autoCommitBufferSize", kb);

        /// <summary>
        /// How full the file must stay, in percent.
        /// </summary>
        public Builder AutoCompactFillRate(int percent) => Set("autoCompactFillRate", percent);

        /// <summary>
        /// The file to open.
        /// </summary>
        public Builder FileName(string fileName) => Set("fileName", fileName);

        /// <summary>
        /// The password the file is encrypted with.
        /// </summary>
        public Builder EncryptionKey(char[] password) => Set("encryptionKey", password);

        /// <summary>
        /// Opens read-only.
        /// </summary>
        public Builder ReadOnly() => Set("readOnly", 1);

        /// <summary>
        /// How many keys a page holds before it splits.
        /// </summary>
        public Builder KeysPerPage(int keyCount) => Set("keysPerPage", keyCount);

        /// <summary>
        /// Opens in recovery mode.
        /// </summary>
        public Builder RecoveryMode() => Set("recoveryMode", 1);

        /// <summary>
        /// The cache size, in megabytes.
        /// </summary>
        public Builder CacheSize(int mb) => Set("cacheSize", mb);

        /// <summary>
        /// The cache concurrency.
        /// </summary>
        public Builder CacheConcurrency(int concurrency) => Set("cacheConcurrency", concurrency);

        /// <summary>
        /// Compresses pages with the fast algorithm.
        /// </summary>
        public Builder Compress() => Set("compress", 1);

        /// <summary>
        /// Compresses pages with the thorough algorithm.
        /// </summary>
        public Builder CompressHigh() => Set("compress", 2);

        /// <summary>
        /// Splits pages past this size.
        /// </summary>
        public Builder PageSplitSize(int pageSplitSize) => Set("pageSplitSize", pageSplitSize);

        /// <summary>
        /// What a background failure is reported to.
        /// </summary>
        public Builder BackgroundExceptionHandler(Action<Thread, Exception> exceptionHandler)
            => Set("backgroundExceptionHandler", exceptionHandler);

        /// <summary>
        /// Uses an open file without owning it.
        /// </summary>
        public Builder FileStore(IFileStore store) => Set("fileStore", store);

        /// <summary>
        /// Adopts an open file, closing it with the store.
        /// </summary>
        public Builder AdoptFileStore(IFileStore store)
        {
            Set("fileStoreIsAdopted", true);
            return FileStore(store);
        }

        /// <summary>
        /// Opens the store.
        /// </summary>
        public MVStore Open() => new MVStore(config);

        /// <inheritdoc/>
        public override string ToString() => DataUtils.AppendMap(new StringBuilder(), config).ToString();

        /// <summary>
        /// A builder from its string form.
        /// </summary>
        public static Builder FromString(string s)
        {
            var config = new Dictionary<string, object?>();
            foreach (KeyValuePair<string, string> entry in DataUtils.ParseMap(s))
            {
                config[entry.Key] = entry.Value;
            }

            return new Builder(config);
        }
    }
}
