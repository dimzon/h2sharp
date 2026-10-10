using System;
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Util;

/// <summary>
/// The cache a table keeps its rows in, least recently used first and in a hash by position.
/// </summary>
/// <remarks>
/// Records live in a hash table for lookup and in a doubly linked list for eviction, and the two are
/// kept in step by hand: every operation that touches the hash touches the list too. That is the
/// whole trick of this cache and also its whole cost, since a record that is in one and not the other
/// is a record the cache can neither find nor forget.
/// <para>
/// The memory the cache holds is counted, not measured. Each record reports its own size and the
/// count moves with it, which is why the cache knows how full it is without ever asking the runtime.
/// The table is sized from the memory, one bucket per 64 bytes, rounded up to a power of two, which is
/// what keeps the index a mask rather than a division.
/// </para>
/// <para>
/// Whether records are moved to the front on read is what separates the two kinds this class is
/// built into. The least recently used kind moves them, the first-in-first-out kind does not, and
/// everything else is shared, which is why the flag exists instead of a second class.
/// </para>
/// </remarks>
public class CacheLRU : Cache
{
    internal const string TypeName = "LRU";

    private readonly CacheWriter writer;

    private readonly bool fifo;

    private readonly CacheObject head = new CacheHead();

    private readonly int mask;

    private CacheObject?[]? values;

    private int recordCount;

    private readonly int len;

    private long maxMemory;

    private long memory;

    /// <summary>
    /// A cache that writes dropped records back elsewhere.
    /// </summary>
    /// <param name="writer">where the dropped records go</param>
    /// <param name="maxMemoryKb">how many kilobytes to hold</param>
    /// <param name="fifo">
    /// whether reads move records to the front. When true this is a first-in-first-out cache, and when
    /// false it is least recently used.
    /// </param>
    internal CacheLRU(CacheWriter writer, int maxMemoryKb, bool fifo)
    {
        this.writer = writer;
        this.fifo = fifo;
        SetMaxMemory(maxMemoryKb);
        long tmpLen = maxMemory / 64;
        if (tmpLen > int.MaxValue)
        {
            throw new ArgumentException();
        }

        len = MathUtils.NextPowerOf2((int)tmpLen);
        mask = len - 1;
        Clear();
    }

    /// <summary>
    /// The cache a named type asks for.
    /// </summary>
    /// <param name="writer">where the dropped records go</param>
    /// <param name="cacheType">LRU, TQ, or either with a SOFT_ second level</param>
    /// <param name="cacheSize">how many kilobytes to hold</param>
    /// <returns>the cache</returns>
    public static Cache GetCache(CacheWriter writer, string cacheType, int cacheSize)
    {
        SoftValuesHashMap<int, CacheObject>? secondLevel = null;
        if (cacheType.StartsWith("SOFT_", StringComparison.Ordinal))
        {
            secondLevel = new SoftValuesHashMap<int, CacheObject>();
            cacheType = cacheType.Substring("SOFT_".Length);
        }

        Cache cache;
        if (TypeName.Equals(cacheType, StringComparison.Ordinal))
        {
            cache = new CacheLRU(writer, cacheSize, false);
        }
        else if (CacheTQ.TypeName.Equals(cacheType, StringComparison.Ordinal))
        {
            cache = new CacheTQ(writer, cacheSize);
        }
        else
        {
            throw DbException.GetInvalidValueException("CACHE_TYPE", cacheType);
        }

        if (secondLevel is not null)
        {
            cache = new CacheSecondLevel(cache, secondLevel);
        }

        return cache;
    }


    /// <inheritdoc/>
    public override void Clear()
    {
        head.CacheNext = head;
        head.CachePrevious = head;

        // first set to null - avoiding out of memory
        values = null;
        values = new CacheObject?[len];
        recordCount = 0;
        memory = len * (long)Constants.MemoryPointer;
    }

    /// <inheritdoc/>
    public override void Put(CacheObject rec)
    {
        if (SysProperties.Check)
        {
            int pos = rec.GetPos();
            CacheObject? old = Find(pos);
            if (old is not null)
            {
                throw DbException.GetInternalError("try to add a record twice at pos " + pos.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        int index = rec.GetPos() & mask;
        rec.CacheChained = values![index];
        values[index] = rec;
        recordCount++;
        memory += rec.GetMemory();
        AddToFront(rec);
        RemoveOldIfRequired();
    }

    /// <inheritdoc/>
    public override CacheObject? Update(int pos, CacheObject rec)
    {
        CacheObject? old = Find(pos);
        if (old is null)
        {
            Put(rec);
        }
        else
        {
            if (old != rec)
            {
                throw DbException.GetInternalError("old!=record pos:" + pos + " old:" + old + " new:" + rec);
            }

            if (!fifo)
            {
                RemoveFromLinkedList(rec);
                AddToFront(rec);
            }
        }

        return old;
    }

    /// <summary>
    /// Drops whatever does not fit, writing the changed records back first.
    /// </summary>
    private void RemoveOldIfRequired()
    {
        // a small method, to allow inlining
        if (memory >= maxMemory)
        {
            RemoveOld();
        }
    }

    private void RemoveOld()
    {
        int i = 0;
        List<CacheObject> changed = [];
        long mem = memory;
        int rc = recordCount;
        bool flushed = false;
        CacheObject? next = head.CacheNext;
        while (true)
        {
            if (rc <= Constants.CacheMinRecords)
            {
                break;
            }

            if (changed.Count == 0)
            {
                if (mem <= maxMemory)
                {
                    break;
                }
            }
            else if (mem * 4 <= maxMemory * 3)
            {
                break;
            }

            CacheObject check = next!;
            next = check.CacheNext;
            i++;
            if (i >= recordCount)
            {
                if (!flushed)
                {
                    writer.FlushLog();
                    flushed = true;
                    i = 0;
                }
                else
                {
                    // can't remove any record, because the records can not be
                    // removed hopefully this does not happen frequently, but it
                    // can happen
                    writer.GetTrace()
                        .Info("cannot remove records, cache size too small? records:"
                            + recordCount + " memory:" + memory);
                    break;
                }
            }

            if (check == head)
            {
                throw DbException.GetInternalError("try to remove head");
            }

            // we are not allowed to remove it if the log is not yet written
            // (because we need to log before writing the data)
            // also, can't write it if the record is pinned
            if (!check.CanRemove())
            {
                RemoveFromLinkedList(check);
                AddToFront(check);
                continue;
            }

            rc--;
            mem -= check.GetMemory();
            if (check.IsChanged())
            {
                changed.Add(check);
            }
            else
            {
                _ = Remove(check.GetPos());
            }
        }

        if (changed.Count != 0)
        {
            if (!flushed)
            {
                writer.FlushLog();
            }

            changed.Sort();
            long max = maxMemory;
            int size = changed.Count;
            try
            {
                // temporary disable size checking,
                // to avoid stack overflow
                maxMemory = long.MaxValue;
                for (i = 0; i < size; i++)
                {
                    writer.WriteBack(changed[i]);
                }
            }
            finally
            {
                maxMemory = max;
            }

            for (i = 0; i < size; i++)
            {
                CacheObject rec = changed[i];
                _ = Remove(rec.GetPos());
                if (rec.CacheNext is not null)
                {
                    throw DbException.GetInternalError();
                }
            }
        }
    }

    private void AddToFront(CacheObject rec)
    {
        if (rec == head)
        {
            throw DbException.GetInternalError("try to move head");
        }

        rec.CacheNext = head;
        rec.CachePrevious = head.CachePrevious;
        rec.CachePrevious!.CacheNext = rec;
        head.CachePrevious = rec;
    }

    private void RemoveFromLinkedList(CacheObject rec)
    {
        if (rec == head)
        {
            throw DbException.GetInternalError("try to remove head");
        }

        rec.CachePrevious!.CacheNext = rec.CacheNext;
        rec.CacheNext!.CachePrevious = rec.CachePrevious;

        // TODO cache: mystery: why is this required? needs more memory if we
        // don't do this
        rec.CacheNext = null;
        rec.CachePrevious = null;
    }

    /// <inheritdoc/>
    public override bool Remove(int pos)
    {
        int index = pos & mask;
        CacheObject? rec = values![index];
        if (rec is null)
        {
            return false;
        }

        if (rec.GetPos() == pos)
        {
            values[index] = rec.CacheChained;
        }
        else
        {
            CacheObject last;
            do
            {
                last = rec;
                rec = rec.CacheChained;
                if (rec is null)
                {
                    return false;
                }
            }
            while (rec.GetPos() != pos);

            last.CacheChained = rec.CacheChained;
        }

        recordCount--;
        memory -= rec.GetMemory();
        RemoveFromLinkedList(rec);
        if (SysProperties.Check)
        {
            rec.CacheChained = null;
            CacheObject? o = Find(pos);
            if (o is not null)
            {
                throw DbException.GetInternalError("not removed: " + o);
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override CacheObject? Find(int pos)
    {
        CacheObject? rec = values![pos & mask];
        while (rec is not null && rec.GetPos() != pos)
        {
            rec = rec.CacheChained;
        }

        return rec;
    }

    /// <inheritdoc/>
    public override CacheObject? Get(int pos)
    {
        CacheObject? rec = Find(pos);
        if (rec is not null && !fifo)
        {
            RemoveFromLinkedList(rec);
            AddToFront(rec);
        }

        return rec;
    }

    /// <inheritdoc/>
    public override List<CacheObject> GetAllChanged()
    {
        List<CacheObject> list = [];
        CacheObject? rec = head.CacheNext;
        while (rec != head)
        {
            if (rec!.IsChanged())
            {
                list.Add(rec);
            }

            rec = rec.CacheNext;
        }

        return list;
    }

    /// <inheritdoc/>
    public override void SetMaxMemory(int maxKb)
    {
        long newSize = (maxKb * 1024L) / 4;
        maxMemory = newSize < 0 ? 0 : newSize;

        // can not resize, otherwise existing records are lost
        // resize(maxSize);
        RemoveOldIfRequired();
    }

    /// <inheritdoc/>
    public override int GetMaxMemory() => (int)((maxMemory * 4L) / 1024);

    /// <inheritdoc/>
    public override int GetMemory() => (int)((memory * 4L) / 1024);
}
