/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The shape of a table-valued .NET function: the columns it produces and the rows it
 * yields. The columns are chosen per call, so a function can read them from its input
 * (CSVREAD reads the header of its file).
 */
using System;
using System.Collections.Generic;

namespace H2Sharp.Data.Extensions;

/// <summary>
/// One column of a table-valued function's result.
/// </summary>
public sealed class H2TableUdfColumn
{
    /// <summary>
    /// Creates a column.
    /// </summary>
    /// <param name="name">the SQL column name</param>
    /// <param name="type">the CLR type of the values; it is mapped to the SQL type</param>
    public H2TableUdfColumn(string name, Type type)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(type);
        Name = name;
        Type = type;
    }

    /// <summary>The SQL column name.</summary>
    public string Name { get; }

    /// <summary>The CLR type of the column's values.</summary>
    public Type Type { get; }
}

/// <summary>
/// The result of a table-valued function: its columns and its rows.
/// </summary>
/// <remarks>
/// The rows are read lazily: a prepared statement asks the function for the columns only,
/// and enumerates the rows when the statement runs.
/// </remarks>
public sealed class H2TableUdfResult
{
    /// <summary>
    /// Creates a result.
    /// </summary>
    /// <param name="columns">the columns, in the order the SQL sees them</param>
    /// <param name="rows">the rows; every row has one value per column</param>
    public H2TableUdfResult(IReadOnlyList<H2TableUdfColumn> columns, IEnumerable<object?[]> rows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        Columns = columns;
        Rows = rows;
    }

    /// <summary>The columns, in the order the SQL sees them.</summary>
    public IReadOnlyList<H2TableUdfColumn> Columns { get; }

    /// <summary>The rows, one value per column.</summary>
    public IEnumerable<object?[]> Rows { get; }
}
