/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * The ADO.NET provider's connection. No Java counterpart, and not a transliteration:
 * this is the surface a .NET application talks to, and it has no equivalent in the
 * reference because it is not part of H2 either — the reference reaches a caller through
 * JDBC, and this type is what the same job looks like on this side.
 *
 * Everything that needs the database refuses. That is the honest state of the port: there
 * is no engine yet (no Database, no Session, no Parser), so a connection string is text
 * this type can hold and nothing more. The refusals are TODOException rather than a
 * plausible answer, because a provider that answers "version 2.5" or "database H2" without
 * having connected is worse than one that says it cannot.
 *
 * Naming. H2Connection rather than Connection, because `Connection` in a namespace that is
 * imported next to System.Data.Common collides with DbConnection, SqlConnection and every
 * other provider's own type; the prefix also says which product the type belongs to, which
 * is the one thing a provider type name is for.
 */
using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using H2Sharp.Engine;
using H2DbException = H2Sharp.Message.DbException;

namespace H2Sharp.Data;

/// <summary>
/// A connection to an H2Sharp database. This is the entry point of the ADO.NET provider.
/// </summary>
public sealed class H2Connection : DbConnection
{
    private string connectionString = string.Empty;

    private SessionLocal? session;

    private string? openedDatabaseName;

    /// <summary>
    /// Creates a connection with no connection string.
    /// </summary>
    public H2Connection()
    {
    }

    /// <summary>
    /// Creates a connection for the given connection string.
    /// </summary>
    /// <param name="connectionString">the connection string</param>
    public H2Connection(string connectionString)
    {
        this.connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    /// <summary>
    /// Gets or sets the connection string. Setting it to null or empty is the ADO.NET
    /// spelling of "no connection string", so it is stored rather than refused: the base
    /// contract allows it, and refusing would make a data source throw before it ever got
    /// to say anything useful. The <see cref="AllowNullAttribute"/> is not decoration —
    /// <see cref="DbConnection"/> declares it, so an override that drops it is a nullability
    /// error rather than a warning.
    /// </summary>
    [AllowNull]
    public override string ConnectionString
    {
        get => connectionString;
        set => connectionString = value ?? string.Empty;
    }

    /// <summary>
    /// Gets the name of the current database, or the initial catalog.
    /// </summary>
    /// <remarks>
    /// Refuses: there is no connection yet, and a name invented from the connection string
    /// would be a guess that reads like an answer.
    /// </remarks>
    public override string Database => throw new TODOException("H2Connection.Database: no engine yet");

    /// <summary>
    /// Gets the name of the server this connection is attached to.
    /// </summary>
    public override string DataSource => throw new TODOException("H2Connection.DataSource: no engine yet");

    /// <summary>
    /// Gets the version of the server this connection is attached to.
    /// </summary>
    /// <remarks>
    /// Refuses rather than reporting H2Sharp's own version: a server version is what the
    /// server says, and a client that answers for it is lying in a way callers act on.
    /// </remarks>
    public override string ServerVersion => throw new TODOException("H2Connection.ServerVersion: no engine yet");

    /// <summary>
    /// Gets the state of the connection.
    /// </summary>
    public override ConnectionState State => session is null ? ConnectionState.Closed : ConnectionState.Open;

    /// <summary>
    /// Gets the database this connection is attached to, or null while closed.
    /// </summary>
    /// <remarks>
    /// Not the <c>Database</c> property above, which answers the *name* of the current database
    /// as ADO.NET defines it. The type here is <c>H2Sharp.Engine.Database</c>, and a property of
    /// that name cannot say so: inside this class the word binds to the type, not to the member.
    /// </remarks>
    public Engine.Database? CurrentDatabase => session?.GetDatabase();

    /// <summary>
    /// Gets the session this connection runs statements on, or null while closed.
    /// </summary>
    internal SessionLocal? Session => session;

    /// <summary>
    /// Opens the connection: parses the connection string and gets a session on the database it
    /// names.
    /// </summary>
    /// <remarks>
    /// Opening twice is refused rather than ignored, and that is the reference's rule: a second
    /// open on an open connection would hand out a second session, and the close of either one
    /// would then take the database away from the other.
    /// </remarks>
    public override void Open()
    {
        if (session is not null)
        {
            throw H2DbException.FromUser("08003", "The connection is already open");
        }

        ConnectionInfo connectionInfo = new(connectionString);
        session = H2Sharp.Engine.Engine.OpenSession(connectionInfo);
        openedDatabaseName = connectionInfo.GetName();
    }

    /// <summary>
    /// Closes the connection, and gives the session back.
    /// </summary>
    /// <remarks>
    /// Closing twice, or closing one that was never open, does nothing — which is what makes
    /// <c>using</c> safe and what keeps a database alive until its *last* session is gone.
    /// </remarks>
    public override void Close()
    {
        SessionLocal? current = session;
        session = null;
        current?.Close();
        if (openedDatabaseName is not null && H2Sharp.Engine.Engine.RemoveIfClosed(openedDatabaseName))
        {
            openedDatabaseName = null;
        }
    }

    /// <summary>
    /// Changes the current database for this connection.
    /// </summary>
    /// <param name="databaseName">the database name</param>
    public override void ChangeDatabase(string databaseName) => throw new TODOException("H2Connection.ChangeDatabase");

    /// <summary>
    /// Begins a database transaction.
    /// </summary>
    /// <param name="isolationLevel">the isolation level</param>
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        => throw new TODOException("H2Connection.BeginDbTransaction");

    /// <summary>
    /// Creates a command for this connection.
    /// </summary>
    protected override DbCommand CreateDbCommand()
    {
        SessionLocal? current = session;
        if (current is null)
        {
            throw H2DbException.FromUser("08003", "The connection is closed");
        }

        return new H2Command { Connection = this };
    }
}
