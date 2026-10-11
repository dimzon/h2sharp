/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.result.RowFactory.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Creates rows.
 *
 * A row factory exists because a row on disk may be shorter than the table: an index stores the
 * key columns only, so reading one needs a different row shape from the same bytes. The factory
 * is what turns "the bytes of a key" into a row with the right number of columns, and
 * GetIndexes is how the comparison knows which of those columns to compare in which order.
 *
 * Partially ported: the shape is here and the default factory builds a plain row, but the
 * specialised factories the reference installs for sparse rows refuse, because each of them
 * needs the row layout of a kind of table that is not ported yet.
 */
using H2Sharp.Engine;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Result;

/// <summary>
/// Creates rows.
/// </summary>
public abstract class RowFactory
{
    private static readonly RowFactory Default = new DefaultRowFactory();

    /// <summary>
    /// Gets the default row factory.
    /// </summary>
    /// <returns>the factory</returns>
    public static RowFactory GetDefaultRowFactory()
    {
        return Default;
    }

    /// <summary>
    /// Creates a new row factory for the given columns.
    /// </summary>
    /// <param name="provider">the cast provider</param>
    /// <param name="compareMode">the compare mode</param>
    /// <param name="handler">the data handler</param>
    /// <param name="columns">the list of columns</param>
    /// <param name="indexColumns">the list of index columns</param>
    /// <param name="storeKeys">whether row keys are stored</param>
    /// <returns>the (possibly new) row factory</returns>
    public virtual RowFactory CreateRowFactory(
        CastDataProvider? provider,
        CompareMode compareMode,
        Store.DataHandler? handler,
        Typed[] columns,
        IndexColumn[]? indexColumns,
        bool storeKeys)
    {
        return this;
    }

    /// <summary>
    /// Gets the indexes of the columns this factory stores, or null when it stores all of them.
    /// </summary>
    /// <returns>the indexes, or null</returns>
    public virtual int[]? GetIndexes()
    {
        return null;
    }

    /// <summary>
    /// Creates a new row.
    /// </summary>
    /// <param name="key">the row key, or null</param>
    /// <returns>the row</returns>
    public abstract Row CreateRow(SearchRow? key);

    /// <summary>
    /// The factory that builds a plain row with every column.
    /// </summary>
    private sealed class DefaultRowFactory : RowFactory
    {
        public override Row CreateRow(SearchRow? key)
        {
            return key as Row ?? throw new TODOException("RowFactory.createRow: a plain row from values");
        }
    }
}
