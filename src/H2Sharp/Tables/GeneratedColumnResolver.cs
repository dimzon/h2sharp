/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.table.GeneratedColumnResolver.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The resolver of a generated column: the expression of such a column sees the other
 * columns of the row that is being written.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Expressions;
using H2Sharp.Result;
using H2Sharp.Values;

namespace H2Sharp.Tables;

/// <summary>
/// The column resolver of a generated column: it reads the values of the row that is being
/// written, and it knows only the columns that are not generated themselves.
/// </summary>
/// <remarks>
/// A generated column is computed from the other columns of the same row, so its expression
/// cannot be bound to a table filter: the row does not exist yet. This resolver stands in for
/// the filter while the expression is evaluated, and the row is put into it for the time of
/// the evaluation.
/// </remarks>
internal sealed class GeneratedColumnResolver : ColumnResolver
{
    private readonly Table table;

    private Column[]? columns;

    private Dictionary<string, Column>? columnMap;

    private Row? current;

    /// <summary>
    /// Creates the resolver.
    /// </summary>
    /// <param name="table">the table the row belongs to</param>
    internal GeneratedColumnResolver(Table table)
    {
        this.table = table;
    }

    /// <summary>
    /// Sets the row the values are read from.
    /// </summary>
    /// <param name="current">the row, or null when there is none</param>
    internal void Set(Row? current)
    {
        this.current = current;
    }

    /// <inheritdoc/>
    public Column[] GetColumns()
    {
        Column[]? result = columns;
        if (result is null)
        {
            result = columns = CreateColumns();
        }

        return result;
    }

    /// <summary>
    /// The columns a generated expression may name: the ones that are not generated
    /// themselves.
    /// </summary>
    /// <returns>the columns</returns>
    private Column[] CreateColumns()
    {
        Column[] allColumns = table.GetColumns();
        int totalCount = allColumns.Length;
        int baseCount = totalCount;
        foreach (Column allColumn in allColumns)
        {
            if (allColumn.IsGenerated())
            {
                baseCount--;
            }
        }

        Column[] baseColumns = new Column[baseCount];
        for (int i = 0, j = 0; i < totalCount; i++)
        {
            Column c = allColumns[i];
            if (!c.IsGenerated())
            {
                baseColumns[j++] = c;
            }
        }

        return baseColumns;
    }

    /// <inheritdoc/>
    public Column? FindColumn(string name)
    {
        Dictionary<string, Column>? map = columnMap;
        if (map is null)
        {
            map = new Dictionary<string, Column>(StringComparer.OrdinalIgnoreCase);
            foreach (Column c in GetColumns())
            {
                map[c.GetName()] = c;
            }

            columnMap = map;
        }

        return map.GetValueOrDefault(name);
    }

    /// <inheritdoc/>
    public Value? GetValue(Column column)
    {
        Row? row = current;
        if (row is null)
        {
            return null;
        }

        int columnId = column.GetColumnId();
        if (columnId == -1)
        {
            return ValueBigint.Get(row.GetKey());
        }

        return row.GetValue(columnId);
    }
}
