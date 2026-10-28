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
 * A statement that cannot be prepared yet is deferred, as in the reference: the parser stops
 * at the first one and keeps the rest of the text, and it is parsed and run once the earlier
 * statements have had their effect. A CREATE TABLE followed by a DELETE in one text is the
 * reason: the DELETE cannot name the table before the CREATE has run.
 */
using System;
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

    private readonly List<Parameter> parameters;

    private string? remaining;

    /// <summary>
    /// Wraps the prepared statements of one text.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="sql">the statement text</param>
    /// <param name="prepared">the prepared statements, in order</param>
    /// <param name="parameters">the parameters of the whole text</param>
    /// <param name="remaining">the text that was deferred until execution, or null</param>
    public CommandList(
        SessionLocal session,
        string sql,
        List<Prepared> prepared,
        List<Parameter> parameters,
        string? remaining)
        : base(session, sql)
    {
        commands = new List<CommandContainer>(prepared.Count);
        foreach (Prepared p in prepared)
        {
            CommandContainer container = new(session, p.GetSql(), p);
            commands.Add(container);
        }

        this.parameters = parameters;
        this.remaining = remaining;
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
    public override List<ParameterInterface> GetParameters()
    {
        List<ParameterInterface> result = new(parameters.Count);
        foreach (Parameter parameter in parameters)
        {
            result.Add(parameter);
        }

        return result;
    }

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

        ExecuteRemaining();
        return first;
    }

    /// <inheritdoc/>
    public override ResultInterface Query(long maxrows) => commands[0].Query(maxrows);

    /// <summary>
    /// Parses and runs the statement text that was deferred while the command was prepared.
    /// </summary>
    internal void ExecuteRemaining()
    {
        string? text = remaining;
        if (text is null)
        {
            return;
        }

        remaining = null;
        Command command = new Parser(Session).PrepareCommand(text, parameters);
        ExecuteForEffect(command);
    }

    private static void ExecuteForEffect(Command command)
    {
        if (command is CommandList list)
        {
            foreach (CommandContainer container in list.GetCommands())
            {
                ExecuteContainer(container);
            }

            list.ExecuteRemaining();
            list.Close();
            return;
        }

        if (command is CommandContainer single)
        {
            ExecuteContainer(single);
            single.Close();
            return;
        }

        throw new InvalidOperationException("The deferred statement is not a runnable command");
    }

    private static void ExecuteContainer(CommandContainer container)
    {
        if (container.IsQuery())
        {
            using ResultInterface result = container.ExecuteQuery(-1, 0, false);
            while (result.Next())
            {
            }
        }
        else
        {
            _ = container.ExecuteUpdate(false);
        }
    }

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
