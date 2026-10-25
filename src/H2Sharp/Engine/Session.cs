/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.engine.Session.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A session, as the things that do not care whether it is local or remote see it.
 *
 * The reference has two sessions: a local one, and one that stands in for a remote session on
 * the server side. Everything that accepts a session — a result to copy for, a command to run —
 * takes this one, which is why it exists as an abstraction rather than as SessionLocal
 * everywhere. Its members are declared here and answered by SessionLocal, which is the
 * implementation in this port; anything that needs the transaction machinery behind them is
 * not answered yet and refuses rather than inventing an answer.
 *
 * The lock is real: it is a counter of how deep the session is held, because a statement may
 * run inside a call that already holds it, and a plain Monitor would be wrong to enter twice.
 */
using System;
using System.Collections.Generic;
using System.Threading;
using H2Sharp.Message;
using H2Sharp.Store;
using H2Sharp.Values;

namespace H2Sharp.Engine;

/// <summary>
/// A session, local or remote. The base of the session types; everything that is not specific
/// to an in-process database is here.
/// </summary>
public abstract class Session : CastDataProvider, IDisposable
{
    // Approved D-023: Java ReentrantLock is a private reentrant BCL Monitor.
    private readonly object @lock = new();

    /// <summary>
    /// Acquires the lock of this session.
    /// </summary>
    public void Lock()
    {
        Monitor.Enter(@lock);
    }

    /// <summary>
    /// Releases the lock of this session.
    /// </summary>
    public void Unlock()
    {
        Monitor.Exit(@lock);
    }

    /// <summary>
    /// Whether this session is locked by the current thread.
    /// </summary>
    /// <returns>true if it is</returns>
    public bool IsLockedByCurrentThread()
    {
        return Monitor.IsEntered(@lock);
    }

    /// <summary>
    /// Gets the names of the cluster servers.
    /// </summary>
    /// <returns>the cluster servers</returns>
    public abstract List<string> GetClusterServers();

    /// <summary>
    /// Prepares a command for execution.
    /// </summary>
    /// <param name="sql">the SQL statement</param>
    /// <returns>the prepared command</returns>
    public abstract H2Sharp.Command.CommandInterface PrepareCommand(string sql);

    /// <summary>
    /// Closes the session.
    /// </summary>
    public abstract void Close();

    /// <summary>
    /// Gets the trace object for this session.
    /// </summary>
    /// <returns>the trace object</returns>
    public abstract Trace GetTrace();

    /// <summary>
    /// Checks if this session is closed.
    /// </summary>
    /// <returns>true if it is closed</returns>
    public abstract bool IsClosed();

    /// <summary>
    /// Gets the data handler.
    /// </summary>
    /// <returns>the data handler</returns>
    public abstract DataHandler GetDataHandler();

    /// <summary>
    /// Checks whether the session has a pending transaction.
    /// </summary>
    /// <returns>true if it has</returns>
    public abstract bool HasPendingTransaction();

    /// <summary>
    /// Cancels the current statement.
    /// </summary>
    public abstract void Cancel();

    /// <summary>
    /// Gets the auto commit flag.
    /// </summary>
    /// <returns>the auto commit flag</returns>
    public abstract bool GetAutoCommit();

    /// <summary>
    /// Sets the auto commit flag.
    /// </summary>
    /// <param name="autoCommit">the new value</param>
    public abstract void SetAutoCommit(bool autoCommit);

    /// <summary>
    /// Adds a temporary lob.
    /// </summary>
    /// <param name="v">the value</param>
    /// <returns>the value that was added</returns>
    public abstract ValueLob AddTemporaryLob(ValueLob v);

    /// <summary>
    /// Checks if this is a remote session.
    /// </summary>
    /// <returns>true if it is</returns>
    public abstract bool IsRemote();

    /// <summary>
    /// Sets the current schema name.
    /// </summary>
    /// <param name="schema">the schema name</param>
    public abstract void SetCurrentSchemaName(string schema);

    /// <summary>
    /// Gets the current schema name.
    /// </summary>
    /// <returns>the schema name</returns>
    public abstract string GetCurrentSchemaName();

    /// <summary>
    /// Sets the network connection information.
    /// </summary>
    /// <param name="networkConnectionInfo">the connection information</param>
    public abstract void SetNetworkConnectionInfo(object networkConnectionInfo);

    /// <summary>
    /// Gets the isolation level.
    /// </summary>
    /// <returns>the isolation level</returns>
    public abstract IsolationLevel GetIsolationLevel();

    /// <summary>
    /// Sets the isolation level.
    /// </summary>
    /// <param name="isolationLevel">the isolation level</param>
    public abstract void SetIsolationLevel(IsolationLevel isolationLevel);

    /// <summary>
    /// Gets the static settings.
    /// </summary>
    /// <returns>the settings</returns>
    public abstract object GetStaticSettings();

    /// <summary>
    /// Gets the dynamic settings.
    /// </summary>
    /// <returns>the settings</returns>
    public abstract object GetDynamicSettings();

    /// <summary>
    /// Gets the database meta object.
    /// </summary>
    /// <returns>the meta object</returns>
    public abstract object GetDatabaseMeta();

    /// <summary>
    /// Whether this is an old information schema.
    /// </summary>
    /// <returns>true if it is</returns>
    public abstract bool IsOldInformationSchema();

    /// <summary>
    /// Returns the current timestamp with maximum resolution. The value must be the same within
    /// a transaction or within execution of a command.
    /// </summary>
    /// <returns>the current timestamp for CURRENT_TIMESTAMP(9)</returns>
    public abstract Values.ValueTimestampTimeZone CurrentTimestamp();

    /// <summary>
    /// Returns the current time zone.
    /// </summary>
    /// <returns>the current time zone</returns>
    public abstract Util.TimeZoneProvider CurrentTimeZone();

    /// <summary>
    /// Returns are ENUM values 0-based.
    /// </summary>
    /// <returns>are ENUM values 0-based</returns>
    public abstract bool ZeroBasedEnums();

    /// <summary>
    /// Returns the database mode.
    /// </summary>
    /// <returns>the database mode</returns>
    public abstract Mode GetMode();

    /// <summary>
    /// Releases what this session holds. Closing a session gives its database back, which is
    /// what an in-memory database's lifetime is measured in.
    /// </summary>
    public virtual void Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Sets this session as thread local session, if this session is a local session.
    /// </summary>
    /// <remarks>
    /// A remote session has no thread of its own to be the current session on, so it does not
    /// take the slot. <see cref="SessionLocal"/> overrides this and stores itself, because the
    /// thread local session is what a comparison between two types asks about.
    /// </remarks>
    /// <returns>old thread local session, or <c>null</c></returns>
    public virtual Session? SetThreadLocalSession()
    {
        return null;
    }

    /// <summary>
    /// Resets the thread local session.
    /// </summary>
    /// <param name="oldSession">the old session</param>
    public virtual void ResetThreadLocalSession(Session? oldSession)
    {
    }
}
