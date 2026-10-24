/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.mvstore.db.MVIndex.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * An index that stores the data in a MVStore.
 */
using System.Collections.Generic;
using H2Sharp.Indexes;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Db;

/// <summary>
/// An index that stores the data in a MVStore.
/// </summary>
/// <typeparam name="TKey">the key type</typeparam>
/// <typeparam name="TValue">the value type</typeparam>
public abstract class MVIndex<TKey, TValue> : Index
{
    /// <summary>
    /// Creates an index stored in an MVStore.
    /// </summary>
    /// <param name="newTable">the table</param>
    /// <param name="id">the object id</param>
    /// <param name="name">the index name</param>
    /// <param name="newIndexColumns">the columns that are indexed</param>
    /// <param name="uniqueColumnCount">count of unique columns</param>
    /// <param name="newIndexType">the index type</param>
    protected MVIndex(
        Table? newTable,
        int id,
        string name,
        IndexColumn[]? newIndexColumns,
        int uniqueColumnCount,
        IndexType? newIndexType)
        : base(newTable, id, name, newIndexColumns, uniqueColumnCount, newIndexType)
    {
    }

    /// <summary>
    /// Add the rows to a temporary storage (not to the index yet). The rows are
    /// sorted by the index columns. This is to more quickly build the index.
    /// </summary>
    /// <param name="rows">the rows</param>
    /// <param name="bufferName">the name of the temporary storage</param>
    public abstract void AddRowsToBuffer(List<Row> rows, string bufferName);

    /// <summary>
    /// Add all the index data from the buffers to the index. The index will
    /// typically use merge sort to add the data more quickly in sorted order.
    /// </summary>
    /// <param name="bufferNames">the names of the temporary storage</param>
    public abstract void AddBufferedRows(List<string> bufferNames);

    /// <summary>
    /// Gets the map this index is stored in.
    /// </summary>
    /// <returns>the map</returns>
    public abstract MVMap<TKey, VersionedValue<TValue>> GetMVMap();

    /// <inheritdoc/>
    public override long GetDiskSpaceUsed(bool approximate)
        => GetMVMap().GetRootPage().GetDiskSpaceUsed(approximate);
}
