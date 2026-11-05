/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.DeallocateProcedure.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * This class represents the statement
 * DEALLOCATE
 */
using H2Sharp.Engine;

namespace H2Sharp.Command.Ddl;

/// <summary>
/// The DEALLOCATE statement: it removes a procedure the session prepared with PREPARE.
/// </summary>
public sealed class DeallocateProcedure : DefineCommand
{
    private string? procedureName;

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    public DeallocateProcedure(SessionLocal session)
        : base(session)
    {
    }

    /// <inheritdoc/>
    public override long Update()
    {
        GetSession().RemoveProcedure(procedureName!);
        return 0;
    }

    /// <summary>Sets the name of the procedure to remove.</summary>
    public void SetProcedureName(string name) => procedureName = name;

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.Deallocate;
}
