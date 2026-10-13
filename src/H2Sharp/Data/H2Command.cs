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
 * Running it refuses for now, and says which piece is missing: the parser. Everything around it
 * is real, so that the red tests stop being red for the wrong reason — a command that was still
 * a stub would make "no command at all" and "no parser" look the same failure, and the red list
 * would stop pointing at the work.
 */
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
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

    /// <summary>
    /// Creates a command.
    /// </summary>
    public H2Command()
    {
        parameterCollection = new H2ParameterCollection(this, parameters);
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
        set => commandText = value ?? string.Empty;
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
    /// A transaction that belongs to another connection is refused, which is the check that
    /// catches a command reused across two connections: without it the second connection would
    /// run the statement inside a transaction of the first, and the failure would surface as a
    /// mysterious deadlock rather than as a mistake at the call site.
    /// </remarks>
    protected override DbTransaction? DbTransaction
    {
        get => transaction;
        set
        {
            if (value is not null && value.Connection != connection)
            {
                throw new InvalidOperationException("The transaction belongs to another connection");
            }

            transaction = (H2Transaction?)value;
        }
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
    public override int ExecuteNonQuery() => ExecuteCore();

    /// <summary>
    /// Runs the command and returns the first column of the first row.
    /// </summary>
    public override object? ExecuteScalar() => ExecuteCore();

    /// <summary>
    /// Runs the command and returns a reader over its results.
    /// </summary>
    /// <param name="behavior">how the reader behaves</param>
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        => throw new TODOException("H2Command.ExecuteReader: the statement produced no reader yet");

    /// <summary>
    /// Prepares the command.
    /// </summary>
    /// <remarks>
    /// A no-op, as it is in H2: there is no server round trip to prepare for, the statement is
    /// parsed when it runs. It is not an error either — ADO.NET callers call it as part of the
    /// normal path, and refusing would make Dapper's prepared path fail on a provider that is
    /// behaving correctly.
    /// </remarks>
    public override void Prepare()
    {
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
    /// Runs the statement.
    /// </summary>
    private int ExecuteCore()
    {
        SessionLocal? current = connection?.Session;
        if (current is null)
        {
            throw H2DbException.FromUser("08003", "The connection is closed");
        }

        if (commandText.Length == 0)
        {
            throw H2DbException.FromUser("42000", "The command text is empty");
        }

        _ = current.PrepareCommand(commandText);
        throw new TODOException("H2Command: the statement ran but produced no result yet");
    }
}
