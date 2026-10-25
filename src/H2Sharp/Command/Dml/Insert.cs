/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.dml.Insert.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * INSERT INTO.
 *
 * The statement holds two ways of getting its rows and one rule about the count it answers:
 * rows given as VALUES arrive one list at a time through AddRow, rows given as a query are read
 * from that query, and either way GetRowCount is the number of rows that were *inserted* — not
 * the number that were offered. A statement that failed halfway has offered more rows than it
 * has written, and a caller that counted the offers would be told a lie about its own table.
 *
 * Not ported: the members below are the reference's shape, and each one refuses. The insert
 * itself needs the table's transaction, the identity handling and the duplicate-key path, none
 * of which exists here yet.
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
/// An INSERT statement.
/// </summary>
public sealed class Insert : CommandWithValues, ResultTarget
{
    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    public Insert(SessionLocal session)
        : base(session)
    {
    }

    /// <summary>
    /// Sets the command that is this statement's parent.
    /// </summary>
    /// <param name="command">the parent command</param>
    public new void SetCommand(Command command)
        => throw new TODOException("Insert.setCommand: not ported");


    /// <summary>
    /// The table this statement writes into, set by the statement's own parser.
    /// </summary>
    /// <returns>the table, or null before it has been bound</returns>
    public override Table? GetTable() => table;

    /// <summary>
    /// The table this statement writes into.
    /// </summary>
    /// <remarks>
    /// Set by the statement's own parser, as in the reference.
    /// </remarks>
    private Table? table;

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
    /// The columns the values are for, in row order.
    /// </summary>
    private Column[]? columns;

    private long rowNumber;

    private Query.Query? query;
    private bool? overridingSystem;
    private bool insertFromSelect;
    private ResultTarget? deltaChangeCollector;
    private Tables.DataChangeDeltaTable.ResultOption? deltaChangeCollectionMode;

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
    /// Sets whether rows that would duplicate a key are skipped rather than refused.
    /// </summary>
    /// <param name="ignore">true to ignore duplicates</param>
    public void SetIgnore(bool ignore)
        => throw new TODOException("Insert.setIgnore: not ported");

    /// <summary>
    /// Sets the query the rows are read from, for INSERT INTO ... SELECT.
    /// </summary>
    /// <param name="query">the query</param>
    public void SetQuery(Query.Query query)
        => this.query = query;

    /// <summary>
    /// Sets whether the values override what the system would generate.
    /// </summary>
    /// <param name="overridingSystem">true when the statement says OVERRIDING SYSTEM VALUE</param>
    public void SetOverridingSystem(bool? overridingSystem)
        => this.overridingSystem = overridingSystem;

    /// <summary>
    /// Sets what a column becomes when the row duplicates a key, for ON DUPLICATE KEY UPDATE.
    /// </summary>
    /// <param name="column">the column</param>
    /// <param name="expression">what to set it to</param>
    public void AddAssignmentForDuplicate(Column column, Expression expression)
        => throw new TODOException("Insert.addAssignmentForDuplicate: not ported");

    /// <inheritdoc/>
    /// <remarks>
    /// Rows given as VALUES, evaluated here and not at prepare: a row may name a parameter,
    /// and its value is not known until the statement runs. Triggers, locks, delta collectors
    /// and INSERT FROM SELECT wait for their machinery; the rows this reads are the VALUES
    /// ones the parser kept as expressions.
    /// </remarks>
    public override long Run(ResultTarget? deltaChangeCollector, Tables.DataChangeDeltaTable.ResultOption? deltaChangeCollectionMode)
    {
        this.deltaChangeCollector = deltaChangeCollector;
        this.deltaChangeCollectionMode = deltaChangeCollectionMode;
        try { return InsertRows(); }
        finally
        {
            this.deltaChangeCollector = null;
            this.deltaChangeCollectionMode = null;
        }
    }

    private long InsertRows()
    {
        SessionLocal session = GetSession();
        Table target = table ?? throw Message.DbException.GetInternalError("INSERT without a table");
        Column[] targetColumns = columns
            ?? throw Message.DbException.GetInternalError("INSERT without columns");
        rowNumber = 0;
        foreach (Expression[] expr in ValuesExpressionList)
        {
            Row newRow = target.GetTemplateRow();
            for (int i = 0; i < targetColumns.Length; i++)
            {
                if (!ReferenceEquals(expr[i], ValueExpression.Default))
                    newRow.SetValue(targetColumns[i].GetColumnId(), expr[i].GetValue(session));
            }

            rowNumber++;
            target.ConvertInsertRow(session, newRow, overridingSystem);
            if (deltaChangeCollectionMode == Tables.DataChangeDeltaTable.ResultOption.NEW)
            {
                // The row as it is now: the defaults are evaluated, the triggers have not run.
                deltaChangeCollector?.AddRow([.. newRow.GetValueList()]);
            }

            // The reference fires the before-row triggers without asking fireRow first; the
            // after-row fires only for the row that was written.
            if (!target.FireBeforeRow(session, null, newRow))
            {
                target.Lock(session, Table.WriteLock);
                target.AddRow(session, newRow);
                Tables.DataChangeDeltaTable.CollectForInsert(
                    session, target, deltaChangeCollector, deltaChangeCollectionMode, newRow);
                target.FireAfterRow(session, null, newRow, rollback: false);
            }
            else
            {
                Tables.DataChangeDeltaTable.CollectForInsert(
                    session, target, deltaChangeCollector, deltaChangeCollectionMode, newRow);
            }
        }

        if (ValuesExpressionList.Count == 0)
        {
            target.Lock(session, Table.WriteLock);
            if (insertFromSelect)
            {
                // H2 insertRows: query.query(0, this), the ResultTarget streaming path.
                _ = query!.Run(0, this);
            }
            else
            {
                ResultInterface rows = query!.Run(0);
                try
                {
                    while (rows.Next()) AddRow(rows.CurrentRow());
                }
                finally { rows.Close(); }
            }
        }

        return rowNumber;
    }

    /// <summary>
    /// Adds one row's values to the statement.
    /// </summary>
    /// <param name="values">the values</param>
    public void AddRow(params Value?[] values)
    {
        Table target = table!;
        Row row = target.GetTemplateRow();
        SetCurrentRowNumber(++rowNumber);
        for (int i = 0; i < columns!.Length; i++) row.SetValue(columns[i].GetColumnId(), values[i]!);
        target.ConvertInsertRow(GetSession(), row, overridingSystem);
        if (deltaChangeCollectionMode == Tables.DataChangeDeltaTable.ResultOption.NEW)
            deltaChangeCollector?.AddRow([.. row.GetValueList()]);
        if (!target.FireBeforeRow(GetSession(), null, row))
        {
            target.AddRow(GetSession(), row);
            Tables.DataChangeDeltaTable.CollectForInsert(GetSession(), target, deltaChangeCollector, deltaChangeCollectionMode, row);
            target.FireAfterRow(GetSession(), null, row, false);
        }
        else Tables.DataChangeDeltaTable.CollectForInsert(GetSession(), target, deltaChangeCollector, deltaChangeCollectionMode, row);
    }

    /// <inheritdoc/>
    public override long GetRowCount() => rowNumber;

    /// <inheritdoc/>
    public void LimitsWereApplied()
    { }

    /// <inheritdoc/>
    public override StringBuilder GetPlanSql(StringBuilder builder, int sqlFlags)
        => throw new TODOException("Insert.getPlanSQL: not ported");

    /// <summary>
    /// Gets the kind of this statement.
    /// </summary>
    /// <returns>the kind</returns>
    public override int GetCommandType() => CommandInterface.Insert;

    /// <inheritdoc/>
    public override string GetStatementName() => "INSERT";

    /// <summary>
    /// Sets whether the rows come from a select rather than from values.
    /// </summary>
    /// <param name="value">true for INSERT INTO ... SELECT</param>
    public void SetInsertFromSelect(bool value)
        => insertFromSelect = value;

    /// <summary>
    /// Gets what a column becomes when the row duplicates a key.
    /// </summary>
    /// <param name="columnIndex">the column index</param>
    /// <returns>the value, or null when there is none</returns>
    public Value? GetOnDuplicateKeyValue(int columnIndex)
        => throw new TODOException("Insert.getOnDuplicateKeyValue: not ported");

    /// <inheritdoc/>
    public override void CollectDependencies(HashSet<DbObject> dependencies)
        => throw new TODOException("Insert.collectDependencies: not ported");






    /// <inheritdoc/>
    public override bool IsRetryable() => true;
    /// <inheritdoc/>
    /// <remarks>
    /// The parser reads the text first and hands this statement its table, columns and rows,
    /// so prepare only checks that every row has a value for every column.
    /// </remarks>
    protected override void DoPrepare()
    {
        if (columns is null)
            columns = ValuesExpressionList.Count > 0 && ValuesExpressionList[0].Length == 0
                ? [] : table!.GetVisibleColumns();
        Column[] targetColumns = columns
            ?? throw Message.DbException.GetInternalError("INSERT without columns");
        foreach (Expression[] row in ValuesExpressionList)
        {
            if (row.Length != targetColumns.Length)
            {
                throw Message.DbException.Get(H2Sharp.Api.ErrorCode.ColumnCountDoesNotMatch);
            }

            for (int i = 0; i < row.Length; i++)
            {
                Expression expression = row[i].Optimize(GetSession());
                if (expression is Parameter parameter)
                {
                    parameter.SetColumn(targetColumns[i]);
                }

                row[i] = expression;
            }
        }
        if (ValuesExpressionList.Count == 0)
        {
            query!.Prepare();
            if (query.GetColumnCount() != targetColumns.Length)
                throw Message.DbException.Get(Api.ErrorCode.ColumnCountDoesNotMatch);
        }
    }
}
