/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.expression.aggregate.AggregateData.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Engine;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Aggregate;

/// <summary>
/// Abstract class for the computation of an aggregate.
/// </summary>
internal abstract class AggregateData
{
    /// <summary>
    /// Add a value to this aggregate.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="v">the value</param>
    internal abstract void Add(SessionLocal session, Value? v);

    /// <summary>
    /// Get the aggregate result.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the value</returns>
    internal abstract Value GetValue(SessionLocal session);
}
