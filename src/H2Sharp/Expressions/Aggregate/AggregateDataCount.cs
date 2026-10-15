/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.expression.aggregate.AggregateDataCount.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Engine;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Aggregate;

/// <summary>
/// Data stored while calculating a COUNT aggregate.
/// </summary>
internal sealed class AggregateDataCount : AggregateData
{
    private readonly bool all;

    private long count;

    internal AggregateDataCount(bool all)
    {
        this.all = all;
    }

    /// <inheritdoc/>
    internal override void Add(SessionLocal session, Value? v)
    {
        if (all || v is not ValueNull)
        {
            count++;
        }
    }

    /// <inheritdoc/>
    internal override Value GetValue(SessionLocal session) => ValueBigint.Get(count);
}
