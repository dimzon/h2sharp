/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.dml.Update.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * UPDATE ... SET ... WHERE.
 *
 * What the reference does that a setter-shaped port would not: it reads the row under a lock and
 * rechecks the condition after locking, so a row another transaction changed between the scan
 * and the write is not overwritten by a condition that was true when it was read. That check
 * lives in DataChangeStatement.LockAndRecheckCondition, and it is the reason this class holds a
 * condition at all rather than a list of rows.
 *
 * Not ported: the members are the reference's and each one refuses.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tables;

namespace H2Sharp.Command.Dml;

/// <summary>
/// An UPDATE statement.
/// </summary>
public sealed class Update : FilteredDataChangeStatement
{
    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    public Update(SessionLocal session)
        : base(session)
    {
    }

    /// <summary>
    /// What to set each column to.
    /// </summary>
    private SetClauseList? setClauseList;

    /// <summary>
    /// Sets what to set each column to.
    /// </summary>
    /// <param name="setClauseList">the assignments</param>
    public void SetSetClauseList(SetClauseList setClauseList)
    {
        ArgumentNullException.ThrowIfNull(setClauseList);
        this.setClauseList = setClauseList;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Collected first, written after: the scan walks the live index, and writing while it
    /// walks would move what it has not read yet. The reference stages the rows the same way
    /// before it writes them. Locks, triggers, delta collectors and FETCH limits wait for
    /// their layers.
    /// </remarks>
    public override long Run(ResultTarget? deltaChangeCollector, Tables.DataChangeDeltaTable.ResultOption? deltaChangeCollectionMode)
    {
        SessionLocal session = GetSession();
        Tables.TableFilter filter = GetTableFilter();
        Tables.Table table = filter.GetTable();
        SetClauseList assignments = setClauseList
            ?? throw Message.DbException.GetInternalError("UPDATE without assignments");
        filter.SetIndex(table.GetScanIndex(session)!, reverse: false);
        filter.StartQuery(session);
        filter.Reset();
        List<Result.Row> matched = [];
        Expressions.Expression? condition = GetCondition();
        while (filter.Next())
        {
            Result.Row? row = filter.Get();
            session.SetCurrentRow(row);
            if (row is not null && (condition is null || condition.GetBooleanValue(session)))
            {
                matched.Add(row);
            }
        }

        long count = 0;
        foreach (Result.Row row in matched)
        {
            session.SetCurrentRow(row);
            Result.Row newRow = assignments.PrepareUpdate(session, row);
            if (deltaChangeCollectionMode == Tables.DataChangeDeltaTable.ResultOption.OLD)
            {
                // The row as it was, before the write.
                deltaChangeCollector?.AddRow(row.GetValueList());
            }
            else if (deltaChangeCollectionMode == Tables.DataChangeDeltaTable.ResultOption.NEW)
            {
                // The row as it is now: the defaults are evaluated, the triggers have not run.
                deltaChangeCollector?.AddRow([.. newRow.GetValueList()]);
            }

            // The reference fires the before-row in SetClauseList.prepareUpdate and the
            // after-row in a batch after the write; with one row at a time the pair fires
            // around the write, with the same checks.
            if (!table.FireRow() || !table.FireBeforeRow(session, row, newRow))
            {
                table.UpdateRow(session, row, newRow);
                if (deltaChangeCollectionMode == Tables.DataChangeDeltaTable.ResultOption.FINAL)
                {
                    deltaChangeCollector?.AddRow(newRow.GetValueList());
                }

                table.FireAfterRow(session, row, newRow, rollback: false);
            }

            count++;
        }

        session.SetCurrentRow(null);
        return count;
    }

    /// <inheritdoc/>
    public override StringBuilder GetPlanSql(StringBuilder builder, int sqlFlags)
        => throw new TODOException("Update.getPlanSQL: not ported");

    /// <summary>
    /// Gets the kind of this statement.
    /// </summary>
    /// <returns>the kind</returns>
    public override int GetCommandType() => CommandInterface.Update;

    /// <inheritdoc/>
    public override string GetStatementName() => "UPDATE";

    /// <inheritdoc/>
    public override void CollectDependencies(HashSet<DbObject> dependencies)
        => throw new TODOException("Update.collectDependencies: not ported");

    /// <summary>
    /// Gets the insert used when a duplicate key turns this statement into an insert.
    /// </summary>
    /// <returns>the insert, or null</returns>
    public Insert? GetOnDuplicateKeyInsert()
        => throw new TODOException("Update.getOnDuplicateKeyInsert: not ported");






    /// <inheritdoc/>
    public override bool IsRetryable() => true;
    /// <inheritdoc/>
    /// <remarks>
    /// The parser reads the text first and hands this statement its filter, assignments and
    /// condition, so prepare only maps the columns and optimizes the condition.
    /// </remarks>
    protected override void DoPrepare()
    {
        Tables.TableFilter filter = GetTableFilter();
        if (setClauseList is not null)
        {
            setClauseList.MapColumns(GetSession(), filter);
        }

        Expressions.Expression? condition = GetCondition();
        if (condition is not null)
        {
            condition.MapColumns(filter, 0, Expressions.Expression.MapInitial);
            SetCondition(condition.OptimizeCondition(GetSession()));
        }
    }
}
