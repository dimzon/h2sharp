/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.index.SingleRowCursor.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A cursor with at most one row.
 */
using H2Sharp.Message;
using H2Sharp.Result;

namespace H2Sharp.Indexes;

/// <summary>
/// A cursor with at most one row.
/// </summary>
public sealed class SingleRowCursor : Cursor
{
    /// <summary>
    /// An empty cursor.
    /// </summary>
    public static readonly SingleRowCursor Empty = new(null);

    private Row? row;

    private bool end;

    /// <summary>
    /// Creates a new cursor.
    /// </summary>
    /// <param name="row">the single row, or null for an empty cursor</param>
    public SingleRowCursor(Row? row) => this.row = row;

    /// <inheritdoc/>
    public Row? Get() => row;

    /// <inheritdoc/>
    public SearchRow? GetSearchRow() => row;

    /// <inheritdoc/>
    /// <remarks>
    /// Asking for a second row answers "no" and clears the row, so a cursor that has run out holds
    /// nothing. The reference does exactly this and it is worth keeping: a caller that reads the row
    /// after the end gets null rather than a row it already consumed.
    /// </remarks>
    public bool Next()
    {
        if (row is null || end)
        {
            row = null;
            return false;
        }

        end = true;
        return true;
    }

    /// <inheritdoc/>
    public bool Previous() => throw DbException.GetInternalError($"{this}");
}
