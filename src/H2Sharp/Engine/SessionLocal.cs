/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.engine.SessionLocal.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A session, to the extent the connection path needs it yet: it knows its database, it counts
 * as a user of that database, and closing it gives the database back. Everything that answers a
 * question about SQL — the current timestamp, the identity, preparing a command — is stubbed and
 * refuses, because a session that answered those without the engine behind it would produce
 * rows that look right and are stamped wrong.
 */
using System;
using H2Sharp.Api;

namespace H2Sharp.Engine;

/// <summary>
/// A session in a local (in-process) database.
/// </summary>
public sealed class SessionLocal
{
    private readonly Database database;

    private readonly string userName;

    private bool closed;

    /// <summary>
    /// Opens a session on the given database.
    /// </summary>
    /// <param name="database">the database</param>
    /// <param name="userName">the user name</param>
    public SessionLocal(Database database, string userName)
    {
        this.database = database;
        this.userName = userName;
        database.AddSession(this);
    }

    /// <summary>
    /// Gets the database this session belongs to.
    /// </summary>
    /// <returns>the database</returns>
    public Database GetDatabase()
    {
        return database;
    }

    /// <summary>
    /// Gets the user name.
    /// </summary>
    /// <returns>the user name</returns>
    public string GetUserName()
    {
        return userName;
    }

    /// <summary>
    /// Whether this session is closed.
    /// </summary>
    /// <returns>true if it is</returns>
    public bool IsClosed()
    {
        return closed;
    }

    /// <summary>
    /// Closes the session, and the database with it when it was the last session.
    /// </summary>
    public void Close()
    {
        if (closed)
        {
            return;
        }

        closed = true;
        _ = database.RemoveSession(this);
    }

    /// <summary>
    /// Commits the current transaction.
    /// </summary>
    /// <remarks>
    /// A no-op, and deliberately so: with nothing prepared there is nothing to commit, and a
    /// commit that reported rows would be reporting an invention. It becomes the real thing
    /// with the transaction layer.
    /// </remarks>
    public void Commit()
    {
    }

    /// <summary>
    /// Rolls the current transaction back.
    /// </summary>
    public void Rollback()
    {
    }

    /// <summary>
    /// Gets the column resolver for the given table alias.
    /// </summary>
    /// <param name="alias">the table alias</param>
    /// <returns>the resolver, or null when this query has no table by that alias</returns>
    public H2Sharp.Tables.ColumnResolver? GetResolver(string alias)
        => throw new TODOException("SessionLocal.getResolver: the query layer is not ported");

    /// <summary>
    /// Compares two values, answering the SQL rule for NULL: a comparison with NULL is
    /// unknown unless the mode says otherwise, and unknown sorts before everything.
    /// </summary>
    /// <param name="left">the left value</param>
    /// <param name="right">the right value</param>
    /// <param name="ignoreCase">whether case is ignored</param>
    /// <returns>-1, 0 or 1</returns>
    public int CompareWithNull(Values.Value left, Values.Value right, bool ignoreCase)
        => throw new TODOException("SessionLocal.compareWithNull");

    /// <summary>
    /// Whether two values are equal, where NULL equals NULL.
    /// </summary>
    /// <param name="left">the left value</param>
    /// <param name="right">the right value</param>
    /// <returns>true if they are equal</returns>
    public bool AreEqual(Values.Value left, Values.Value right)
        => throw new TODOException("SessionLocal.areEqual");

    /// <summary>
    /// Gets the database mode, which decides how identifiers and aliases are spelled.
    /// </summary>
    public Mode GetMode()
        => throw new TODOException("SessionLocal.getMode: Mode is not ported");

    /// <summary>
    /// Prepares a command for execution.
    /// </summary>
    /// <param name="sql">the SQL</param>
    /// <returns>the prepared command</returns>
    public object PrepareCommand(string sql)
        => throw new TODOException("SessionLocal.prepareCommand: the parser is not ported");

    /// <summary>
    /// Gets the current timestamp of this session.
    /// </summary>
    public Values.ValueTimestamp GetCurrentTimestamp()
        => throw new TODOException("SessionLocal.getCurrentTimestamp");

    /// <summary>
    /// Whether this session is in autocommit mode.
    /// </summary>
    public bool GetAutoCommit()
        => throw new TODOException("SessionLocal.getAutoCommit");

    /// <summary>
    /// Checks whether this session may read the given table.
    /// </summary>
    /// <param name="table">the table</param>
    public void CheckReadRight(H2Sharp.Tables.Table? table)
        => throw new TODOException("SessionLocal.checkReadRight");

    /// <summary>
    /// Checks whether this session may modify the given table.
    /// </summary>
    /// <param name="table">the table</param>
    public void CheckRight(H2Sharp.Tables.Table? table)
        => throw new TODOException("SessionLocal.checkRight");
}
