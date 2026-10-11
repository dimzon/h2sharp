/*
 * Copyright 2006-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from java.sql.ResultSetMetaData.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The nullability words of result metadata.
 *
 * The reference gets these from java.sql.ResultSetMetaData, and .NET has no equivalent —
 * ADO.NET spells nullability as a boolean in a schema row, not as a three-valued constant —
 * so the three constants are carried here under the same names. They are a part of the result's
 * answer rather than an implementation detail: a caller reads getNullable and compares it with
 * one of these, and a port that answered its own numbering would be unreadable to that caller
 * even when it was otherwise right.
 */
namespace H2Sharp.Result;

/// <summary>
/// The nullability constants of result metadata, as <c>java.sql.ResultSetMetaData</c> spells them.
/// </summary>
public static class ResultSetMetaData
{
    /// <summary>
    /// The column is definitely not nullable.
    /// </summary>
    public const int ColumnNoNulls = 0;

    /// <summary>
    /// The column is nullable.
    /// </summary>
    public const int ColumnNullable = 1;

    /// <summary>
    /// The nullability of the column is unknown.
    /// </summary>
    public const int ColumnNullableUnknown = 2;
}
