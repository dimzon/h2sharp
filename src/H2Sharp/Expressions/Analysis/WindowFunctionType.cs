/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.analysis.WindowFunctionType.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;

namespace H2Sharp.Expressions.Analysis;

/// <summary>
/// A type of a window function.
/// </summary>
public enum WindowFunctionType
{
    /// <summary>
    /// The type for ROW_NUMBER() window function.
    /// </summary>
    RowNumber,

    /// <summary>
    /// The type for RANK() window function.
    /// </summary>
    Rank,

    /// <summary>
    /// The type for DENSE_RANK() window function.
    /// </summary>
    DenseRank,

    /// <summary>
    /// The type for PERCENT_RANK() window function.
    /// </summary>
    PercentRank,

    /// <summary>
    /// The type for CUME_DIST() window function.
    /// </summary>
    CumeDist,

    /// <summary>
    /// The type for NTILE() window function.
    /// </summary>
    Ntile,

    /// <summary>
    /// The type for LEAD() window function.
    /// </summary>
    Lead,

    /// <summary>
    /// The type for LAG() window function.
    /// </summary>
    Lag,

    /// <summary>
    /// The type for FIRST_VALUE() window function.
    /// </summary>
    FirstValue,

    /// <summary>
    /// The type for LAST_VALUE() window function.
    /// </summary>
    LastValue,

    /// <summary>
    /// The type for NTH_VALUE() window function.
    /// </summary>
    NthValue,

    /// <summary>
    /// The type for RATIO_TO_REPORT() window function.
    /// </summary>
    RatioToReport,
}

/// <summary>
/// The members of <see cref="WindowFunctionType"/>, as the reference's static methods.
/// </summary>
public static class WindowFunctionTypeExtensions
{
    /// <summary>
    /// Returns the type of window function with the specified name, or null.
    /// </summary>
    /// <param name="name">name of a window function</param>
    /// <returns>the type of window function, or null</returns>
    public static WindowFunctionType? Get(string name) => name switch
    {
        "ROW_NUMBER" => WindowFunctionType.RowNumber,
        "RANK" => WindowFunctionType.Rank,
        "DENSE_RANK" => WindowFunctionType.DenseRank,
        "PERCENT_RANK" => WindowFunctionType.PercentRank,
        "CUME_DIST" => WindowFunctionType.CumeDist,
        "NTILE" => WindowFunctionType.Ntile,
        "LEAD" => WindowFunctionType.Lead,
        "LAG" => WindowFunctionType.Lag,
        "FIRST_VALUE" => WindowFunctionType.FirstValue,
        "LAST_VALUE" => WindowFunctionType.LastValue,
        "NTH_VALUE" => WindowFunctionType.NthValue,
        "RATIO_TO_REPORT" => WindowFunctionType.RatioToReport,
        _ => null,
    };

    /// <summary>
    /// Returns SQL representation.
    /// </summary>
    /// <param name="type">the type</param>
    /// <returns>SQL representation</returns>
    public static string GetSql(this WindowFunctionType type) => type switch
    {
        WindowFunctionType.RowNumber => "ROW_NUMBER",
        WindowFunctionType.Rank => "RANK",
        WindowFunctionType.DenseRank => "DENSE_RANK",
        WindowFunctionType.PercentRank => "PERCENT_RANK",
        WindowFunctionType.CumeDist => "CUME_DIST",
        WindowFunctionType.Ntile => "NTILE",
        WindowFunctionType.Lead => "LEAD",
        WindowFunctionType.Lag => "LAG",
        WindowFunctionType.FirstValue => "FIRST_VALUE",
        WindowFunctionType.LastValue => "LAST_VALUE",
        WindowFunctionType.NthValue => "NTH_VALUE",
        WindowFunctionType.RatioToReport => "RATIO_TO_REPORT",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>
    /// Returns whether window function of this type requires window ordering clause.
    /// </summary>
    /// <param name="type">the type</param>
    /// <returns>true if it does, false if it may be omitted</returns>
    public static bool RequiresWindowOrdering(this WindowFunctionType type) => type switch
    {
        WindowFunctionType.Rank
            or WindowFunctionType.DenseRank
            or WindowFunctionType.Ntile
            or WindowFunctionType.Lead
            or WindowFunctionType.Lag => true,
        _ => false,
    };
}
