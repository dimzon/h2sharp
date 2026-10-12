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
    /// Sets what to set each column to.
    /// </summary>
    /// <param name="setClauseList">the assignments</param>
    public void SetSetClauseList(SetClauseList setClauseList)
        => throw new TODOException("Update.setSetClauseList: SetClauseList is not ported");

    /// <inheritdoc/>
    public override long Run(ResultTarget? deltaChangeCollector, Tables.DataChangeDeltaTable.ResultOption? deltaChangeCollectionMode)
        => throw new TODOException("Update.update: not ported");

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
    protected override void DoPrepare()
        => throw new TODOException("Prepare.doPrepare: the statement is not bound to its table yet");
}
