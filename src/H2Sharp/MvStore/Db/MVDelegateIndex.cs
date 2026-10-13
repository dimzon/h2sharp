/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.mvstore.db.MVDelegateIndex.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * An index that delegates indexing to another index.
 */
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.MvStore;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Db;

/// <summary>
/// An index that delegates indexing to another index.
/// </summary>
/// <remarks>
/// A secondary key over the primary's own data: it stores nothing itself, so adding,
/// removing, updating and truncating are no-ops, and rebuilding is never required. What it
/// does is price the key: a lookup through it costs a range lookup over the primary's rows.
/// The members that read rows — the row factory, the row lookup, the cursors, the row id
/// answers — are not here. Some wait for the <see cref="Index"/> base to reach them
/// (a public row factory, first-column checks, a session row count), some for
/// <see cref="MVPrimaryIndex"/> to grow them (row lookup, scans). Until then the base's own
/// answers stand, which refuse where the storage is not ported. <see cref="Index.GetColumnIndex(Column)"/>
/// is inherited as is because the base answers from the index columns, and this index holds
/// exactly the wrapped main column.
/// </remarks>
public sealed class MVDelegateIndex : MVIndex<long, SearchRow>
{
    private readonly MVPrimaryIndex mainIndex;

    /// <summary>
    /// Creates a delegate over the primary index's main column.
    /// </summary>
    /// <param name="table">the table</param>
    /// <param name="id">the object id</param>
    /// <param name="name">the index name</param>
    /// <param name="mainIndex">the primary index holding the data</param>
    /// <param name="indexType">the index type</param>
    public MVDelegateIndex(
        MVTable table,
        int id,
        string? name,
        MVPrimaryIndex mainIndex,
        IndexType? indexType)
        : base(table, id, name!, IndexColumn.Wrap([table.GetColumn(mainIndex.GetMainIndexColumn())]),
            1, indexType)
    {
        this.mainIndex = mainIndex;
        if (id < 0)
        {
            throw DbException.GetInternalError(name!);
        }
    }

    /// <inheritdoc/>
    public override void AddRowsToBuffer(List<Row> rows, string bufferName)
    {
        throw DbException.GetInternalError();
    }

    /// <inheritdoc/>
    public override void AddBufferedRows(List<string> bufferNames)
    {
        throw DbException.GetInternalError();
    }

    /// <inheritdoc/>
    public override MVMap<long, VersionedValue<SearchRow>> GetMVMap()
    {
        return mainIndex.GetMVMap();
    }

    /// <inheritdoc/>
    public override void Add(SessionLocal session, Row row)
    {
        // nothing to do
    }

    /// <inheritdoc/>
    public override void Close(SessionLocal session)
    {
        // nothing to do
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
    {
        return 10 * GetCostRangeIndex(masks, mainIndex.GetRowCountApproximation(session),
            filters, filter, sortOrder, isScanIndex: true, allColumnsSet, isSelectCommand);
    }

    /// <inheritdoc/>
    public override bool NeedRebuild()
    {
        return false;
    }

    /// <inheritdoc/>
    public override int GetColumnIndex(Tables.Column column)
    {
        return IsFirstColumn(column) ? 0 : -1;
    }

    /// <inheritdoc/>
    public override bool IsFirstColumn(Tables.Column column)
    {
        return column.GetColumnId() == mainIndex.GetMainIndexColumn() && column.GetTable() == table;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Reads through the primary: the delegate keeps no data of its own, so its walk is the
    /// primary's walk. In the reference this is the find override; here the cursor member
    /// carries it, because the base answers cursors rather than finds.
    /// </remarks>
    public override Indexes.Cursor? GetCursor(SessionLocal session, SearchRow? first, SearchRow? last)
    {
        return mainIndex.GetCursor(session, first, last);
    }

    /// <inheritdoc/>
    public override void Remove(SessionLocal session, Row row)
    {
        // nothing to do
    }

    /// <inheritdoc/>
    public override void Update(SessionLocal session, SearchRow oldRow, SearchRow newRow)
    {
        // nothing to do
    }

    /// <inheritdoc/>
    public override void Remove(SessionLocal session)
    {
        mainIndex.SetMainIndexColumn(SearchRow.RowidIndex);
    }

    /// <inheritdoc/>
    public override void Truncate(SessionLocal session)
    {
        // nothing to do
    }
}
