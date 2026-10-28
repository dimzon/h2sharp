/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * A command over the provider.
 *
 * The reference reaches a caller through JdbcCommand, which wraps the same work: a command text,
 * a parameter list, and the three ways to run it. This type is that shape without the wrapper,
 * because on this side there is no driver manager and no wire protocol between the caller and
 * the session — a command holds the connection's session and hands the text to it.
 *
 * Running hands the text to the session's parser, binds the parameter values by the positions
 * the rewriter gave them, and keeps one container per statement of the text: the first answers,
 * and the rest run for effect — drained if they are queries, counted if they are updates.
 *
 * Named parameters are written "$name" and rewritten to "$N" before the text reaches the engine.
 * "@" and ":" are not parameter prefixes: the first is an H2 session variable and the second is
 * ordinary syntax (see JSON_OBJECT), so they pass through untouched.
 */
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using H2Sharp.Engine;
using H2DbException = H2Sharp.Message.DbException;

namespace H2Sharp.Data;

/// <summary>
/// A command to run against a database.
/// </summary>
public sealed class H2Command : DbCommand
{
    private string commandText = string.Empty;

    private H2Connection? connection;

    private H2Transaction? transaction;

    private readonly List<H2Parameter> parameters = [];

    /// <summary>
    /// The parameter collection, created once: the list behind it never changes identity, only
    /// contents, so one wrapper serves every access instead of garbage per statement.
    /// </summary>
    private readonly H2ParameterCollection parameterCollection;

    private int openReaders;

    /// <summary>
    /// Creates a command.
    /// </summary>
    public H2Command()
    {
        parameterCollection = new H2ParameterCollection(this, parameters);
    }

    /// <summary>
    /// Creates a command with the given text on the given connection.
    /// </summary>
    /// <param name="commandText">the command text</param>
    /// <param name="connection">the connection</param>
    public H2Command(string commandText, H2Connection connection)
        : this()
    {
        CommandText = commandText;
        DbConnection = connection;
    }

    /// <summary>
    /// Gets or sets the command text.
    /// </summary>
    /// <remarks>
    /// The base declares the setter as [AllowNull] — null means "no statement", which is the
    /// same as empty here — and an override that drops the attribute is a nullability error
    /// rather than a warning.
    /// </remarks>
    [AllowNull]
    public override string CommandText
    {
        get => commandText;
        set
        {
            if (openReaders > 0)
            {
                throw new InvalidOperationException("The command has an open reader");
            }

            commandText = value ?? string.Empty;
        }
    }

    /// <summary>
    /// Gets or sets the connection this command runs on.
    /// </summary>
    /// <remarks>
    /// A connection of another provider is refused rather than stored: ADO.NET's public
    /// Connection property forwards here, so accepting it would hand the command to a session
    /// that has nothing to do with the statement.
    /// </remarks>
    protected override DbConnection? DbConnection
    {
        get => connection;
        set
        {
            if (openReaders > 0)
            {
                throw new InvalidOperationException("The command has an open reader");
            }

            if (value is null)
            {
                connection = null;
                return;
            }

            connection = value as H2Connection
                ?? throw new ArgumentException("The connection is not an H2Connection", nameof(value));
        }
    }

    /// <summary>
    /// Gets the connection this command runs on.
    /// </summary>
    internal H2Connection? H2Connection => connection;

    /// <summary>
    /// Gets or sets how the command is run. Only text is meaningful here, and it is the default:
    /// a stored procedure call is not a thing this provider has.
    /// </summary>
    public override CommandType CommandType { get; set; } = CommandType.Text;

    /// <summary>
    /// Gets or sets the transaction this command runs in.
    /// </summary>
    /// <remarks>
    /// Stored, not checked: a transaction of another connection refuses when the command
    /// runs, where the mistake surfaces next to the statement it would have run in.
    /// </remarks>
    protected override DbTransaction? DbTransaction
    {
        get => transaction;
        set => transaction = (H2Transaction?)value;
    }

    /// <summary>
    /// Gets or sets how long the command may take, in seconds. Zero means no limit, which is
    /// what the provider reports and what the reference does for a statement with no timeout.
    /// </summary>
    public override int CommandTimeout { get; set; }

    /// <summary>
    /// Gets or sets how a parameter without a value is treated.
    /// </summary>
    public override bool DesignTimeVisible { get; set; }

    /// <summary>
    /// Gets or sets whether the command's text is shown together with its parameters.
    /// </summary>
    public override UpdateRowSource UpdatedRowSource { get; set; }

    /// <summary>
    /// Gets the parameters of this command.
    /// </summary>
    protected override DbParameterCollection DbParameterCollection => parameterCollection;

    /// <summary>
    /// Runs the command and returns the rows it affected.
    /// </summary>
    /// <remarks>
    /// A query run this way still runs: ADO.NET answers -1 for it, and the rows it
    /// would have produced are read and dropped rather than left open underneath
    /// the caller. Of several statements, the first answers and the rest run for effect.
    /// The commands close on the way out, so the next execution finds them reusable
    /// instead of parsing again.
    /// </remarks>
    public override int ExecuteNonQuery()
    {
        List<Command.CommandContainer> containers = PrepareEngines(out Command.CommandList? lazyRemaining);
        try
        {
            long first;
            if (containers[0].IsQuery())
            {
                using Result.ResultInterface result = containers[0].ExecuteQuery(-1, 0, false);
                while (result.Next())
                {
                }

                first = -1;
            }
            else
            {
                first = containers[0].ExecuteUpdate(false).GetUpdateCount();
            }

            RunRemaining(containers);
            lazyRemaining?.ExecuteRemaining();
            return first < 0 ? -1 : Convert.ToInt32(first);
        }
        finally
        {
            CloseContainers(containers);
            lazyRemaining?.Close();
        }
    }

    /// <summary>
    /// Runs the command and returns the first column of the first row.
    /// </summary>
    public override object? ExecuteScalar()
    {
        List<Command.CommandContainer> containers = PrepareEngines(out Command.CommandList? lazyRemaining);
        try
        {
            if (!containers[0].IsQuery())
            {
                _ = containers[0].ExecuteUpdate(false);
                RunRemaining(containers);
                lazyRemaining?.ExecuteRemaining();
                return null;
            }

            object? scalar;
            using (Result.ResultInterface result = containers[0].ExecuteQuery(1, 0, false))
            {
                if (!result.Next())
                {
                    scalar = null;
                }
                else
                {
                    Values.Value?[] row = result.CurrentRow();
                    scalar = row.Length == 0 ? null : H2ValueMapper.ToObject(row[0]);
                }
            }

            RunRemaining(containers);
            lazyRemaining?.ExecuteRemaining();
            return scalar;
        }
        finally
        {
            CloseContainers(containers);
            lazyRemaining?.Close();
        }
    }

    /// <summary>
    /// Runs the command and returns a reader over its results.
    /// </summary>
    /// <param name="behavior">how the reader behaves</param>
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        if (openReaders > 0)
        {
            throw new InvalidOperationException("The command already has an open reader");
        }

        List<Command.CommandContainer> containers = PrepareEngines(out Command.CommandList? lazyRemaining);
        H2DataReader reader;
        if (!containers[0].IsQuery())
        {
            long count = containers[0].ExecuteUpdate(false).GetUpdateCount();
            RunRemaining(containers);
            lazyRemaining?.ExecuteRemaining();
            lazyRemaining?.Close();

            reader = new H2DataReader(count);
            // An update result has no rows and no result set to close; it must not keep
            // the command marked as having an open reader (H2 Statement does not either).
            return reader;
        }
        else
        {
            long maxrows = (behavior & CommandBehavior.SingleRow) != 0 ? 1
                : (behavior & CommandBehavior.SchemaOnly) != 0 ? 0 : -1;
            Result.ResultInterface result = containers[0].ExecuteQuery(maxrows, 0, false);
            lazyRemaining?.ExecuteRemaining();
            bool moreSets = ((behavior & (CommandBehavior.SingleRow
                | CommandBehavior.SingleResult | CommandBehavior.SchemaOnly)) == 0)
                && containers.Count > 1;
            List<Command.CommandContainer> remaining =
                moreSets ? containers.GetRange(1, containers.Count - 1) : [];
            Action onClose = () =>
            {
                CloseContainers(containers);
                lazyRemaining?.Close();
                openReaders--;
                if ((behavior & CommandBehavior.CloseConnection) != 0)
                {
                    connection?.Close();
                }
            };
            reader = new H2DataReader(result, onClose, remaining, maxrows);
        }

        openReaders++;
        return reader;
    }

    /// <summary>
    /// Runs the command and returns a reader over its results, asynchronously.
    /// </summary>
    /// <remarks>
    /// Synchronous underneath — the work has no I/O to await — so cancellation only
    /// refuses before it starts. File-backed runs will await for real; see OpenAsync.
    /// </remarks>
    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
        CommandBehavior behavior, CancellationToken cancellationToken)
        => SyncTask.Run(() => ExecuteDbDataReader(behavior), cancellationToken);

    /// <summary>
    /// Runs the command and returns the rows it affected, asynchronously.
    /// </summary>
    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        => SyncTask.Run(ExecuteNonQuery, cancellationToken);

    /// <summary>
    /// Runs the command and returns the first column of the first row, asynchronously.
    /// </summary>
    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
        => SyncTask.Run(ExecuteScalar, cancellationToken);

    /// <summary>
    /// Prepares the command.
    /// </summary>
    /// <remarks>
    /// There is no server round trip to prepare for — the statement is parsed when it runs —
    /// but the states that cannot run are refused here rather than at the run: no connection,
    /// a closed one, and no text are mistakes in the call, and running would fail on them
    /// further from where they were made.
    /// </remarks>
    public override void Prepare()
    {
        _ = CheckRunnableState();
    }

    /// <summary>
    /// Cancels the command.
    /// </summary>
    public override void Cancel()
    {
    }

    /// <summary>
    /// Creates a new parameter for this command.
    /// </summary>
    protected override DbParameter CreateDbParameter()
    {
        return new H2Parameter();
    }

    /// <summary>
    /// Runs every container after the first for effect: a query drains, an update counts.
    /// </summary>
    /// <param name="containers">the containers of one execution</param>
    private static void RunRemaining(List<Command.CommandContainer> containers)
    {
        for (int i = 1; i < containers.Count; i++)
        {
            Command.CommandContainer container = containers[i];
            if (container.IsQuery())
            {
                using Result.ResultInterface rest = container.ExecuteQuery(-1, 0, false);
                while (rest.Next())
                {
                }
            }
            else
            {
                _ = container.ExecuteUpdate(false);
            }
        }
    }

    /// <summary>
    /// Refuses the states in which the command cannot run and returns the session it runs on.
    /// </summary>
    /// <returns>the open session</returns>
    private SessionLocal CheckRunnableState()
    {
        SessionLocal? current = connection?.Session;
        if (current is null)
        {
            throw new InvalidOperationException("The connection is not open");
        }

        if (commandText.Length == 0)
        {
            throw new InvalidOperationException("The command text is empty");
        }

        return current;
    }

    /// <summary>
    /// Closes every container of one execution, returning them to the cache.
    /// </summary>
    /// <param name="containers">the containers that ran</param>
    private static void CloseContainers(List<Command.CommandContainer> containers)
    {
        foreach (Command.CommandContainer container in containers)
        {
            container.Close();
        }
    }

    private static readonly List<H2Parameter> NoParameters = [];

    /// <summary>
    /// Prepares the text, binds the values, and hands back the running commands: one per
    /// statement the text holds.
    /// </summary>
    /// <remarks>
    /// The engine knows "?", "?N" and "$N"; "$name" is rewritten to "$N" before the text
    /// reaches it, and the values are bound by the positions they took. A placeholder with no
    /// value is refused here rather than read as NULL there: NULL is a value the caller could
    /// have meant, a missing one is a mistake in the call. Parameters are numbered across the
    /// whole text, and every container of one parse shares the same list, so one pass binds
    /// every statement of it.
    /// </remarks>
    /// <returns>the prepared containers, in order</returns>
    private List<Command.CommandContainer> PrepareEngines(out Command.CommandList? lazyRemaining)
    {
        SessionLocal current = CheckRunnableState();
        H2Connection owner = connection!;

        string text = commandText;
        List<H2Parameter> ordered = NoParameters;
        if (parameters.Count > 0 || commandText.IndexOf('$') >= 0)
        {
            (text, ordered) = H2ParameterRewriter.Rewrite(
                commandText, parameterCollection, current);
        }

        if (owner.CurrentTransaction is not null && transaction is null)
        {
            throw new InvalidOperationException("The connection has an open transaction");
        }

        if (transaction is not null && !ReferenceEquals(transaction.OwningConnection, owner))
        {
            throw new InvalidOperationException("The transaction belongs to another connection");
        }

        H2Sharp.Command.CommandInterface command = current.PrepareCommand(text);
        List<Command.CommandContainer> containers;
        if (command is Command.CommandList list)
        {
            containers = [.. list.GetCommands()];
            lazyRemaining = list;
        }
        else if (command is Command.CommandContainer container)
        {
            containers = [container];
            lazyRemaining = null;
        }
        else
        {
            throw new InvalidOperationException("The statement is not a runnable command");
        }
        foreach (Expressions.ParameterInterface engineParameter in containers[0].GetParameters())
        {
            Expressions.Parameter parameter = (Expressions.Parameter)engineParameter;
            int index = parameter.GetIndex();
            if (index < 0 || index >= ordered.Count)
            {
                throw Message.DbException.Get(
                    H2Sharp.Api.ErrorCode.ParameterNotSet1,
                    "#" + (parameter.GetIndex() + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            object? value = ordered[index].Value;
            if (value is null)
            {
                throw new InvalidOperationException(
                    "Parameter '" + ordered[index].ParameterName + "' has no value;"
                    + " use DBNull.Value for NULL");
            }

            // The declared type of the parameter goes with the value: a TimeSpan declared as a
            // time of day is a TIME, and the same value without that declaration is an
            // INTERVAL DAY TO SECOND.
            parameter.SetValue(H2ValueMapper.ToValue(value, ordered[index].DbType, current), false);
        }

        return containers;
    }
}
