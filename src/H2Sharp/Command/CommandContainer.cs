/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.CommandContainer.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * One statement, wrapped so that a caller can run it.
 *
 * This is what a prepared statement becomes once it is handed out. Every question the command
 * contract asks is asked of the statement and the answer passed on: is it a query, is it
 * transactional, what are its parameters, what rows does it produce. That is the whole job, and
 * doing it here rather than in each statement is what keeps a statement about SQL and this
 * about running one.
 *
 * The statement is told about this container in the constructor, and that is not bookkeeping:
 * a statement that caches anything — a plan, a result — has to be able to reach the command it
 * belongs to, and the only one that can know is the container that made it.
 */
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Result;

namespace H2Sharp.Command;

/// <summary>
/// A prepared statement that can be run.
/// </summary>
public class CommandContainer : Command
{
    private Prepared command;

    /// <summary>
    /// Wraps a prepared statement.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="sql">the statement text</param>
    /// <param name="prepared">the prepared statement</param>
    public CommandContainer(SessionLocal session, string sql, Prepared prepared)
        : base(session, sql)
    {
        System.ArgumentNullException.ThrowIfNull(prepared);
        command = prepared;
        prepared.SetCommand(this);
    }

    /// <summary>
    /// Gets the prepared statement this container runs.
    /// </summary>
    /// <returns>the prepared statement</returns>
    public Prepared GetPreparedCommand() => command;

    /// <inheritdoc/>
    public override int GetCommandType() => command.GetCommandType();

    /// <inheritdoc/>
    public override bool IsTransactional() => command.IsTransactional();

    /// <inheritdoc/>
    public override bool IsQuery() => command.IsQuery();

    /// <inheritdoc/>
    public override bool IsReadOnly() => command.IsReadOnly();

    /// <inheritdoc/>
    /// <remarks>
    /// A statement that carries its own values (the {n: ...} form) hands out an empty list:
    /// handing the parameters out would let a caller bind them a second time. C# generics
    /// are invariant where Java wildcards are not, so the live list cannot be handed out as
    /// a list of the interface without an adapter; it is copied once, sized exactly. Both
    /// callers only enumerate.
    /// </remarks>
    public override List<ParameterInterface> GetParameters()
    {
        List<Parameter>? parameters = command.GetParameters();
        if (parameters is not null && parameters.Count > 0 && command.IsWithParamValues())
        {
            return [];
        }

        List<ParameterInterface> result = new(parameters?.Count ?? 0);
        if (parameters is not null)
        {
            foreach (Parameter parameter in parameters)
            {
                result.Add(parameter);
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public override ResultInterface? QueryMeta() => command.QueryMeta();

    /// <inheritdoc/>
    /// <remarks>
    /// The statement runs; what surrounds it — the commit of an open transaction before a
    /// non-transactional statement, the commit at the end when the session is in auto-commit —
    /// is the reference's <c>Command.executeUpdate</c> wrapper and belongs to
    /// <see cref="Command.ExecuteUpdate"/>, which is how a caller runs this.
    /// </remarks>
    public override ResultWithGeneratedKeys UpdateRows(object generatedKeysRequest)
    {
        return ResultWithGeneratedKeys.Of(command.Update());
    }

    /// <inheritdoc/>
    public override ResultInterface Query(long maxrows) => command.Run(maxrows);

    /// <inheritdoc/>
    public override bool IsCacheable() => command.IsCacheable();

    /// <inheritdoc/>
    public override ISet<DbObject> GetDependencies()
    {
        HashSet<DbObject> dependencies = [];
        command.CollectDependencies(dependencies);
        return dependencies;
    }

    /// <inheritdoc/>
    public override void InvalidateCachedResult(Tables.Table reason) => command.InvalidateCachedResult(reason);

    /// <inheritdoc/>
    protected override bool IsRetryable() => command.IsRetryable();
}
