using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using H2Sharp.MvStore;

namespace H2Sharp.MvStore.Cache;

/// <summary>
/// A scan-resistant cache keyed by longs: pages and chunk tables live here.
/// </summary>
/// <remarks>
/// An approximation of the LIRS replacement algorithm, split into segments that are each a small
/// LIRS cache of their own. Hot entries sit on the stack; cold ones queue, and the cold that fall
/// off the queue linger as non-residents so that a rescan does not evict everything hot. Entries
/// are only moved to the top of the stack once enough others have moved first, which keeps a
/// burst of accesses from churning the order.
/// <para>
/// The concurrency contract is the reference's: reads walk the old segment without locking, and
/// writes lock the segment they touch. Counters and memory totals are plain fields read without
/// locking, exactly as racy and exactly as harmless.
/// </para>
/// </remarks>
/// <typeparam name="V">the value type</typeparam>
public class CacheLongKeyLIRS<V>
{
    /// <summary>
    /// The maximum memory this cache should use.
    /// </summary>
    private long maxMemory;

    private readonly Segment[] segments;

    private readonly int segmentCount;

    private readonly int segmentShift;

    private readonly int segmentMask;

    private readonly int stackMoveDistance;

    private readonly int nonResidentQueueSize;

    private readonly int nonResidentQueueSizeHigh;

    private readonly object sync = new();

    /// <summary>
    /// A cache from a configuration.
    /// </summary>
    public CacheLongKeyLIRS(Config config)
    {
        nonResidentQueueSize = config.NonResidentQueueSize;
        nonResidentQueueSizeHigh = config.NonResidentQueueSizeHigh;
        DataUtils.CheckArgument(
            BitOperations.PopCount((uint)config.SegmentCount) == 1,
            "The segment count must be a power of 2, is {0}", config.SegmentCount);
        segmentCount = config.SegmentCount;
        segmentMask = segmentCount - 1;
        stackMoveDistance = config.StackMoveDistance;
        // use the high bits for the segment
        segmentShift = 32 - BitOperations.PopCount((uint)segmentMask);
        segments = new Segment[segmentCount];
        Clear();
        SetMaxMemory(config.MaxMemory);
    }

    /// <summary>
    /// Drops everything.
    /// </summary>
    public void Clear()
    {
        long max = GetMaxItemSize();
        for (int i = 0; i < segmentCount; i++)
        {
            segments[i] = new Segment(max, stackMoveDistance, 8, nonResidentQueueSize,
                nonResidentQueueSizeHigh);
        }
    }

    /// <summary>
    /// The largest item that fits.
    /// </summary>
    public long GetMaxItemSize() => Math.Max(1, maxMemory / segmentCount);

    private Entry? Find(long key)
    {
        int hash = GetHash(key);
        return GetSegment(hash).Find(key, hash);
    }

    /// <summary>
    /// Whether a resident entry exists, without touching the order.
    /// </summary>
    public bool ContainsKey(long key)
    {
        Entry? e = Find(key);
        return e is not null && e.value is not null;
    }

    /// <summary>
    /// The value, if resident, without touching the order.
    /// </summary>
    public V? Peek(long key)
    {
        Entry? e = Find(key);
        return e is null ? default : e.GetValue();
    }

    /// <summary>
    /// Stores a value, sizing it by sampling.
    /// </summary>
    public V? Put(long key, V value) => Put(key, value, SizeOf(value));

    /// <summary>
    /// Stores a value: unknown entries go cold, known ones go hot.
    /// </summary>
    public V? Put(long key, V value, long memory)
    {
        if (value is null)
        {
            throw DataUtils.NewIllegalArgumentException("The value may not be null");
        }

        int hash = GetHash(key);
        int segmentIndex = GetSegmentIndex(hash);
        Segment s = segments[segmentIndex];
        // check whether resize is required: synchronize on s, to avoid
        // concurrent resizes (concurrent reads read
        // from the old segment)
        lock (s.Sync)
        {
            s = ResizeIfNeeded(s, segmentIndex);
            return s.Put(key, hash, value, memory);
        }
    }

    private Segment ResizeIfNeeded(Segment s, int segmentIndex)
    {
        int newLen = s.GetNewMapLen();
        if (newLen == 0)
        {
            return s;
        }

        // another thread might have resized
        // (as we retrieved the segment before synchronizing on it)
        Segment s2 = segments[segmentIndex];
        if (ReferenceEquals(s, s2))
        {
            // no other thread resized, so we do
            s = new Segment(s, newLen);
            segments[segmentIndex] = s;
        }

        return s;
    }

    /// <summary>
    /// How big a value is. Sampling size by default.
    /// </summary>
    protected virtual long SizeOf(V value) => 16;

    /// <summary>
    /// Drops an entry, resident or not.
    /// </summary>
    public V? Remove(long key)
    {
        int hash = GetHash(key);
        int segmentIndex = GetSegmentIndex(hash);
        Segment s = segments[segmentIndex];
        // check whether resize is required: synchronize on s, to avoid
        // concurrent resizes (concurrent reads read
        // from the old segment)
        lock (s.Sync)
        {
            s = ResizeIfNeeded(s, segmentIndex);
            return s.Remove(key, hash);
        }
    }

    /// <summary>
    /// The memory of a resident entry, or zero.
    /// </summary>
    public long GetMemory(long key)
    {
        Entry? e = Find(key);
        return e is null ? 0L : e.GetMemory();
    }

    /// <summary>
    /// The overhead per value.
    /// </summary>
    public static int GetMemoryOverhead() => Entry.TotalMemoryOverhead;

    /// <summary>
    /// The value, if resident, keeping hot entries hot.
    /// </summary>
    public V? Get(long key)
    {
        int hash = GetHash(key);
        Segment s = GetSegment(hash);
        Entry? e = s.Find(key, hash);
        return s.Get(e);
    }

    private Segment GetSegment(int hash) => segments[GetSegmentIndex(hash)];

    private int GetSegmentIndex(int hash) => (hash >>> segmentShift) & segmentMask;

    /// <summary>
    /// The hash, spread a second time against tight key ranges.
    /// </summary>
    internal static int GetHash(long key)
    {
        int hash = (int)(((ulong)key >> 32) ^ (ulong)key);
        // a supplemental secondary hash function
        // to protect against hash codes that don't differ much
        hash = ((hash >>> 16) ^ hash) * 0x45d9f3b;
        hash = ((hash >>> 16) ^ hash) * 0x45d9f3b;
        hash = (hash >>> 16) ^ hash;
        return hash;
    }

    /// <summary>
    /// The memory in use.
    /// </summary>
    public long GetUsedMemory()
    {
        long x = 0;
        foreach (Segment s in segments)
        {
            x += s.usedMemory;
        }

        return x;
    }

    /// <summary>
    /// Sets the memory limit. Entries are not removed at once; only the limit changes.
    /// </summary>
    public void SetMaxMemory(long maxMemory)
    {
        DataUtils.CheckArgument(maxMemory > 0, "Max memory must be larger than 0, is {0}", maxMemory);
        this.maxMemory = maxMemory;
        long max = 1 + maxMemory / segments.Length;
        foreach (Segment s in segments)
        {
            s.SetMaxMemory(max);
        }
    }

    /// <summary>
    /// The memory limit.
    /// </summary>
    public long GetMaxMemory() => maxMemory;

    /// <summary>
    /// The resident entries.
    /// </summary>
    public ICollection<KeyValuePair<long, V>> EntrySet()
    {
        lock (sync)
        {
            return GetMap();
        }
    }

    /// <summary>
    /// The resident keys.
    /// </summary>
    public ISet<long> KeySet()
    {
        var set = new HashSet<long>();
        foreach (Segment s in segments)
        {
            set.UnionWith(s.KeySet());
        }

        return set;
    }

    /// <summary>
    /// How many non-resident entries there are.
    /// </summary>
    public int SizeNonResident()
    {
        int x = 0;
        foreach (Segment s in segments)
        {
            x += s.queue2Size;
        }

        return x;
    }

    /// <summary>
    /// The length of the map arrays put together.
    /// </summary>
    public int SizeMapArray()
    {
        int x = 0;
        foreach (Segment s in segments)
        {
            x += s.entries.Length;
        }

        return x;
    }

    /// <summary>
    /// How many hot entries there are.
    /// </summary>
    public int SizeHot()
    {
        int x = 0;
        foreach (Segment s in segments)
        {
            x += s.mapSize - s.queueSize - s.queue2Size;
        }

        return x;
    }

    /// <summary>
    /// How many hits there were.
    /// </summary>
    public long GetHits()
    {
        long x = 0;
        foreach (Segment s in segments)
        {
            x += s.hits;
        }

        return x;
    }

    /// <summary>
    /// How many misses there were.
    /// </summary>
    public long GetMisses()
    {
        long x = 0;
        foreach (Segment s in segments)
        {
            x += s.misses;
        }

        return x;
    }

    /// <summary>
    /// How many resident entries there are.
    /// </summary>
    public int Size()
    {
        int x = 0;
        foreach (Segment s in segments)
        {
            x += s.mapSize - s.queue2Size;
        }

        return x;
    }

    /// <summary>
    /// The keys, optionally only the cold or the non-resident ones.
    /// </summary>
    public List<long> Keys(bool cold, bool nonResident)
    {
        var keys = new List<long>();
        foreach (Segment s in segments)
        {
            keys.AddRange(s.Keys(cold, nonResident));
        }

        return keys;
    }

    /// <summary>
    /// The resident values.
    /// </summary>
    public List<V> Values()
    {
        var list = new List<V>();
        foreach (long k in KeySet())
        {
            if (Peek(k) is { } value)
            {
                list.Add(value);
            }
        }

        return list;
    }

    /// <summary>
    /// Whether there is nothing resident.
    /// </summary>
    public bool IsEmpty() => Size() == 0;

    /// <summary>
    /// Whether a value is stored.
    /// </summary>
    public bool ContainsValue(V? value) => GetMap().ContainsValue(value!);

    /// <summary>
    /// The resident entries as a map.
    /// </summary>
    public Dictionary<long, V> GetMap()
    {
        var map = new Dictionary<long, V>();
        foreach (long k in KeySet())
        {
            if (Peek(k) is { } x)
            {
                map[k] = x;
            }
        }

        return map;
    }

    /// <summary>
    /// Stores everything, skipping nulls the way put would refuse them.
    /// </summary>
    public void PutAll(IDictionary<long, V> m)
    {
        foreach (KeyValuePair<long, V> e in m)
        {
            // copy only non-null entries
            Put(e.Key, e.Value);
        }
    }

    /// <summary>
    /// Trims every non-resident queue.
    /// </summary>
    public void TrimNonResidentQueue()
    {
        foreach (Segment s in segments)
        {
            lock (s.Sync)
            {
                s.TrimNonResidentQueue();
            }
        }
    }

    /// <summary>
    /// One LIRS cache: the lock the outer methods hold when they touch it.
    /// </summary>
    private sealed class Segment
    {
        internal readonly object Sync = new();

        /// <summary>
        /// How many entries live in the map, hot, cold and non-resident together.
        /// </summary>
        internal int mapSize;

        /// <summary>
        /// How many resident cold entries queue.
        /// </summary>
        internal int queueSize;

        /// <summary>
        /// How many non-resident cold entries queue.
        /// </summary>
        internal int queue2Size;

        /// <summary>
        /// How many hits there were.
        /// </summary>
        internal long hits;

        /// <summary>
        /// How many misses there were.
        /// </summary>
        internal long misses;

        /// <summary>
        /// The map array, always a power of two long.
        /// </summary>
        internal readonly Entry?[] entries;

        /// <summary>
        /// The memory in use.
        /// </summary>
        internal long usedMemory;

        private readonly int stackMoveDistance;

        private long maxMemory;

        private readonly int mask;

        private readonly int nonResidentQueueSize;

        private readonly int nonResidentQueueSizeHigh;

        /// <summary>
        /// The stack of recently referenced elements: every hot entry, plus the recently
        /// referenced cold ones. There is always at least the head.
        /// </summary>
        private readonly Entry stack;

        private int stackSize;

        /// <summary>
        /// The queue of resident cold entries, head included.
        /// </summary>
        private readonly Entry queue;

        /// <summary>
        /// The queue of non-resident cold entries, head included.
        /// </summary>
        private readonly Entry queue2;

        private int stackMoveCounter;

        internal Segment(long maxMemory, int stackMoveDistance, int len,
            int nonResidentQueueSize, int nonResidentQueueSizeHigh)
        {
            SetMaxMemory(maxMemory);
            this.stackMoveDistance = stackMoveDistance;
            this.nonResidentQueueSize = nonResidentQueueSize;
            this.nonResidentQueueSizeHigh = nonResidentQueueSizeHigh;

            // the bit mask has all bits set
            mask = len - 1;

            // initialize the stack and queue heads
            stack = new Entry();
            stack.stackPrev = stack.stackNext = stack;
            queue = new Entry();
            queue.queuePrev = queue.queueNext = queue;
            queue2 = new Entry();
            queue2.queuePrev = queue2.queueNext = queue2;

            entries = new Entry[len];
        }

        /// <summary>
        /// A segment over the entries of another one. The caller holds the old segment's lock.
        /// </summary>
        internal Segment(Segment old, int len)
            : this(old.maxMemory, old.stackMoveDistance, len,
                old.nonResidentQueueSize, old.nonResidentQueueSizeHigh)
        {
            hits = old.hits;
            misses = old.misses;
            Entry? s = old.stack.stackPrev;
            while (!ReferenceEquals(s, old.stack))
            {
                Entry e = new(s!);
                AddToMap(e);
                AddToStack(e);
                s = s!.stackPrev;
            }

            s = old.queue.queuePrev;
            while (!ReferenceEquals(s, old.queue))
            {
                Entry? e = Find(s!.key, GetHash(s.key));
                if (e is null)
                {
                    e = new Entry(s);
                    AddToMap(e);
                }

                AddToQueue(queue, e);
                s = s.queuePrev;
            }

            s = old.queue2.queuePrev;
            while (!ReferenceEquals(s, old.queue2))
            {
                Entry? e = Find(s!.key, GetHash(s.key));
                if (e is null)
                {
                    e = new Entry(s);
                    AddToMap(e);
                }

                AddToQueue(queue2, e);
                s = s.queuePrev;
            }
        }

        /// <summary>
        /// How long the map array should become, or zero to leave it.
        /// </summary>
        internal int GetNewMapLen()
        {
            int len = mask + 1;
            if (len * 3 < mapSize * 4 && len < (1 << 28))
            {
                // more than 75% usage
                return len * 2;
            }
            else if (len > 32 && len / 8 > mapSize)
            {
                // less than 12% usage
                return len / 2;
            }

            return 0;
        }

        private void AddToMap(Entry e)
        {
            int index = GetHash(e.key) & mask;
            e.mapNext = entries[index];
            entries[index] = e;
            usedMemory += e.GetMemory();
            mapSize++;
        }

        /// <summary>
        /// The value of an entry, keeping hot entries hot. A miss when there is none.
        /// </summary>
        internal V? Get(Entry? e)
        {
            lock (Sync)
            {
                V? value = e is null ? default : e.GetValue();
                if (value is null)
                {
                    // the entry was not found
                    // or it was a non-resident entry
                    misses++;
                }
                else
                {
                    Access(e!);
                    hits++;
                }

                return value;
            }
        }

        private void Access(Entry e)
        {
            if (e.IsHot())
            {
                if (!ReferenceEquals(e, stack.stackNext) && e.stackNext is not null)
                {
                    if (stackMoveCounter - e.topMove > stackMoveDistance)
                    {
                        // move a hot entry to the top of the stack
                        // unless it is already there
                        bool wasEnd = ReferenceEquals(e, stack.stackPrev);
                        RemoveFromStack(e);
                        if (wasEnd)
                        {
                            // if moving the last entry, the last entry
                            // could now be cold, which is not allowed
                            PruneStack();
                        }

                        AddToStack(e);
                    }
                }
            }
            else
            {
                V? v = e.GetValue();
                if (v is not null)
                {
                    RemoveFromQueue(e);
                    if (e.reference is not null)
                    {
                        e.value = v;
                        e.reference = null;
                        usedMemory += e.memory;
                    }

                    if (e.stackNext is not null)
                    {
                        // resident, or even non-resident (weak value reference),
                        // cold entries become hot if they are on the stack
                        RemoveFromStack(e);
                        // which means a hot entry needs to become cold
                        // (this entry is cold, that means there is at least one
                        // more entry in the stack, which must be hot)
                        ConvertOldestHotToCold();
                    }
                    else
                    {
                        // cold entries that are not on the stack
                        // move to the front of the queue
                        AddToQueue(queue, e);
                    }

                    // in any case, the cold entry is moved to the top of the stack
                    AddToStack(e);
                    // but if newly promoted cold/non-resident is the only entry on a stack now
                    // that means last one is cold, need to prune
                    PruneStack();
                }
            }
        }

        /// <summary>
        /// Stores an entry: unknown ones go cold, known ones go hot.
        /// </summary>
        internal V? Put(long key, int hash, V value, long memory)
        {
            lock (Sync)
            {
                Entry? e = Find(key, hash);
                bool existed = e is not null;
                V? old = default;
                if (existed)
                {
                    old = e!.GetValue();
                    Remove(key, hash);
                }

                if (memory + Entry.TotalMemoryOverhead > maxMemory)
                {
                    // the new entry is too big to fit
                    return old;
                }

                e = new Entry(key, value, memory);
                int index = hash & mask;
                e.mapNext = entries[index];
                entries[index] = e;
                usedMemory += e.memory;
                if (usedMemory > maxMemory)
                {
                    // old entries needs to be removed
                    Evict();
                    // if the cache is full, the new entry is
                    // cold if possible
                    if (stackSize > 0)
                    {
                        // the new cold entry is at the top of the queue
                        AddToQueue(queue, e);
                    }
                }

                mapSize++;
                // added entries are always added to the stack
                AddToStack(e);
                if (existed)
                {
                    // if it was there before (even non-resident), it becomes hot
                    Access(e);
                }

                return old;
            }
        }

        /// <summary>
        /// Drops an entry, resident or not.
        /// </summary>
        internal V? Remove(long key, int hash)
        {
            lock (Sync)
            {
                int index = hash & mask;
                Entry? e = entries[index];
                if (e is null)
                {
                    return default;
                }

                if (e.key == key)
                {
                    entries[index] = e.mapNext;
                }
                else
                {
                    Entry last;
                    do
                    {
                        last = e;
                        e = e.mapNext;
                        if (e is null)
                        {
                            return default;
                        }
                    } while (e.key != key);

                    last.mapNext = e.mapNext;
                }

                V? old = e.GetValue();
                mapSize--;
                usedMemory -= e.GetMemory();
                if (e.stackNext is not null)
                {
                    RemoveFromStack(e);
                }

                if (e.IsHot())
                {
                    // when removing a hot entry, the newest cold entry gets hot,
                    // so the number of hot entries does not change
                    e = queue.queueNext;
                    if (!ReferenceEquals(e, queue))
                    {
                        RemoveFromQueue(e!);
                        if (e!.stackNext is null)
                        {
                            AddToStackBottom(e);
                        }
                    }

                    PruneStack();
                }
                else
                {
                    RemoveFromQueue(e);
                }

                return old;
            }
        }

        /// <summary>
        /// Evicts cold entries, resident and non-resident, down to the limit.
        /// </summary>
        private void Evict()
        {
            do
            {
                EvictBlock();
            } while (usedMemory > maxMemory);
        }

        private void EvictBlock()
        {
            // ensure there are not too many hot entries: right shift of 5 is
            // division by 32, that means if there are only 1/32 (3.125%) or
            // less cold entries, a hot entry needs to become cold
            while (queueSize <= ((mapSize - queue2Size) >>> 5) && stackSize > 0)
            {
                ConvertOldestHotToCold();
            }

            // the oldest resident cold entries become non-resident
            while (usedMemory > maxMemory && queueSize > 0)
            {
                Entry e = queue.queuePrev!;
                usedMemory -= e.memory;
                RemoveFromQueue(e);
                e.reference = new WeakReference(e.value);
                e.value = default;
                AddToQueue(queue2, e);
                // the size of the non-resident-cold entries needs to be limited
                TrimNonResidentQueue();
            }
        }

        internal void TrimNonResidentQueue()
        {
            int residentCount = mapSize - queue2Size;
            int maxQueue2SizeHigh = nonResidentQueueSizeHigh * residentCount;
            int maxQueue2Size = nonResidentQueueSize * residentCount;
            while (queue2Size > maxQueue2Size)
            {
                Entry e = queue2.queuePrev!;
                if (queue2Size <= maxQueue2SizeHigh)
                {
                    WeakReference? reference = e.reference;
                    if (reference is not null && reference.Target is not null)
                    {
                        break;  // stop trimming if entry holds a value
                    }
                }

                int hash = GetHash(e.key);
                Remove(e.key, hash);
            }
        }

        private void ConvertOldestHotToCold()
        {
            // the last entry of the stack is known to be hot
            Entry last = stack.stackPrev!;
            if (ReferenceEquals(last, stack))
            {
                // never remove the stack head itself (this would mean the
                // internal structure of the cache is corrupt)
                throw new InvalidOperationException();
            }

            // remove from stack - which is done anyway in the stack pruning,
            // but we can do it here as well
            RemoveFromStack(last);
            // adding an entry to the queue will make it cold
            AddToQueue(queue, last);
            PruneStack();
        }

        /// <summary>
        /// Keeps the last stack entry hot.
        /// </summary>
        private void PruneStack()
        {
            while (true)
            {
                Entry last = stack.stackPrev!;
                // must stop at a hot entry or the stack head,
                // but the stack head itself is also hot, so we
                // don't have to test it
                if (last.IsHot())
                {
                    break;
                }

                // the cold entry is still in the queue
                RemoveFromStack(last);
            }
        }

        internal Entry? Find(long key, int hash)
        {
            int index = hash & mask;
            Entry? e = entries[index];
            while (e is not null && e.key != key)
            {
                e = e.mapNext;
            }

            return e;
        }

        private void AddToStack(Entry e)
        {
            e.stackPrev = stack;
            e.stackNext = stack.stackNext;
            e.stackNext!.stackPrev = e;
            stack.stackNext = e;
            stackSize++;
            e.topMove = stackMoveCounter++;
        }

        private void AddToStackBottom(Entry e)
        {
            e.stackNext = stack;
            e.stackPrev = stack.stackPrev;
            e.stackPrev!.stackNext = e;
            stack.stackPrev = e;
            stackSize++;
        }

        private void RemoveFromStack(Entry e)
        {
            e.stackPrev!.stackNext = e.stackNext;
            e.stackNext!.stackPrev = e.stackPrev;
            e.stackPrev = e.stackNext = null;
            stackSize--;
        }

        private void AddToQueue(Entry q, Entry e)
        {
            e.queuePrev = q;
            e.queueNext = q.queueNext;
            e.queueNext!.queuePrev = e;
            q.queueNext = e;
            if (e.value is not null)
            {
                queueSize++;
            }
            else
            {
                queue2Size++;
            }
        }

        private void RemoveFromQueue(Entry e)
        {
            e.queuePrev!.queueNext = e.queueNext;
            e.queueNext!.queuePrev = e.queuePrev;
            e.queuePrev = e.queueNext = null;
            if (e.value is not null)
            {
                queueSize--;
            }
            else
            {
                queue2Size--;
            }
        }

        /// <summary>
        /// The keys on the stack, or on one of the queues.
        /// </summary>
        internal List<long> Keys(bool cold, bool nonResident)
        {
            lock (Sync)
            {
                var keys = new List<long>();
                if (cold)
                {
                    Entry start = nonResident ? queue2 : queue;
                    for (Entry? e = start.queueNext; !ReferenceEquals(e, start); e = e!.queueNext)
                    {
                        keys.Add(e!.key);
                    }
                }
                else
                {
                    for (Entry? e = stack.stackNext; !ReferenceEquals(e, stack); e = e!.stackNext)
                    {
                        keys.Add(e!.key);
                    }
                }

                return keys;
            }
        }

        /// <summary>
        /// The resident keys.
        /// </summary>
        internal ISet<long> KeySet()
        {
            lock (Sync)
            {
                var set = new HashSet<long>();
                for (Entry? e = stack.stackNext; !ReferenceEquals(e, stack); e = e!.stackNext)
                {
                    set.Add(e!.key);
                }

                for (Entry? e = queue.queueNext; !ReferenceEquals(e, queue); e = e!.queueNext)
                {
                    set.Add(e!.key);
                }

                return set;
            }
        }

        internal void SetMaxMemory(long maxMemory) => this.maxMemory = maxMemory;
    }

    /// <summary>
    /// One cache entry: hot on the stack alone, cold in a queue and maybe on the stack,
    /// non-resident cold with a weak value in the second queue.
    /// </summary>
    private sealed class Entry
    {
        internal const int TotalMemoryOverhead = 112;

        /// <summary>
        /// The key.
        /// </summary>
        internal readonly long key;

        /// <summary>
        /// The value, null for non-resident entries.
        /// </summary>
        internal V? value;

        /// <summary>
        /// The weak value, set for non-resident entries only.
        /// </summary>
        internal WeakReference? reference;

        /// <summary>
        /// The estimated memory.
        /// </summary>
        internal readonly long memory;

        /// <summary>
        /// When the item last moved to the top of the stack.
        /// </summary>
        internal int topMove;

        internal Entry? stackNext;

        internal Entry? stackPrev;

        internal Entry? queueNext;

        internal Entry? queuePrev;

        internal Entry? mapNext;

        internal Entry()
            : this(0L, default, 0L)
        {
        }

        internal Entry(long key, V? value, long memory)
        {
            this.key = key;
            this.memory = memory + TotalMemoryOverhead;
            this.value = value;
        }

        internal Entry(Entry old)
        {
            key = old.key;
            memory = old.memory;
            value = old.value;
            reference = old.reference;
            topMove = old.topMove;
        }

        /// <summary>
        /// Whether the entry is hot: cold entries sit in one of the queues.
        /// </summary>
        internal bool IsHot() => queueNext is null;

        internal V? GetValue()
            => value is not null ? value
                : reference is null ? default
                : reference.Target is V v ? v : default;

        internal long GetMemory() => value is null ? 0L : memory;
    }

    /// <summary>
    /// How big a cache to make.
    /// </summary>
    public sealed class Config
    {
        /// <summary>
        /// How much memory to use (1 or larger).
        /// </summary>
        public long MaxMemory { get; set; } = 1;

        /// <summary>
        /// How many segments (a power of two).
        /// </summary>
        public int SegmentCount { get; set; } = 16;

        /// <summary>
        /// How many others move to the top before an entry does.
        /// </summary>
        public int StackMoveDistance { get; set; } = 32;

        /// <summary>
        /// The non-resident queue low watermark, as a factor of the other entries.
        /// </summary>
        public int NonResidentQueueSize { get; } = 3;

        /// <summary>
        /// The non-resident queue high watermark, as a factor of the other entries.
        /// </summary>
        public int NonResidentQueueSizeHigh { get; } = 12;
    }
}
