/* Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.expression.analysis.DataAnalysisOperation.
 * .NET idiom: the ordered-window row arrays are rented from a shared pool and returned
 * when the partition result is materialized, instead of allocating fresh per-row arrays
 * on every run (Java allocates a new Value[] per row). Observable SQL semantics are
 * identical; the pool only changes where the memory comes from. */
using System;
using System.Buffers;
using System.Collections.Generic;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Analysis;

/// <summary>
/// A list of ordered-window rows backed by the shared array pool. Returning the rows
/// after the window result is materialized lets repeated runs reuse the arrays, so the
/// per-row allocation cost is paid once per warm pool instead of once per run.
/// </summary>
internal sealed class PooledRowList : List<Value?[]>, IDisposable
{
    /// <summary>
    /// Returns every rented row to the pool. Only called once the window result has been
    /// materialized and the rows are unreachable (<see cref="PartitionData.SetOrderedResult"/>).
    /// </summary>
    public void Dispose()
    {
        foreach (Value?[] row in this)
        {
            ArrayPool<Value?>.Shared.Return(row, clearArray: false);
        }

        Clear();
    }
}
