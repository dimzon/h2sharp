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
    public override List<ParameterInterface> GetParameters()
    {
        List<Parameter>? parameters = command.GetParameters();
        List<ParameterInterface> result = [];
        if (parameters is not null)
        {
            // A statement that carries its parameter values hands out a copy of the list, so that
            // a caller who adds to what it got back cannot change what the statement will run
            // with.
            if (parameters.Count > 0 && command.IsWithParamValues())
            {
                foreach (Parameter parameter in parameters)
                {
                    result.Add(parameter);
                }

                return result;
            }

            foreach (Parameter parameter in parameters)
            {
                result.Add(parameter);
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public override ResultInterface QueryMeta() => command.QueryMeta();

    /// <inheritdoc/>
    public override ResultWithGeneratedKeys UpdateRows(object generatedKeysRequest)
        => ResultWithGeneratedKeys.Of(command.Update());

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
