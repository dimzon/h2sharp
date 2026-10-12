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
 * A row on disk may be shorter than the table: an index stores the key columns only. This is
 * what turns "the bytes of a key" into a row of the right shape, and GetIndexes is how the
 * comparison knows which of those columns to compare and in which order — the map in the
 * default factory is the inverse of that, from stored column position to written position.
 *
 * Partially ported. The factory has its reference shape (it decides between a normal and a
 * sparse row layout) and the default one, which stores every column; the sparse one is where a
 * row skips the columns the index does not key on, and it refuses until the layout it needs —
 * the column types and the per-index column lists — exists.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.MvStore.Db;
using H2Sharp.Store;
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
    /// Create a new row.
    /// </summary>
    /// <param name="data">the values</param>
    /// <param name="memory">the estimated memory usage in bytes</param>
    /// <returns>the created row</returns>
    public abstract Row CreateRow(Value[] data, int memory);

    /// <summary>
    /// Create new row.
    /// </summary>
    /// <returns>the created row</returns>
    public abstract SearchRow CreateRow();

    /// <summary>
    /// Gets the data type of the rows this factory builds.
    /// </summary>
    /// <returns>the row data type</returns>
    public abstract RowDataType GetRowDataType();

    /// <summary>
    /// Gets the indexes of the columns this factory stores, or null when it stores all of them.
    /// </summary>
    /// <returns>the indexes, or null</returns>
    public abstract int[]? GetIndexes();

    /// <summary>
    /// Gets the types of the columns this factory stores.
    /// </summary>
    /// <returns>the column types, or null</returns>
    public abstract TypeInfo[]? GetColumnTypes();

    /// <summary>
    /// Gets the number of columns of the rows this factory builds.
    /// </summary>
    /// <returns>the column count</returns>
    public abstract int GetColumnCount();

    /// <summary>
    /// Create a new row factory.
    /// </summary>
    /// <param name="provider">the cast provider</param>
    /// <param name="compareMode">the compare mode</param>
    /// <param name="handler">the data handler</param>
    /// <param name="columns">the columns</param>
    /// <param name="indexColumns">the index columns</param>
    /// <param name="storeKeys">whether row keys are stored</param>
    /// <param name="columnCount">the number of columns</param>
    /// <param name="columnTypes">the column types</param>
    /// <returns>the (possibly new) row factory</returns>
    public virtual RowFactory CreateRowFactory(
        CastDataProvider? provider,
        CompareMode compareMode,
        DataHandler? handler,
        Typed[]? columns,
        IndexColumn[]? indexColumns,
        bool storeKeys,
        int columnCount,
        TypeInfo[]? columnTypes)
    {
        return this;
    }

    /// <summary>
    /// The default implementation of row factory: every column is stored, in order.
    /// </summary>
    private sealed class DefaultRowFactory : RowFactory
    {
        private readonly RowDataType dataType;

        private readonly int columnCount;

        private readonly TypeInfo[]? columnTypes;

        internal DefaultRowFactory()
        {
            dataType = new RowDataType(null, CompareMode.GetInstance(null, 0), null, null, null, 0, true);
            columnCount = 0;
        }

        internal DefaultRowFactory(RowDataType dataType, int columnCount, TypeInfo[]? columnTypes)
        {
            this.dataType = dataType;
            this.columnCount = columnCount;
            this.columnTypes = columnTypes;
        }

        public override Row CreateRow(Value[] data, int memory)
        {
            ArgumentNullException.ThrowIfNull(data);
            return new DefaultRow(data, memory);
        }

        public override SearchRow CreateRow()
            => throw new TODOException("RowFactory.createRow(): the concrete row is not ported");

        public override RowDataType GetRowDataType()
        {
            return dataType;
        }

        public override int[]? GetIndexes()
        {
            return null;
        }

        public override TypeInfo[]? GetColumnTypes()
        {
            return columnTypes;
        }

        public override int GetColumnCount()
        {
            return columnCount;
        }
    }
}
