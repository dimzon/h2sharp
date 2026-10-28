// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.api.DatabaseEventListener.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified: Java interface constants become C# const members of a static class beside
// the interface (C# interfaces cannot declare constants), and java.util.EventListener
// has no .NET counterpart, so the interface extends nothing. SQLException is DbException.
using System.Data.Common;

namespace H2Sharp.Api;

/// <summary>
/// The states a <see cref="DatabaseEventListener"/> is told about.
/// </summary>
public static class DatabaseEventListenerStates
{
    /// <summary>
    /// This state is used when scanning the database file.
    /// </summary>
    public const int StateScanFile = 0;

    /// <summary>
    /// This state is used when re-creating an index.
    /// </summary>
    public const int StateCreateIndex = 1;

    /// <summary>
    /// This state is used when re-applying the transaction log or rolling back
    /// uncommitted transactions.
    /// </summary>
    public const int StateRecover = 2;

    /// <summary>
    /// This state is used during the BACKUP command.
    /// </summary>
    public const int StateBackupFile = 3;

    /// <summary>
    /// This state is used after re-connecting to a database (if auto-reconnect
    /// is enabled).
    /// </summary>
    public const int StateReconnected = 4;

    /// <summary>
    /// This state is used when a query starts.
    /// </summary>
    public const int StateStatementStart = 5;

    /// <summary>
    /// This state is used when a query ends.
    /// </summary>
    public const int StateStatementEnd = 6;

    /// <summary>
    /// This state is used for periodic notification during long-running queries.
    /// </summary>
    public const int StateStatementProgress = 7;
}

/// <summary>
/// A class that implements this interface can get notified about exceptions
/// and other events. A database event listener can be registered when
/// connecting to a database. Example connection string:
/// Data Source=test;Storage=File;DATABASE_EVENT_LISTENER='com.acme.DbListener'
/// </summary>
public interface DatabaseEventListener
{
    /// <summary>
    /// This method is called just after creating the object.
    /// This is done when opening the database if the listener is specified
    /// in the database URL, but may be later if the listener is set at
    /// runtime with the SET SQL statement.
    /// </summary>
    /// <param name="url">the database URL</param>
    void Init(string url)
    {
    }

    /// <summary>
    /// This method is called after the database has been opened. It is safe to
    /// connect to the database and execute statements at this point.
    /// </summary>
    void Opened()
    {
    }

    /// <summary>
    /// This method is called if an exception occurred.
    /// </summary>
    /// <param name="e">the exception</param>
    /// <param name="sql">the SQL statement</param>
    void ExceptionThrown(DbException e, string sql)
    {
    }

    /// <summary>
    /// This method is called for long running events, such as recovering,
    /// scanning a file or building an index.
    /// <para>
    /// More states might be added in future versions, therefore implementations
    /// should silently ignore states that they don't understand.
    /// </para>
    /// </summary>
    /// <param name="state">the state</param>
    /// <param name="name">the object name</param>
    /// <param name="x">the current position</param>
    /// <param name="max">the highest possible value or 0 if unknown</param>
    void SetProgress(int state, string name, long x, long max)
    {
    }

    /// <summary>
    /// This method is called before the database is closed normally. It is safe
    /// to connect to the database and execute statements at this point, however
    /// the connection must be closed before the method returns.
    /// </summary>
    void ClosingDatabase()
    {
    }
}

