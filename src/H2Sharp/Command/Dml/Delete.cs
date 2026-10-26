/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.dml.Delete.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * DELETE FROM ... WHERE.
 *
 * The shortest of the three data change statements, and the only one whose count can be wrong in
 * a way that is easy to miss: the scan is over the index, and a row removed while the scan was
 * running is gone, so the reference counts the rows it removed rather than the rows the
 * condition matched. A port that counted matches would answer a larger number than the rows
 * that were actually deleted.
 *
 * Not ported: the members are the reference's and each one refuses.
 */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Command.Dml;

/// <summary>
/// A DELETE statement.
/// </summary>
public sealed class Delete : FilteredDataChangeStatement
{
    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    public Delete(SessionLocal session)
        : base(session)
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Collected first, removed after, for the same reason updates stage their rows: the scan
    /// walks the live index. The count is the rows removed, not the rows matched.
    /// </remarks>
    public override long Run(ResultTarget? deltaChangeCollector, Tables.DataChangeDeltaTable.ResultOption? deltaChangeCollectionMode)
    {
        SessionLocal session = GetSession();
        Tables.TableFilter filter = GetTableFilter();
        Tables.Table table = filter.GetTable();
        filter.SetIndex(table.GetScanIndex(session)!, reverse: false);
        filter.StartQuery(session);
        filter.Reset();
        List<Result.Row> matched = [];
        Expressions.Expression? condition = GetCondition();
        long limitRows = -1;
        if (fetchExpr is not null)
        {
            Value v = fetchExpr.GetValue(session);
            if (v == ValueNull.Instance || (limitRows = v.GetLong()) < 0)
            {
                throw DbException.GetInvalidValueException("FETCH", v);
            }
        }

        while (filter.Next())
        {
            if (limitRows >= 0 && matched.Count >= limitRows)
            {
                break;
            }

            Result.Row? row = filter.Get();
            session.SetCurrentRow(row);
            if (row is not null && (condition is null || condition.GetBooleanValue(session)))
            {
                matched.Add(row);
            }
        }

        session.SetCurrentRow(null);
        foreach (Result.Row row in matched)
        {
            if (deltaChangeCollectionMode == Tables.DataChangeDeltaTable.ResultOption.OLD)
            {
                // The row as it was, before it is removed.
                deltaChangeCollector?.AddRow(row.GetValueList());
            }

            // The reference asks fireRow first: with nothing to fire, the row goes without
            // a call, and with something to fire, the before-row decides.
            if (!table.FireRow() || !table.FireBeforeRow(session, row, null))
            {
                table.RemoveRow(session, row.GetKey());
                table.FireAfterRow(session, row, null, rollback: false);
            }
        }

        return matched.Count;
    }

    /// <inheritdoc/>
    public override StringBuilder GetPlanSql(StringBuilder builder, int sqlFlags)
        => throw new TODOException("Delete.getPlanSQL: not ported");

    /// <summary>
    /// Gets the kind of this statement.
    /// </summary>
    /// <returns>the kind</returns>
    public override int GetCommandType() => CommandInterface.Delete;

    /// <inheritdoc/>
    public override string GetStatementName() => "DELETE";

    /// <inheritdoc/>
    public override void CollectDependencies(HashSet<DbObject> dependencies)
        => throw new TODOException("Delete.collectDependencies: not ported");






    /// <inheritdoc/>
    public override bool IsRetryable() => true;
    /// <inheritdoc/>
    /// <remarks>
    /// The parser reads the text first and hands this statement its filter and condition,
    /// so prepare only maps the columns and optimizes the condition.
    /// </remarks>
    protected override void DoPrepare()
    {
        Tables.TableFilter filter = GetTableFilter();
        Expressions.Expression? condition = GetCondition();
        if (condition is not null)
        {
            condition.MapColumns(filter, 0, Expressions.Expression.MapInitial);
            SetCondition(condition.OptimizeCondition(GetSession()));
        }

        if (fetchExpr is not null)
        {
            fetchExpr.MapColumns(filter, 0, Expressions.Expression.MapInitial);
            fetchExpr = fetchExpr.Optimize(GetSession());
        }
    }
}
