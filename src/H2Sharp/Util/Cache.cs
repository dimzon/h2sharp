using System.Collections.Generic;

namespace H2Sharp.Util;

/// <summary>
/// Where cached rows live.
/// </summary>
/// <remarks>
/// A result may hold rows or only an update count, and the cache only ever deals with the rows that
/// exist. The two halves that are not about lookup — <see cref="GetAllChanged"/> and
/// <see cref="SetMaxMemory"/> — are part of the same contract because the thing that decides when to
/// write a row back needs both, and it holds one of these rather than two.
/// </remarks>
public abstract class Cache
{
    /// <summary>
    /// Every record waiting to be written back, in the order they should be.
    /// </summary>
    /// <returns>the changed records</returns>
    public abstract List<CacheObject> GetAllChanged();

    /// <summary>
    /// Forget everything.
    /// </summary>
    public abstract void Clear();

    /// <summary>
    /// Look a record up without making it more recently used.
    /// </summary>
    /// <param name="pos">the position</param>
    /// <returns>the record, or null</returns>
    public abstract CacheObject? Find(int pos);

    /// <summary>
    /// Look a record up and make it the most recently used.
    /// </summary>
    /// <param name="pos">the position</param>
    /// <returns>the record, or null</returns>
    public abstract CacheObject? Get(int pos);

    /// <summary>
    /// Add a record.
    /// </summary>
    /// <param name="r">the record</param>
    public abstract void Put(CacheObject r);

    /// <summary>
    /// Replace the record at a position.
    /// </summary>
    /// <param name="pos">the position</param>
    /// <param name="record">the record</param>
    /// <returns>what was there</returns>
    public abstract CacheObject? Update(int pos, CacheObject record);

    /// <summary>
    /// Drop the record at a position.
    /// </summary>
    /// <param name="pos">the position</param>
    /// <returns>true when there was one</returns>
    public abstract bool Remove(int pos);

    /// <summary>
    /// How many bytes the cache may hold.
    /// </summary>
    /// <param name="size">kilobytes</param>
    public abstract void SetMaxMemory(int size);

    /// <summary>
    /// How many bytes the cache may hold.
    /// </summary>
    /// <returns>kilobytes</returns>
    public abstract int GetMaxMemory();

    /// <summary>
    /// How many bytes the cache is holding.
    /// </summary>
    /// <returns>kilobytes</returns>
    public abstract int GetMemory();
}

/// <summary>
/// A cache in front of another one, searching both in order.
/// </summary>
/// <remarks>
/// Used for the soft-referenced caches, where the first level is bounded and whatever falls out of it
/// is kept weakly rather than discarded. The two levels answer together, and a record that goes in or
/// is replaced goes into both, so that a record evicted from memory and later resurfacing does not
/// resurface marked clean.
/// </remarks>
internal sealed class CacheSecondLevel(Cache baseCache, SoftValuesHashMap<int, CacheObject> map)
    : Cache
{
    /// <inheritdoc/>
    public override void Clear()
    {
        map.Clear();
        baseCache.Clear();
    }

    /// <inheritdoc/>
    public override CacheObject? Find(int pos)
        => baseCache.Find(pos) ?? map.Get(pos);

    /// <inheritdoc/>
    public override CacheObject? Get(int pos)
        => baseCache.Get(pos) ?? map.Get(pos);

    /// <inheritdoc/>
    public override List<CacheObject> GetAllChanged() => baseCache.GetAllChanged();

    /// <inheritdoc/>
    public override int GetMaxMemory() => baseCache.GetMaxMemory();

    /// <inheritdoc/>
    public override int GetMemory() => baseCache.GetMemory();

    /// <inheritdoc/>
    public override void Put(CacheObject r)
    {
        baseCache.Put(r);
        _ = map.Put(r.GetPos(), r);
    }

    /// <inheritdoc/>
    public override bool Remove(int pos)
    {
        bool result = baseCache.Remove(pos);
        return map.Remove(pos) is not null || result;
    }

    /// <inheritdoc/>
    public override void SetMaxMemory(int size) => baseCache.SetMaxMemory(size);

    /// <inheritdoc/>
    public override CacheObject? Update(int pos, CacheObject record)
    {
        CacheObject? oldRec = baseCache.Update(pos, record);
        _ = map.Put(pos, record);
        return oldRec;
    }
}
