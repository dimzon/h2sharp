using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using H2Sharp.MvStore.Type;
using H2Sharp.Util;

namespace H2Sharp.MvStore;

/// <summary>
/// A link in a cursor chain that the page surgery can walk without knowing the key type.
/// </summary>
/// <remarks>
/// Public because the public <see cref="DecisionMaker{V}"/> takes one: the rewrite decision only
/// needs two things from a link — the link above it and the position of the page it stands on.
/// Java gets that through erasure; here it is this interface, implemented by
/// <see cref="CursorPos{K, V}"/>.
/// </remarks>
public interface ICursorLink
{
    /// <summary>
    /// The link above, or null at the root.
    /// </summary>
    ICursorLink? ParentLink { get; }

    /// <summary>
    /// The position of the page this link stands on.
    /// </summary>
    long PagePos { get; }
}

/// <summary>
/// A persistent concurrent map: a B-tree whose pages are never mutated, only replaced.
/// </summary>
/// <remarks>
/// Nothing under a root reference ever moves. A write copies the leaf, then each page on the path
/// back to the root, and swaps the new root in with a compare-and-set; a swap that loses the race
/// walks the tree again. That is why readers never lock: the version they hold stays intact no
/// matter what anyone else does, and old versions stay readable until the store stops keeping them.
/// <para>
/// There are two ways to write. The usual one goes through <see cref="Operate"/> with a
/// <see cref="DecisionMaker{V}"/>, which decides per key whether to put, remove, abort, or try
/// again. The fast one is <see cref="Append"/>, but only for a single writer stuffing keys in
/// order: appends sit in a buffer and are flushed into the tree in bulk, either when the buffer
/// fills or when anyone asks for a root.
/// </para>
/// <para>
/// Keys and values may not be null on the way in. A missing value reads back as the default for
/// the value type, and <see cref="ContainsKey"/> is a null check on <see cref="Get(object?)"/>,
/// which means a stored null value would read as absent — the reference has the same rule, only
/// spelled with references.
/// </para>
/// </remarks>
/// <typeparam name="K">the key type</typeparam>
/// <typeparam name="V">the value type</typeparam>
public class MVMap<K, V> : IEnumerable<KeyValuePair<K, V>>, IAnyMap
{
    /// <summary>
    /// The store this map lives in.
    /// </summary>
    public readonly MVStore Store;

    /// <summary>
    /// The current root, swapped with a compare-and-set on every change.
    /// </summary>
    private RootReference<K, V>? rootRef;

    private readonly int id;

    private readonly long createVersion;

    private readonly DataType<K> keyType;

    private readonly DataType<V> valueType;

    private readonly int keysPerPage;

    private readonly bool singleWriter;

    private readonly K[]? keysBuffer;

    private readonly V[]? valuesBuffer;

    private readonly object lockObj = new();

    private volatile bool notificationRequested;

    /// <summary>
    /// Whether the map is closed. Volatile so a write on another thread can not slip into a closed
    /// map unnoticed.
    /// </summary>
    private volatile bool closed;

    private bool readOnly;

    private bool isVolatile;

    private readonly AtomicLong? avgKeySize;

    private readonly AtomicLong? avgValSize;

    private static readonly int CpuCount = Environment.ProcessorCount;

    /// <summary>
    /// A map from a configuration: the store, the key and value types, the id, and whether there
    /// is a single writer appending in order.
    /// </summary>
    protected MVMap(IDictionary<string, object?> config, DataType<K> keyType, DataType<V> valueType)
        : this((MVStore)config["store"]!, keyType, valueType,
            DataUtils.ReadHexInt(config, "id", 0),
            DataUtils.ReadHexLong(config, "createVersion", 0),
            null,
            ((MVStore)config["store"]!).GetKeysPerPage(),
            config.TryGetValue("singleWriter", out object? singleWriter) && singleWriter is true)
    {
        SetInitialRoot(CreateEmptyLeaf(), Store.GetCurrentVersion());
    }

    /// <summary>
    /// A clone sharing the current root but nothing else, for read-only snapshots.
    /// </summary>
    protected MVMap(MVMap<K, V> source)
        : this(source.Store, source.keyType, source.valueType, source.id, source.createVersion,
            Volatile.Read(ref source.rootRef), source.keysPerPage, source.singleWriter)
    {
    }

    /// <summary>
    /// The meta map constructor.
    /// </summary>
    internal MVMap(MVStore store, int id, DataType<K> keyType, DataType<V> valueType)
        : this(store, keyType, valueType, id, 0, null, store.GetKeysPerPage(), false)
    {
        SetInitialRoot(CreateEmptyLeaf(), store.GetCurrentVersion());
    }

    private MVMap(MVStore store, DataType<K> keyType, DataType<V> valueType, int id, long createVersion,
        RootReference<K, V>? root, int keysPerPage, bool singleWriter)
    {
        Store = store;
        this.id = id;
        this.createVersion = createVersion;
        this.keyType = keyType;
        this.valueType = valueType;
        rootRef = root;
        this.keysPerPage = keysPerPage;
        keysBuffer = singleWriter ? keyType.CreateStorage(keysPerPage) : null;
        valuesBuffer = singleWriter ? valueType.CreateStorage(keysPerPage) : null;
        this.singleWriter = singleWriter;
        avgKeySize = keyType.IsMemoryEstimationAllowed() ? new AtomicLong() : null;
        avgValSize = valueType.IsMemoryEstimationAllowed() ? new AtomicLong() : null;
    }

    /// <summary>
    /// A copy sharing the root, for the caller to turn read-only.
    /// </summary>
    protected MVMap<K, V> CloneIt() => new MVMap<K, V>(this);

    /// <summary>
    /// The layout key the root position of a map is stored under.
    /// </summary>
    internal static string GetMapRootKey(int mapId)
        => DataUtils.LayoutRoot + mapId.ToString("x", CultureInfo.InvariantCulture);

    /// <summary>
    /// The meta key a map is stored under.
    /// </summary>
    internal static string GetMapKey(int mapId)
        => DataUtils.MetaMap + mapId.ToString("x", CultureInfo.InvariantCulture);

    /// <summary>
    /// Stores a value, answering the previous one, if any.
    /// </summary>
    public virtual V? Put(K key, V value)
    {
        DataUtils.CheckArgument(value is not null, "The value may not be null");
        return Operate(key, value, DecisionMaker<V>.PutDecision());
    }

    /// <summary>
    /// The first key, or the default when the map is empty.
    /// </summary>
    /// <remarks>
    /// For a value-type key the default stands in for the reference's null: callers that need to
    /// tell them apart check <see cref="IsEmpty"/> first. The same holds for every neighbor below.
    /// </remarks>
    public K? FirstKey() => GetFirstLast(true);

    /// <summary>
    /// The last key, or the default when the map is empty.
    /// </summary>
    public K? LastKey() => GetFirstLast(false);

    /// <summary>
    /// The key at an index, or the default past the end.
    /// </summary>
    public K? GetKey(long index)
    {
        if (index < 0 || index >= SizeAsLong())
        {
            return default;
        }

        Page<K, V> page = GetRootPage();
        long offset = 0;
        while (true)
        {
            if (page.IsLeaf())
            {
                if (index >= offset + page.GetKeyCount())
                {
                    return default;
                }

                return page.GetKey((int)(index - offset));
            }

            int i = 0, size = GetChildPageCount(page);
            for (; i < size; i++)
            {
                long c = page.GetCounts(i);
                if (index < c + offset)
                {
                    break;
                }

                offset += c;
            }

            if (i == size)
            {
                return default;
            }

            page = page.GetChildPage(i);
        }
    }

    /// <summary>
    /// The keys as a read-only list, addressed by index.
    /// </summary>
    public IList<K?> KeyList() => new KeyListView(this);

    /// <summary>
    /// The index a key sits at, or a negative number encoding where it would go.
    /// </summary>
    public long GetKeyIndex(K key)
    {
        Page<K, V> page = GetRootPage();
        if (page.GetTotalCount() == 0)
        {
            return -1;
        }

        long offset = 0;
        while (true)
        {
            int x = page.BinarySearch(key);
            if (page.IsLeaf())
            {
                if (x < 0)
                {
                    offset = -offset;
                }

                return offset + x;
            }

            if (x++ < 0)
            {
                x = -x;
            }

            for (int i = 0; i < x; i++)
            {
                offset += page.GetCounts(i);
            }

            page = page.GetChildPage(x);
        }
    }

    private K? GetFirstLast(bool first)
    {
        Page<K, V> page = GetRootPage();
        return GetFirstLast(page, first);
    }

    private K? GetFirstLast(Page<K, V> page, bool first)
    {
        if (page.GetTotalCount() == 0)
        {
            return default;
        }

        while (true)
        {
            if (page.IsLeaf())
            {
                return page.GetKey(first ? 0 : page.GetKeyCount() - 1);
            }

            page = page.GetChildPage(first ? 0 : GetChildPageCount(page) - 1);
        }
    }

    /// <summary>
    /// The smallest key above a key.
    /// </summary>
    /// <remarks>
    /// A null key sorts before everything, so the ceiling and the higher key of null are the
    /// first key and there is no floor or lower key: the reference gets the same answers by
    /// comparing the null type, which also sorts first.
    /// </remarks>
    public K? HigherKey(object? key) => key is null ? FirstKey() : GetMinMax((K)key, false, true);

    /// <summary>
    /// The smallest key above a key, read at a root.
    /// </summary>
    public K? HigherKey(RootReference<K, V> rootRef, K key) => GetMinMax(rootRef, key, false, true);

    /// <summary>
    /// The smallest key at or above a key.
    /// </summary>
    public K? CeilingKey(object? key) => key is null ? FirstKey() : GetMinMax((K)key, false, false);

    /// <summary>
    /// The largest key at or below a key.
    /// </summary>
    public K? FloorKey(object? key) => key is null ? default : GetMinMax((K)key, true, false);

    /// <summary>
    /// The largest key below a key.
    /// </summary>
    public K? LowerKey(object? key) => key is null ? default : GetMinMax((K)key, true, true);

    /// <summary>
    /// The largest key below a key, read at a root.
    /// </summary>
    public K? LowerKey(RootReference<K, V> rootRef, K key) => GetMinMax(rootRef, key, true, true);

    private K? GetMinMax(K key, bool min, bool excluding)
        => GetMinMax(FlushAndGetRoot(), key, min, excluding);

    private K? GetMinMax(RootReference<K, V> rootRef, K key, bool min, bool excluding)
    {
        TryGetMinMax(rootRef.root, key, min, excluding, out K? result);
        return result;
    }

    private K? GetMinMax(Page<K, V> page, K key, bool min, bool excluding)
    {
        TryGetMinMax(page, key, min, excluding, out K? result);
        return result;
    }

    /// <summary>
    /// A neighboring key and whether there is one: the value alone cannot say it, because a
    /// missing neighbor reads as default and default can be a key.
    /// </summary>
    private bool TryGetMinMax(Page<K, V> page, K key, bool min, bool excluding, out K? result)
    {
        int x = page.BinarySearch(key);
        if (page.IsLeaf())
        {
            if (x < 0)
            {
                x = -x - (min ? 2 : 1);
            }
            else if (excluding)
            {
                x += min ? -1 : 1;
            }

            if (x < 0 || x >= page.GetKeyCount())
            {
                result = default;
                return false;
            }

            result = page.GetKey(x);
            return true;
        }

        if (x++ < 0)
        {
            x = -x;
        }

        while (true)
        {
            if (x < 0 || x >= GetChildPageCount(page))
            {
                result = default;
                return false;
            }

            if (TryGetMinMax(page.GetChildPage(x), key, min, excluding, out result))
            {
                return true;
            }

            x += min ? -1 : 1;
        }
    }

    /// <summary>
    /// The value for a key, or the default when it is not there.
    /// </summary>
    public V? Get(object? key) => Get(GetRootPage(), (K)key!);

    /// <summary>
    /// The value for a key under a page. Overridden for trees that do not sort by the key itself.
    /// </summary>
    public virtual V? Get(Page<K, V> page, K key) => Page<K, V>.Get(page, key);

    /// <summary>
    /// Whether a key is present. A null value reads as absent, because nulls may not be stored.
    /// </summary>
    /// <remarks>
    /// Presence is answered by the key index, not by reading the value: for a value-type value a
    /// missing key reads as default, which a stored default is indistinguishable from.
    /// </remarks>
    public bool ContainsKey(object? key) => GetKeyIndex((K)key!) >= 0;

    /// <summary>
    /// Drops everything.
    /// </summary>
    public void Clear() => ClearIt();

    /// <summary>
    /// Drops everything, answering the root left behind.
    /// </summary>
    internal RootReference<K, V> ClearIt()
    {
        Page<K, V> emptyRootPage = CreateEmptyLeaf();
        int attempt = 0;
        while (true)
        {
            RootReference<K, V> rootReference = FlushAndGetRoot();
            if (rootReference.GetTotalCount() == 0)
            {
                return rootReference;
            }

            bool locked = rootReference.IsLockedByCurrentThread();
            if (!locked)
            {
                if (attempt++ == 0)
                {
                    BeforeWrite();
                }
                else if (attempt > 3 || rootReference.IsLocked())
                {
                    rootReference = LockRoot(rootReference, attempt);
                    locked = true;
                }
            }

            Page<K, V> rootPage = rootReference.root;
            long version = rootReference.version;
            try
            {
                if (!locked)
                {
                    RootReference<K, V>? updated = rootReference.UpdateRootPage(emptyRootPage, attempt);
                    if (updated is null)
                    {
                        continue;
                    }

                    rootReference = updated;
                }

                if (IsPersistent())
                {
                    RegisterUnsavedMemory(rootPage.RemoveAllRecursive(version));
                }

                rootPage = emptyRootPage;
                return rootReference;
            }
            finally
            {
                if (locked)
                {
                    UnlockRoot(rootPage);
                }
            }
        }
    }

    /// <summary>
    /// Tells the store how much changed, when anything is persistent.
    /// </summary>
    protected internal void RegisterUnsavedMemory(int memory)
    {
        if (IsPersistent())
        {
            Store.RegisterUnsavedMemory(memory);
        }
    }

    /// <summary>
    /// Closes the map. Writes after this fail.
    /// </summary>
    internal void Close() => closed = true;

    /// <summary>
    /// Whether the map is closed.
    /// </summary>
    public bool IsClosed() => closed;

    /// <summary>
    /// Drops a key, answering the previous value, if any.
    /// </summary>
    public V? Remove(object? key) => Operate((K)key!, default, DecisionMaker<V>.RemoveDecision());

    /// <summary>
    /// Stores a value only when the key is absent, answering the value already there, if any.
    /// </summary>
    public V? PutIfAbsent(K key, V value) => Operate(key, value, DecisionMaker<V>.IfAbsentDecision());

    /// <summary>
    /// Drops a key only when it holds an equal value.
    /// </summary>
    public bool Remove(object? key, object? value)
    {
        var decisionMaker = new EqualsDecisionMaker<V>(valueType, (V?)value, remove: true);
        Operate((K)key!, default, decisionMaker);
        return decisionMaker.GetDecision() != Decision.Abort;
    }

    /// <summary>
    /// Whether two values count as the same: identical, or ordered equal by their type.
    /// </summary>
    internal static bool AreValuesEqual<X>(DataType<X> datatype, X? a, X? b)
        => a is null ? b is null : b is not null && datatype.Compare(a, b) == 0;

    /// <summary>
    /// Swaps a value only when the expected one is there.
    /// </summary>
    public bool Replace(K key, V oldValue, V newValue)
    {
        var decisionMaker = new EqualsDecisionMaker<V>(valueType, oldValue, remove: false);
        V? result = Operate(key, newValue, decisionMaker);
        bool res = decisionMaker.GetDecision() != Decision.Abort;
        Debug.Assert(!res || AreValuesEqual(valueType, oldValue, result), oldValue + " != " + result);
        return res;
    }

    /// <summary>
    /// Swaps a value only when the key is present, answering the previous one, if any.
    /// </summary>
    public V? Replace(K key, V value) => Operate(key, value, DecisionMaker<V>.IfPresentDecision());

    /// <summary>
    /// How two keys order.
    /// </summary>
    internal int Compare(K a, K b) => keyType.Compare(a, b);

    /// <summary>
    /// The key type.
    /// </summary>
    public DataType<K> KeyType => keyType;

    /// <summary>
    /// The value type.
    /// </summary>
    public DataType<V> ValueType => valueType;

    /// <summary>
    /// The key type.
    /// </summary>
    public DataType<K> GetKeyType() => keyType;

    /// <summary>
    /// The value type.
    /// </summary>
    public DataType<V> GetValueType() => valueType;

    /// <summary>
    /// Whether appends go through the single-writer buffer.
    /// </summary>
    internal bool IsSingleWriter() => singleWriter;

    /// <summary>
    /// Reads a page back from the store.
    /// </summary>
    internal Page<K, V> ReadPage(long pos) => Store.ReadPage(this, pos);

    /// <summary>
    /// Takes a root read from the layout, copying it when another map instance owns it.
    /// </summary>
    internal void SetRootPos(long rootPos, long version)
    {
        Page<K, V> root = ReadOrCreateRootPage(rootPos);
        if (!ReferenceEquals(root.map, this))
        {
            // this can only happen on concurrent opening of existing map,
            // when second thread picks up some cached page already owned by
            // the first map's instantiation (both maps share the same id)
            Debug.Assert(id == root.map.id);
            // since it is unknown which one will win the race,
            // let each map instance to have its own copy
            root = root.Copy(this, false);
        }

        SetInitialRoot(root, version - 1);
        SetWriteVersion(version);
    }

    private Page<K, V> ReadOrCreateRootPage(long rootPos)
    {
        Page<K, V> root = rootPos == 0 ? CreateEmptyLeaf() : ReadPage(rootPos);
        return root;
    }

    /// <summary>
    /// Rewrites a saved page through the tree, so that it is read back and written again.
    /// </summary>
    internal bool RewritePage(long pagePos)
    {
        Page<K, V> page = ReadPage(pagePos);
        if (page.GetKeyCount() == 0)
        {
            return true;
        }

        Debug.Assert(page.IsSaved());
        K key = page.GetKey(0);
        if (!IsClosed())
        {
            var decisionMaker = new RewriteDecisionMaker<V>(page.GetPos());
            V? result = Operate(key, default, decisionMaker);
            bool res = decisionMaker.GetDecision() != Decision.Abort;
            Debug.Assert(!res || result is not null);
            return res;
        }

        return false;
    }

    /// <summary>
    /// The keys from a starting point on.
    /// </summary>
    /// <remarks>
    /// The bound is <c>object?</c> rather than <c>K?</c> on purpose: for a value-type key, <c>K?</c>
    /// collapses to <c>K</c> and can neither take null nor spell an open end. An absent bound is
    /// always null here, whatever the key type.
    /// </remarks>
    public IEnumerator<K> KeyIterator(object? from) => Cursor(from, null, false);

    /// <summary>
    /// The keys from a starting point back.
    /// </summary>
    public IEnumerator<K> KeyIteratorReverse(object? from) => Cursor(from, null, true);

    /// <summary>
    /// A cursor over the keys from a starting point on.
    /// </summary>
    public Cursor<K, V> Cursor(object? from) => Cursor(from, null, false);

    /// <summary>
    /// A cursor over the keys between two points, in either direction.
    /// </summary>
    public Cursor<K, V> Cursor(object? from, object? to, bool reverse)
        => Cursor(FlushAndGetRoot(), from, to, reverse);

    /// <summary>
    /// A cursor at a root, for iterators that already flushed.
    /// </summary>
    public Cursor<K, V> Cursor(RootReference<K, V> rootReference, object? from, object? to, bool reverse)
        => new Cursor<K, V>(rootReference, from, to, reverse);

    /// <summary>
    /// The entries as a live set: iterating walks the tree, the size follows the map.
    /// </summary>
    public ICollection<KeyValuePair<K, V>> EntrySet()
    {
        RootReference<K, V> rootReference = FlushAndGetRoot();
        return new EntrySetView(this, rootReference);
    }

    /// <summary>
    /// The keys as a live set. Overridden for trees that do not sort by the key itself.
    /// </summary>
    public virtual ICollection<K> KeySet()
    {
        RootReference<K, V> rootReference = FlushAndGetRoot();
        return new KeySetView(this, rootReference);
    }

    /// <summary>
    /// The entries enumerated, for <c>foreach</c>.
    /// </summary>
    public IEnumerator<KeyValuePair<K, V>> GetEnumerator() => EntrySet().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private sealed class EntrySetView(MVMap<K, V> map, RootReference<K, V> rootReference)
        : ICollection<KeyValuePair<K, V>>
    {
        public int Count => map.Count;

        public bool IsReadOnly => true;

        public IEnumerator<KeyValuePair<K, V>> GetEnumerator()
        {
            Cursor<K, V> cursor = map.Cursor(rootReference, default, default, false);
            while (cursor.HasNext())
            {
                K key = cursor.Next();
                yield return new KeyValuePair<K, V>(key, cursor.GetValue());
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool Contains(KeyValuePair<K, V> item) => map.ContainsKey(item.Key);

        public void CopyTo(KeyValuePair<K, V>[] array, int arrayIndex)
        {
            foreach (KeyValuePair<K, V> entry in this)
            {
                array[arrayIndex++] = entry;
            }
        }

        public void Add(KeyValuePair<K, V> item) => throw new NotSupportedException();

        public bool Remove(KeyValuePair<K, V> item) => throw new NotSupportedException();

        public void Clear() => throw new NotSupportedException();
    }

    private sealed class KeySetView(MVMap<K, V> map, RootReference<K, V> rootReference) : ICollection<K>
    {
        public int Count => map.Count;

        public bool IsReadOnly => true;

        public IEnumerator<K> GetEnumerator() => map.Cursor(rootReference, default, default, false);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool Contains(K item) => map.ContainsKey(item);

        public void CopyTo(K[] array, int arrayIndex)
        {
            foreach (K key in this)
            {
                array[arrayIndex++] = key;
            }
        }

        public void Add(K item) => throw new NotSupportedException();

        public bool Remove(K item) => throw new NotSupportedException();

        public void Clear() => throw new NotSupportedException();
    }

    private sealed class KeyListView(MVMap<K, V> map) : IList<K?>
    {
        public K? this[int index]
        {
            get => map.GetKey(index);
            set => throw new NotSupportedException();
        }

        public int Count => map.Count;

        public bool IsReadOnly => true;

        public int IndexOf(K? item) => (int)map.GetKeyIndex(item!);

        public bool Contains(K? item) => IndexOf(item) >= 0;

        public void CopyTo(K?[] array, int arrayIndex)
        {
            for (int i = 0; i < Count; i++)
            {
                array[arrayIndex++] = map.GetKey(i);
            }
        }

        public IEnumerator<K?> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
            {
                yield return map.GetKey(i);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Add(K? item) => throw new NotSupportedException();

        public void Insert(int index, K? item) => throw new NotSupportedException();

        public bool Remove(K? item) => throw new NotSupportedException();

        public void RemoveAt(int index) => throw new NotSupportedException();

        public void Clear() => throw new NotSupportedException();
    }

    /// <summary>
    /// The name the store knows this map by.
    /// </summary>
    public string? GetName() => Store.GetMapName(id);

    /// <summary>
    /// The store this map lives in.
    /// </summary>
    public MVStore GetStore() => Store;

    /// <summary>
    /// Whether writes reach a file. A volatile map never does.
    /// </summary>
    protected internal bool IsPersistent() => Store.IsPersistent() && !isVolatile;

    /// <summary>
    /// The map id.
    /// </summary>
    public int GetId() => id;

    /// <summary>
    /// The current root page, flushing appends first.
    /// </summary>
    public Page<K, V> GetRootPage() => FlushAndGetRoot().root;

    /// <summary>
    /// The current root.
    /// </summary>
    public RootReference<K, V> GetRoot() => Volatile.Read(ref rootRef)!;

    /// <summary>
    /// The current root, flushing a pending append buffer first.
    /// </summary>
    public RootReference<K, V> FlushAndGetRoot()
    {
        RootReference<K, V> rootReference = GetRoot();
        if (singleWriter && rootReference.GetAppendCounter() > 0)
        {
            return FlushAppendBuffer(rootReference, true);
        }

        return rootReference;
    }

    /// <summary>
    /// Sets the very first root.
    /// </summary>
    internal void SetInitialRoot(Page<K, V> rootPage, long version)
        => Volatile.Write(ref rootRef, new RootReference<K, V>(rootPage, version));

    /// <summary>
    /// Swaps the root when it still holds what the caller saw.
    /// </summary>
    internal bool CompareAndSetRoot(RootReference<K, V> expectedRootReference,
        RootReference<K, V> updatedRootReference)
        => ReferenceEquals(Interlocked.CompareExchange(ref rootRef, updatedRootReference, expectedRootReference),
            expectedRootReference);

    /// <summary>
    /// Walks the root chain back to a version.
    /// </summary>
    internal bool RollbackRoot(long version)
    {
        RootReference<K, V> rootReference = FlushAndGetRoot();
        RootReference<K, V>? previous;
        while (rootReference.version >= version && (previous = rootReference.previous) is not null)
        {
            if (CompareAndSetRoot(rootReference, previous))
            {
                rootReference = previous;
                closed = false;
            }
        }

        SetWriteVersion(version);
        return rootReference.version < version;
    }

    /// <summary>
    /// Swaps in a new root page, answering whether the swap won.
    /// </summary>
    internal static bool UpdateRoot(RootReference<K, V> expectedRootReference, Page<K, V> newRootPage,
        int attemptUpdateCounter)
        => expectedRootReference.UpdateRootPage(newRootPage, attemptUpdateCounter) is not null;

    private void RemoveUnusedOldVersions(RootReference<K, V> rootReference)
        => rootReference.RemoveUnusedOldVersions(Store.GetOldestVersionToKeep());

    /// <summary>
    /// Whether writes fail. A read-only map is usually a version snapshot.
    /// </summary>
    public bool IsReadOnly() => readOnly;

    /// <summary>
    /// Makes the map volatile, or persistent again. Volatile maps never reach a file.
    /// </summary>
    public void SetVolatile(bool volatile_) => isVolatile = volatile_;

    /// <summary>
    /// Whether the map is volatile.
    /// </summary>
    public bool IsVolatile() => isVolatile;

    /// <summary>
    /// Checks a write may happen: the map is open, writable, and the root is not held.
    /// </summary>
    protected internal void BeforeWrite()
    {
        Debug.Assert(!GetRoot().IsLockedByCurrentThread(), GetRoot().ToString());
        if (closed)
        {
            string? mapName = Store.GetMapName(id);
            throw DataUtils.NewMvStoreException(DataUtils.ErrorClosed, "Map {0}({1}) is closed. {2}",
                mapName, id, Store.GetPanicException());
        }

        if (readOnly)
        {
            throw DataUtils.NewUnsupportedOperationException("This map is read-only");
        }

        Store.BeforeWrite(this);
    }

    /// <inheritdoc/>
    public override int GetHashCode() => id;

    /// <inheritdoc/>
    public override bool Equals(object? o) => ReferenceEquals(this, o);

    /// <summary>
    /// How many entries there are, saturating at the largest int.
    /// </summary>
    public int Count
    {
        get
        {
            long size = SizeAsLong();
            return size > int.MaxValue ? int.MaxValue : (int)size;
        }
    }

    /// <summary>
    /// How many entries there are.
    /// </summary>
    public long SizeAsLong() => GetRoot().GetTotalCount();

    /// <summary>
    /// Whether there are no entries.
    /// </summary>
    public bool IsEmpty() => SizeAsLong() == 0;

    /// <summary>
    /// The version this map was created in.
    /// </summary>
    internal long GetCreateVersion() => createVersion;

    /// <summary>
    /// A read-only map answering for an old version.
    /// </summary>
    public MVMap<K, V> OpenVersion(long version)
    {
        if (readOnly)
        {
            throw DataUtils.NewUnsupportedOperationException("This map is read-only; need to call "
                + "the method on the writable map");
        }

        DataUtils.CheckArgument(version >= createVersion,
            "Unknown version {0}; this map was created in version is {1}", version, createVersion);
        RootReference<K, V> rootReference = FlushAndGetRoot();
        RemoveUnusedOldVersions(rootReference);
        RootReference<K, V>? previous;
        while ((previous = rootReference.previous) is not null && previous.version >= version)
        {
            rootReference = previous;
        }

        if (previous is null && version < Store.GetOldestVersionToKeep())
        {
            throw DataUtils.NewIllegalArgumentException("Unknown version {0}", version);
        }

        MVMap<K, V> m = OpenReadOnly(rootReference.root, version);
        Debug.Assert(m.GetVersion() <= version, m.GetVersion() + " <= " + version);
        return m;
    }

    /// <summary>
    /// A read-only map over a stored root position.
    /// </summary>
    internal MVMap<K, V> OpenReadOnly(long rootPos, long version)
    {
        Page<K, V> root = ReadOrCreateRootPage(rootPos);
        return OpenReadOnly(root, version);
    }

    private MVMap<K, V> OpenReadOnly(Page<K, V> root, long version)
    {
        MVMap<K, V> m = CloneIt();
        m.readOnly = true;
        m.SetInitialRoot(root, version);
        return m;
    }

    /// <summary>
    /// The version the current root answers for.
    /// </summary>
    public long GetVersion() => GetRoot().GetVersion();

    /// <summary>
    /// Whether anything changed since a version.
    /// </summary>
    internal bool HasChangesSince(long version) => GetRoot().HasChangesSince(version, IsPersistent());

    /// <summary>
    /// How many children a page has. Overridden for trees that pack pages differently.
    /// </summary>
    protected internal virtual int GetChildPageCount(Page<K, V> page) => page.GetRawChildPageCount();

    /// <summary>
    /// The map type, for the metadata. Plain maps have none.
    /// </summary>
    /// <remarks>
    /// Named to dodge <see cref="object.GetType"/>: the reference calls it <c>getType</c>.
    /// </remarks>
    protected internal virtual string? GetMapType() => null;

    /// <summary>
    /// The metadata line for a map.
    /// </summary>
    protected internal virtual string AsString(string? name)
    {
        StringBuilder buff = new();
        if (name is not null)
        {
            DataUtils.AppendMap(buff, "name", name);
        }

        if (createVersion != 0)
        {
            DataUtils.AppendMap(buff, "createVersion", createVersion);
        }

        string? type = GetMapType();
        if (type is not null)
        {
            DataUtils.AppendMap(buff, "type", type);
        }

        return buff.ToString();
    }

    /// <summary>
    /// Moves the root to a write version, dropping versions nobody keeps.
    /// </summary>
    internal RootReference<K, V>? SetWriteVersion(long writeVersion)
    {
        int attempt = 0;
        while (true)
        {
            RootReference<K, V> rootReference = FlushAndGetRoot();
            if (rootReference.version >= writeVersion)
            {
                return rootReference;
            }

            if (IsClosed())
            {
                // map was closed a while back and can not possibly be in use by now
                // it's time to remove it completely from the store (it was anonymous already)
                if (rootReference.GetVersion() + 1 < Store.GetOldestVersionToKeep())
                {
                    Store.DeregisterMapRoot(id);
                    return null;
                }
            }

            RootReference<K, V>? lockedRootReference = null;
            if (++attempt > 3 || rootReference.IsLocked())
            {
                lockedRootReference = LockRoot(rootReference, attempt);
                rootReference = FlushAndGetRoot();
            }

            try
            {
                RootReference<K, V>? updated = rootReference.TryUnlockAndUpdateVersion(writeVersion, attempt);
                if (updated is not null)
                {
                    lockedRootReference = null;
                    RemoveUnusedOldVersions(updated);
                    return updated;
                }
            }
            finally
            {
                if (lockedRootReference is not null)
                {
                    UnlockRoot();
                }
            }
        }
    }

    /// <summary>
    /// An empty leaf of this map.
    /// </summary>
    internal Page<K, V> CreateEmptyLeaf() => Page<K, V>.CreateEmptyLeaf(this);

    /// <summary>
    /// An empty node of this map.
    /// </summary>
    internal Page<K, V> CreateEmptyNode() => Page<K, V>.CreateEmptyNode(this);

    /// <summary>
    /// Copies another map's tree into this one, page by page.
    /// </summary>
    internal void CopyFrom(MVMap<K, V> sourceMap)
    {
        MVStore.TxCounter txCounter = Store.RegisterVersionUsage();
        try
        {
            BeforeWrite();
            Copy(sourceMap.GetRootPage(), null, 0);
        }
        finally
        {
            Store.DeregisterVersionUsage(txCounter);
        }
    }

    private void Copy(Page<K, V> source, Page<K, V>? parent, int index)
    {
        Page<K, V> target = source.Copy(this, true);
        if (parent is null)
        {
            SetInitialRoot(target, MVStore.InitialVersion);
        }
        else
        {
            parent.SetChild(index, target);
        }

        if (!source.IsLeaf())
        {
            for (int i = 0; i < GetChildPageCount(target); i++)
            {
                if (source.GetChildPagePos(i) != 0)
                {
                    // position 0 means no child
                    // (for example the last entry of an r-tree node)
                    // (the MVMap is also used for r-trees for compacting)
                    Copy(source.GetChildPage(i), target, i);
                }
            }

            target.SetComplete();
        }

        Store.RegisterUnsavedMemoryAndCommitIfNeeded(target.GetMemory());
    }

    /// <summary>
    /// Flushes the append buffer into the tree, leaving at most a page behind unless told to flush
    /// everything.
    /// </summary>
    private RootReference<K, V> FlushAppendBuffer(RootReference<K, V> rootReference, bool fullFlush)
    {
        bool preLocked = rootReference.IsLockedByCurrentThread();
        bool locked = preLocked;
        int keysPerPage = Store.GetKeysPerPage();
        try
        {
            var unsavedMemoryHolder = new IntValueHolder();
            int attempt = 0;
            int keyCount;
            int availabilityThreshold = fullFlush ? 0 : keysPerPage - 1;
            while ((keyCount = rootReference.GetAppendCounter()) > availabilityThreshold)
            {
                if (!locked)
                {
                    // instead of just calling lockRoot() we loop here and check if someone else
                    // already flushed the buffer, then we don't need a lock
                    RootReference<K, V>? lockedRef = TryLock(rootReference, ++attempt);
                    if (lockedRef is null)
                    {
                        rootReference = GetRoot();
                        continue;
                    }

                    rootReference = lockedRef;
                    locked = true;
                }

                Page<K, V> rootPage = rootReference.root;
                long version = rootReference.version;
                CursorPos<K, V>? pos = rootPage.GetAppendCursorPos(null);
                Debug.Assert(pos is not null);
                Debug.Assert(pos.index < 0, pos.index.ToString(CultureInfo.InvariantCulture));
                int index = -pos.index - 1;
                Debug.Assert(index == pos.page.GetKeyCount(),
                    index.ToString(CultureInfo.InvariantCulture) + " != "
                    + pos.page.GetKeyCount().ToString(CultureInfo.InvariantCulture));
                Page<K, V> page = pos.page;
                CursorPos<K, V>? tip = pos;
                pos = pos.parent;

                int remainingBuffer = 0;
                Page<K, V>? splitPage = null;
                int available = keysPerPage - page.GetKeyCount();
                if (available > 0)
                {
                    page = page.Copy();
                    if (keyCount <= available)
                    {
                        page.Expand(keyCount, keysBuffer!, valuesBuffer);
                    }
                    else
                    {
                        page.Expand(available, keysBuffer!, valuesBuffer);
                        keyCount -= available;
                        if (fullFlush)
                        {
                            K[] keys = page.CreateKeyStorage(keyCount);
                            V[] values = page.CreateValueStorage(keyCount);
                            Array.Copy(keysBuffer!, available, keys, 0, keyCount);
                            if (valuesBuffer is not null)
                            {
                                Array.Copy(valuesBuffer, available, values, 0, keyCount);
                            }

                            splitPage = Page<K, V>.CreateLeaf(this, keys, values, 0);
                        }
                        else
                        {
                            Array.Copy(keysBuffer!, available, keysBuffer!, 0, keyCount);
                            if (valuesBuffer is not null)
                            {
                                Array.Copy(valuesBuffer, available, valuesBuffer, 0, keyCount);
                            }

                            remainingBuffer = keyCount;
                        }
                    }
                }
                else
                {
                    tip = tip.parent;
                    splitPage = Page<K, V>.CreateLeaf(this,
                        keysBuffer![..keyCount],
                        valuesBuffer is null ? null : valuesBuffer[..keyCount],
                        0);
                }

                unsavedMemoryHolder.Value = 0;
                if (splitPage is not null)
                {
                    Debug.Assert(ReferenceEquals(splitPage.map, this));
                    Debug.Assert(splitPage.GetKeyCount() > 0);
                    K key = splitPage.GetKey(0);
                    unsavedMemoryHolder.Value += splitPage.GetMemory();
                    while (true)
                    {
                        if (pos is null)
                        {
                            if (page.GetKeyCount() == 0)
                            {
                                page = splitPage;
                            }
                            else
                            {
                                K[] keys = page.CreateKeyStorage(1);
                                keys[0] = key;
                                Page<K, V>.PageReference[] children = Page<K, V>.CreateRefStorage(2);
                                children[0] = new Page<K, V>.PageReference(page);
                                children[1] = new Page<K, V>.PageReference(splitPage);
                                unsavedMemoryHolder.Value += page.GetMemory();
                                page = Page<K, V>.CreateNode(this, keys, children,
                                    page.GetTotalCount() + splitPage.GetTotalCount(), 0);
                            }

                            break;
                        }

                        Page<K, V> c = page;
                        page = pos.page;
                        index = pos.index;
                        pos = pos.parent;
                        page = page.Copy();
                        page.SetChild(index, splitPage);
                        page.InsertNode(index, key, c);
                        keyCount = page.GetKeyCount();
                        int at = keyCount - (page.IsLeaf() ? 1 : 2);
                        if (keyCount <= keysPerPage
                            && (page.GetMemory() < Store.GetMaxPageSize() || at <= 0))
                        {
                            break;
                        }

                        key = page.GetKey(at);
                        splitPage = page.Split(at);
                        unsavedMemoryHolder.Value += page.GetMemory() + splitPage.GetMemory();
                    }
                }

                page = ReplacePage(pos, page, unsavedMemoryHolder);
                RootReference<K, V>? updated =
                    rootReference.UpdatePageAndLockedStatus(page, preLocked || IsPersistent(), remainingBuffer);
                if (updated is not null)
                {
                    // should always be the case, except for spurious failure?
                    locked = preLocked || IsPersistent();
                    if (IsPersistent() && tip is not null)
                    {
                        RegisterUnsavedMemory(unsavedMemoryHolder.Value + tip.ProcessRemovalInfo(version));
                    }

                    Debug.Assert(rootReference.GetAppendCounter() <= availabilityThreshold);
                    break;
                }

                rootReference = GetRoot();
            }
        }
        finally
        {
            if (locked && !preLocked)
            {
                rootReference = UnlockRoot();
            }
        }

        return rootReference;
    }

    private static Page<K, V> ReplacePage(CursorPos<K, V>? path, Page<K, V> replacement,
        IntValueHolder unsavedMemoryHolder)
    {
        int unsavedMemory = replacement.IsSaved() ? 0 : replacement.GetMemory();
        while (path is not null)
        {
            Page<K, V> parent = path.page;
            // condition below should always be true, but older versions (up to 1.4.197)
            // may create single-childed (with no keys) internal nodes, which we skip here
            if (parent.GetKeyCount() > 0)
            {
                Page<K, V> child = replacement;
                replacement = parent.Copy();
                replacement.SetChild(path.index, child);
                unsavedMemory += replacement.GetMemory();
            }

            path = path.parent;
        }

        unsavedMemoryHolder.Value += unsavedMemory;
        return replacement;
    }

    /// <summary>
    /// Appends a key in order. Only for a single writer: appends sit in a buffer and reach the tree
    /// on flush, in bulk.
    /// </summary>
    public void Append(K key, V value)
    {
        if (singleWriter)
        {
            BeforeWrite();
            RootReference<K, V> rootReference = LockRoot(GetRoot(), 1);
            int appendCounter = rootReference.GetAppendCounter();
            try
            {
                if (appendCounter >= keysPerPage)
                {
                    rootReference = FlushAppendBuffer(rootReference, false);
                    appendCounter = rootReference.GetAppendCounter();
                    Debug.Assert(appendCounter < keysPerPage);
                }

                keysBuffer![appendCounter] = key;
                if (valuesBuffer is not null)
                {
                    valuesBuffer[appendCounter] = value;
                }

                ++appendCounter;
            }
            finally
            {
                UnlockRoot(appendCounter);
            }
        }
        else
        {
            Put(key, value);
        }
    }

    /// <summary>
    /// Drops the last key.
    /// </summary>
    public void TrimLast()
    {
        if (singleWriter)
        {
            RootReference<K, V> rootReference = GetRoot();
            int appendCounter = rootReference.GetAppendCounter();
            bool useRegularRemove = appendCounter == 0;
            if (!useRegularRemove)
            {
                rootReference = LockRoot(rootReference, 1);
                try
                {
                    appendCounter = rootReference.GetAppendCounter();
                    useRegularRemove = appendCounter == 0;
                    if (!useRegularRemove)
                    {
                        --appendCounter;
                    }
                }
                finally
                {
                    UnlockRoot(appendCounter);
                }
            }

            if (useRegularRemove)
            {
                Page<K, V> lastLeaf = rootReference.root.GetAppendCursorPos(null).page;
                Debug.Assert(lastLeaf.IsLeaf());
                Debug.Assert(lastLeaf.GetKeyCount() > 0);
                object? key = lastLeaf.GetKey(lastLeaf.GetKeyCount() - 1);
                Remove(key);
            }
        }
        else
        {
            Remove(LastKey());
        }
    }

    /// <inheritdoc/>
    public override string ToString() => AsString(null);

    /// <summary>
    /// Runs one write against the tree, retrying while the root moves under it.
    /// </summary>
    /// <remarks>
    /// Most attempts never lock: the walk goes down, the decision builds a new path to the root,
    /// and the swap either wins or the walk repeats. The lock comes out only when the root is
    /// already held or the fifth attempt fails, which keeps uncontended writes cheap and
    /// contended ones correct.
    /// </remarks>
    public virtual V? Operate(K key, V? value, DecisionMaker<V> decisionMaker)
    {
        CursorPos<K, V>? tip = null;
        var unsavedMemoryHolder = new IntValueHolder();
        for (int attempt = 0; ; decisionMaker.Reset())
        {
            RootReference<K, V> rootReference = FlushAndGetRoot();
            bool locked = rootReference.IsLockedByCurrentThread();
            Debug.Assert(attempt > 0 || !locked, attempt.ToString(CultureInfo.InvariantCulture) + " " + rootReference);
            if (!locked)
            {
                if (attempt++ == 0)
                {
                    BeforeWrite();
                }

                if (attempt > 5 || rootReference.IsLocked())
                {
                    rootReference = LockRoot(rootReference, attempt);
                    locked = true;
                }
            }

            Page<K, V> rootPage = rootReference.root;
            long version = rootReference.version;
            try
            {
                tip = CursorPos<K, V>.TraverseDown(rootPage, key, tip);
                if (!locked && !ReferenceEquals(rootReference, GetRoot()))
                {
                    continue;
                }

                CursorPos<K, V>? cp = decisionMaker.Decide(tip, key, value);
                if (cp is null)
                {
                    continue;
                }

                if (ReferenceEquals(cp, tip))
                {
                    if (!locked && !ReferenceEquals(rootReference, GetRoot()))
                    {
                        continue;
                    }
                }
                else
                {
                    unsavedMemoryHolder.Value = cp.index;
                    rootPage = ReplacePage(cp.parent, cp.page, unsavedMemoryHolder);
                    if (!locked)
                    {
                        RootReference<K, V>? updated = rootReference.UpdateRootPage(rootPage, attempt);
                        if (updated is null)
                        {
                            continue;
                        }

                        rootReference = updated;
                    }

                    if (IsPersistent())
                    {
                        RegisterUnsavedMemory(unsavedMemoryHolder.Value + tip.ProcessRemovalInfo(version));
                    }

                    decisionMaker.OnPageReplaced();
                }

                Page<K, V> page = tip.page;
                int index = tip.index;
                V? result = index < 0 ? default : page.GetValue(index);
                return result;
            }
            finally
            {
                if (locked)
                {
                    UnlockRoot(rootPage);
                }
            }
        }
    }

    private RootReference<K, V> LockRoot(RootReference<K, V> rootReference, int attempt)
    {
        while (true)
        {
            RootReference<K, V>? lockedRootReference = TryLock(rootReference, attempt++);
            if (lockedRootReference is not null)
            {
                return lockedRootReference;
            }

            rootReference = GetRoot();
        }
    }

    /// <summary>
    /// Takes the root lock, waiting with a spin, then a yield, then a short sleep as the attempts
    /// pile up.
    /// </summary>
    protected internal RootReference<K, V>? TryLock(RootReference<K, V> rootReference, int attempt)
    {
        RootReference<K, V>? lockedRootReference = rootReference.TryLock(attempt);
        if (lockedRootReference is not null)
        {
            return lockedRootReference;
        }

        Debug.Assert(!rootReference.IsLockedByCurrentThread(), rootReference.ToString());
        RootReference<K, V>? oldRootReference = rootReference.previous;
        if (attempt < CpuCount)
        {
            Thread.SpinWait(1);
        }
        else
        {
            int estimatedContention = EstimateContention(rootReference, oldRootReference);
            if (attempt < CpuCount + (CpuCount + estimatedContention) / 2)
            {
                Thread.Yield();
            }
            else
            {
                lock (lockObj)
                {
                    notificationRequested = true;
                    try
                    {
                        Monitor.Wait(lockObj, 1);
                    }
                    catch (ThreadInterruptedException)
                    {
                    }
                }
            }
        }

        return null;
    }

    private static int EstimateContention(RootReference<K, V> rootReference,
        RootReference<K, V>? oldRootReference)
    {
        int contention = 1;
        if (oldRootReference is not null)
        {
            long updateAttemptCounter =
                rootReference.updateAttemptCounter - oldRootReference.updateAttemptCounter;
            Debug.Assert(updateAttemptCounter >= 0, updateAttemptCounter.ToString(CultureInfo.InvariantCulture));
            long updateCounter = rootReference.updateCounter - oldRootReference.updateCounter;
            Debug.Assert(updateCounter >= 0, updateCounter.ToString(CultureInfo.InvariantCulture));
            Debug.Assert(updateAttemptCounter >= updateCounter,
                updateAttemptCounter.ToString(CultureInfo.InvariantCulture) + " >= "
                + updateCounter.ToString(CultureInfo.InvariantCulture));
            contention += (int)((updateAttemptCounter + 1) / (updateCounter + 1));
        }

        return contention;
    }

    private RootReference<K, V> UnlockRoot() => UnlockRoot(null, -1);

    /// <summary>
    /// Unlocks over a new root page.
    /// </summary>
    internal void UnlockRoot(Page<K, V>? newRootPage) => UnlockRoot(newRootPage, -1);

    private void UnlockRoot(int appendCounter) => UnlockRoot(null, appendCounter);

    private RootReference<K, V> UnlockRoot(Page<K, V>? newRootPage, int appendCounter)
    {
        RootReference<K, V>? updatedRootReference;
        do
        {
            RootReference<K, V> rootReference = GetRoot();
            Debug.Assert(rootReference.IsLockedByCurrentThread());
            updatedRootReference = rootReference.UpdatePageAndLockedStatus(
                newRootPage ?? rootReference.root,
                false,
                appendCounter == -1 ? rootReference.GetAppendCounter() : appendCounter);
        } while (updatedRootReference is null);

        NotifyWaiters();
        return updatedRootReference;
    }

    private void NotifyWaiters()
    {
        if (notificationRequested)
        {
            lock (lockObj)
            {
                notificationRequested = false;
                Monitor.Pulse(lockObj);
            }
        }
    }

    /// <summary>
    /// Whether sizes are sampled rather than measured exactly.
    /// </summary>
    internal bool IsMemoryEstimationAllowed() => avgKeySize is not null || avgValSize is not null;

    /// <summary>
    /// The sampled memory of a run of keys.
    /// </summary>
    internal int EvaluateMemoryForKeys(K[] storage, int count)
        => avgKeySize is null
            ? CalculateMemory(keyType, storage, count)
            : MemoryEstimator.EstimateMemory(avgKeySize, keyType, storage, count);

    /// <summary>
    /// The sampled memory of a run of values.
    /// </summary>
    internal int EvaluateMemoryForValues(V[] storage, int count)
        => avgValSize is null
            ? CalculateMemory(valueType, storage, count)
            : MemoryEstimator.EstimateMemory(avgValSize, valueType, storage, count);

    private static int CalculateMemory<T>(DataType<T> dataType, T[] storage, int count)
    {
        int mem = count * Engine.Constants.MemoryPointer;
        for (int i = 0; i < count; i++)
        {
            mem += dataType.GetMemory(storage[i]);
        }

        return mem;
    }

    /// <summary>
    /// The sampled memory of one key.
    /// </summary>
    internal int EvaluateMemoryForKey(K key)
        => avgKeySize is null ? keyType.GetMemory(key) : MemoryEstimator.EstimateMemory(avgKeySize, keyType, key);

    /// <summary>
    /// The sampled memory of one value.
    /// </summary>
    internal int EvaluateMemoryForValue(V value)
        => avgValSize is null
            ? valueType.GetMemory(value)
            : MemoryEstimator.EstimateMemory(avgValSize, valueType, value);

    /// <summary>
    /// What share of the size samples still gets measured, in percent.
    /// </summary>
    internal static int SamplingPct(AtomicLong stats) => MemoryEstimator.SamplingPct(stats);

    /// <summary>
    /// Builds a map from a configuration.
    /// </summary>
    /// <typeparam name="M">the map type</typeparam>
    /// <typeparam name="KK">the key type</typeparam>
    /// <typeparam name="VV">the value type</typeparam>
    public interface IMapBuilder<M, KK, VV> where M : MVMap<KK, VV>
    {
        /// <summary>
        /// Builds the map.
        /// </summary>
        M Create(MVStore store, IDictionary<string, object?> config);

        /// <summary>
        /// The key type.
        /// </summary>
        DataType<KK>? GetKeyType();

        /// <summary>
        /// The value type.
        /// </summary>
        DataType<VV>? GetValueType();

        /// <summary>
        /// Sets the key type.
        /// </summary>
        void SetKeyType(DataType<KK> dataType);

        /// <summary>
        /// Sets the value type.
        /// </summary>
        void SetValueType(DataType<VV> dataType);
    }

    /// <summary>
    /// A builder holding the key and value types.
    /// </summary>
    public abstract class BasicBuilder<M, KK, VV> : IMapBuilder<M, KK, VV> where M : MVMap<KK, VV>
    {
        private DataType<KK>? keyType;

        private DataType<VV>? valueType;

        /// <summary>
        /// An empty builder.
        /// </summary>
        protected BasicBuilder()
        {
        }

        /// <inheritdoc/>
        public DataType<KK>? GetKeyType() => keyType;

        /// <inheritdoc/>
        public DataType<VV>? GetValueType() => valueType;

        /// <inheritdoc/>
        public void SetKeyType(DataType<KK> dataType) => keyType = dataType;

        /// <inheritdoc/>
        public void SetValueType(DataType<VV> dataType) => valueType = dataType;

        /// <summary>
        /// Sets the key type, fluently.
        /// </summary>
        public BasicBuilder<M, KK, VV> KeyType(DataType<KK> dataType)
        {
            SetKeyType(dataType);
            return this;
        }

        /// <summary>
        /// Sets the value type, fluently.
        /// </summary>
        public BasicBuilder<M, KK, VV> ValueType(DataType<VV> dataType)
        {
            SetValueType(dataType);
            return this;
        }

        /// <inheritdoc/>
        public M Create(MVStore store, IDictionary<string, object?> config)
        {
            if (GetKeyType() is null)
            {
                SetKeyType(new ObjectDataType<KK>());
            }

            if (GetValueType() is null)
            {
                SetValueType(new ObjectDataType<VV>());
            }

            DataType<KK> resolvedKeyType = GetKeyType()!;
            DataType<VV> resolvedValueType = GetValueType()!;
            config["store"] = store;
            config["key"] = resolvedKeyType;
            config["val"] = resolvedValueType;
            return Create(config);
        }

        /// <summary>
        /// Builds the map from a configuration.
        /// </summary>
        protected abstract M Create(IDictionary<string, object?> config);
    }

    /// <summary>
    /// Builds plain maps, optionally single-writer.
    /// </summary>
    public class Builder<KK, VV> : BasicBuilder<MVMap<KK, VV>, KK, VV>
    {
        private bool singleWriter;

        /// <summary>
        /// An empty builder.
        /// </summary>
        public Builder()
        {
        }

        /// <summary>
        /// Sets the key type, fluently.
        /// </summary>
        public new Builder<KK, VV> KeyType(DataType<KK> dataType)
        {
            SetKeyType(dataType);
            return this;
        }

        /// <summary>
        /// Sets the value type, fluently.
        /// </summary>
        public new Builder<KK, VV> ValueType(DataType<VV> dataType)
        {
            SetValueType(dataType);
            return this;
        }

        /// <summary>
        /// Turns on the single-writer append buffer.
        /// </summary>
        public Builder<KK, VV> SingleWriter()
        {
            singleWriter = true;
            return this;
        }

        /// <inheritdoc/>
        protected override MVMap<KK, VV> Create(IDictionary<string, object?> config)
        {
            config["singleWriter"] = singleWriter;
            config.TryGetValue("type", out object? type);
            if (type is null || "rtree".Equals(type))
            {
                return new MVMap<KK, VV>(config, GetKeyType()!, GetValueType()!);
            }

            throw new ArgumentException("Incompatible map type");
        }
    }

    private sealed class EqualsDecisionMaker<VV> : DecisionMaker<VV>
    {
        private readonly DataType<VV> dataType;

        private readonly VV? expectedValue;

        private readonly bool remove;

        private Decision? decision;

        internal EqualsDecisionMaker(DataType<VV> dataType, VV? expectedValue, bool remove)
        {
            this.dataType = dataType;
            this.expectedValue = expectedValue;
            this.remove = remove;
        }

        public override Decision Decide(VV? existingValue, VV? providedValue)
        {
            Debug.Assert(decision is null);
            decision = !AreValuesEqual(dataType, expectedValue, existingValue) ? Decision.Abort
                : providedValue is null ? Decision.Remove : Decision.Put;
            return decision.Value;
        }

        public override Decision Decide(VV? existingValue, VV? providedValue, ICursorLink? tip, bool present)
        {
            Debug.Assert(decision is null);
            // Whether to remove or put travels with the maker, not in a null value: for a value
            // type a missing value reads as default, which a stored default is indistinguishable
            // from, so a null check here would remove-or-put by accident.
            decision = !present || !AreValuesEqual(dataType, expectedValue, existingValue) ? Decision.Abort
                : remove ? Decision.Remove : Decision.Put;
            return decision.Value;
        }

        public override void Reset() => decision = null;

        internal Decision? GetDecision() => decision;

        public override string ToString() => "equals_to " + expectedValue;
    }

    private sealed class RewriteDecisionMaker<VV> : DecisionMaker<VV>
    {
        private readonly long pagePos;

        private Decision? decision;

        internal RewriteDecisionMaker(long pagePos) => this.pagePos = pagePos;

        public override Decision Decide(VV? existingValue, VV? providedValue, ICursorLink? tip, bool present)
        {
            Debug.Assert(decision is null);
            decision = Decision.Abort;
            if (!DataUtils.IsLeafPosition(pagePos))
            {
                while ((tip = tip?.ParentLink) is not null)
                {
                    if (tip.PagePos == pagePos)
                    {
                        decision = present ? Decide(existingValue, providedValue) : Decision.Abort;
                        break;
                    }
                }
            }
            else if (tip is not null && tip.PagePos == pagePos)
            {
                decision = present ? Decide(existingValue, providedValue) : Decision.Abort;
            }

            return decision ?? Decision.Abort;
        }

        public override Decision Decide(VV? existingValue, VV? providedValue)
        {
            decision = existingValue is null ? Decision.Abort : Decision.Put;
            return decision.Value;
        }

        public override T SelectValue<T>(T existingValue, T providedValue) => existingValue;

        public override void Reset() => decision = null;

        internal Decision? GetDecision() => decision;

        public override string ToString() => "rewrite";
    }

    private sealed class IntValueHolder
    {
        internal int Value;
    }

    // IAnyMap: the store sees every map through this, without knowing the key and value types.

    /// <inheritdoc/>
    int IAnyMap.MapId => id;

    /// <inheritdoc/>
    long IAnyMap.MapCreateVersion => createVersion;

    /// <inheritdoc/>
    bool IAnyMap.MapIsClosed => closed;

    /// <inheritdoc/>
    bool IAnyMap.MapIsVolatile => isVolatile;

    /// <inheritdoc/>
    bool IAnyMap.MapIsSingleWriter => singleWriter;

    /// <inheritdoc/>
    void IAnyMap.MapClose() => Close();

    /// <inheritdoc/>
    IRootView? IAnyMap.MapSetWriteVersion(long version) => SetWriteVersion(version);

    /// <inheritdoc/>
    bool IAnyMap.MapHasChangesSince(long version) => HasChangesSince(version);

    /// <inheritdoc/>
    bool IAnyMap.MapRollbackRoot(long version) => RollbackRoot(version);

    /// <inheritdoc/>
    void IAnyMap.MapSetRootPos(long rootPos, long version) => SetRootPos(rootPos, version);

    /// <inheritdoc/>
    object IAnyMap.MapGetRootPage() => GetRootPage();

    /// <inheritdoc/>
    string IAnyMap.MapAsString(string? name) => AsString(name);

    /// <inheritdoc/>
    (long updateCounter, long updateAttemptCounter) IAnyMap.MapClearIt()
    {
        RootReference<K, V> root = ClearIt();
        return (root.updateCounter, root.updateAttemptCounter);
    }

    /// <inheritdoc/>
    (long updateCounter, long updateAttemptCounter) IAnyMap.MapGetRootCounters()
    {
        RootReference<K, V> root = GetRoot();
        return (root.updateCounter, root.updateAttemptCounter);
    }

    /// <inheritdoc/>
    bool IAnyMap.MapRootIsLockedByCurrentThread() => GetRoot().IsLockedByCurrentThread();

    /// <inheritdoc/>
    bool IAnyMap.MapRewritePage(long pagePos) => RewritePage(pagePos);
}

/// <summary>
/// What a decision maker wants done with a key.
/// </summary>
/// <remarks>
/// The reference nests this in the map; here it is top-level so that
/// <see cref="DecisionMaker{V}"/> can answer with it without naming the map's key type.
/// </remarks>
public enum Decision
{
    /// <summary>
    /// Leave the tree alone.
    /// </summary>
    Abort,

    /// <summary>
    /// Drop the key.
    /// </summary>
    Remove,

    /// <summary>
    /// Store the value.
    /// </summary>
    Put,

    /// <summary>
    /// Walk the tree again.
    /// </summary>
    Repeat,
}

/// <summary>
/// Decides what an <see cref="MVMap{K, V}"/> operation does with a key: put, remove, abort, or walk
/// again.
/// </summary>
/// <remarks>
/// The reference nests this in the map with its own value parameter shadowing the map's; here it
/// is top-level, which says the same thing without the shadow.
/// </remarks>
/// <remarks>
/// The entry-level question — <see cref="DecisionMaker{V}.Decide(V?, V?)"/> — sees only the value
/// already there and the value offered. The page-level answer —
/// <see cref="DecisionMaker{V}.Decide{K}(CursorPos{K, V}?, K, V?)"/> —
/// does the tree surgery for a put or a remove: copying the leaf, splitting it while it overflows,
/// growing a new root when the old one splits, or collapsing the path when the last key of a leaf
/// goes away.
/// <para>
/// The singletons behind <see cref="DefaultDecision"/>, <see cref="PutDecision"/> and the rest are
/// stateless. The ones that remember — the equality check and the page rewrite — reset on every
/// retry, because a decision made against a moved root must not survive it.
/// </para>
/// </remarks>
/// <typeparam name="V">the value type of the map</typeparam>
public abstract class DecisionMaker<V>
{
    // One singleton per closed value type: a DecisionMaker<object> is not a DecisionMaker<V> the
    // way erasure makes every DecisionMaker one class, so the shared instances live on the
    // generic class itself instead of being cast through object.
    private static readonly DecisionMaker<V> Default = new DefaultDecisionMaker();

    private static readonly DecisionMaker<V> Put = new PutDecisionMaker();

    private static readonly DecisionMaker<V> Remove = new RemoveDecisionMaker();

    private static readonly DecisionMaker<V> IfAbsent = new IfAbsentDecisionMaker();

    private static readonly DecisionMaker<V> IfPresent = new IfPresentDecisionMaker();

    /// <summary>
    /// The default: a null value removes, anything else puts.
    /// </summary>
    public static DecisionMaker<V> DefaultDecision() => Default;

    /// <summary>
    /// Always puts.
    /// </summary>
    public static DecisionMaker<V> PutDecision() => Put;

    /// <summary>
    /// Always removes.
    /// </summary>
    public static DecisionMaker<V> RemoveDecision() => Remove;

    /// <summary>
    /// Puts only when there is nothing there, for <c>putIfAbsent</c>.
    /// </summary>
    public static DecisionMaker<V> IfAbsentDecision() => IfAbsent;

    /// <summary>
    /// Puts only when there is something there, for <c>replace</c>.
    /// </summary>
    public static DecisionMaker<V> IfPresentDecision() => IfPresent;

    /// <summary>
    /// Decides about one entry: the value already there against the value offered.
    /// </summary>
    /// <remarks>
    /// Kept for compatibility with the reference's shape; the operation calls the four-argument
    /// question below, which also says whether the key is there at all.
    /// </remarks>
    public abstract Decision Decide(V? existingValue, V? providedValue);

    /// <summary>
    /// Decides about one entry, at a cursor position, knowing whether the key is there.
    /// </summary>
    /// <remarks>
    /// Presence travels separately because a missing value reads as default, and for a value
    /// type that is indistinguishable from a stored default. Delegates to the entry-level
    /// question, which is enough for decisions that do not care.
    /// </remarks>
    public virtual Decision Decide(V? existingValue, V? providedValue, ICursorLink? tip, bool present)
        => Decide(existingValue, providedValue);

    /// <summary>
    /// Which value actually gets stored. The offered one, unless overridden.
    /// </summary>
    public virtual T SelectValue<T>(T existingValue, T providedValue) => providedValue;

    /// <summary>
    /// Forgets everything, for the next retry.
    /// </summary>
    public virtual void Reset()
    {
    }

    /// <summary>
    /// Runs after the new path replaced the old one.
    /// </summary>
    public virtual void OnPageReplaced()
    {
    }

    /// <summary>
    /// Does the tree surgery the entry-level decision asked for.
    /// </summary>
    /// <remarks>
    /// Answering with the tip itself means nothing changed; answering null means walking again.
    /// Anything else is a new leaf whose index field carries the unsaved memory, for the caller to
    /// pick up — an odd channel, but it is the reference's own.
    /// </remarks>
    public CursorPos<K, V>? Decide<K>(CursorPos<K, V>? tip, K key, V? providedValue)
    {
        Page<K, V> p = tip!.page;
        Debug.Assert(p.IsLeaf());
        int index = tip.index;
        bool present = index >= 0;
        V? result = present ? p.GetValue(index) : default;
        Decision decision = Decide(result, providedValue, tip, present);
        CursorPos<K, V>? pos = tip.parent;
        MVMap<K, V> map = p.map;

        switch (decision)
        {
            case Decision.Remove:
                {
                    if (index < 0)
                    {
                        return tip;
                    }

                    if (p.GetTotalCount() == 1 && pos is not null)
                    {
                        int keyCount;
                        do
                        {
                            p = pos.page;
                            index = pos.index;
                            pos = pos.parent;
                            keyCount = p.GetKeyCount();
                            // condition below should always be false, but older
                            // versions (up to 1.4.197) may create
                            // single-childed (with no keys) internal nodes,
                            // which we skip here
                        } while (keyCount == 0 && pos is not null);

                        if (keyCount <= 1)
                        {
                            if (keyCount == 1)
                            {
                                Debug.Assert(index <= 1);
                                p = p.GetChildPage(1 - index);
                            }
                            else
                            {
                                // if root happens to be such single-childed
                                // (with no keys) internal node, then just
                                // replace it with empty leaf
                                p = Page<K, V>.CreateEmptyLeaf(map);
                            }

                            return new CursorPos<K, V>(p, 0, pos);
                        }
                    }

                    p = p.Copy();
                    p.Remove(index);
                    return new CursorPos<K, V>(p, 0, pos);
                }

            case Decision.Put:
                {
                    int unsavedMemory = 0;
                    V? value = SelectValue(result!, providedValue!);
                    p = p.Copy();
                    if (index < 0)
                    {
                        p.InsertLeaf(-index - 1, key, value);
                        int keyCount;
                        MVStore store = map.Store;
                        while ((keyCount = p.GetKeyCount()) > store.GetKeysPerPage()
                            || p.GetMemory() > store.GetMaxPageSize()
                                && keyCount > (p.IsLeaf() ? 1 : 2))
                        {
                            long totalCount = p.GetTotalCount();
                            int at = keyCount >> 1;
                            K k = p.GetKey(at);
                            Page<K, V> split = p.Split(at);
                            unsavedMemory += p.GetMemory() + split.GetMemory();
                            // if root was split, create a new root with two children (increase tree height)
                            if (pos is null)
                            {
                                K[] keys = p.CreateKeyStorage(1);
                                keys[0] = k;
                                Page<K, V>.PageReference[] children = Page<K, V>.CreateRefStorage(2);
                                children[0] = new Page<K, V>.PageReference(p);
                                children[1] = new Page<K, V>.PageReference(split);
                                p = Page<K, V>.CreateNode(map, keys, children, totalCount, 0);
                                return new CursorPos<K, V>(p, unsavedMemory, pos);
                            }

                            Page<K, V> c = p;
                            p = pos.page;
                            index = pos.index;
                            pos = pos.parent;
                            p = p.Copy();
                            p.SetChild(index, split);
                            p.InsertNode(index, k, c);
                        }
                    }
                    else
                    {
                        p.SetValue(index, value);
                    }

                    return new CursorPos<K, V>(p, unsavedMemory, pos);
                }

            case Decision.Abort:
                return tip;

            case Decision.Repeat:
            default:
                return null;
        }
    }

    private sealed class DefaultDecisionMaker : DecisionMaker<V>
    {
        public override Decision Decide(V? existingValue, V? providedValue)
            => providedValue is null ? Decision.Remove : Decision.Put;

        public override string ToString() => "default";
    }

    private sealed class PutDecisionMaker : DecisionMaker<V>
    {
        public override Decision Decide(V? existingValue, V? providedValue) => Decision.Put;

        public override string ToString() => "put";
    }

    private sealed class RemoveDecisionMaker : DecisionMaker<V>
    {
        public override Decision Decide(V? existingValue, V? providedValue) => Decision.Remove;

        public override string ToString() => "remove";
    }

    private sealed class IfAbsentDecisionMaker : DecisionMaker<V>
    {
        public override Decision Decide(V? existingValue, V? providedValue)
            => existingValue is null ? Decision.Put : Decision.Abort;

        public override Decision Decide(V? existingValue, V? providedValue, ICursorLink? tip, bool present)
            => present ? Decision.Abort : Decision.Put;

        public override string ToString() => "if_absent";
    }

    private sealed class IfPresentDecisionMaker : DecisionMaker<V>
    {
        public override Decision Decide(V? existingValue, V? providedValue)
            => existingValue is not null ? Decision.Put : Decision.Abort;

        public override Decision Decide(V? existingValue, V? providedValue, ICursorLink? tip, bool present)
            => present ? Decision.Put : Decision.Abort;

        public override string ToString() => "if_present";
    }
}
