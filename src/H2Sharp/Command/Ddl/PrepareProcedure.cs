/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.PrepareProcedure.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * This class represents the statement
 * PREPARE
 */
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Expressions;

namespace H2Sharp.Command.Ddl;

/// <summary>
/// The PREPARE statement: it stores the statement it names under a name of the session,
/// with the parameters the text holds.
/// </summary>
public sealed class PrepareProcedure : DefineCommand
{
    private string? procedureName;

    private Prepared? prepared;

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    public PrepareProcedure(SessionLocal session)
        : base(session)
    {
    }

    /// <inheritdoc/>
    public override void CheckParameters()
    {
        // no not check parameters
    }

    /// <inheritdoc/>
    public override long Update()
    {
        Prepared prep = prepared!;
        Procedure proc = new(procedureName!, prep);
        // The field, not GetParameters(): the override answers an empty list for callers
        // asking about this command, while the stored statement takes the list the parser
        // gave this command, as the reference's update() does.
        prep.SetParameterList(parameters);
        prep.SetPrepareAlways(IsPrepareAlways());
        prep.Prepare();
        GetSession().AddProcedure(proc);
        return 0;
    }

    /// <summary>Sets the name EXECUTE and DEALLOCATE will use.</summary>
    public void SetProcedureName(string name) => procedureName = name;

    /// <summary>Sets the statement the session stores.</summary>
    public void SetPrepared(Prepared prep) => prepared = prep;

    /// <inheritdoc/>
    public override List<Expressions.Parameter> GetParameters() => [];

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.Prepare;
}
