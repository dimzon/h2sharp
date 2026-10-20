/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.index.VirtualConstructedTableIndex.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The index of a virtual table that returns a result set: a search in this index performs
 * a scan over all the rows and should be avoided.
 */
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Indexes;

/// <summary>
/// An index for a virtual table that returns a result set.
/// </summary>
public sealed class VirtualConstructedTableIndex : Index
{
    private readonly VirtualConstructedTable virtualTable;

    /// <summary>
    /// Creates the index of a virtual table.
    /// </summary>
    /// <param name="table">the table</param>
    public VirtualConstructedTableIndex(VirtualConstructedTable table)
        : base(table, 0, "VIRTUAL_CONSTRUCTED_TABLE_INDEX", [], 0, null)
    {
        virtualTable = table;
    }

    /// <inheritdoc/>
    public override Cursor? GetCursor(SessionLocal session, SearchRow? first, SearchRow? last, bool reverse)
        => new VirtualConstructedTableCursor(this, first, last, virtualTable.GetResult(session));

    /// <inheritdoc/>
    public override double GetCost(
        SessionLocal session,
        int[]? masks,
        TableFilter[]? filters,
        int filter,
        SortOrder? sortOrder,
        Command.Query.AllColumnsForPlan? allColumnsSet,
        bool isSelectCommand)
        => 10d;

    /// <inheritdoc/>
    public override string? GetCreateSql() => null;

    /// <inheritdoc/>
    public override long GetRowCount(SessionLocal session) => virtualTable.GetResult(session).GetRowCount();

    /// <inheritdoc/>
    public override long GetRowCountApproximation(SessionLocal session) => long.MaxValue;

    /// <inheritdoc/>
    public override bool CanGetFirstOrLast() => false;

    /// <inheritdoc/>
    public override string GetPlanSql() => "table scan";

    /// <inheritdoc/>
    public override void Close(SessionLocal session)
    {
    }

    /// <inheritdoc/>
    public override bool NeedRebuild() => false;
}

/// <summary>
/// A cursor over the result of a virtual table.
/// </summary>
/// <remarks>
/// The rows are filtered: only the rows that are not smaller than the first one and not larger
/// than the last one are returned.
/// </remarks>
internal sealed class VirtualConstructedTableCursor : Cursor
{
    private readonly Index index;

    private readonly SearchRow? first;

    private readonly SearchRow? last;

    private readonly ResultInterface result;

    private Value?[]? values;

    private Row? row;

    /// <summary>
    /// Creates the cursor.
    /// </summary>
    /// <param name="index">the index</param>
    /// <param name="first">the first row, or null</param>
    /// <param name="last">the last row, or null</param>
    /// <param name="result">the result the rows come from</param>
    internal VirtualConstructedTableCursor(Index index, SearchRow? first, SearchRow? last, ResultInterface result)
    {
        this.index = index;
        this.first = first;
        this.last = last;
        this.result = result;
    }

    /// <inheritdoc/>
    public Row? Get() => row;

    /// <inheritdoc/>
    public SearchRow? GetSearchRow() => Get();

    /// <inheritdoc/>
    public bool Next()
    {
        while (result.Next())
        {
            values = result.CurrentRow();
            Row current = Row.Get(values, 1);
            if (first is not null && index.CompareRows(current, first) < 0)
            {
                continue;
            }

            if (last is not null && index.CompareRows(current, last) > 0)
            {
                break;
            }

            row = current;
            return true;
        }

        values = null;
        row = null;
        return false;
    }

    /// <inheritdoc/>
    public bool Previous() => false;
}
