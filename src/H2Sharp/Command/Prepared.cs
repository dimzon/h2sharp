/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.Prepared.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A statement that has been read but not yet made ready to run.
 *
 * This is the other half of the command layer, and the half that is easy to mistake for the
 * wrong one: the reference has two hierarchies, not one. A *prepared statement* is what the
 * parser produced — an Insert, a Select — and a *command* is what a caller holds and runs.
 * <see cref="CommandContainer"/> is the join: it holds a prepared statement, sets itself on it,
 * and answers the command's questions by asking it. A port that makes the statements themselves
 * commands has one hierarchy where the reference has two, and the container's whole job — "ask
 * the statement what it is" — becomes impossible.
 *
 * Only three members are abstract, as in the reference. The rest have answers that are the same
 * for every statement and do not belong in a list every statement would have to fill with the
 * same lines: a statement is not transactional unless it says so, it is read only unless it
 * says otherwise, and it may be cached unless it says otherwise.
 *
 * The modification id is the reason a prepared statement remembers when it was made. A command
 * cached here is only safe to hand back while the database has not changed since; that is
 * checked against this id, which is why it is taken at construction and not read on demand.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Expressions;
using H2Sharp.Result;
using H2Sharp.Tables;

namespace H2Sharp.Command;

/// <summary>
/// A statement that has been parsed.
/// </summary>
public abstract class Prepared
{
    private SessionLocal session;

    private long modificationMetaId;

    private List<Parameter>? parameters;

    private string sql = string.Empty;

    private List<Token>? sqlTokens;

    private Command? command;

    private bool withParamValues;

    private bool prepareAlways;

    // The reference leaves this at Java's int zero; zero means "no id yet", negative means
    // "an id was read and the original waits behind the bit flip for a re-run".
    private int persistedObjectId;

    private long currentRowNumber;

    /// <summary>
    /// Creates a prepared statement.
    /// </summary>
    /// <param name="session">the session</param>
    protected Prepared(SessionLocal session)
    {
        this.session = session;
        modificationMetaId = session.GetDatabase().GetModificationMetaId();
    }

    /// <summary>
    /// Check if this command is transactional.
    /// </summary>
    /// <returns>true if it is</returns>
    public abstract bool IsTransactional();

    /// <summary>
    /// Get an empty result set containing the metadata.
    /// </summary>
    /// <returns>an empty result set</returns>
    public abstract ResultInterface? QueryMeta();

    /// <summary>
    /// Get the command type.
    /// </summary>
    /// <returns>the command type</returns>
    public abstract int GetCommandType();

    /// <summary>
    /// Check if this command is read only.
    /// </summary>
    /// <returns>true if it is</returns>
    public virtual bool IsReadOnly() => true;

    /// <summary>
    /// Check if this command needs to be recompiled.
    /// </summary>
    /// <returns>true if it needs to be recompiled</returns>
    public virtual bool NeedRecompile()
    {
        // H2 Prepared.needRecompile; DbSettings.recompileAlways is not ported,
        // and its reference default is false.
        return prepareAlways || modificationMetaId < session.GetDatabase().GetModificationMetaId();
    }

    /// <summary>
    /// Get the list of parameters.
    /// </summary>
    /// <returns>the list of parameters</returns>
    public List<Parameter>? GetParameters() => parameters;

    /// <summary>
    /// Set the list of parameters.
    /// </summary>
    /// <param name="parameters">the list of parameters</param>
    public void SetParameterList(List<Parameter>? parameters) => this.parameters = parameters;

    /// <summary>
    /// Check if the command has a parameter value set.
    /// </summary>
    /// <returns>true if it has</returns>
    public bool IsWithParamValues() => withParamValues;

    /// <summary>
    /// Set the parameter values.
    /// </summary>
    /// <param name="withParamValues">whether the parameter values are set</param>
    public void SetWithParamValues(bool withParamValues) => this.withParamValues = withParamValues;

    /// <summary>
    /// Set the command.
    /// </summary>
    /// <param name="command">the command</param>
    public void SetCommand(Command command) => this.command = command;

    /// <summary>
    /// Get the command.
    /// </summary>
    /// <returns>the command</returns>
    public Command? GetCommand() => command;

    /// <summary>
    /// Check if this command is a query.
    /// </summary>
    /// <returns>true if it is</returns>
    public virtual bool IsQuery() => false;

    /// <summary>
    /// Prepares this command.
    /// </summary>
    /// <remarks>
    /// Nothing to do, as in the reference. A statement that has to be prepared does it by
    /// overriding this; a statement that does not is already ready, and asking every one of them
    /// to answer "nothing" would be a list of the same line written out.
    /// </remarks>
    public virtual void Prepare()
    {
        // nothing to do
    }

    /// <summary>
    /// Execute an updating statement.
    /// </summary>
    /// <returns>the number of rows updated</returns>
    public virtual long Update()
        => throw new TODOException("Prepared.update: the statement is not run yet");

    /// <summary>
    /// Execute a query statement.
    /// </summary>
    /// <param name="maxrows">the maximum number of rows returned</param>
    /// <returns>the result set</returns>
    public virtual ResultInterface Run(long maxrows)
        => throw new TODOException("Prepared.query: the statement is not run yet");

    /// <summary>
    /// Set the SQL of this command.
    /// </summary>
    /// <param name="sql">the SQL</param>
    /// <param name="sqlTokens">the tokens</param>
    public void SetSql(string sql, List<Token>? sqlTokens)
    {
        this.sql = sql;
        this.sqlTokens = sqlTokens;
    }

    /// <summary>
    /// Get the SQL of this command.
    /// </summary>
    /// <returns>the SQL</returns>
    public string GetSql() => sql;

    /// <summary>
    /// Get the tokens of this command.
    /// </summary>
    /// <returns>the tokens</returns>
    public List<Token>? GetSqlTokens() => sqlTokens;

    /// <summary>
    /// Get the modification meta id of this command.
    /// </summary>
    /// <returns>the modification meta id</returns>
    public long GetModificationMetaId() => modificationMetaId;

    /// <summary>Sets the metadata modification id of this statement.</summary>
    internal void SetModificationMetaId(long id) => modificationMetaId = id;

    /// <summary>
    /// Get the object id of this command.
    /// </summary>
    /// <returns>the object id</returns>
    public int GetPersistedObjectId() => persistedObjectId;

    /// <summary>
    /// Set the object id of this command.
    /// </summary>
    /// <param name="id">the object id</param>
    public void SetPersistedObjectId(int id) => persistedObjectId = id;

    /// <summary>
    /// Get the current object id, or get a new id from the database. The object
    /// id is used when creating new database object (CREATE statement). This
    /// method may be called only once.
    /// </summary>
    /// <returns>the object id</returns>
    protected int GetObjectId()
    {
        int id = persistedObjectId;
        if (id == 0)
        {
            id = GetSession().GetDatabase().AllocateObjectId();
        }
        else if (id < 0)
        {
            throw DbException.GetInternalError("Prepared.GetObjectId() was called before");
        }

        persistedObjectId = ~persistedObjectId; // while negative, it can be restored later
        return id;
    }

    /// <summary>
    /// Get the plan SQL of this command.
    /// </summary>
    /// <param name="sqlFlags">the SQL flags</param>
    /// <returns>the plan SQL</returns>
    public string GetPlanSql(int sqlFlags)
    {
        StringBuilder builder = new();
        return GetPlanSql(builder, sqlFlags).ToString();
    }

    /// <summary>
    /// Get the plan SQL of this command.
    /// </summary>
    /// <param name="builder">the builder</param>
    /// <param name="sqlFlags">the SQL flags</param>
    /// <returns>the builder</returns>
    public virtual StringBuilder GetPlanSql(StringBuilder builder, int sqlFlags)
        => throw new TODOException("Prepared.getPlanSQL: the plan is not ported");

    /// <summary>
    /// Check if this command was canceled.
    /// </summary>
    public virtual void CheckCanceled()
    {
    }

    /// <summary>
    /// Set the current session.
    /// </summary>
    /// <param name="currentSession">the session</param>
    public virtual void SetSession(SessionLocal currentSession)
        => session = currentSession;

    /// <summary>
    /// Set the prepare always flag.
    /// </summary>
    /// <param name="prepareAlways">whether to prepare always</param>
    public void SetPrepareAlways(bool prepareAlways) => this.prepareAlways = prepareAlways;

    /// <summary>
    /// Whether this command has to be prepared again every time it runs.
    /// </summary>
    /// <returns>true if it does</returns>
    public bool IsPrepareAlways() => prepareAlways;

    /// <summary>
    /// Set the current row number.
    /// </summary>
    /// <param name="rowNumber">the row number</param>
    public void SetCurrentRowNumber(long rowNumber) => currentRowNumber = rowNumber;

    /// <summary>
    /// Get the current row number.
    /// </summary>
    /// <returns>the row number</returns>
    public long GetCurrentRowNumber() => currentRowNumber;

    /// <summary>
    /// Collect all database objects, this command depends on.
    /// </summary>
    /// <param name="dependencies">the set to add to</param>
    public virtual void CollectDependencies(HashSet<DbObject> dependencies)
    {
        // Most statements depend on nothing that is not already in the session.
    }

    /// <summary>
    /// Check if this command is cacheable.
    /// </summary>
    /// <returns>true if it is</returns>
    public virtual bool IsCacheable() => false;

    /// <summary>
    /// Checks if this command can be repeated on locking failure.
    /// </summary>
    /// <returns>true if this command can be repeated on locking failure</returns>
    public virtual bool IsRetryable() => false;

    /// <summary>
    /// Clear cached results of all prepared statements, which depends on a given table.
    /// </summary>
    /// <param name="reason">table causing invalidation</param>
    public virtual void InvalidateCachedResult(Table reason)
    {
        // Only a statement that caches a result has anything to drop.
    }

    /// <summary>
    /// Get the session of this command.
    /// </summary>
    /// <returns>the session</returns>
    public SessionLocal GetSession() => session;

    /// <inheritdoc/>
    /// <remarks>
    /// The SQL and the parameters, as the reference writes it — except that a statement whose
    /// parameters were never collected would fail here, and a ToString that throws is worse than
    /// useless: it is what a test runner prints when a test fails, so it turns a readable failure
    /// into a stack trace from inside the formatter. The text alone is worth having, and it is
    /// never wrong — it is the SQL the caller wrote, not an answer about it.
    /// </remarks>
    public override string ToString()
    {
        List<Parameter>? list = parameters;
        return list is null ? sql : sql + Trace.FormatParams(list);
    }

    /// <summary>
    /// Get the SQL of the given expressions, as a plan prints them.
    /// </summary>
    /// <param name="list">the expressions</param>
    /// <returns>the SQL</returns>
    public static string GetSimpleSql(Expression[] list)
    {
        ArgumentNullException.ThrowIfNull(list);
        StringBuilder builder = new();
        for (int i = 0; i < list.Length; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            _ = list[i].GetSql(builder, 0);
        }

        return builder.ToString();
    }
}
