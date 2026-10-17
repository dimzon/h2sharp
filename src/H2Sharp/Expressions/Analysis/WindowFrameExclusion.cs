/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.expression.analysis.WindowFrameExclusion.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */

using System;

namespace H2Sharp.Expressions.Analysis;

/// <summary>
/// Window frame exclusion clause.
/// </summary>
public enum WindowFrameExclusion
{
    /// <summary>
    /// EXCLUDE CURRENT ROW exclusion clause.
    /// </summary>
    ExcludeCurrentRow,

    /// <summary>
    /// EXCLUDE GROUP exclusion clause.
    /// </summary>
    ExcludeGroup,

    /// <summary>
    /// EXCLUDE TIES exclusion clause.
    /// </summary>
    ExcludeTies,

    /// <summary>
    /// EXCLUDE NO OTHERS exclusion clause.
    /// </summary>
    ExcludeNoOthers,
}

/// <summary>
/// Helpers on <see cref="WindowFrameExclusion"/>.
/// </summary>
public static class WindowFrameExclusionExtensions
{
    /// <summary>
    /// Returns true if this exclusion clause excludes or includes the whole group.
    /// </summary>
    /// <param name="exclusion">the exclusion clause</param>
    /// <returns>true if this exclusion clause is <see cref="WindowFrameExclusion.ExcludeGroup"/>
    /// or <see cref="WindowFrameExclusion.ExcludeNoOthers"/></returns>
    public static bool IsGroupOrNoOthers(this WindowFrameExclusion exclusion)
        => exclusion is WindowFrameExclusion.ExcludeGroup or WindowFrameExclusion.ExcludeNoOthers;

    /// <summary>
    /// Returns the SQL representation of the exclusion clause.
    /// </summary>
    /// <param name="exclusion">the exclusion clause</param>
    /// <returns>the SQL representation</returns>
    public static string GetSql(this WindowFrameExclusion exclusion) => exclusion switch
    {
        WindowFrameExclusion.ExcludeCurrentRow => "EXCLUDE CURRENT ROW",
        WindowFrameExclusion.ExcludeGroup => "EXCLUDE GROUP",
        WindowFrameExclusion.ExcludeTies => "EXCLUDE TIES",
        WindowFrameExclusion.ExcludeNoOthers => "EXCLUDE NO OTHERS",
        _ => throw new InvalidOperationException("exclusion=" + exclusion),
    };
}
