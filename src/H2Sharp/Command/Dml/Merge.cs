/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.dml.Merge.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * MERGE INTO, and the MySQL REPLACE spelling it shares the class with.
 *
 * Each row is tried as an update first and inserted when the update matches
 * nothing, which is the whole statement in one sentence: the keys say which
 * row an update would hit, and a row no update hits is a new row.
 *
 * What is left out, and why:
 *
 * * Statement-level trigger fire and table lock in the query path. The row
 *   path (fireBeforeRow / fireAfterRow / addRow) is Insert's path and is
 *   here; the statement fire has no machinery anywhere in the port and
 *   Insert skips it the same way.
 * * The concurrent-merge check on a duplicate key. The reference reads the
 *   offending index off the exception and reports CONCURRENT_UPDATE_1 when
 *   its columns are the merge keys; the port's duplicate-key errors carry no
 *   index, so the check has nothing to read and the original error travels.
 * * Row-context wrapping of row failures (the reference's setRow). The port
 *   has no exception SQL-appending, so a failing row propagates as is.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Command.Dml;

/// <summary>
/// MERGE INTO, or the MySQL REPLACE statement.
/// </summary>
public sealed class Merge : CommandWithValues
{
    private readonly bool isReplace;

    private Table? table;

    private Column[]? columns;

    private Column[]? keys;

    private Query.Query? query;

    private Update? update;

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="isReplace">whether this is REPLACE rather than MERGE</param>
    public Merge(SessionLocal session, bool isReplace)
        : base(session)
    {
        this.isReplace = isReplace;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Hides rather than overrides, as in Insert: the container binds the base method.
    /// Called on this type it also propagates to the query, as the reference does.
    /// </remarks>
    public new void SetCommand(Command command)
    {
        base.SetCommand(command);
        query?.SetCommand(command);
    }

    /// <inheritdoc/>
    public override Table? GetTable() => table;

    /// <summary>
    /// Sets the table the rows go into.
    /// </summary>
    /// <param name="table">the table</param>
    public void SetTable(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        this.table = table;
    }

    /// <summary>
    /// Sets the columns the values are for.
    /// </summary>
    /// <param name="columns">the columns</param>
    public void SetColumns(Column[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        this.columns = columns;
    }

    /// <summary>
    /// Sets the key columns an update matches on.
    /// </summary>
    /// <param name="keys">the key columns</param>
    public void SetKeys(Column[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        this.keys = keys;
    }

    /// <summary>
    /// Sets the query the rows are read from, for MERGE INTO ... SELECT.
    /// </summary>
    /// <param name="query">the query</param>
    public void SetQuery(Query.Query query)
    {
        ArgumentNullException.ThrowIfNull(query);
        this.query = query;
    }

    /// <inheritdoc/>
    public override long Run(
        ResultTarget? deltaChangeCollector,
        DataChangeDeltaTable.ResultOption? deltaChangeCollectionMode)
    {
        SessionLocal session = GetSession();
        Table target = table ?? throw DbException.GetInternalError("MERGE without a table");
        Column[] targetColumns = columns ?? throw DbException.GetInternalError("MERGE without columns");
        long count = 0;
        SetCurrentRowNumber(0);
        if (ValuesExpressionList.Count != 0)
        {
            // process values in list
            for (int x = 0, size = ValuesExpressionList.Count; x < size; x++)
            {
                SetCurrentRowNumber(x + 1);
                Expression[] expr = ValuesExpressionList[x];
                Row newRow = target.GetTemplateRow();
                for (int i = 0, len = targetColumns.Length; i < len; i++)
                {
                    Column c = targetColumns[i];
                    int index = c.GetColumnId();
                    Expression e = expr[i];
                    if (e != ValueExpression.Default)
                    {
                        newRow.SetValue(index, e.GetValue(session));
                    }
                }

                count += MergeRow(newRow, expr, deltaChangeCollector, deltaChangeCollectionMode);
            }
        }
        else
        {
            // process select data for list
            Query.Query source = query ?? throw DbException.GetInternalError("MERGE without a query");
            source.SetNeverLazy(true);
            ResultInterface rows = source.Run(0);
            target.Lock(session, Table.WriteLock);
            while (rows.Next())
            {
                Value?[] r = rows.CurrentRow();
                Row newRow = target.GetTemplateRow();
                SetCurrentRowNumber(count);
                for (int j = 0; j < targetColumns.Length; j++)
                {
                    newRow.SetValue(targetColumns[j].GetColumnId(), r[j] ?? ValueNull.Instance);
                }

                count += MergeRow(newRow, null, deltaChangeCollector, deltaChangeCollectionMode);
            }

            rows.Close();
        }

        return count;
    }

    /// <summary>
    /// Updates an existing row or inserts a new one.
    /// </summary>
    /// <param name="row">the row to replace</param>
    /// <param name="expressions">the source expressions, or null</param>
    /// <param name="deltaChangeCollector">where the changed rows go</param>
    /// <param name="deltaChangeCollectionMode">what to collect, or null for none</param>
    /// <returns>1 when the row was inserted, 1 when updated by MERGE, 2 when updated by REPLACE</returns>
    private int MergeRow(
        Row row,
        Expression[]? expressions,
        ResultTarget? deltaChangeCollector,
        DataChangeDeltaTable.ResultOption? deltaChangeCollectionMode)
    {
        SessionLocal session = GetSession();
        Table target = table!;
        Column[] targetColumns = columns!;
        Column[] keyColumns = keys!;
        long count;
        if (update is null)
        {
            // if there is no valid primary key,
            // the REPLACE statement degenerates to an INSERT
            count = 0;
        }
        else
        {
            List<Expressions.Parameter> parameters = update.GetParameters()
                ?? throw DbException.GetInternalError("MERGE without parameters");
            int j = 0;
            for (int i = 0, l = targetColumns.Length; i < l; i++)
            {
                Column col = targetColumns[i];
                if (col.IsGeneratedAlways())
                {
                    if (expressions is null || expressions[i] != ValueExpression.Default)
                    {
                        throw DbException.Get(
                            ErrorCode.GeneratedColumnCannotBeAssigned1,
                            col.GetSql(new StringBuilder(), HasSql.TraceSqlFlags).ToString());
                    }
                }
                else
                {
                    Value? v = row.GetValue(col.GetColumnId());
                    if (v is null)
                    {
                        Expression? defaultExpression = GetEffectiveDefaultExpression(col);
                        v = defaultExpression is not null
                            ? defaultExpression.GetValue(session)
                            : ValueNull.Instance;
                    }

                    parameters[j++].SetValue(v);
                }
            }

            foreach (Column col in keyColumns)
            {
                Value? v = row.GetValue(col.GetColumnId());
                if (v is null)
                {
                    throw DbException.Get(ErrorCode.ColumnContainsNullValues1, col.GetName());
                }

                TypeInfo colType = col.GetTypeInfo();
                TypeInfo.CheckComparable(colType, v.GetTypeInfo());
                parameters[j++].SetValue(v.ConvertForAssignTo(colType, session.CastDataProvider, col));
            }

            count = update.Run(deltaChangeCollector, deltaChangeCollectionMode);
        }

        // if update fails try an insert
        if (count == 0)
        {
            try
            {
                target.ConvertInsertRow(session, row, null);
                if (deltaChangeCollectionMode == DataChangeDeltaTable.ResultOption.NEW)
                {
                    deltaChangeCollector?.AddRow([.. row.GetValueList()]);
                }

                if (!target.FireBeforeRow(session, null, row))
                {
                    target.Lock(session, Table.WriteLock);
                    target.AddRow(session, row);
                    DataChangeDeltaTable.CollectForInsert(
                        session, target, deltaChangeCollector, deltaChangeCollectionMode, row);
                    target.FireAfterRow(session, null, row, rollback: false);
                }
                else
                {
                    DataChangeDeltaTable.CollectForInsert(
                        session, target, deltaChangeCollector, deltaChangeCollectionMode, row);
                }

                return 1;
            }
            catch (H2SharpException e) when (e.H2ErrorCode == ErrorCode.DuplicateKey1)
            {
                // The reference reads the offending index off the exception and reports a
                // concurrent update when its columns are the merge keys. The port's
                // duplicate-key errors carry no index, so the check has nothing to read and
                // the original error travels.
                throw;
            }
        }
        else if (count == 1)
        {
            return isReplace ? 2 : 1;
        }

        throw DbException.Get(ErrorCode.DuplicateKey1, target.GetTableName());
    }

    /// <inheritdoc/>
    public override StringBuilder GetPlanSql(StringBuilder builder, int sqlFlags)
    {
        Table target = table ?? throw DbException.GetInternalError("MERGE without a table");
        Column[] targetColumns = columns ?? throw DbException.GetInternalError("MERGE without columns");
        builder.Append(isReplace ? "REPLACE INTO " : "MERGE INTO ");
        target.GetSql(builder, sqlFlags).Append('(');
        WriteColumns(builder, targetColumns, sqlFlags);
        builder.Append(')');
        if (!isReplace && keys is not null)
        {
            builder.Append(" KEY(");
            WriteColumns(builder, keys, sqlFlags);
            builder.Append(')');
        }

        builder.Append('\n');
        if (ValuesExpressionList.Count != 0)
        {
            builder.Append("VALUES ");
            int row = 0;
            foreach (Expression[] expr in ValuesExpressionList)
            {
                if (row++ > 0)
                {
                    builder.Append(", ");
                }

                builder.Append('(');
                for (int i = 0; i < expr.Length; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(", ");
                    }

                    expr[i].GetSql(builder, sqlFlags);
                }

                builder.Append(')');
            }
        }
        else
        {
            query!.GetPlanSql(builder, sqlFlags);
        }

        return builder;
    }

    /// <summary>
    /// Writes a comma-separated column list.
    /// </summary>
    private static void WriteColumns(StringBuilder builder, Column[] columns, int sqlFlags)
    {
        for (int i = 0; i < columns.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            columns[i].GetSql(builder, sqlFlags);
        }
    }

    /// <inheritdoc/>
    protected override void DoPrepare()
    {
        SessionLocal session = GetSession();
        Table target = table ?? throw DbException.GetInternalError("MERGE without a table");
        if (columns is null)
        {
            if (ValuesExpressionList.Count != 0 && ValuesExpressionList[0].Length == 0)
            {
                // special case where table is used as a sequence
                columns = [];
            }
            else
            {
                columns = target.GetColumns();
            }
        }

        if (ValuesExpressionList.Count != 0)
        {
            foreach (Expression[] expr in ValuesExpressionList)
            {
                if (expr.Length != columns.Length)
                {
                    throw DbException.Get(ErrorCode.ColumnCountDoesNotMatch);
                }

                for (int i = 0; i < expr.Length; i++)
                {
                    Expression e = expr[i];
                    if (e is not null)
                    {
                        expr[i] = e.Optimize(session);
                    }
                }
            }
        }
        else
        {
            Query.Query source = query ?? throw DbException.GetInternalError("MERGE without a query");
            source.Prepare();
            if (source.GetColumnCount() != columns.Length)
            {
                throw DbException.Get(ErrorCode.ColumnCountDoesNotMatch);
            }
        }

        if (keys is null)
        {
            Indexes.Index? primary = target.FindPrimaryKey();
            if (primary is null)
            {
                throw DbException.Get(ErrorCode.ConstraintNotFound1, "PRIMARY KEY");
            }

            List<Column> keyList = [];
            foreach (Tables.IndexColumn indexColumn in primary.GetColumns())
            {
                keyList.Add(indexColumn.GetColumn());
            }

            keys = [.. keyList];
        }

        if (isReplace)
        {
            // if there is no valid primary key,
            // the REPLACE statement degenerates to an INSERT
            foreach (Column key in keys)
            {
                bool found = false;
                foreach (Column column in columns)
                {
                    if (column.GetColumnId() == key.GetColumnId())
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return;
                }
            }
        }

        StringBuilder builder = target.GetSql(new StringBuilder("UPDATE "), HasSql.DefaultSqlFlags).Append(" SET ");
        bool hasColumn = false;
        for (int i = 0, l = columns.Length; i < l; i++)
        {
            Column column = columns[i];
            if (!column.IsGeneratedAlways())
            {
                if (hasColumn)
                {
                    builder.Append(", ");
                }

                hasColumn = true;
                column.GetSql(builder, HasSql.DefaultSqlFlags).Append("=?");
            }
        }

        if (!hasColumn)
        {
            throw DbException.GetSyntaxError(
                GetSql(), GetSql().Length, "Valid MERGE INTO statement with at least one updatable column");
        }

        builder.Append(" WHERE ");
        for (int i = 0; i < keys.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(" AND ");
            }

            keys[i].GetSql(builder, HasSql.DefaultSqlFlags).Append("=?");
        }

        CommandInterface prepared = session.PrepareCommand(builder.ToString());
        update = (Update)((CommandContainer)prepared).GetPreparedCommand();
    }

    /// <inheritdoc/>
    public override int GetCommandType() => isReplace ? CommandInterface.Replace : CommandInterface.Merge;

    /// <inheritdoc/>
    public override string GetStatementName() => isReplace ? "REPLACE" : "MERGE";

    /// <inheritdoc/>
    public override void CollectDependencies(HashSet<DbObject> dependencies)
        => throw new TODOException("Merge.collectDependencies: not ported");

    /// <summary>
    /// The effective default of a column: identity columns (sequence-backed) have none.
    /// </summary>
    /// <remarks>
    /// The reference also falls back to the domain's default; the port has no domains,
    /// so the column's own default is the whole answer.
    /// </remarks>
    private static Expression? GetEffectiveDefaultExpression(Column column)
        => column.IsIdentity() ? null : column.GetDefaultExpression();
}
