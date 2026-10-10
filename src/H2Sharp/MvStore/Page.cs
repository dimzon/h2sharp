using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using H2Sharp.Compression;
using H2Sharp.Engine;
using H2Sharp.MvStore.Type;
using H2Sharp.Util;

namespace H2Sharp.MvStore;

/// <summary>
/// A page: one node of the tree that is a stored map. A leaf holds keys and values, a node holds keys
/// and the positions of its children.
/// </summary>
/// <remarks>
/// Pages are copy-on-write and never modified in place once they are reachable. Everything that
/// changes a page — inserting, removing, splitting, copying — returns a new page and leaves the old
/// one alone, which is what lets readers keep working while a writer builds the next version.
/// <para>
/// The position of a page packs where it lives: a chunk id, an offset, a length code and a type in
/// the lowest bit. Zero means "never written", and one means "removed". Those two values are
/// compared by identity all over the store, and the one thing to know about them is that a page that
/// was never written and a page that was removed are different answers to "where is it".
/// </para>
/// </remarks>
/// <typeparam name="K">the key type</typeparam>
/// <typeparam name="V">the value type</typeparam>
public abstract class Page<K, V> : IPage
{
    /// <summary>
    /// The map this page belongs to.
    /// </summary>
    public readonly MVMap<K, V> map;

    private long pos;

    /// <summary>
    /// The number of this page inside its chunk, or -1 when it has none.
    /// </summary>
    public int pageNo = -1;

    private int cachedCompare;

    private int memory;

    private int diskSpaceUsed;

    private K[] keys;

    /// <summary>
    /// How much memory a child reference costs: one pointer and two saved positions.
    /// </summary>
    internal static readonly int PageMemoryChild = Constants.MemoryPointer + 16;

    private static readonly int PageMemory =
        Constants.MemoryObject + 2 * Constants.MemoryPointer + Constants.MemoryArray + 17;

    private static readonly int PageNodeMemory =
        PageMemory + Constants.MemoryPointer + Constants.MemoryArray + 8;

    private static readonly int PageLeafMemory =
        PageMemory + Constants.MemoryPointer + Constants.MemoryArray;

    private static readonly int InMemory = int.MinValue;

    private static readonly PageReference[] SingleEmpty = [PageReference.Empty];

    /// <summary>
    /// A page over no keys.
    /// </summary>
    internal Page(MVMap<K, V> map)
        : this(map, map.KeyType.CreateStorage(0))
    {
    }

    /// <summary>
    /// A page that shares the keys of another one.
    /// </summary>
    internal Page(MVMap<K, V> map, Page<K, V> source)
        : this(map, source.keys) => memory = source.memory;

    /// <summary>
    /// A page over the given keys.
    /// </summary>
    internal Page(MVMap<K, V> map, K[] keys)
    {
        this.map = map;
        this.keys = keys;
    }

    /// <summary>
    /// An empty leaf, with storage for nothing yet.
    /// </summary>
    public static Page<K, V> CreateEmptyLeaf(MVMap<K, V> map)
        => CreateLeaf(
            map,
            map.KeyType.CreateStorage(0),
            map.ValueType.CreateStorage(0),
            PageLeafMemory);

    /// <summary>
    /// An empty node, with one empty child.
    /// </summary>
    internal static Page<K, V> CreateEmptyNode(MVMap<K, V> map)
        => CreateNode(
            map,
            map.KeyType.CreateStorage(0),
            SingleEmpty,
            0,
            PageNodeMemory + Constants.MemoryPointer + PageMemoryChild);

    /// <summary>
    /// A node over the given keys and children.
    /// </summary>
    public static Page<K, V> CreateNode(MVMap<K, V> map, K[] keys, PageReference[] children, long totalCount, int memory)
    {
        Debug.Assert(keys is not null);
        Page<K, V> page = new NonLeaf(map, keys, children, totalCount);
        page.InitMemoryAccount(memory);
        return page;
    }

    /// <summary>
    /// A leaf over the given keys and values.
    /// </summary>
    internal static Page<K, V> CreateLeaf(MVMap<K, V> map, K[] keys, V[]? values, int memory)
    {
        Debug.Assert(keys is not null);
        Page<K, V> page = new Leaf(map, keys, values);
        page.InitMemoryAccount(memory);
        return page;
    }

    private void InitMemoryAccount(int memoryCount)
    {
        if (!map.IsPersistent())
        {
            memory = InMemory;
        }
        else if (memoryCount == 0)
        {
            RecalculateMemory();
        }
        else
        {
            AddMemory(memoryCount);
            Debug.Assert(memoryCount == GetMemory());
        }
    }

    /// <summary>
    /// The value for a key, or the default when the tree does not hold it.
    /// </summary>
    internal static V? Get(Page<K, V> page, K key)
    {
        while (true)
        {
            int index = page.BinarySearch(key);
            if (page.IsLeaf())
            {
                return index >= 0 ? page.GetValue(index) : default;
            }

            if (index++ < 0)
            {
                index = -index;
            }

            page = page.GetChildPage(index);
        }
    }

    /// <summary>
    /// A page read back from its bytes.
    /// </summary>
    internal static Page<K, V> Read(byte[] buffer, ref int position, long pos, MVMap<K, V> map)
    {
        bool leaf = (DataUtils.GetPageType(pos) & 1) == DataUtils.PageTypeLeaf;
        Page<K, V> page = leaf ? new Leaf(map) : new NonLeaf(map);
        Volatile.Write(ref page.pos, pos);
        page.Read(buffer, ref position);
        return page;
    }

    /// <summary>
    /// The id of the map this page belongs to.
    /// </summary>
    public int GetMapId() => map.GetId();

    int IPage.PageNo => pageNo;

    int IPage.GetMapId() => GetMapId();

    long IPage.GetTotalCount() => GetTotalCount();

    long IPage.GetPos() => GetPos();

    bool IPage.IsLeaf() => IsLeaf();

    int IPage.GetMemory() => GetMemory();

    void IPage.WriteUnsavedRecursive(FileStore.PageSerializationManager manager)
        => WriteUnsavedRecursive(manager);

    void IPage.ReleaseSavedPages() => ReleaseSavedPages();

    /// <summary>
    /// A copy of this page for another map, optionally without its children.
    /// </summary>
    internal abstract Page<K, V> Copy(MVMap<K, V> map, bool eraseChildrenRefs);

    /// <summary>
    /// The key at an index.
    /// </summary>
    public K GetKey(int index) => keys[index];

    /// <summary>
    /// The child at an index.
    /// </summary>
    public abstract Page<K, V> GetChildPage(int index);

    /// <summary>
    /// Where the child at an index lives.
    /// </summary>
    public abstract long GetChildPagePos(int index);

    /// <summary>
    /// The value at an index.
    /// </summary>
    public abstract V? GetValue(int index);

    /// <summary>
    /// How many keys this page holds.
    /// </summary>
    public int GetKeyCount() => keys.Length;

    /// <summary>
    /// Whether this page holds values rather than children.
    /// </summary>
    public bool IsLeaf() => GetNodeType() == DataUtils.PageTypeLeaf;

    /// <summary>
    /// Leaf or node, as the lowest bit of a page position says.
    /// </summary>
    public abstract int GetNodeType();

    /// <summary>
    /// Where this page lives, or zero when it never did and one when it was removed.
    /// </summary>
    public long GetPos() => Volatile.Read(ref pos);

    /// <inheritdoc/>
    public override string ToString()
    {
        StringBuilder buff = new();
        Dump(buff);
        return buff.ToString();
    }

    /// <summary>
    /// Describes this page for a message about a broken file.
    /// </summary>
    protected virtual void Dump(StringBuilder buff)
    {
        _ = buff.Append("id: ").Append(RuntimeHelpers.GetHashCode(this)).Append('\n');
        _ = buff.Append("pos: ").Append(GetPos().ToString("x", CultureInfo.InvariantCulture)).Append('\n');
        if (IsSaved())
        {
            int chunkId = DataUtils.GetPageChunkId(GetPos());
            _ = buff.Append("chunk:").Append(chunkId.ToString("x", CultureInfo.InvariantCulture));
            if (pageNo >= 0)
            {
                _ = buff.Append(",no:").Append(pageNo.ToString("x", CultureInfo.InvariantCulture));
            }

            _ = buff.Append('\n');
        }
    }

    /// <summary>
    /// A copy that was never written: same keys and values, no position.
    /// </summary>
    public Page<K, V> Copy()
    {
        Page<K, V> newPage = (Page<K, V>)MemberwiseClone();
        Volatile.Write(ref newPage.pos, 0);
        newPage.pageNo = -1;
        return newPage;
    }

    /// <summary>
    /// Where a key would land: its index, or the complement of where it would go.
    /// </summary>
    internal int BinarySearch(K key)
    {
        int res = map.KeyType.BinarySearch(key, keys, GetKeyCount(), cachedCompare);
        cachedCompare = res < 0 ? ~res : res + 1;
        return res;
    }

    /// <summary>
    /// Which child to descend into for a key that is not here.
    /// </summary>
    internal abstract int CalculateTraversalIndex(K key);

    /// <summary>
    /// Splits this page at an index, returning the right half and keeping the left.
    /// </summary>
    internal abstract Page<K, V> Split(int at);

    /// <summary>
    /// Splits the keys, keeping the left part here and returning the right.
    /// </summary>
    internal K[] SplitKeys(int aCount, int bCount)
    {
        Debug.Assert(aCount + bCount <= GetKeyCount());
        K[] aKeys = CreateKeyStorage(aCount);
        K[] bKeys = CreateKeyStorage(bCount);
        Array.Copy(keys, 0, aKeys, 0, aCount);
        Array.Copy(keys, GetKeyCount() - bCount, bKeys, 0, bCount);
        keys = aKeys;
        return bKeys;
    }

    /// <summary>
    /// Makes room for more keys and values.
    /// </summary>
    internal abstract void Expand(int extraKeyCount, K[] extraKeys, V[]? extraValues);

    /// <summary>
    /// Makes room for more keys.
    /// </summary>
    internal void ExpandKeys(int extraKeyCount, K[] extraKeys)
    {
        int keyCount = GetKeyCount();
        K[] newKeys = CreateKeyStorage(keyCount + extraKeyCount);
        Array.Copy(keys, 0, newKeys, 0, keyCount);
        Array.Copy(extraKeys, 0, newKeys, keyCount, extraKeyCount);
        keys = newKeys;
    }

    /// <summary>
    /// Removes the keys whose bits are set, returning the page that holds the rest.
    /// </summary>
    public virtual Page<K, V> Remove(long positionsToRemove)
        => throw new NotSupportedException();

    /// <summary>
    /// How many values live under this page.
    /// </summary>
    public abstract long GetTotalCount();

    /// <summary>
    /// How many values live under the child at an index.
    /// </summary>
    internal abstract long GetCounts(int index);

    /// <summary>
    /// Replaces the child at an index.
    /// </summary>
    public abstract void SetChild(int index, Page<K, V> child);

    /// <summary>
    /// Replaces the key at an index, copying the key array first.
    /// </summary>
    public void SetKey(int index, K key)
    {
        keys = (K[])keys.Clone();
        if (IsPersistent())
        {
            K old = keys[index];
            if (!map.IsMemoryEstimationAllowed() || old is null)
            {
                int mem = map.EvaluateMemoryForKey(key);
                if (old is not null)
                {
                    mem -= map.EvaluateMemoryForKey(old);
                }

                AddMemory(mem);
            }
        }

        keys[index] = key;
    }

    /// <summary>
    /// Replaces the value at an index.
    /// </summary>
    public abstract V? SetValue(int index, V value);

    /// <summary>
    /// Inserts a key and a value into a leaf.
    /// </summary>
    public abstract void InsertLeaf(int index, K key, V value);

    /// <summary>
    /// Inserts a key and a child into a node.
    /// </summary>
    public abstract void InsertNode(int index, K key, Page<K, V> childPage);

    /// <summary>
    /// Inserts a key, making room for it first.
    /// </summary>
    internal void InsertKey(int index, K key)
    {
        int keyCount = GetKeyCount();
        Debug.Assert(index <= keyCount, index + " > " + keyCount);
        K[] newKeys = CreateKeyStorage(keyCount + 1);
        DataUtils.CopyWithGap(keys, newKeys, keyCount, index);
        keys = newKeys;
        keys[index] = key;
        if (IsPersistent())
        {
            AddMemory(Constants.MemoryPointer + map.EvaluateMemoryForKey(key));
        }
    }

    /// <summary>
    /// Removes the key at an index.
    /// </summary>
    public virtual void Remove(int index)
    {
        int keyCount = GetKeyCount();
        if (index == keyCount)
        {
            --index;
        }

        if (IsPersistent() && !map.IsMemoryEstimationAllowed())
        {
            K old = GetKey(index);
            AddMemory(-Constants.MemoryPointer - map.EvaluateMemoryForKey(old));
        }

        K[] newKeys = CreateKeyStorage(keyCount - 1);
        DataUtils.CopyExcept(keys, newKeys, keyCount, index);
        keys = newKeys;
    }

    /// <summary>
    /// Reads this page out of its bytes.
    /// </summary>
    private void Read(byte[] buffer, ref int position)
    {
        int chunkId = DataUtils.GetPageChunkId(GetPos());
        int offset = DataUtils.GetPageOffset(GetPos());
        int start = position;
        int pageLength = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(position, 4));
        position += 4;

        // does not include optional part (pageNo)
        int remaining = (buffer.Length - position) + 4;
        if (pageLength > remaining || pageLength < 4)
        {
            throw DataUtils.NewMvStoreException(
                DataUtils.ErrorFileCorrupt,
                "File corrupted in chunk {0}, expected page length 4..{1}, got {2}",
                chunkId, remaining, pageLength);
        }

        short check = BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(position, 2));
        position += 2;
        int checkTest = DataUtils.GetCheckValue(chunkId)
            ^ DataUtils.GetCheckValue(offset)
            ^ DataUtils.GetCheckValue(pageLength);
        if (check != (short)checkTest)
        {
            throw DataUtils.NewMvStoreException(
                DataUtils.ErrorFileCorrupt,
                "File corrupted in chunk {0}, expected check value {1}, got {2}",
                chunkId, checkTest, check);
        }

        pageNo = DataUtils.ReadVarInt(buffer, ref position);
        if (pageNo < 0)
        {
            throw DataUtils.NewMvStoreException(
                DataUtils.ErrorFileCorrupt,
                "File corrupted in chunk {0}, got negative page No {1}", chunkId, pageNo);
        }

        int mapId = DataUtils.ReadVarInt(buffer, ref position);
        if (mapId != map.GetId())
        {
            throw DataUtils.NewMvStoreException(
                DataUtils.ErrorFileCorrupt,
                "File corrupted in chunk {0}, expected map id {1}, got {2}",
                chunkId, map.GetId(), mapId);
        }

        int keyCount = DataUtils.ReadVarInt(buffer, ref position);
        keys = CreateKeyStorage(keyCount);
        int type = buffer[position++];
        if (IsLeaf() != ((type & 1) == DataUtils.PageTypeLeaf))
        {
            throw DataUtils.NewMvStoreException(
                DataUtils.ErrorFileCorrupt,
                "File corrupted in chunk {0}, expected node type {1}, got {2}",
                chunkId, IsLeaf() ? "0" : "1", type);
        }

        // to restrain a data type that grabs the whole remainder of the buffer
        int end = start + pageLength;
        if (!IsLeaf())
        {
            ReadPayLoad(buffer, ref position, end);
        }

        bool compressed = (type & DataUtils.PageCompressed) != 0;
        if (compressed)
        {
            Compressor compressor = (type & DataUtils.PageCompressedHigh) == DataUtils.PageCompressedHigh
                ? map.GetStore().GetCompressorHigh()
                : map.GetStore().GetCompressorFast();
            int lenAdd = DataUtils.ReadVarInt(buffer, ref position);
            int compLen = end - position;
            byte[] expanded = new byte[compLen + lenAdd];
            compressor.Expand(buffer, position, compLen, expanded, 0, expanded.Length);
            buffer = expanded;
            position = 0;
            end = expanded.Length;
        }

        map.KeyType.Read(buffer.AsSpan(), keys, keyCount, ref position);
        if (IsLeaf())
        {
            ReadPayLoad(buffer, ref position, end);
        }

        diskSpaceUsed = pageLength;
        RecalculateMemory();
    }

    /// <summary>
    /// Reads the payload: the children of a node, or the values of a leaf.
    /// </summary>
    protected abstract void ReadPayLoad(byte[] buffer, ref int position, int end);

    /// <summary>
    /// Whether this page was ever written.
    /// </summary>
    public bool IsSaved() => DataUtils.IsPageSaved(GetPos());

    /// <summary>
    /// Whether this page was removed.
    /// </summary>
    public bool IsRemoved() => DataUtils.IsPageRemoved(GetPos());

    private bool MarkAsRemoved()
    {
        Debug.Assert(GetTotalCount() > 0, ToString());
        long pagePos;
        do
        {
            pagePos = GetPos();
            if (DataUtils.IsPageSaved(pagePos))
            {
                return false;
            }

            Debug.Assert(!DataUtils.IsPageRemoved(pagePos));
        }
        while (Interlocked.CompareExchange(ref pos, 1L, 0L) != 0L);

        return true;
    }

    /// <summary>
    /// Writes this page, returning where the children start.
    /// </summary>
    protected int Write(FileStore.PageSerializationManager manager)
    {
        pageNo = manager.GetPageNo();
        int keyCount = GetKeyCount();
        WriteBuffer buff = manager.GetBuffer();
        int start = buff.Position();
        _ = buff.PutInt(0).PutShort(0).PutVarInt(pageNo).PutVarInt(map.GetId()).PutVarInt(keyCount);
        int typePos = buff.Position();
        int type = IsLeaf() ? DataUtils.PageTypeLeaf : DataUtils.PageTypeNode;
        _ = buff.Put((byte)type);
        int childrenPos = buff.Position();
        WriteChildren(buff, true);
        int compressStart = buff.Position();
        map.KeyType.Write(buff, keys, keyCount);
        WriteValues(buff);
        MVStore store = map.GetStore();
        int expLen = buff.Position() - compressStart;
        if (expLen > 16)
        {
            int compressionLevel = store.GetCompressionLevel();
            if (compressionLevel > 0)
            {
                Compressor compressor;
                int compressType;
                if (compressionLevel == 1)
                {
                    compressor = store.GetCompressorFast();
                    compressType = DataUtils.PageCompressed;
                }
                else
                {
                    compressor = store.GetCompressorHigh();
                    compressType = DataUtils.PageCompressedHigh;
                }

                byte[] comp = new byte[expLen * 2];
                byte[] grown = buff.ToArray();
                int compLen = compressor.Compress(grown, compressStart, expLen, comp, 0);
                int plus = DataUtils.GetVarIntLen(expLen - compLen);
                if (compLen + plus < expLen)
                {
                    _ = buff.Position(typePos).Put((byte)(type | compressType));
                    _ = buff.Position(compressStart).PutVarInt(expLen - compLen).Put(comp, 0, compLen);
                }
            }
        }

        int pageLength = buff.Position() - start;
        long pagePos = manager.GetPagePosition(GetMapId(), start, pageLength, type);
        if (IsSaved())
        {
            throw DataUtils.NewMvStoreException(DataUtils.ErrorInternal, "Page already stored");
        }

        bool isDeleted = IsRemoved();
        long expected = isDeleted ? 1L : 0L;
        while (Interlocked.CompareExchange(ref pos, pagePos, expected) != expected)
        {
            isDeleted = IsRemoved();
            expected = isDeleted ? 1L : 0L;
        }

        int pageLengthDecoded = DataUtils.GetPageMaxLength(pagePos);
        diskSpaceUsed = pageLengthDecoded != DataUtils.PageLarge ? pageLengthDecoded : pageLength;
        bool singleWriter = map.IsSingleWriter();
        manager.OnPageSerialized(this, isDeleted, pageLengthDecoded, singleWriter);
        return childrenPos;
    }

    /// <summary>
    /// Writes the values of a leaf.
    /// </summary>
    protected abstract void WriteValues(WriteBuffer buff);

    /// <summary>
    /// Writes the children of a node, with their counts when asked.
    /// </summary>
    protected abstract void WriteChildren(WriteBuffer buff, bool withCounts);

    /// <summary>
    /// Writes every unsaved page under this one.
    /// </summary>
    internal abstract void WriteUnsavedRecursive(FileStore.PageSerializationManager manager);

    /// <summary>
    /// Releases every saved page under this one.
    /// </summary>
    internal abstract void ReleaseSavedPages();

    /// <summary>
    /// How many children this page has without reading them.
    /// </summary>
    public abstract int GetRawChildPageCount();

    /// <summary>
    /// Whether memory is counted for this page at all.
    /// </summary>
    protected bool IsPersistent() => memory != InMemory;

    /// <summary>
    /// How many bytes this page accounts for, or nothing when it is not counted.
    /// </summary>
    public int GetMemory()
    {
        if (IsPersistent())
        {
            return memory;
        }

        return 0;
    }

    /// <summary>
    /// How many bytes on disk this page and everything under it take.
    /// </summary>
    public long GetDiskSpaceUsed(bool approximate)
        => IsPersistent()
            ? approximate ? GetDiskSpaceUsedApproximation(3, false) : GetDiskSpaceUsedAccurate()
            : 0L;

    private long GetDiskSpaceUsedAccurate()
    {
        long r = diskSpaceUsed;
        if (!IsLeaf())
        {
            for (int i = 0, l = GetRawChildPageCount(); i < l; i++)
            {
                long childPos = GetChildPagePos(i);
                if (childPos != 0)
                {
                    r += GetChildPage(i).GetDiskSpaceUsedAccurate();
                }
            }
        }

        return r;
    }

    private long GetDiskSpaceUsedApproximation(int maxLevel, bool first)
    {
        long r = diskSpaceUsed;
        if (!IsLeaf())
        {
            int l = GetRawChildPageCount();
            if (--maxLevel == 0 && l > 4)
            {
                if (first)
                {
                    for (int i = 0; i < l; i++)
                    {
                        long childPos = GetChildPagePos(i);
                        if (childPos != 0)
                        {
                            r += GetChildPage(i).GetDiskSpaceUsedApproximation(maxLevel, first) * l;
                            break;
                        }
                    }
                }
                else
                {
                    for (int i = l; --i >= 0;)
                    {
                        long childPos = GetChildPagePos(i);
                        if (childPos != 0)
                        {
                            r += GetChildPage(i).GetDiskSpaceUsedApproximation(maxLevel, first) * l;
                            break;
                        }
                    }
                }
            }
            else
            {
                for (int i = 0; i < l; i++)
                {
                    long childPos = GetChildPagePos(i);
                    if (childPos != 0)
                    {
                        r += GetChildPage(i).GetDiskSpaceUsedApproximation(maxLevel, first);
                        first = !first;
                    }
                }
            }
        }

        return r;
    }

    /// <summary>
    /// Adds to what this page accounts for.
    /// </summary>
    internal void AddMemory(int mem)
    {
        memory += mem;
        Debug.Assert(memory >= 0);
    }

    /// <summary>
    /// Counts what this page holds, now that it is persistent.
    /// </summary>
    internal void RecalculateMemory()
    {
        Debug.Assert(IsPersistent());
        memory = CalculateMemory();
    }

    /// <summary>
    /// What this page holds, not counting the children.
    /// </summary>
    protected virtual int CalculateMemory() => map.EvaluateMemoryForKeys(keys, GetKeyCount());

    /// <summary>
    /// Whether this page is complete, which an incompletely written node is not.
    /// </summary>
    public virtual bool IsComplete() => true;

    /// <summary>
    /// Marks this page complete.
    /// </summary>
    public virtual void SetComplete()
    {
    }

    /// <summary>
    /// Removes this page and everything under it, returning what was never saved.
    /// </summary>
    public int RemovePage(long version)
    {
        if (IsPersistent() && GetTotalCount() > 0)
        {
            MVStore store = map.Store;
            if (!MarkAsRemoved())
            {
                // only if it has been saved already
                long pagePos = GetPos();
                store.AccountForRemovedPage(pagePos, version, map.IsSingleWriter(), pageNo);
            }
            else
            {
                return -memory;
            }
        }

        return 0;
    }

    /// <summary>
    /// The cursor position before the first key of this page.
    /// </summary>
    public abstract CursorPos<K, V> GetPrependCursorPos(CursorPos<K, V>? cursorPos);

    /// <summary>
    /// The cursor position after the last key of this page.
    /// </summary>
    public abstract CursorPos<K, V> GetAppendCursorPos(CursorPos<K, V>? cursorPos);

    /// <summary>
    /// Removes everything under this page.
    /// </summary>
    public abstract int RemoveAllRecursive(long version);

    /// <summary>
    /// An array to hold keys of this map.
    /// </summary>
    public K[] CreateKeyStorage(int size) => map.KeyType.CreateStorage(size);

    /// <summary>
    /// An array to hold values of this map.
    /// </summary>
    internal V[] CreateValueStorage(int size) => map.ValueType.CreateStorage(size);

    /// <summary>
    /// Whether this page shares its key array with another one.
    /// </summary>
    internal bool SameKeys(Page<K, V> page) => ReferenceEquals(keys, page.keys);

    /// <summary>
    /// An array to hold child references.
    /// </summary>
    public static PageReference[] CreateRefStorage(int size) => new PageReference[size];

    /// <summary>
    /// Where a child lives: its page when it is in memory, or where to read it from when it is not.
    /// </summary>
    /// <remarks>
    /// The page is held with acquire and released with release semantics, which is what makes a
    /// reader that sees a cleared reference also see the position that was stamped before it. A plain
    /// field would let the two drift apart across threads, and the position a reader then used would
    /// belong to a page that is already gone.
    /// </remarks>
    public sealed class PageReference
    {
        /// <summary>
        /// What an array of references starts out holding before it is filled.
        /// </summary>
        internal static readonly PageReference Empty = new(null, 0, 0);

        private long pos;

        private Page<K, V>? page;

        /// <summary>
        /// How many values live under the child.
        /// </summary>
        internal readonly long count;

        /// <summary>
        /// What <see cref="Empty"/> stands for: no page, nowhere, nothing under it.
        /// </summary>
        public static PageReference EmptyRef() => Empty;

        /// <summary>
        /// A reference to a page in memory.
        /// </summary>
        public PageReference(Page<K, V>? page)
            : this(page, page is null ? 0 : page.GetPos(), page is null ? 0 : page.GetTotalCount())
        {
        }

        /// <summary>
        /// A reference to a page on disk.
        /// </summary>
        internal PageReference(long pos, long count)
            : this(null, pos, count)
        {
            Debug.Assert(DataUtils.IsPageSaved(pos));
        }

        private PageReference(Page<K, V>? page, long pos, long count)
        {
            Volatile.Write(ref this.page, page);
            this.pos = pos;
            this.count = count;
        }

        /// <summary>
        /// The page, when it is in memory.
        /// </summary>
        public Page<K, V>? GetPage() => Volatile.Read(ref page);

        /// <summary>
        /// Forgets the page, releasing what it held first.
        /// </summary>
        internal void ClearPageReference()
        {
            Page<K, V>? p = page;
            if (p is not null)
            {
                p.ReleaseSavedPages();
                if (p.IsSaved())
                {
                    Debug.Assert(pos == p.GetPos());
                    Debug.Assert(count == p.GetTotalCount(), count + " != " + p.GetTotalCount());

                    // 'pos' was stamped by resetPos() earlier on this thread; the release
                    // write below publishes it to any reader that observes the cleared page
                    Volatile.Write(ref page, null);
                }
            }
        }

        /// <summary>
        /// Where the child lives.
        /// </summary>
        internal long GetPos() => pos;

        /// <summary>
        /// Restamps the position from the page, when the page has been written since.
        /// </summary>
        internal void ResetPos()
        {
            Page<K, V>? p = page;
            if (p is not null && p.IsSaved())
            {
                pos = p.GetPos();
                Debug.Assert(count == p.GetTotalCount());
            }
        }

        /// <inheritdoc/>
        public override string ToString()
            => "Cnt:" + count + ", pos:"
                + (pos == 0
                    ? "0"
                    : DataUtils.GetPageChunkId(pos)
                        + (page is null ? "" : "/" + page.pageNo)
                        + "-" + DataUtils.GetPageOffset(pos)
                        + ":" + DataUtils.GetPageMaxLength(pos))
                + ((page is null ? DataUtils.GetPageType(pos) == 0 : page.IsLeaf()) ? " leaf" : " node")
                + ", page:{" + page + "}";
    }

    private class NonLeaf : Page<K, V>
    {
        private PageReference[] children;

        private long totalCount;

        internal NonLeaf(MVMap<K, V> map)
            : base(map)
        {
            children = [];
        }

        internal NonLeaf(MVMap<K, V> map, NonLeaf source, PageReference[] children, long totalCount)
            : base(map, source)
        {
            this.children = children;
            this.totalCount = totalCount;
        }

        internal NonLeaf(MVMap<K, V> map, K[] keys, PageReference[] children, long totalCount)
            : base(map, keys)
        {
            this.children = children;
            this.totalCount = totalCount;
        }

        public override int GetNodeType() => DataUtils.PageTypeNode;

        internal override Page<K, V> Copy(MVMap<K, V> map, bool eraseChildrenRefs)
            => eraseChildrenRefs
                ? new IncompleteNonLeaf(map, this)
                : new NonLeaf(map, this, children, totalCount);

        public override Page<K, V> GetChildPage(int index)
        {
            PageReference reference = children[index];
            Page<K, V>? page = reference.GetPage();
            if (page is null)
            {
                page = map.ReadPage(reference.GetPos());
                Debug.Assert(reference.GetPos() == page.GetPos());
                Debug.Assert(reference.count == page.GetTotalCount());
            }

            return page;
        }

        public override long GetChildPagePos(int index) => children[index].GetPos();

        public override V? GetValue(int index) => throw new NotSupportedException();

        internal override int CalculateTraversalIndex(K key)
        {
            int index = BinarySearch(key);
            if (++index < 0)
            {
                index = -index;
            }

            return index;
        }

        internal override Page<K, V> Split(int at)
        {
            Debug.Assert(!IsSaved());
            int b = GetKeyCount() - at;
            K[] bKeys = SplitKeys(at, b - 1);
            PageReference[] aChildren = CreateRefStorage(at + 1);
            PageReference[] bChildren = CreateRefStorage(b);
            Array.Copy(children, 0, aChildren, 0, at + 1);
            Array.Copy(children, at + 1, bChildren, 0, b);
            children = aChildren;

            long t = 0;
            foreach (PageReference x in aChildren)
            {
                t += x.count;
            }

            totalCount = t;
            t = 0;
            foreach (PageReference x in bChildren)
            {
                t += x.count;
            }

            Page<K, V> newPage = CreateNode(map, bKeys, bChildren, t, 0);
            if (IsPersistent())
            {
                RecalculateMemory();
            }

            return newPage;
        }

        internal override void Expand(int extraKeyCount, K[] extraKeys, V[]? extraValues)
            => throw new NotSupportedException();

        public override long GetTotalCount()
        {
            Debug.Assert(!IsComplete() || totalCount == CalculateTotalCount(),
                "Total count: " + totalCount + " != " + CalculateTotalCount());
            return totalCount;
        }

        private long CalculateTotalCount()
        {
            long check = 0;
            int keyCount = GetKeyCount();
            for (int i = 0; i <= keyCount; i++)
            {
                check += children[i].count;
            }

            return check;
        }

        internal void RecalculateTotalCount() => totalCount = CalculateTotalCount();

        internal override long GetCounts(int index) => children[index].count;

        public override void SetChild(int index, Page<K, V> child)
        {
            Debug.Assert(child is not null);
            PageReference reference = children[index];
            if (child != reference.GetPage() || child.GetPos() != reference.GetPos())
            {
                totalCount += child.GetTotalCount() - reference.count;
                children = (PageReference[])children.Clone();
                children[index] = new PageReference(child);
            }
        }

        public override V? SetValue(int index, V value) => throw new NotSupportedException();

        public override void InsertLeaf(int index, K key, V value) => throw new NotSupportedException();

        public override void InsertNode(int index, K key, Page<K, V> childPage)
        {
            int childCount = GetRawChildPageCount();
            InsertKey(index, key);

            PageReference[] newChildren = CreateRefStorage(childCount + 1);
            DataUtils.CopyWithGap(children, newChildren, childCount, index);
            children = newChildren;
            children[index] = new PageReference(childPage);

            totalCount += childPage.GetTotalCount();
            if (IsPersistent())
            {
                AddMemory(Constants.MemoryPointer + PageMemoryChild);
            }
        }

        public override void Remove(int index)
        {
            int childCount = GetRawChildPageCount();
            base.Remove(index);
            if (IsPersistent())
            {
                if (map.IsMemoryEstimationAllowed())
                {
                    AddMemory(-GetMemory() / childCount);
                }
                else
                {
                    AddMemory(-Constants.MemoryPointer - PageMemoryChild);
                }
            }

            totalCount -= children[index].count;
            PageReference[] newChildren = CreateRefStorage(childCount - 1);
            DataUtils.CopyExcept(children, newChildren, childCount, index);
            children = newChildren;
        }

        public override int RemoveAllRecursive(long version)
        {
            int unsavedMemory = RemovePage(version);
            if (IsPersistent())
            {
                for (int i = 0, size = map.GetChildPageCount(this); i < size; i++)
                {
                    PageReference reference = children[i];
                    Page<K, V>? page = reference.GetPage();
                    if (page is not null)
                    {
                        unsavedMemory += page.RemoveAllRecursive(version);
                    }
                    else
                    {
                        long pagePos = reference.GetPos();
                        Debug.Assert(DataUtils.IsPageSaved(pagePos));
                        if (DataUtils.IsLeafPosition(pagePos))
                        {
                            map.Store.AccountForRemovedPage(
                                pagePos, version, map.IsSingleWriter(), -1);
                        }
                        else
                        {
                            unsavedMemory += map.ReadPage(pagePos).RemoveAllRecursive(version);
                        }
                    }
                }
            }

            return unsavedMemory;
        }

        public override CursorPos<K, V> GetPrependCursorPos(CursorPos<K, V>? cursorPos)
        {
            Page<K, V> childPage = GetChildPage(0);
            return childPage.GetPrependCursorPos(new CursorPos<K, V>(this, 0, cursorPos));
        }

        public override CursorPos<K, V> GetAppendCursorPos(CursorPos<K, V>? cursorPos)
        {
            int keyCount = GetKeyCount();
            Page<K, V> childPage = GetChildPage(keyCount);
            return childPage.GetAppendCursorPos(new CursorPos<K, V>(this, keyCount, cursorPos));
        }

        protected override void ReadPayLoad(byte[] buffer, ref int position, int end)
        {
            int keyCount = GetKeyCount();
            children = CreateRefStorage(keyCount + 1);
            long[] positions = new long[keyCount + 1];
            for (int i = 0; i <= keyCount; i++)
            {
                positions[i] = BinaryPrimitives.ReadInt64BigEndian(buffer.AsSpan(position, 8));
                position += 8;
            }

            long total = 0;
            for (int i = 0; i <= keyCount; i++)
            {
                long s = DataUtils.ReadVarLong(buffer, ref position);
                long childPos = positions[i];
                Debug.Assert(childPos == 0 ? s == 0 : s >= 0);
                total += s;
                children[i] = childPos == 0
                    ? PageReference.Empty
                    : new PageReference(childPos, s);
            }

            totalCount = total;
        }

        protected override void WriteValues(WriteBuffer buff)
        {
        }

        protected override void WriteChildren(WriteBuffer buff, bool withCounts)
        {
            int keyCount = GetKeyCount();
            for (int i = 0; i <= keyCount; i++)
            {
                _ = buff.PutLong(children[i].GetPos());
            }

            if (withCounts)
            {
                for (int i = 0; i <= keyCount; i++)
                {
                    _ = buff.PutVarLong(children[i].count);
                }
            }
        }

        internal override void WriteUnsavedRecursive(FileStore.PageSerializationManager manager)
        {
            if (!IsSaved())
            {
                int patch = Write(manager);
                WriteChildrenRecursive(manager);
                WriteBuffer buff = manager.GetBuffer();
                int old = buff.Position();
                _ = buff.Position(patch);
                WriteChildren(buff, false);
                _ = buff.Position(old);
            }
        }

        internal void WriteChildrenRecursive(FileStore.PageSerializationManager manager)
        {
            int len = GetRawChildPageCount();
            for (int i = 0; i < len; i++)
            {
                PageReference reference = children[i];
                Page<K, V>? page = reference.GetPage();
                if (page is not null)
                {
                    page.WriteUnsavedRecursive(manager);
                    reference.ResetPos();
                }
            }
        }

        internal override void ReleaseSavedPages()
        {
            int len = GetRawChildPageCount();
            for (int i = 0; i < len; i++)
            {
                children[i].ClearPageReference();
            }
        }

        public override int GetRawChildPageCount() => GetKeyCount() + 1;

        protected override int CalculateMemory()
            => base.CalculateMemory() + PageNodeMemory
                + (GetRawChildPageCount() * (Constants.MemoryPointer + PageMemoryChild));

        protected override void Dump(StringBuilder buff)
        {
            base.Dump(buff);
            int keyCount = GetKeyCount();
            for (int i = 0; i <= keyCount; i++)
            {
                if (i > 0)
                {
                    _ = buff.Append(" ");
                }

                _ = buff.Append("[").Append(children[i].GetPos().ToString("x", CultureInfo.InvariantCulture)).Append("]");
                if (i < keyCount)
                {
                    _ = buff.Append(" ").Append(GetKey(i));
                }
            }
        }
    }

    private sealed class IncompleteNonLeaf : NonLeaf
    {
        private bool complete;

        internal IncompleteNonLeaf(MVMap<K, V> map, NonLeaf source)
            : base(map, source, ConstructEmptyPageRefs(source.GetRawChildPageCount()), source.GetTotalCount())
        {
        }

        private static PageReference[] ConstructEmptyPageRefs(int size)
        {
            // replace child pages with empty pages
            PageReference[] children = CreateRefStorage(size);
            Array.Fill(children, PageReference.Empty);
            return children;
        }

        internal override void WriteUnsavedRecursive(FileStore.PageSerializationManager manager)
        {
            if (complete)
            {
                base.WriteUnsavedRecursive(manager);
            }
            else if (!IsSaved())
            {
                WriteChildrenRecursive(manager);
            }
        }

        public override bool IsComplete() => complete;

        public override void SetComplete()
        {
            RecalculateTotalCount();
            complete = true;
        }

        protected override void Dump(StringBuilder buff)
        {
            base.Dump(buff);
            _ = buff.Append(", complete:").Append(complete);
        }
    }

    private sealed class Leaf : Page<K, V>
    {
        private V[]? values;

        internal Leaf(MVMap<K, V> map)
            : base(map)
        {
        }

        private Leaf(MVMap<K, V> map, Leaf source)
            : base(map, source) => values = source.values;

        internal Leaf(MVMap<K, V> map, K[] keys, V[]? values)
            : base(map, keys) => this.values = values;

        public override int GetNodeType() => DataUtils.PageTypeLeaf;

        internal override Page<K, V> Copy(MVMap<K, V> map, bool eraseChildrenRefs)
            => new Leaf(map, this);

        public override Page<K, V> GetChildPage(int index) => throw new NotSupportedException();

        public override long GetChildPagePos(int index) => throw new NotSupportedException();

        public override V? GetValue(int index) => values is null ? default : values[index];

        internal override int CalculateTraversalIndex(K key) => BinarySearch(key);

        internal override Page<K, V> Split(int at)
        {
            Debug.Assert(!IsSaved());
            int b = GetKeyCount() - at;
            K[] bKeys = SplitKeys(at, b);
            V[] bValues = CreateValueStorage(b);
            if (values is not null)
            {
                V[] aValues = CreateValueStorage(at);
                Array.Copy(values, 0, aValues, 0, at);
                Array.Copy(values, at, bValues, 0, b);
                values = aValues;
            }

            Page<K, V> newPage = CreateLeaf(map, bKeys, bValues, 0);
            if (IsPersistent())
            {
                RecalculateMemory();
            }

            return newPage;
        }

        internal override void Expand(int extraKeyCount, K[] extraKeys, V[]? extraValues)
        {
            int keyCount = GetKeyCount();
            ExpandKeys(extraKeyCount, extraKeys);
            if (values is not null)
            {
                V[] newValues = CreateValueStorage(keyCount + extraKeyCount);
                Array.Copy(values, 0, newValues, 0, keyCount);
                Array.Copy(extraValues!, 0, newValues, keyCount, extraKeyCount);
                values = newValues;
            }

            if (IsPersistent())
            {
                RecalculateMemory();
            }
        }

        public override Page<K, V> Remove(long positionsToRemove)
        {
            Debug.Assert(positionsToRemove != 0);
            int keyCount = GetKeyCount() - BitOperations.PopCount((ulong)positionsToRemove);
            if (keyCount == 0)
            {
                return map.CreateEmptyLeaf();
            }

            K[] newKeys = CreateKeyStorage(keyCount);
            V[] newValues = values is null ? null! : CreateValueStorage(keyCount);
            for (int src = 0, dst = 0; dst < keyCount; ++src, positionsToRemove = (long)((ulong)positionsToRemove >> 1))
            {
                if ((positionsToRemove & 1L) == 0)
                {
                    newKeys[dst] = GetKey(src);
                    if (newValues is not null)
                    {
                        newValues[dst] = values![src];
                    }

                    ++dst;
                }
            }

            return CreateLeaf(map, newKeys, newValues!, 0);
        }

        public override long GetTotalCount() => GetKeyCount();

        internal override long GetCounts(int index) => throw new NotSupportedException();

        public override void SetChild(int index, Page<K, V> child) => throw new NotSupportedException();

        public override V? SetValue(int index, V value)
        {
            values = (V[])values!.Clone();
            V old = SetValueInternal(index, value);
            if (IsPersistent() && !map.IsMemoryEstimationAllowed())
            {
                AddMemory(map.EvaluateMemoryForValue(value) - map.EvaluateMemoryForValue(old));
            }

            return old;
        }

        private V SetValueInternal(int index, V value)
        {
            V old = values![index];
            values[index] = value;
            return old;
        }

        public override void InsertLeaf(int index, K key, V value)
        {
            int keyCount = GetKeyCount();
            InsertKey(index, key);

            if (values is not null)
            {
                V[] newValues = CreateValueStorage(keyCount + 1);
                DataUtils.CopyWithGap(values, newValues, keyCount, index);
                values = newValues;
                _ = SetValueInternal(index, value);
                if (IsPersistent())
                {
                    AddMemory(Constants.MemoryPointer + map.EvaluateMemoryForValue(value));
                }
            }
        }

        public override void InsertNode(int index, K key, Page<K, V> childPage)
            => throw new NotSupportedException();

        public override void Remove(int index)
        {
            int keyCount = GetKeyCount();
            base.Remove(index);
            if (values is not null)
            {
                if (IsPersistent())
                {
                    if (map.IsMemoryEstimationAllowed())
                    {
                        AddMemory(-GetMemory() / keyCount);
                    }
                    else
                    {
                        V old = GetValue(index)!;
                        AddMemory(-Constants.MemoryPointer - map.EvaluateMemoryForValue(old));
                    }
                }

                V[] newValues = CreateValueStorage(keyCount - 1);
                DataUtils.CopyExcept(values, newValues, keyCount, index);
                values = newValues;
            }
        }

        public override int RemoveAllRecursive(long version) => RemovePage(version);

        public override CursorPos<K, V> GetPrependCursorPos(CursorPos<K, V>? cursorPos)
            => new(this, -1, cursorPos);

        public override CursorPos<K, V> GetAppendCursorPos(CursorPos<K, V>? cursorPos)
        {
            int keyCount = GetKeyCount();
            return new CursorPos<K, V>(this, ~keyCount, cursorPos);
        }

        protected override void ReadPayLoad(byte[] buffer, ref int position, int end)
        {
            int keyCount = GetKeyCount();
            values = CreateValueStorage(keyCount);
            map.ValueType.Read(buffer.AsSpan(), values, keyCount, ref position);
        }

        protected override void WriteValues(WriteBuffer buff)
            => map.ValueType.Write(buff, values!, GetKeyCount());

        protected override void WriteChildren(WriteBuffer buff, bool withCounts)
        {
        }

        internal override void WriteUnsavedRecursive(FileStore.PageSerializationManager manager)
        {
            if (!IsSaved())
            {
                _ = Write(manager);
            }
        }

        internal override void ReleaseSavedPages()
        {
        }

        public override int GetRawChildPageCount() => 0;

        protected override int CalculateMemory()
            => base.CalculateMemory() + PageLeafMemory
                + (values is null ? 0 : map.EvaluateMemoryForValues(values, GetKeyCount()));

        protected override void Dump(StringBuilder buff)
        {
            base.Dump(buff);
            int keyCount = GetKeyCount();
            for (int i = 0; i < keyCount; i++)
            {
                if (i > 0)
                {
                    _ = buff.Append(" ");
                }

                _ = buff.Append(GetKey(i));
                if (values is not null)
                {
                    _ = buff.Append(':');
                    _ = buff.Append(GetValue(i));
                }
            }
        }
    }
}
