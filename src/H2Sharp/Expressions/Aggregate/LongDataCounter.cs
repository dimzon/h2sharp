/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.expression.aggregate.LongDataCounter.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */

namespace H2Sharp.Expressions.Aggregate;

/// <summary>
/// Counter.
/// </summary>
internal sealed class LongDataCounter
{
    /// <summary>
    /// The count.
    /// </summary>
    internal long Count;
}
