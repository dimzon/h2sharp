/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.result.LazyResult.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Lazy execution support for queries.
 */
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Result;

/// <summary>
/// Lazy execution support for queries.
/// </summary>
public abstract class LazyResult : FetchedResult
{
    private readonly SessionLocal session;

    /// <summary>
    /// The expressions that produce the columns. Protected rather than private because the
    /// query's own row sources read the list, which nested classes do freely in Java.
    /// </summary>
    protected readonly Expression[] expressions;

    private bool closed;

    private long limit;

    /// <summary>
    /// Creates a lazy result over the given expressions.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="expressions">the expressions</param>
    protected LazyResult(SessionLocal session, Expression[] expressions)
    {
        this.session = session;
        this.expressions = expressions;
    }

    /// <summary>
    /// Limits how many rows this result hands out.
    /// </summary>
    /// <param name="limit">the limit</param>
    public void SetLimit(long limit)
    {
        this.limit = limit;
    }

    /// <inheritdoc/>
    public override bool IsLazy() => true;

    /// <inheritdoc/>
    public override void Reset()
    {
        if (closed)
        {
            throw DbException.GetInternalError();
        }

        rowId = -1L;
        afterLast = false;
        currentRow = null;
        nextRow = null;
    }

    /// <summary>
    /// Go to the next row and skip it.
    /// </summary>
    /// <returns>true when a row exists</returns>
    public bool Skip()
    {
        if (closed || afterLast)
        {
            return false;
        }

        currentRow = null;
        if (nextRow is not null)
        {
            nextRow = null;
            return true;
        }

        if (SkipNextRow())
        {
            return true;
        }

        afterLast = true;
        return false;
    }

    /// <inheritdoc/>
    public override bool HasNext()
    {
        if (closed || afterLast)
        {
            return false;
        }

        if (nextRow is null && (limit <= 0 || rowId + 1 < limit))
        {
            nextRow = FetchNextRow();
        }

        return nextRow is not null;
    }

    /// <summary>
    /// Fetch next row or null if none available.
    /// </summary>
    /// <returns>next row or null</returns>
    protected abstract Value?[]? FetchNextRow();

    /// <summary>
    /// Skip next row.
    /// </summary>
    /// <returns>true when a row was available</returns>
    protected virtual bool SkipNextRow() => FetchNextRow() is not null;

    /// <inheritdoc/>
    public override long GetRowCount()
    {
        throw DbException.GetUnsupportedException("Row count is unknown for lazy result.");
    }

    /// <inheritdoc/>
    public override bool IsClosed() => closed;

    /// <inheritdoc/>
    public override void Close()
    {
        closed = true;
    }

    /// <inheritdoc/>
    public override string GetAlias(int i) => expressions[i].GetAlias(session, i);

    /// <inheritdoc/>
    public override string? GetSchemaName(int i) => expressions[i].GetSchemaName();

    /// <inheritdoc/>
    public override string? GetTableName(int i) => expressions[i].GetTableName();

    /// <inheritdoc/>
    public override string GetColumnName(int i) => expressions[i].GetColumnName(session, i);

    /// <inheritdoc/>
    public override TypeInfo GetColumnType(int i) => expressions[i].GetTypeInfo();

    /// <inheritdoc/>
    public override bool IsIdentity(int i) => expressions[i].IsIdentity();

    /// <inheritdoc/>
    public override int GetNullable(int i) => expressions[i].GetNullable();

    /// <inheritdoc/>
    public override void SetFetchSize(int fetchSize)
    {
        // ignore
    }

    /// <inheritdoc/>
    public override int GetFetchSize()
    {
        // We always fetch rows one by one.
        return 1;
    }
}
