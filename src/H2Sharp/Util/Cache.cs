using System.Collections.Generic;

namespace H2Sharp.Util;

/// <summary>
/// Where cached rows live.
/// </summary>
/// <remarks>
/// A result may hold rows or only an update count, and the cache only ever deals with the rows that
/// exist. The two halves that are not about lookup — <see cref="GetAllChanged"/> and
/// <see cref="SetMaxMemory"/> — are part of the same interface because the thing that decides when to
/// write a row back needs both, and it holds one of these rather than two.
/// </remarks>
public interface Cache
{
    /// <summary>
    /// Every record waiting to be written back, in the order they should be.
    /// </summary>
    /// <returns>the changed records</returns>
    List<CacheObject> GetAllChanged();

    /// <summary>
    /// Forget everything.
    /// </summary>
    void Clear();

    /// <summary>
    /// Look a record up without making it more recently used.
    /// </summary>
    /// <param name="pos">the position</param>
    /// <returns>the record, or null</returns>
    CacheObject? Find(int pos);

    /// <summary>
    /// Look a record up and make it the most recently used.
    /// </summary>
    /// <param name="pos">the position</param>
    /// <returns>the record, or null</returns>
    CacheObject? Get(int pos);

    /// <summary>
    /// Add a record.
    /// </summary>
    /// <param name="r">the record</param>
    void Put(CacheObject r);

    /// <summary>
    /// Replace a record with the same position.
    /// </summary>
    /// <param name="pos">the position</param>
    /// <param name="record">the record</param>
    /// <returns>what was there</returns>
    CacheObject? Update(int pos, CacheObject record);

    /// <summary>
    /// Drop a record.
    /// </summary>
    /// <param name="pos">the position</param>
    /// <returns>true when there was one</returns>
    bool Remove(int pos);

    /// <summary>
    /// How many bytes the cache may hold.
    /// </summary>
    /// <param name="size">kilobytes</param>
    void SetMaxMemory(int size);

    /// <summary>
    /// How many bytes the cache may hold.
    /// </summary>
    /// <returns>kilobytes</returns>
    int GetMaxMemory();

    /// <summary>
    /// How many bytes the cache is holding.
    /// </summary>
    /// <returns>kilobytes</returns>
    int GetMemory();
}

/// <summary>
/// A cache in front of another one, holding what the other one dropped.
/// </summary>
/// <remarks>
/// Used for the soft-referenced caches, where the first level is bounded and what falls out of it is
/// kept weakly rather than discarded. The two levels are searched in order, and the change is
/// recorded in both, so that a record which is evicted from memory and later comes back does not come
/// back marked clean.
/// </remarks>
internal sealed class CacheSecondLevel(Dictionary<int, CacheObject> map, Cache baseCache)
    : Cache
{
    /// <inheritdoc/>
    public void Clear()
    {
        map.Clear();
        baseCache.Clear();
    }

    /// <inheritdoc/>
    public CacheObject? Find(int pos) => baseCache.Find(pos) ?? (map.TryGetValue(pos, out CacheObject? r) ? r : null);

    /// <inheritdoc/>
    public CacheObject? Get(int pos) => baseCache.Get(pos) ?? (map.TryGetValue(pos, out CacheObject? r) ? r : null);

    /// <inheritdoc/>
    public List<CacheObject> GetAllChanged() => baseCache.GetAllChanged();

    /// <inheritdoc/>
    public int GetMaxMemory() => baseCache.GetMaxMemory();

    /// <inheritdoc/>
    public int GetMemory() => baseCache.GetMemory();

    /// <inheritdoc/>
    public void Put(CacheObject r)
    {
        baseCache.Put(r);
        map[r.GetPos()] = r;
    }

    /// <inheritdoc/>
    public bool Remove(int pos)
    {
        bool result = baseCache.Remove(pos);
        return map.Remove(pos) || result;
    }

    /// <inheritdoc/>
    public void SetMaxMemory(int size) => baseCache.SetMaxMemory(size);

    /// <inheritdoc/>
    public CacheObject? Update(int pos, CacheObject record)
    {
        CacheObject? oldRec = baseCache.Update(pos, record);
        map[pos] = record;
        return oldRec;
    }
}
