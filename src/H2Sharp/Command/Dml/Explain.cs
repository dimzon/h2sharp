/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.dml.Explain, H2 2.5.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * EXPLAIN and EXPLAIN ANALYZE.
 *
 * The reference runs the nested statement for ANALYZE and prints its plan either way. Store
 * statistics are not ported, so the plan of a persistent database comes without the statistics
 * block the reference appends; the in-memory databases used by the tests have no statistics
 * block there either.
 */
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Command.Dml;

/// <summary>
/// The EXPLAIN and EXPLAIN ANALYZE statements.
/// </summary>
public sealed class Explain : Prepared
{
    private Prepared command = null!;

    private LocalResult? result;

    private bool executeCommand;

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    public Explain(SessionLocal session)
        : base(session)
    {
    }

    /// <summary>Sets the statement whose plan is printed.</summary>
    // The reference also exposes getCommand(); Prepared.GetCommand already returns Command?,
    // so the nested Prepared cannot be returned under that name here. Nothing in the port
    // calls it: the parser only sets the nested statement.
    public void SetCommand(Prepared command) => this.command = command;

    /// <summary>Whether EXPLAIN ANALYZE runs the statement before printing its plan.</summary>
    public void SetExecuteCommand(bool executeCommand) => this.executeCommand = executeCommand;

    /// <inheritdoc/>
    public override void Prepare() => command.Prepare();

    /// <inheritdoc/>
    public override bool IsQuery() => true;

    /// <inheritdoc/>
    public override bool IsTransactional() => true;

    /// <inheritdoc/>
    public override bool IsReadOnly() => command.IsReadOnly();

    /// <inheritdoc/>
    public override int GetCommandType()
        => executeCommand ? CommandInterface.ExplainAnalyze : CommandInterface.Explain;

    /// <inheritdoc/>
    public override ResultInterface QueryMeta() => Run(-1);

    /// <inheritdoc/>
    public override ResultInterface Run(long maxrows)
    {
        Database db = GetSession().GetDatabase();
        Expression[] expressions =
        [
            new ExpressionColumn(db, new Column("PLAN", TypeInfo.TypeVarchar)),
        ];
        result = new LocalResult(GetSession(), expressions, 1, 1);
        const int sqlFlags = HasSql.AddPlanInformation;
        // The port's unbounded query is -1; the reference's JDBC layer passes 0 for no limit.
        // Both mean "add the plan", while SchemaOnly's 0 means "metadata only".
        if (maxrows != 0)
        {
            string plan;
            if (executeCommand)
            {
                if (command.IsQuery())
                {
                    command.Run(maxrows);
                }
                else
                {
                    command.Update();
                }
            }

            plan = command.GetPlanSql(sqlFlags);
            result.AddRow(ValueVarchar.Get(plan));
        }

        result.Done();
        return result;
    }

    /// <inheritdoc/>
    public override void CollectDependencies(HashSet<DbObject> dependencies)
        => command.CollectDependencies(dependencies);
}
