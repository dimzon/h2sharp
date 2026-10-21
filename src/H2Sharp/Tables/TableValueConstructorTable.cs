/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.table.TableValueConstructorTable.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A table for a table value constructor.
 */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Indexes;
using H2Sharp.Result;
using H2Sharp.Schemas;
using H2Sharp.Values;

namespace H2Sharp.Tables;

/// <summary>
/// The table of a table value constructor: the rows a VALUES clause names.
/// </summary>
public sealed class TableValueConstructorTable : VirtualConstructedTable
{
    private readonly List<List<Expression>> rows;

    private readonly Column[] columns;

    private readonly Expression[] columnExpressions;

    private readonly Schema? schema;

    /// <summary>
    /// Creates the table of a table value constructor.
    /// </summary>
    /// <param name="schema">the schema the table belongs to</param>
    /// <param name="session">the session</param>
    /// <param name="columns">the columns of the table</param>
    /// <param name="columnExpressions">the expressions that name the columns</param>
    /// <param name="rows">the rows of the table</param>
    public TableValueConstructorTable(
        Schema? schema,
        SessionLocal session,
        Column[] columns,
        Expression[] columnExpressions,
        List<List<Expression>> rows)
    {
        this.schema = schema;
        this.columns = columns;
        this.columnExpressions = columnExpressions;
        this.rows = rows;
    }

    /// <inheritdoc/>
    public override ResultInterface GetResult(SessionLocal session)
    {
        int columnCount = columns.Length;
        LocalResult result = new(session, columnExpressions, columnCount, columnCount);
        for (int r = 0; r < rows.Count; r++)
        {
            List<Expression> row = rows[r];
            Value?[] values = new Value?[columnCount];
            for (int i = 0; i < columnCount; i++)
            {
                values[i] = row[i].GetValue(session).ConvertToType(columns[i].GetTypeInfo());
            }

            result.AddRow(values);
        }

        return result;
    }

    /// <inheritdoc/>
    public override string GetTableName() => "VALUES";

    /// <inheritdoc/>
    public override Index? GetIndexFor(SessionLocal session, SearchRow row) => GetScanIndex(session);

    /// <inheritdoc/>
    public override int GetTableType() => TABLE;

    /// <inheritdoc/>
    public override int GetColumnCount() => columns.Length;

    /// <inheritdoc/>
    public override Column GetColumn(int index) => columns[index];

    /// <inheritdoc/>
    public override Column[] GetColumns() => columns;

    /// <inheritdoc/>
    public override bool CanGetRowCount(SessionLocal session) => true;

    /// <inheritdoc/>
    public override long GetRowCount(SessionLocal session) => rows.Count;

    /// <inheritdoc/>
    public override long GetRowCountApproximation(SessionLocal session) => rows.Count;

    /// <inheritdoc/>
    public override string? GetCreateSql() => null;

    /// <inheritdoc/>
    public override Schema? GetSchema() => schema;

    /// <inheritdoc/>
    public override void Truncate(SessionLocal session)
    {
    }

    /// <inheritdoc/>
    public override void Close()
    {
    }

    /// <inheritdoc/>
    public override bool IsClosed() => false;

    /// <inheritdoc/>
    public override string GetSql(int sqlFlags) => GetSql(new StringBuilder(), sqlFlags).ToString();

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        _ = builder.Append("VALUES ");
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            _ = builder.Append('(');
            List<Expression> row = rows[i];
            for (int c = 0; c < row.Count; c++)
            {
                if (c > 0)
                {
                    _ = builder.Append(", ");
                }

                _ = row[c].GetUnenclosedSql(builder, sqlFlags);
            }

            _ = builder.Append(')');
        }

        return builder;
    }
}
