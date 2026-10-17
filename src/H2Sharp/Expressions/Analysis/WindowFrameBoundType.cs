/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.expression.analysis.WindowFrameBoundType.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */

using System;

namespace H2Sharp.Expressions.Analysis;

/// <summary>
/// Window frame bound type.
/// </summary>
public enum WindowFrameBoundType
{
    /// <summary>
    /// UNBOUNDED PRECEDING clause.
    /// </summary>
    UnboundedPreceding,

    /// <summary>
    /// PRECEDING clause.
    /// </summary>
    Preceding,

    /// <summary>
    /// CURRENT ROW clause.
    /// </summary>
    CurrentRow,

    /// <summary>
    /// FOLLOWING clause.
    /// </summary>
    Following,

    /// <summary>
    /// UNBOUNDED FOLLOWING clause.
    /// </summary>
    UnboundedFollowing,
}

/// <summary>
/// Helpers on <see cref="WindowFrameBoundType"/>.
/// </summary>
public static class WindowFrameBoundTypeExtensions
{
    /// <summary>
    /// Returns the SQL representation of the bound type.
    /// </summary>
    /// <param name="type">the bound type</param>
    /// <returns>the SQL representation</returns>
    public static string GetSql(this WindowFrameBoundType type) => type switch
    {
        WindowFrameBoundType.UnboundedPreceding => "UNBOUNDED PRECEDING",
        WindowFrameBoundType.Preceding => "PRECEDING",
        WindowFrameBoundType.CurrentRow => "CURRENT ROW",
        WindowFrameBoundType.Following => "FOLLOWING",
        WindowFrameBoundType.UnboundedFollowing => "UNBOUNDED FOLLOWING",
        _ => throw new InvalidOperationException("type=" + type),
    };
}
