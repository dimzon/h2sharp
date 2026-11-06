/* Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.expression.analysis.DataAnalysisOperation.
 * .NET idiom: ordered-window row arrays are rented from a private pool with exact
 * lengths and returned once the partition result is materialized, instead of
 * allocating fresh per-row arrays on every run (Java allocates a new Value[] per
 * row). Observable SQL semantics are identical; the pool only changes where the
 * memory comes from. */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Analysis;

/// <summary>
/// A list of ordered-window rows backed by a private pool. Returning the rows after
/// the window result is materialized lets repeated runs reuse the arrays, so the
/// per-row allocation cost is paid once per warm pool instead of once per run.
/// </summary>
/// <remarks>
/// A private pool, not <see cref="ArrayPool{T}.Shared"/>: SharedArrayPool rejects
/// arrays it did not hand out, and its minimum bucket is 16 elements, which does not
/// match the exact row width the window frame allocates.
/// </remarks>
internal sealed class PooledRowList : List<Value?[]>, IDisposable
{
    /// <summary>
    /// Per-length pools. Rows are always returned to the bucket of their exact length,
    /// so a renter sees an array it can fill completely.
    /// </summary>
    private static readonly ConcurrentDictionary<int, ConcurrentBag<Value?[]>> Pool = new();

    /// <summary>
    /// The per-length pool cap.
    /// </summary>
    private const int PoolCap = 4096;

    /// <summary>
    /// Rents a row array of the exact length.
    /// </summary>
    internal static Value?[] Rent(int length)
    {
        ConcurrentBag<Value?[]> bucket = Pool.GetOrAdd(length, static _ => new());
        if (bucket.TryTake(out Value?[]? row))
        {
            return row;
        }

        return new Value?[length];
    }

    /// <summary>
    /// Returns a row array to the pool, until the per-length cap is reached.
    /// </summary>
    internal static void Return(Value?[] row)
    {
        ConcurrentBag<Value?[]> bucket = Pool.GetOrAdd(row.Length, static _ => new());
        if (bucket.Count >= PoolCap)
        {
            return;
        }

        bucket.Add(row);
    }

    /// <summary>
    /// Returns every rented row to the pool. Idempotent: after the first call the list
    /// is empty, so later calls (for example the group-data reset) do nothing.
    /// </summary>
    public void Dispose()
    {
        foreach (Value?[] row in this)
        {
            Return(row);
        }

        Clear();
    }
}
