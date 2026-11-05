/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.dml.ExecuteProcedure.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * This class represents the statement
 * EXECUTE
 */
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Result;
using H2Sharp.Values;

namespace H2Sharp.Command.Dml;

/// <summary>
/// The EXECUTE statement: it runs a statement the session prepared with PREPARE, binding
/// the argument expressions to its parameters by position.
/// </summary>
public sealed class ExecuteProcedure : Prepared
{
    private readonly List<Expression> expressions = [];

    private Procedure? procedure;

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    public ExecuteProcedure(SessionLocal session)
        : base(session)
    {
    }

    /// <summary>Sets the procedure this statement runs.</summary>
    public void SetProcedure(Procedure procedure) => this.procedure = procedure;

    /// <summary>
    /// Set the expression at the given index.
    /// </summary>
    /// <param name="index">the index (0 based)</param>
    /// <param name="expr">the expression</param>
    public void SetExpression(int index, Expression expr) => expressions.Insert(index, expr);

    private void SetParameters()
    {
        Prepared prepared = procedure!.GetPrepared();
        List<Expressions.Parameter>? parameters = prepared.GetParameters();
        if (parameters is null)
        {
            return;
        }

        int count = parameters.Count < expressions.Count ? parameters.Count : expressions.Count;
        for (int i = 0; i < count; i++)
        {
            Expressions.Parameter p = parameters[i];
            p.SetValue(expressions[i].GetValue(GetSession()));
        }
    }

    /// <inheritdoc/>
    public override bool IsQuery() => procedure!.GetPrepared().IsQuery();

    /// <inheritdoc/>
    public override long Update()
    {
        SetParameters();
        return procedure!.GetPrepared().Update();
    }

    /// <inheritdoc/>
    public override ResultInterface Run(long maxRows)
    {
        SetParameters();
        return procedure!.GetPrepared().Run(maxRows);
    }

    /// <inheritdoc/>
    public override bool IsTransactional() => true;

    /// <inheritdoc/>
    public override ResultInterface? QueryMeta() => procedure!.GetPrepared().QueryMeta();

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.Execute;
}
