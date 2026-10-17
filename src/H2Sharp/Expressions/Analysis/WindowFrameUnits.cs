/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.expression.analysis.WindowFrameUnits.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */

namespace H2Sharp.Expressions.Analysis;

/// <summary>
/// Window frame units.
/// </summary>
public enum WindowFrameUnits
{
    /// <summary>
    /// ROWS unit.
    /// </summary>
    Rows,

    /// <summary>
    /// RANGE unit.
    /// </summary>
    Range,

    /// <summary>
    /// GROUPS unit.
    /// </summary>
    Groups,
}

/// <summary>
/// Helpers on <see cref="WindowFrameUnits"/>.
/// </summary>
public static class WindowFrameUnitsExtensions
{
    /// <summary>
    /// Returns the SQL representation of the unit.
    /// </summary>
    /// <param name="units">the unit</param>
    /// <returns>the SQL representation</returns>
    public static string GetSql(this WindowFrameUnits units) => units.ToString().ToUpperInvariant();
}
