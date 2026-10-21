/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.index.QueryExpressionCursor.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Indexes;

/// <summary>
/// The cursor implementation of a query expression index.
/// </summary>
public sealed class QueryExpressionCursor : Cursor
{
    private readonly Table table;

    private readonly QueryExpressionIndex index;

    private readonly ResultInterface result;

    private readonly SearchRow? first;

    private readonly SearchRow? last;

    private Row? current;

    /// <summary>
    /// Creates a cursor over the result of a query.
    /// </summary>
    /// <param name="index">the index</param>
    /// <param name="result">the result of the query</param>
    /// <param name="first">the first row of the range, or null</param>
    /// <param name="last">the last row of the range, or null</param>
    public QueryExpressionCursor(QueryExpressionIndex index, ResultInterface result, SearchRow? first, SearchRow? last)
    {
        this.table = index.GetTable()!;
        this.index = index;
        this.result = result;
        this.first = first;
        this.last = last;
    }

    /// <inheritdoc/>
    public Row? Get() => current;

    /// <inheritdoc/>
    public SearchRow? GetSearchRow() => current;

    /// <inheritdoc/>
    public bool Next()
    {
        while (true)
        {
            if (!result.Next())
            {
                result.Close();
                current = null;
                return false;
            }

            current = table.GetTemplateRow();
            Value?[] values = result.CurrentRow();
            for (int i = 0, len = current.GetColumnCount(); i < len; i++)
            {
                Value v = i < values.Length ? values[i]! : ValueNull.Instance;
                current.SetValue(i, v);
            }

            if (first is not null && index.CompareRows(current, first) < 0)
            {
                continue;
            }

            if (last is not null && index.CompareRows(current, last) > 0)
            {
                continue;
            }

            return true;
        }
    }

    /// <inheritdoc/>
    public bool Previous()
    {
        throw DbException.GetInternalError(GetType().Name);
    }
}
