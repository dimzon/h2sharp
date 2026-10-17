/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.expression.aggregate.AggregateDataDistinctWithCounts.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Collections.Generic;

using H2Sharp.Engine;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Aggregate;

/// <summary>
/// Data stored while calculating an aggregate that needs distinct values with their counts.
/// </summary>
internal sealed class AggregateDataDistinctWithCounts : AggregateData
{
    private readonly bool ignoreNulls;

    private readonly int maxDistinctCount;

    private SortedDictionary<Value, LongDataCounter>? values;

    /// <summary>
    /// Creates new instance of data for aggregate that needs distinct values with their counts.
    /// </summary>
    /// <param name="ignoreNulls">whether NULL values should be ignored</param>
    /// <param name="maxDistinctCount">maximum count of distinct values to collect</param>
    internal AggregateDataDistinctWithCounts(bool ignoreNulls, int maxDistinctCount)
    {
        this.ignoreNulls = ignoreNulls;
        this.maxDistinctCount = maxDistinctCount;
    }

    /// <inheritdoc/>
    internal override void Add(SessionLocal session, Value? v)
    {
        if (ignoreNulls && v is ValueNull)
        {
            return;
        }

        SortedDictionary<Value, LongDataCounter> map = values ??= new(session.GetValueComparer());
        if (!map.TryGetValue(v!, out LongDataCounter? counter))
        {
            if (map.Count >= maxDistinctCount)
            {
                return;
            }

            counter = new LongDataCounter();
            map[v!] = counter;
        }

        counter.Count++;
    }

    /// <inheritdoc/>
    internal override Value GetValue(SessionLocal session)
        => // The reference returns null here and every reader uses GetValues instead;
           // answering would be a lie, so the dead end refuses loudly.
           throw Message.DbException.GetInternalError("AggregateDataDistinctWithCounts.getValue");

    /// <summary>
    /// Returns map with values and their counts.
    /// </summary>
    /// <returns>map with values and their counts</returns>
    internal SortedDictionary<Value, LongDataCounter>? GetValues() => values;
}
