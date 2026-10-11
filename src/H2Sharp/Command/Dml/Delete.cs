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
using H2Sharp.Result;
using H2Sharp.Tables;

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
    public override long Run(ResultTarget? deltaChangeCollector, Tables.DataChangeDeltaTable.ResultOption? deltaChangeCollectionMode)
        => throw new TODOException("Delete.update: not ported");

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
}
