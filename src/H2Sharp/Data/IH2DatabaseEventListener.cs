/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Data.Common;

namespace H2Sharp.Data;

/// <summary>
/// The states a <see cref="IH2DatabaseEventListener"/> is told about, mirroring the
/// reference's <c>DatabaseEventListener</c> constants as an enum.
/// </summary>
public enum H2DatabaseEventState
{
    /// <summary>Scanning the database file.</summary>
    ScanFile = 0,

    /// <summary>Re-creating an index.</summary>
    CreateIndex = 1,

    /// <summary>Re-applying the transaction log or rolling back uncommitted transactions.</summary>
    Recover = 2,

    /// <summary>During the BACKUP command.</summary>
    BackupFile = 3,

    /// <summary>After re-connecting to a database (when auto-reconnect is enabled).</summary>
    Reconnected = 4,

    /// <summary>When a query starts.</summary>
    StatementStart = 5,

    /// <summary>When a query ends.</summary>
    StatementEnd = 6,

    /// <summary>Periodic notification during long-running queries.</summary>
    StatementProgress = 7,
}

/// <summary>
/// Receives the database's lifecycle and exception events. The embedder supplies an
/// implementation through <c>H2DataSource.SetDatabaseEventListener</c> — the reference
/// instead loads a class named in the connection string.
/// </summary>
public interface IH2DatabaseEventListener
{
    /// <summary>
    /// This method is called just after creating the object. This is done when opening the
    /// database if the listener is specified in the database URL, but may be later if the
    /// listener is set at runtime.
    /// </summary>
    /// <param name="url">the database URL</param>
    void Init(string url)
    {
    }

    /// <summary>
    /// This method is called after the database has been opened. It is safe to connect to
    /// the database and execute statements at this point.
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
    /// This method is called for long running events, such as recovering, scanning a file
    /// or building an index. More states might be added in future versions, therefore
    /// implementations should silently ignore states that they don't understand.
    /// </summary>
    /// <param name="state">the state</param>
    /// <param name="name">the object name</param>
    /// <param name="x">the current position</param>
    /// <param name="max">the highest possible value or 0 if unknown</param>
    void SetProgress(H2DatabaseEventState state, string name, long x, long max)
    {
    }

    /// <summary>
    /// This method is called before the database is closed normally. It is safe to connect
    /// to the database and execute statements at this point, however the connection must be
    /// closed before the method returns.
    /// </summary>
    void ClosingDatabase()
    {
    }
}
