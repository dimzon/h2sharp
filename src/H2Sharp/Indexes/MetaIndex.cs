/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.index.MetaIndex and org.h2.index.MetaCursor.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The scan index of a metadata pseudo table holds no data: it asks the table for its rows when
 * the query runs, and a cursor walks that list forward only, as in the reference.
 */
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Indexes;

/// <summary>
/// The index implementation for metadata tables.
/// </summary>
public class MetaIndex : Index
{
    private readonly MetaTable meta;

    private readonly bool scan;

    /// <summary>
    /// Creates a metadata index.
    /// </summary>
    /// <param name="meta">the metadata table</param>
    /// <param name="columns">the index columns</param>
    /// <param name="scan">whether this is the scan index</param>
    public MetaIndex(MetaTable meta, IndexColumn[] columns, bool scan)
        : base(meta, 0, null!, columns, 0, IndexType.CreateNonUnique(true))
    {
        this.meta = meta;
        this.scan = scan;
    }

    /// <inheritdoc/>
    public override void Close(SessionLocal session)
    {
    }

    /// <inheritdoc/>
    public override void Add(SessionLocal session, Row row)
        => throw DbException.GetUnsupportedException("META");

    /// <inheritdoc/>
    public override void Remove(SessionLocal session, Row row)
        => throw DbException.GetUnsupportedException("META");

    /// <inheritdoc/>
    public override Cursor? GetCursor(
        SessionLocal session, SearchRow? first, SearchRow? last, bool reverse)
        => new MetaCursor(meta.GenerateRows(session, first, last));

    /// <inheritdoc/>
    public override double GetCost(
        SessionLocal session,
        int[]? masks,
        TableFilter[]? filters,
        int filter,
        SortOrder? sortOrder,
        Command.Query.AllColumnsForPlan? allColumnsSet,
        bool isSelectCommand)
    {
        if (scan)
        {
            return 10 * MetaTable.RowCountApproximation;
        }

        return GetCostRangeIndex(masks, MetaTable.RowCountApproximation, filters, filter,
            sortOrder, false, allColumnsSet, isSelectCommand);
    }

    /// <inheritdoc/>
    public override void Truncate(SessionLocal session)
        => throw DbException.GetUnsupportedException("META");

    /// <inheritdoc/>
    public override void Remove(SessionLocal session)
        => throw DbException.GetUnsupportedException("META");

    /// <inheritdoc/>
    public override int GetColumnIndex(Column col)
        => scan ? -1 : base.GetColumnIndex(col);

    /// <inheritdoc/>
    public override bool IsFirstColumn(Column column)
        => !scan && base.IsFirstColumn(column);

    /// <inheritdoc/>
    public override void CheckRename() => throw DbException.GetUnsupportedException("META");

    /// <inheritdoc/>
    public override bool NeedRebuild() => false;

    /// <inheritdoc/>
    public override string? GetCreateSql() => null;

    /// <inheritdoc/>
    public override long GetRowCount(SessionLocal session) => MetaTable.RowCountApproximation;

    /// <inheritdoc/>
    public override long GetRowCountApproximation(SessionLocal session) => MetaTable.RowCountApproximation;

    /// <inheritdoc/>
    public override long GetDiskSpaceUsed(bool approximate) => meta.GetDiskSpaceUsed(false, approximate);

    /// <inheritdoc/>
    public override string GetPlanSql() => "meta";
}

/// <summary>
/// A forward-only cursor over the rows a metadata table generated.
/// </summary>
public sealed class MetaCursor : Cursor
{
    private Row? current;

    private readonly List<Row> rows;

    private int index;

    /// <summary>
    /// Creates the cursor.
    /// </summary>
    /// <param name="rows">the generated rows</param>
    internal MetaCursor(List<Row> rows)
    {
        this.rows = rows;
    }

    /// <inheritdoc/>
    public Row? Get() => current;

    /// <inheritdoc/>
    public SearchRow? GetSearchRow() => current;

    /// <inheritdoc/>
    public bool Next()
    {
        current = index >= rows.Count ? null : rows[index++];
        return current is not null;
    }

    /// <inheritdoc/>
    public bool Previous() => throw DbException.GetInternalError(ToString() ?? "MetaCursor");
}
