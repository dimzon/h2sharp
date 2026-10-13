/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.result.FetchedResult.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A result whose rows arrive as they are walked.
 */
using H2Sharp.Values;

namespace H2Sharp.Result;

/// <summary>
/// Abstract fetched result.
/// </summary>
public abstract class FetchedResult : ResultInterface
{
    /// <summary>
    /// The id of the current row, starting before the first one.
    /// </summary>
    protected long rowId = -1;

    /// <summary>
    /// The current row, once the walk stands on one.
    /// </summary>
    protected Value?[]? currentRow;

    /// <summary>
    /// The row fetched ahead, waiting for its turn.
    /// </summary>
    protected Value?[]? nextRow;

    /// <summary>
    /// Whether the walk has run past the end.
    /// </summary>
    protected bool afterLast;

    /// <inheritdoc/>
    public Value?[] CurrentRow() => currentRow!;

    /// <inheritdoc/>
    public bool Next()
    {
        if (HasNext())
        {
            rowId++;
            currentRow = nextRow;
            nextRow = null;
            return true;
        }

        if (!afterLast)
        {
            rowId++;
            currentRow = null;
            afterLast = true;
        }

        return false;
    }

    /// <inheritdoc/>
    public bool IsAfterLast() => afterLast;

    /// <inheritdoc/>
    public long GetRowId() => rowId;

    /// <inheritdoc/>
    public bool NeedToClose() => true;

    /// <inheritdoc/>
    public ResultInterface? CreateShallowCopy(Engine.Session targetSession)
    {
        // The operation is not supported on fetched result.
        return null;
    }

    /// <inheritdoc/>
    public abstract void Reset();

    /// <inheritdoc/>
    public abstract int GetVisibleColumnCount();

    /// <inheritdoc/>
    public abstract long GetRowCount();

    /// <inheritdoc/>
    public abstract bool HasNext();

    /// <inheritdoc/>
    public abstract void Close();

    /// <inheritdoc/>
    public abstract void Dispose();

    /// <inheritdoc/>
    public abstract string GetAlias(int i);

    /// <inheritdoc/>
    public abstract string? GetSchemaName(int i);

    /// <inheritdoc/>
    public abstract string? GetTableName(int i);

    /// <inheritdoc/>
    public abstract string GetColumnName(int i);

    /// <inheritdoc/>
    public abstract TypeInfo GetColumnType(int i);

    /// <inheritdoc/>
    public abstract bool IsIdentity(int i);

    /// <inheritdoc/>
    public abstract int GetNullable(int i);

    /// <inheritdoc/>
    public abstract void SetFetchSize(int fetchSize);

    /// <inheritdoc/>
    public abstract int GetFetchSize();

    /// <inheritdoc/>
    public abstract bool IsLazy();

    /// <inheritdoc/>
    public abstract bool IsClosed();
}
