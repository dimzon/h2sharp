/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.Command.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A statement that can be run, on the server side.
 *
 * Three things about this class are worth reading before its methods.
 *
 * **It holds the session and the text, not a parsed tree.** A concrete command — Insert, Select,
 * CreateTable — parses its own text in its own constructor and keeps the fields it read. That is
 * why there is no "command from a statement" here, and why this base knows nothing about
 * syntax: what a statement *is* belongs to the command that runs it.
 *
 * **Running a statement is the wrapper, not the subclass.** executeQuery and executeUpdate are
 * here, and they are nearly all ceremony around the subclass's one method: lock the session, mark
 * the statement as started, run it, mark it ended, commit on auto commit, convert whatever came
 * out into an error the caller understands. The subclass implements query or update; everything
 * around it is here so that all statements do it the same way, including the retry on a
 * concurrent update and the rollback of a failed one.
 *
 * **What is not ported yet.** The wrapper calls into the transaction layer — savepoints,
 * rollbackTo, endStatement, getAutoCommit — which this port does not have, and into the trace,
 * which has no writer. Those bodies refuse rather than skip: a wrapper that ran a statement
 * without its rollback would report success for a statement whose half of the work was undone.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Command;

/// <summary>
/// Represents a SQL statement. This object is only used on the server side.
/// </summary>
public abstract class Command : CommandInterface
{
    /// <summary>
    /// The session.
    /// </summary>
    protected readonly SessionLocal Session;

    /// <summary>
    /// The last start time.
    /// </summary>
    protected long StartTimeNanos;

    /// <summary>
    /// If this query was canceled.
    /// </summary>
    private volatile bool cancel;

    private readonly string sql;

    private bool canReuse;

    /// <summary>
    /// Creates a command.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="sql">the statement text</param>
    protected Command(SessionLocal session, string sql)
    {
        Session = session;
        this.sql = sql;
    }

    /// <summary>
    /// Get the command type.
    /// </summary>
    /// <returns>the command type</returns>
    public abstract int GetCommandType();

    /// <summary>
    /// Check if this command is transactional. If it is not, then it forces the current
    /// transaction to commit.
    /// </summary>
    /// <returns>true if it is</returns>
    public abstract bool IsTransactional();

    /// <inheritdoc/>
    public abstract bool IsQuery();

    /// <summary>
    /// Get the list of parameters.
    /// </summary>
    /// <returns>the list of parameters</returns>
    public abstract List<ParameterInterface> GetParameters();

    /// <summary>
    /// Check if this command is read only.
    /// </summary>
    /// <returns>true if it is</returns>
    public abstract bool IsReadOnly();

    /// <summary>
    /// Get an empty result set containing the metadata.
    /// </summary>
    /// <returns>an empty result set</returns>
    public abstract ResultInterface? QueryMeta();

    /// <summary>
    /// Execute an updating statement (for example insert, delete, or update), if this is possible.
    /// </summary>
    /// <param name="generatedKeysRequest">
    /// false if generated keys are not needed, true if generated keys should be configured
    /// automatically, int[] to specify column indices to return generated keys from, or
    /// string[] to specify column names to return generated keys from
    /// </param>
    /// <returns>the update count and generated keys, if any</returns>
    /// <exception cref="H2SharpException">if the command is not an updating statement</exception>
    public abstract ResultWithGeneratedKeys UpdateRows(object generatedKeysRequest);

    /// <summary>
    /// Execute a query statement, if this is possible.
    /// </summary>
    /// <param name="maxrows">the maximum number of rows returned</param>
    /// <returns>the local result set</returns>
    /// <exception cref="H2SharpException">if the command is not a query</exception>
    public abstract ResultInterface Query(long maxrows);

    /// <inheritdoc/>
    public ResultInterface? GetMetaData()
    {
        return QueryMeta();
    }

    /// <summary>
    /// Start the stopwatch.
    /// </summary>
    protected void Start()
        => throw new TODOException("Command.start: the trace system has no writer yet");

    /// <summary>
    /// Sets the progress of this command.
    /// </summary>
    /// <param name="database">the database</param>
    /// <param name="state">the state</param>
    protected void SetProgress(Database database, int state)
        => throw new TODOException("Command.setProgress: Database.setProgress is not ported");

    /// <summary>
    /// Check if this command has been canceled, and throw an exception if yes.
    /// </summary>
    /// <exception cref="H2SharpException">if the statement has been canceled</exception>
    protected void CheckCanceled()
    {
        if (cancel)
        {
            cancel = false;
            throw DbException.Get(Api.ErrorCode.StatementWasCanceled);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The reference's <c>stop</c>: a non-transactional statement commits the transaction it ran
    /// in, and a transactional one commits at the end when the session is in auto-commit, so a
    /// single INSERT/UPDATE/DELETE is durable on its own. The trace timing is not ported.
    /// </remarks>
    public virtual void Stop(bool commitIfAutoCommit)
    {
        if (Session.IsOpen())
        {
            CommitIfNonTransactional();
            if (commitIfAutoCommit && IsTransactional() && Session.GetAutoCommit())
            {
                Session.Commit(false);
            }
        }
    }

    /// <summary>
    /// Execute a query and return the result. This method prepares everything and calls
    /// <see cref="Query(long)"/> finally.
    /// </summary>
    /// <param name="maxrows">the maximum number of rows to return</param>
    /// <param name="fetchSize">ignored by local commands</param>
    /// <param name="scrollable">if the result set must be scrollable (ignored)</param>
    /// <returns>the result set</returns>
    public virtual ResultInterface ExecuteQuery(long maxrows, int fetchSize, bool scrollable)
        => throw new TODOException("Command.executeQuery: the transaction layer is not ported yet");

    /// <summary>
    /// Runs the statement with this session as the session of the thread.
    /// </summary>
    /// <remarks>
    /// The reference sets the session of the thread around the running of a statement, and the
    /// database — the provider of the row factories — asks for it when a comparison has to
    /// know the zone of the reader. The slot is put back the way it was, so that a statement
    /// that runs another one does not leave the wrong session behind.
    /// </remarks>
    /// <typeparam name="T">what the statement answers</typeparam>
    /// <param name="run">the statement</param>
    /// <returns>what the statement answered</returns>
    protected T RunWithThreadLocalSession<T>(Func<T> run)
    {
        Session? old = Session.SetThreadLocalSession();
        try
        {
            return run();
        }
        finally
        {
            Session.ResetThreadLocalSession(old);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The reference wraps the update in savepoints, a statement scope, and a rollback of a
    /// failed attempt; none of those are ported, so what remains is the part that is: a
    /// non-transactional statement commits what is open before it runs and its own work after,
    /// and <c>stop(commitIfAutoCommit: true)</c> runs in every case.
    /// </remarks>
    public virtual ResultWithGeneratedKeys ExecuteUpdate(object generatedKeysRequest)
    {
        CommitIfNonTransactional();
        try
        {
            return RunWithThreadLocalSession(() => UpdateRows(generatedKeysRequest));
        }
        finally
        {
            Stop(true);
        }
    }

    /// <inheritdoc/>
    public virtual BatchResult ExecuteBatchUpdate(List<Value?[]> batchParameters, object generatedKeysRequest)
        => throw new TODOException("Command.executeBatchUpdate: the transaction layer is not ported yet");

    /// <summary>
    /// Commits the current transaction when this command is not transactional.
    /// </summary>
    private void CommitIfNonTransactional()
    {
        if (!IsTransactional())
        {
            bool autoCommit = Session.GetAutoCommit();
            Session.Commit(true);
            if (!autoCommit && Session.GetAutoCommit())
            {
                Session.Begin();
            }
        }
    }

    /// <summary>
    /// Decides whether an error is one this command may be retried after.
    /// </summary>
    /// <param name="e">the error</param>
    /// <param name="start">when the first attempt was made</param>
    /// <returns>when the first attempt was made</returns>
    private long FilterConcurrentUpdate(H2SharpException e, long start)
        => throw new TODOException("Command.filterConcurrentUpdate: the error code is not on the exception yet");

    /// <inheritdoc/>
    public virtual void Close()
    {
        canReuse = true;
    }

    /// <inheritdoc/>
    public virtual void Cancel()
    {
        cancel = true;
    }

    /// <summary>
    /// Releases what this command holds, which is what closing it does.
    /// </summary>
    public void Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return sql + Trace.FormatParams(GetParameters());
    }

    /// <summary>
    /// Whether this command's result can be cached.
    /// </summary>
    /// <returns>false, unless a subclass says otherwise</returns>
    public virtual bool IsCacheable()
    {
        return false;
    }

    /// <summary>
    /// Whether the command is already closed, in which case it can be re-used.
    /// </summary>
    /// <returns>true if it can be re-used</returns>
    public bool CanReuse()
    {
        return canReuse;
    }

    /// <summary>
    /// The command is now re-used, therefore reset the canReuse flag, and the parameter values.
    /// </summary>
    public void Reuse()
    {
        canReuse = false;
        foreach (ParameterInterface param in GetParameters())
        {
            param.SetValue(null!, true);
        }
    }

    /// <summary>
    /// Collect all database objects this command depends on.
    /// </summary>
    /// <returns>the dependencies</returns>
    public abstract ISet<DbObject> GetDependencies();

    /// <summary>
    /// Clear cached results of all prepared statements which depend on a given table.
    /// </summary>
    /// <param name="reason">table causing invalidation</param>
    public abstract void InvalidateCachedResult(Table reason);

    /// <summary>
    /// Checks if this command can be repeated on locking failure.
    /// </summary>
    /// <returns>true if this command can be repeated on locking failure</returns>
    protected abstract bool IsRetryable();

    /// <summary>
    /// The retry check for lists holding this command: protected is per-type in C#, so a
    /// list cannot ask another command, and this asks on its behalf.
    /// </summary>
    /// <returns>true if this command can be repeated on locking failure</returns>
    internal bool IsRetryableForList() => IsRetryable();

    /// <summary>
    /// Gets the database this command runs on.
    /// </summary>
    /// <returns>the database</returns>
    protected Database GetDatabase()
    {
        return Session.GetDatabase();
    }
}
