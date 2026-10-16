/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.dml.SetClauseList.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The SET of an UPDATE: what each column becomes.
 *
 * One column and one value is the common case, but the reference also accepts an index into an
 * array column — SET TAGS[2] = 'x' — and that is why the list holds an array of indexes per
 * assignment rather than a column and an expression. The two kinds are told apart here by the
 * index array being empty, and they are printed differently, because the text of an UPDATE has
 * to come back out as it went in.
 *
 * Not ported: the actions themselves, which need the row being updated.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Command.Dml;

/// <summary>
/// What an UPDATE sets each of its columns to.
/// </summary>
public sealed class SetClauseList
{
    private readonly Table table;

    private readonly List<SetSingle> singles = [];

    /// <summary>
    /// One plain assignment: a column becomes an expression.
    /// </summary>
    /// <param name="Column">the column</param>
    /// <param name="ArrayIndexes">the array element indexes, or null</param>
    /// <param name="Value">what to set it to</param>
    private sealed record SetSingle(Column Column, Expression[]? ArrayIndexes, Expression Value);

    /// <summary>
    /// Creates the list for a table.
    /// </summary>
    /// <param name="table">the table being updated</param>
    public SetClauseList(Table table)
    {
        this.table = table;
    }

    /// <summary>
    /// Gets the table being updated.
    /// </summary>
    /// <returns>the table</returns>
    public Table GetTable()
    {
        return table;
    }

    /// <summary>
    /// Adds an assignment to one column, possibly into an element of an array column.
    /// </summary>
    /// <param name="column">the column</param>
    /// <param name="arrayIndexes">the index of the array element, or null</param>
    /// <param name="expression">what to set it to</param>
    public void AddSingle(Column column, Expression[]? arrayIndexes, Expression expression)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(expression);
        foreach (SetSingle single in singles)
        {
            if (single.Column.GetColumnId() == column.GetColumnId())
            {
                throw Message.DbException.Get(
                    Api.ErrorCode.DuplicateColumnName1, column.GetName());
            }
        }

        singles.Add(new SetSingle(column, arrayIndexes, expression));
    }

    /// <summary>
    /// Adds one assignment that sets several columns at once.
    /// </summary>
    /// <param name="columns">the columns</param>
    /// <param name="allIndexes">the indexes of each assignment's array element</param>
    /// <param name="expression">what to set them to</param>
    public void AddMultiple(List<Column> columns, List<Expression[]> allIndexes, Expression expression)
        => throw new TODOException("SetClauseList.addMultiple: not ported");

    /// <summary>
    /// Appends the SQL of this list, as it appears in the UPDATE.
    /// </summary>
    /// <param name="builder">the builder</param>
    /// <param name="sqlFlags">the SQL flags</param>
    /// <returns>the builder</returns>
    public StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => throw new TODOException("SetClauseList.getSQL: not ported");

    /// <summary>
    /// Maps the assigned expressions against the updated table.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="filter">the filter of the table being updated</param>
    public void MapColumns(SessionLocal session, Tables.TableFilter filter)
    {
        foreach (SetSingle single in singles)
        {
            single.Value.MapColumns(filter, 0, Expression.MapInitial);
            if (single.ArrayIndexes is not null)
            {
                foreach (Expression index in single.ArrayIndexes)
                {
                    index.MapColumns(filter, 0, Expression.MapInitial);
                }
            }
        }
    }

    /// <summary>
    /// Builds the new row for the old one: assigned columns are evaluated, the rest
    /// is kept, everything is converted to its column type and validated.
    /// </summary>
    /// <remarks>
    /// The reference's prepareUpdate with the unreachable taken out: ON UPDATE expressions,
    /// generated columns, delta collectors and triggers wait for their layers. Array element
    /// assignments refuse rather than writing half a row.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="oldRow">the row as it is</param>
    /// <returns>the row as it becomes</returns>
    public Row PrepareUpdate(SessionLocal session, Row oldRow)
    {
        Column[] columns = table.GetColumns();
        Row newRow = table.GetTemplateRow();
        for (int i = 0; i < columns.Length; i++)
        {
            Column column = columns[i];
            Value? newValue = null;
            bool assigned = false;
            foreach (SetSingle single in singles)
            {
                if (single.Column.GetColumnId() != column.GetColumnId())
                {
                    continue;
                }

                if (single.ArrayIndexes is not null)
                {
                    throw new TODOException(
                        "SetClauseList.prepareUpdate: array element assignment is not ported");
                }

                newValue = single.Value.GetValue(session);
                if (newValue is ValueNull && column.IsDefaultOnNull())
                {
                    newValue = null;
                }
                else if (column.IsGeneratedAlways())
                {
                    throw Message.DbException.Get(
                        Api.ErrorCode.GeneratedColumnCannotBeAssigned1,
                        column.GetSqlWithTable(new StringBuilder(), Util.HasSql.TraceSqlFlags).ToString());
                }

                assigned = true;
                break;
            }

            if (!assigned)
            {
                newValue = column.IsGenerated() ? null : oldRow.GetValue(i);
            }

            newRow.SetValue(i, newValue!);
        }

        table.ConvertUpdateRow(session, newRow, false);
        int mainIndexColumn = table.GetMainIndexColumn();
        long key = mainIndexColumn == SearchRow.RowidIndex
            ? oldRow.GetKey()
            : newRow.GetValue(mainIndexColumn)!.GetLong();
        if (key != oldRow.GetKey())
        {
            throw new TODOException("SetClauseList.prepareUpdate: updating the primary key is not ported");
        }

        newRow.SetKey(key);
        return newRow;
    }
}
