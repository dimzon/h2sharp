/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.command.CommandList.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A list of SQL statements.
 *
 * One text, several statements: the first answers, the rest run for effect — or, for a
 * reader, when NextResult asks for them. That second half is where this port leaves the
 * reference: H2 runs the rest up front and discards their results, because JDBC has only
 * one result set. ADO.NET walks them, so running them up front would execute every side
 * effect twice — once discarded, once read. Query therefore runs only the first statement;
 * the rest run through the containers below, on NextResult or when an update runs them all.
 *
 * Everything from the first DEFINE command on is also parsed up front here, where the
 * reference defers it until execution so that later statements can use what the earlier
 * ones create. A CREATE TABLE followed by an INSERT in one text fails at prepare with the
 * table missing, loudly rather than wrongly, until the lazy remainder lands.
 */
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Result;

namespace H2Sharp.Command;

/// <summary>
/// Represents a list of SQL statements.
/// </summary>
public sealed class CommandList : Command
{
    private readonly List<CommandContainer> commands;

    /// <summary>
    /// Wraps the prepared statements of one text.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="sql">the statement text</param>
    /// <param name="prepared">the prepared statements, in order</param>
    public CommandList(SessionLocal session, string sql, List<Prepared> prepared)
        : base(session, sql)
    {
        commands = new List<CommandContainer>(prepared.Count);
        foreach (Prepared p in prepared)
        {
            CommandContainer container = new(session, p.GetSql(), p);
            commands.Add(container);
        }
    }

    /// <summary>
    /// Gets the statements of this list, in order.
    /// </summary>
    /// <returns>the statement containers</returns>
    public IReadOnlyList<CommandContainer> GetCommands() => commands;

    /// <inheritdoc/>
    public override int GetCommandType() => commands[0].GetCommandType();

    /// <inheritdoc/>
    public override bool IsTransactional() => true;

    /// <inheritdoc/>
    public override bool IsQuery() => commands[0].IsQuery();

    /// <inheritdoc/>
    public override List<ParameterInterface> GetParameters() => commands[0].GetParameters();

    /// <inheritdoc/>
    public override bool IsReadOnly() => false;

    /// <inheritdoc/>
    public override ResultInterface? QueryMeta() => commands[0].QueryMeta();

    /// <inheritdoc/>
    public override ResultWithGeneratedKeys UpdateRows(object generatedKeysRequest)
    {
        ResultWithGeneratedKeys first = commands[0].UpdateRows(generatedKeysRequest);
        for (int i = 1; i < commands.Count; i++)
        {
            _ = commands[i].UpdateRows(generatedKeysRequest);
        }

        return first;
    }

    /// <inheritdoc/>
    public override ResultInterface Query(long maxrows) => commands[0].Query(maxrows);

    /// <inheritdoc/>
    /// <remarks>
    /// Every statement, not just the list: the cache checks the list, but a statement
    /// handed out twice underneath it would run twice, so each one is marked reusable too.
    /// </remarks>
    public override void Close()
    {
        foreach (CommandContainer container in commands)
        {
            container.Close();
        }

        base.Close();
    }

    /// <inheritdoc/>
    public override ISet<DbObject> GetDependencies()
    {
        HashSet<DbObject> dependencies = [];
        foreach (CommandContainer container in commands)
        {
            foreach (DbObject dependency in container.GetDependencies())
            {
                _ = dependencies.Add(dependency);
            }
        }

        return dependencies;
    }

    /// <inheritdoc/>
    public override void InvalidateCachedResult(Tables.Table reason)
    {
        foreach (CommandContainer container in commands)
        {
            container.InvalidateCachedResult(reason);
        }
    }

    /// <inheritdoc/>
    protected override bool IsRetryable()
    {
        foreach (CommandContainer container in commands)
        {
            if (!container.IsRetryableForList())
            {
                return false;
            }
        }

        return true;
    }
}
