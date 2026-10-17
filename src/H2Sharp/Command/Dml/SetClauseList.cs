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
 * The actions live in one array indexed by column id, as in the reference, so preparing a row
 * is a walk over the columns and not a search through the assignments: an UPDATE over a wide
 * table would otherwise be quadratic per row.
 *
 * One column and one value is the common case, but the reference also accepts an index into an
 * array column — SET TAGS[2] = 'x' — and that is why the list holds an array of indexes per
 * assignment rather than a column and an expression.
 *
 * Not ported: the reference's ON UPDATE expressions, generated columns, delta collectors and
 * triggers, which need their own layers; and row-value assignment (SET (a, b) = ...), which
 * needs ExpressionList. The array element write itself refuses: it would write half a row.
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

    private readonly UpdateAction?[] actions;

    /// <summary>
    /// Creates the list for a table.
    /// </summary>
    /// <param name="table">the table being updated</param>
    public SetClauseList(Table table)
    {
        this.table = table;
        actions = new UpdateAction?[table.GetColumns().Length];
    }

    /// <summary>
    /// Gets the table being updated.
    /// </summary>
    /// <returns>the table</returns>
    public Table GetTable() => table;

    /// <summary>
    /// Adds an assignment to one column, possibly into an element of an array column.
    /// </summary>
    /// <param name="column">the column</param>
    /// <param name="arrayIndexes">the indexes of the array elements, or null</param>
    /// <param name="expression">what to set it to</param>
    public void AddSingle(Column column, Expression[]? arrayIndexes, Expression expression)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(expression);
        int id = column.GetColumnId();
        if (actions[id] is not null)
        {
            throw Message.DbException.Get(Api.ErrorCode.DuplicateColumnName1, column.GetName());
        }

        actions[id] = new SetSimple(arrayIndexes, expression);
        if (expression is Parameter parameter)
        {
            parameter.SetColumn(column);
        }
    }

    /// <summary>
    /// Adds one assignment that sets several columns at once.
    /// </summary>
    /// <param name="columns">the columns</param>
    /// <param name="allIndexes">the indexes of each assignment's array element</param>
    /// <param name="expression">what to set them to</param>
    public void AddMultiple(List<Column> columns, List<Expression[]> allIndexes, Expression expression)
        => throw new TODOException(
            "SetClauseList.addMultiple: row-value assignment needs ExpressionList");

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
    public void MapColumns(SessionLocal session, TableFilter filter)
    {
        for (int i = 0; i < actions.Length; i++)
        {
            actions[i]?.MapAndOptimize(session, filter, null);
        }
    }

    /// <summary>
    /// Checks whether every assigned expression satisfies the visitor.
    /// </summary>
    /// <param name="visitor">the visitor</param>
    /// <returns>whether all assigned expressions satisfy it</returns>
    public bool IsEverything(ExpressionVisitor visitor)
    {
        foreach (UpdateAction? action in actions)
        {
            if (action is not null && !action.IsEverything(visitor))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Builds the new row for the old one: assigned columns are evaluated, the rest is kept,
    /// everything is converted to its column type and validated.
    /// </summary>
    /// <remarks>
    /// The reference's prepareUpdate with the unreachable taken out: ON UPDATE expressions,
    /// generated columns, delta collectors and triggers wait for their layers.
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
            UpdateAction? action = actions[i];
            Value? newValue;
            if (action is null || ReferenceEquals(action, UpdateAction.OnUpdate))
            {
                newValue = column.IsGenerated() ? null : oldRow.GetValue(i);
            }
            else if (ReferenceEquals(action, UpdateAction.SetDefault))
            {
                newValue = !column.IsIdentity() ? null : oldRow.GetValue(i);
            }
            else
            {
                newValue = action.Update(session, oldRow.GetValue(i)!);
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

    private class UpdateAction
    {
        internal static readonly UpdateAction OnUpdate = new();

        internal static readonly UpdateAction SetDefault = new();

        internal virtual Value Update(SessionLocal session, Value oldValue)
            => throw Message.DbException.GetInternalError();

        internal virtual bool IsEverything(ExpressionVisitor visitor) => true;

        internal virtual void MapAndOptimize(SessionLocal session, ColumnResolver resolver1, ColumnResolver? resolver2)
        {
        }

        internal virtual void GetSql(StringBuilder builder, int sqlFlags, Column column)
            => throw Message.DbException.GetInternalError();
    }

    private abstract class SetAction : UpdateAction
    {
        private readonly Expression[]? arrayIndexes;

        protected SetAction(Expression[]? arrayIndexes)
        {
            this.arrayIndexes = arrayIndexes;
        }

        internal override bool IsEverything(ExpressionVisitor visitor)
        {
            if (arrayIndexes is not null)
            {
                foreach (Expression e in arrayIndexes)
                {
                    if (!e.IsEverything(visitor))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        internal override void MapAndOptimize(
            SessionLocal session, ColumnResolver resolver1, ColumnResolver? resolver2)
        {
            if (arrayIndexes is null)
            {
                return;
            }

            for (int i = 0; i < arrayIndexes.Length; i++)
            {
                Expression e = arrayIndexes[i];
                e.MapColumns(resolver1, 0, Expression.MapInitial);
                if (resolver2 is not null)
                {
                    e.MapColumns(resolver2, 0, Expression.MapInitial);
                }

                arrayIndexes[i] = e.Optimize(session);
            }
        }

        internal override Value Update(SessionLocal session, Value oldValue)
        {
            Value newValue = Update(session);
            if (arrayIndexes is not null)
            {
                throw new TODOException(
                    "SetClauseList.updateArray: array element assignment is not ported");
            }

            return newValue;
        }

        protected abstract Value Update(SessionLocal session);
    }

    private sealed class SetSimple : SetAction
    {
        private Expression expression;

        public SetSimple(Expression[]? arrayIndexes, Expression expression)
            : base(arrayIndexes)
        {
            this.expression = expression;
        }

        protected override Value Update(SessionLocal session) => expression.GetValue(session);

        internal override bool IsEverything(ExpressionVisitor visitor)
            => base.IsEverything(visitor) && expression.IsEverything(visitor);

        internal override void MapAndOptimize(
            SessionLocal session, ColumnResolver resolver1, ColumnResolver? resolver2)
        {
            base.MapAndOptimize(session, resolver1, resolver2);
            expression.MapColumns(resolver1, 0, Expression.MapInitial);
            if (resolver2 is not null)
            {
                expression.MapColumns(resolver2, 0, Expression.MapInitial);
            }

            expression = expression.Optimize(session);
        }
    }
}
