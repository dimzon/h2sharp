/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.index.DualIndex.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tables;

namespace H2Sharp.Indexes;

/// <summary>
/// An index for the DUAL table.
/// </summary>
public sealed class DualIndex : Index
{
    /// <summary>
    /// Creates the index of the DUAL table.
    /// </summary>
    /// <param name="table">the DUAL table</param>
    public DualIndex(DualTable table)
        : base(table, 0, "DUAL_INDEX", [], 0, null)
    {
    }

    /// <inheritdoc/>
    public override Cursor? GetCursor(SessionLocal session, SearchRow? first, SearchRow? last, bool reverse)
        => new DualCursor();

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
    public override long GetRowCount(SessionLocal session) => 1L;

    /// <inheritdoc/>
    public override long GetRowCountApproximation(SessionLocal session) => 1L;

    /// <inheritdoc/>
    public override bool CanGetFirstOrLast() => true;

    /// <inheritdoc/>
    public override Cursor FindFirstOrLast(SessionLocal session, bool first)
        => new SingleRowCursor(Row.Get(Values.Value.EmptyValues, 1));

    /// <inheritdoc/>
    public override string GetPlanSql() => "dual index";

    /// <inheritdoc/>
    public override void Close(SessionLocal session)
    {
    }

    /// <inheritdoc/>
    public override bool NeedRebuild() => false;
}
