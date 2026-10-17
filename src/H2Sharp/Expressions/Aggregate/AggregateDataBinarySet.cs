/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.expression.aggregate.AggregateDataBinarySet.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Engine;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Aggregate;

/// <summary>
/// Aggregate data of binary set functions.
/// </summary>
internal abstract class AggregateDataBinarySet : AggregateData
{
    /// <summary>
    /// Add a pair of values to this aggregate.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="yValue">the y value</param>
    /// <param name="xValue">the x value</param>
    internal abstract void Add(SessionLocal session, Value? yValue, Value? xValue);

    internal override sealed void Add(SessionLocal session, Value? v)
        => throw Message.DbException.GetInternalError();
}
