using System;
using System.Collections.Generic;

namespace H2Sharp.Util;

/// <summary>
/// Two queues for two kinds of rows: what has been read lately goes least recently used, everything
/// else goes first-in-first-out.
/// </summary>
/// <remarks>
/// Streams never graduate, because a stream is read through rather than read again. A row read twice
/// moves up, and a row read twice in a row without anything in between does not, because two reads of
/// the same thing back to back is one read for eviction purposes. That is what <c>lastUsed</c> and
/// <c>recentlyUsed</c> are for: whether this read is the second of two consecutive ones.
/// <para>
/// The split is four to one, least recently used to first-in-first-out, and the recently used set
/// holds four entries per kilobyte of memory, which is the only part of the arrangement that is a
/// guess rather than a measurement.
/// </para>
/// </remarks>
public class CacheTQ : Cache
{
    internal const string TypeName = "TQ";

    private readonly Cache lru;

    private readonly Cache fifo;

    private readonly SmallLRUCache<int, object> recentlyUsed = SmallLRUCache<int, object>.NewInstance(1024);

    private int lastUsed = -1;

    private int maxMemory;

    /// <summary>
    /// A two queue cache that writes dropped records back elsewhere.
    /// </summary>
    /// <param name="writer">where the dropped records go</param>
    /// <param name="maxMemoryKb">how many kilobytes to hold</param>
    internal CacheTQ(CacheWriter writer, int maxMemoryKb)
    {
        maxMemory = maxMemoryKb;
        lru = new CacheLRU(writer, (int)(maxMemoryKb * 0.8), false);
        fifo = new CacheLRU(writer, (int)(maxMemoryKb * 0.2), true);
        SetMaxMemory(4 * maxMemoryKb);
    }

    /// <inheritdoc/>
    public override void Clear()
    {
        lru.Clear();
        fifo.Clear();
        recentlyUsed.Clear();
        lastUsed = -1;
    }

    /// <inheritdoc/>
    public override CacheObject? Find(int pos) => lru.Find(pos) ?? fifo.Find(pos);

    /// <inheritdoc/>
    public override CacheObject? Get(int pos)
    {
        CacheObject? r = lru.Find(pos);
        if (r is not null)
        {
            return r;
        }

        r = fifo.Find(pos);
        if (r is not null && !r.IsStream())
        {
            if (recentlyUsed.TryGetValue(pos, out _))
            {
                if (lastUsed != pos)
                {
                    _ = fifo.Remove(pos);
                    lru.Put(r);
                }
            }
            else
            {
                _ = recentlyUsed.Put(pos, this);
            }

            lastUsed = pos;
        }

        return r;
    }

    /// <inheritdoc/>
    public override List<CacheObject> GetAllChanged()
    {
        List<CacheObject> lruChanged = lru.GetAllChanged();
        List<CacheObject> fifoChanged = fifo.GetAllChanged();
        List<CacheObject> changed = new(lruChanged.Count + fifoChanged.Count);
        changed.AddRange(lruChanged);
        changed.AddRange(fifoChanged);
        return changed;
    }

    /// <inheritdoc/>
    public override int GetMaxMemory() => maxMemory;

    /// <inheritdoc/>
    public override int GetMemory() => lru.GetMemory() + fifo.GetMemory();

    /// <inheritdoc/>
    public override void Put(CacheObject r)
    {
        if (r.IsStream())
        {
            fifo.Put(r);
        }
        else if (recentlyUsed.TryGetValue(r.GetPos(), out _))
        {
            lru.Put(r);
        }
        else
        {
            fifo.Put(r);
            lastUsed = r.GetPos();
        }
    }

    /// <inheritdoc/>
    public override bool Remove(int pos)
    {
        bool result = lru.Remove(pos);
        if (!result)
        {
            result = fifo.Remove(pos);
        }

        _ = recentlyUsed.Remove(pos);
        return result;
    }

    /// <inheritdoc/>
    public override void SetMaxMemory(int maxMemoryKb)
    {
        maxMemory = maxMemoryKb;
        lru.SetMaxMemory((int)(maxMemoryKb * 0.8));
        fifo.SetMaxMemory((int)(maxMemoryKb * 0.2));
        recentlyUsed.SetMaxSize(4 * maxMemoryKb);
    }

    /// <inheritdoc/>
    public override CacheObject? Update(int pos, CacheObject record)
        => lru.Find(pos) is not null ? lru.Update(pos, record) : fifo.Update(pos, record);
}
