/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.mvstore.db.MVSecondaryIndex.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A regular index that stores the data in a MVStore.
 */
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Db;

/// <summary>
/// A regular index that stores the data in a MVStore.
/// </summary>
/// <remarks>
/// Not ported. Unlike the delegate, which reads the primary's data, this index keeps a map
/// of its own: creating it opens the map in the store, fills it from the table's rows, and
/// serves lookups from it. None of that storage half is here yet, so every member refuses
/// with what it needs. The signatures are the reference's, so bodies port without reshaping.
/// </remarks>
public sealed class MVSecondaryIndex : MVIndex<SearchRow, Value>
{
    /// <summary>
    /// Creates a secondary index. Refuses: opening its map needs the store plumbing.
    /// </summary>
    /// <param name="db">the database</param>
    /// <param name="table">the table</param>
    /// <param name="id">the object id</param>
    /// <param name="indexName">the index name</param>
    /// <param name="columns">the columns that are indexed</param>
    /// <param name="uniqueColumnCount">count of unique columns</param>
    /// <param name="indexType">the index type</param>
    public MVSecondaryIndex(
        Database db,
        MVTable table,
        int id,
        string? indexName,
        IndexColumn[] columns,
        int uniqueColumnCount,
        IndexType? indexType)
        : base(table, id, indexName!, columns, uniqueColumnCount, indexType)
    {
        throw new TODOException("MVSecondaryIndex: opening the index map is not ported");
    }

    /// <inheritdoc/>
    public override void AddRowsToBuffer(List<Row> rows, string bufferName)
        => throw new TODOException("MVSecondaryIndex: opening the index map is not ported");

    /// <inheritdoc/>
    public override void AddBufferedRows(List<string> bufferNames)
        => throw new TODOException("MVSecondaryIndex: opening the index map is not ported");

    /// <inheritdoc/>
    public override MVMap<SearchRow, VersionedValue<Value>> GetMVMap()
        => throw new TODOException("MVSecondaryIndex: opening the index map is not ported");

    /// <inheritdoc/>
    public override void Close(SessionLocal session)
        => throw new TODOException("MVSecondaryIndex: opening the index map is not ported");

    /// <inheritdoc/>
    public override double GetCost(
        SessionLocal session,
        int[]? masks,
        TableFilter[]? filters,
        int filter,
        SortOrder? sortOrder,
        Command.Query.AllColumnsForPlan? allColumnsSet,
        bool isSelectCommand)
        => throw new TODOException("MVSecondaryIndex: opening the index map is not ported");

    /// <inheritdoc/>
    public override bool NeedRebuild()
        => throw new TODOException("MVSecondaryIndex: opening the index map is not ported");
}
