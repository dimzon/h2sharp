using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Message;

namespace H2Sharp.Util;

/// <summary>
/// Something the cache keeps, and knows where it sits.
/// </summary>
/// <remarks>
/// The three links are the cache's own bookkeeping and they are public because the cache, and only
/// the cache, walks them. <c>CacheNext</c> and <c>CachePrevious</c> are the eviction order; chained is
/// the collision chain, because a cache indexes by position and two positions can share a bucket.
/// <para>
/// The position may only be set while the object is in nobody's list. Setting it afterwards would
/// mean the cache had already hashed it somewhere, and the chain it is on would be pointing at a
/// record that has moved.
/// </para>
/// </remarks>
public abstract class CacheObject : IComparable<CacheObject>
{
    /// <summary>
    /// The next record in eviction order.
    /// </summary>
    public CacheObject? CacheNext;

    /// <summary>
    /// The previous record in eviction order.
    /// </summary>
    public CacheObject? CachePrevious;

    /// <summary>
    /// The next record in the same bucket.
    /// </summary>
    public CacheObject? CacheChained;

    private int pos;

    private bool changed;

    /// <summary>
    /// Whether the cache may drop this record when it needs room.
    /// </summary>
    /// <returns>true when the record can go</returns>
    public abstract bool CanRemove();

    /// <summary>
    /// How many bytes this record accounts for.
    /// </summary>
    /// <returns>the memory</returns>
    public abstract int GetMemory();

    /// <summary>
    /// Sets where the record sits, which may only happen before the cache links it anywhere.
    /// </summary>
    /// <param name="pos">the position</param>
    public void SetPos(int pos)
    {
        if (CachePrevious is not null || CacheNext is not null || CacheChained is not null)
        {
            throw DbException.GetInternalError("setPos too late");
        }

        this.pos = pos;
    }

    /// <summary>
    /// Where the record sits.
    /// </summary>
    /// <returns>the position</returns>
    public int GetPos() => pos;

    /// <summary>
    /// Whether the record was changed since it was last written back.
    /// </summary>
    /// <returns>true when changed</returns>
    public bool IsChanged() => changed;

    /// <summary>
    /// Marks the record as changed or not.
    /// </summary>
    /// <param name="b">whether it was changed</param>
    public void SetChanged(bool b) => changed = b;

    /// <inheritdoc/>
    public int CompareTo(CacheObject? other) => GetPos().CompareTo(other?.GetPos() ?? 0);

    /// <summary>
    /// Whether this record is a stream, which the cache orders differently.
    /// </summary>
    /// <returns>true for a stream</returns>
    public virtual bool IsStream() => false;
}

/// <summary>
/// The marker record at the front of the eviction order, which is never itself evicted.
/// </summary>
/// <remarks>
/// It answers "can I go" with no and takes no memory, which is exactly what a sentinel has to do, and
/// it gives the linked list somewhere to hang off so that the empty list is the head pointing at
/// itself.
/// </remarks>
public class CacheHead : CacheObject
{
    /// <inheritdoc/>
    public override bool CanRemove() => false;

    /// <inheritdoc/>
    public override int GetMemory() => 0;
}
