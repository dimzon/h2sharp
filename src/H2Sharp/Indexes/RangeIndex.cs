/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.index.RangeIndex.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The index of the SYSTEM_RANGE table: it can only scan through all the rows, and a search
 * is not supported.
 */
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Indexes;

/// <summary>
/// The index of the SYSTEM_RANGE table.
/// </summary>
public sealed class RangeIndex : Index
{
    private readonly RangeTable rangeTable;

    /// <summary>
    /// Creates the index of the range table.
    /// </summary>
    /// <param name="table">the table</param>
    /// <param name="columns">the index columns</param>
    public RangeIndex(RangeTable table, IndexColumn[] columns)
        : base(table, 0, "RANGE_INDEX", columns, 0, null)
    {
        rangeTable = table;
    }

    /// <inheritdoc/>
    public override Cursor? GetCursor(SessionLocal session, SearchRow? first, SearchRow? last, bool reverse)
    {
        long min = rangeTable.GetMin(session);
        long max = rangeTable.GetMax(session);
        long step = rangeTable.GetStep(session);
        if (step == 0L)
        {
            throw DbException.Get(ErrorCode.StepSizeMustNotBeZero);
        }

        return new RangeCursor(min, max, step);
    }

    /// <inheritdoc/>
    public override double GetCost(
        SessionLocal session,
        int[]? masks,
        TableFilter[]? filters,
        int filter,
        SortOrder? sortOrder,
        Command.Query.AllColumnsForPlan? allColumnsSet,
        bool isSelectCommand)
        => 1d;

    /// <inheritdoc/>
    public override string? GetCreateSql() => null;

    /// <inheritdoc/>
    public override bool CanGetFirstOrLast() => true;

    /// <inheritdoc/>
    public override Cursor FindFirstOrLast(SessionLocal session, bool first)
    {
        long min = rangeTable.GetMin(session);
        long max = rangeTable.GetMax(session);
        long step = rangeTable.GetStep(session);
        if (step == 0L)
        {
            throw DbException.Get(ErrorCode.StepSizeMustNotBeZero);
        }

        return step > 0
            ? min <= max
                ? new SingleRowCursor(Row.Get([ValueBigint.Get(first ? min : max)], 1))
                : SingleRowCursor.Empty
            : min >= max
                ? new SingleRowCursor(Row.Get([ValueBigint.Get(first ? min : max)], 1))
                : SingleRowCursor.Empty;
    }

    /// <inheritdoc/>
    public override long GetRowCount(SessionLocal session) => rangeTable.GetRowCount(session);

    /// <inheritdoc/>
    public override string GetPlanSql() => "range index";

    /// <inheritdoc/>
    public override void Close(SessionLocal session)
    {
    }

    /// <inheritdoc/>
    public override bool NeedRebuild() => false;
}
