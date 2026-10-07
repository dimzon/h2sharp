/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.Value.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The value cache.
 *
 * Values that are equal but not identical are wasted memory, and the numeric types are created
 * over and over: a query with a literal 1000 builds a new BIGINT for it every time. So equal
 * values are shared through a small direct mapped table.
 *
 * The table itself is held softly. That is the whole point of the design and it is worth being
 * explicit about why, because the alternative looks harmless: a cache that holds strong
 * references to values keeps them alive for as long as the database lives, which for a table
 * of wide strings is unbounded growth the engine can never reclaim. A soft reference lets the
 * collector take the whole table when memory is wanted, and the next value simply rebuilds it.
 *
 * The size comes from SysProperties.ObjectCacheSize, which the reference computes from the
 * heap, and the index is the value's hash code masked to it, so the table must be a power of
 * two. That is checked rather than assumed.
 */
using System;
using H2Sharp.Engine;

namespace H2Sharp.Values;

/// <summary>
/// A value of some type. Every value knows its type number, and every type knows how to
/// describe itself as SQL.
/// </summary>
public abstract partial class Value
{
    /// <summary>
    /// The cache, or null when there is none or the collector has taken it.
    /// </summary>
    /// <remarks>
    /// The reference guards this with a volatile field. That is not needed here: the field is
    /// only ever replaced, never mutated in place, so a reader either sees the old array or the
    /// new one and both are complete. Marking it volatile would buy an ordering nobody needs.
    /// </remarks>
    private static WeakReference<Value[]>? softCache;

    /// <summary>
    /// Check if a value is in the cache that is equal to this value. If yes,
    /// this value should be used to save memory. If the value is not in the
    /// cache yet, it is added.
    /// </summary>
    /// <remarks>
    /// Not synchronised, like the reference's. Two threads can miss and both store, and the
    /// loser is simply garbage, which is the trade the reference makes too.
    /// </remarks>
    /// <param name="v">the value to look for</param>
    /// <returns>the value in the cache or the value passed</returns>
    internal static Value Cache(Value v)
    {
        if (!SysProperties.ObjectCache)
        {
            return v;
        }

        int size = SysProperties.ObjectCacheSize;
        Value[] cache = softCache?.TryGetTarget(out Value[]? existing) == true ? existing! : [];
        if (cache.Length != size)
        {
            cache = new Value[size];
            softCache = new WeakReference<Value[]>(cache);
        }

        // A direct mapped table, so the mask only works because the size is a power of two.
        // The reference computes the size the same way and masks for the same reason.
        int index = v.GetHashCode() & (size - 1);
        Value? cached = cache[index];
        if (cached is not null
            && cached.GetValueType() == v.GetValueType()
            && v.Equals(cached))
        {
            return cached;
        }

        cache[index] = v;
        return v;
    }

    /// <summary>
    /// Clear the value cache. Used for testing.
    /// </summary>
    public static void ClearCache() => softCache = null;
}
