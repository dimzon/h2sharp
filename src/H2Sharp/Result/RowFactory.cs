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
 * Ported from org.h2.result.RowFactory (H2 2.5.x).
 */
using System;
using H2Sharp.Engine;
using H2Sharp.MvStore.Db;
using H2Sharp.Store;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Result;

/// <summary>
/// Creates rows.
/// </summary>
/// <remarks>
/// Ported from org.h2.result.RowFactory (H2 2.5.x).
/// </remarks>
public abstract class RowFactory
{
    private static readonly RowFactory Default = new DefaultRowFactory();

    /// <summary>
    /// Gets the default row factory.
    /// </summary>
    /// <remarks>
    /// Reference: org.h2.result.RowFactory.java:29-31
    /// </remarks>
    /// <returns>the factory</returns>
    public static RowFactory GetDefaultRowFactory()
    {
        return Default;
    }

    /// <summary>
    /// Create a new row factory.
    /// </summary>
    /// <remarks>
    /// Reference: org.h2.result.RowFactory.java:48-51
    /// Default returns this (the singleton). Subclasses may return a new factory.
    /// </remarks>
    /// <param name="provider">the cast provider</param>
    /// <param name="compareMode">the compare mode</param>
    /// <param name="handler">the data handler</param>
    /// <param name="columns">the columns</param>
    /// <param name="indexColumns">the index columns</param>
    /// <param name="storeKeys">whether row keys are stored</param>
    /// <returns>the (possibly new) row factory</returns>
    public virtual RowFactory CreateRowFactory(
        CastDataProvider? provider,
        CompareMode compareMode,
        DataHandler? handler,
        Typed[]? columns,
        IndexColumn[]? indexColumns,
        bool storeKeys)
    {
        return this;
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
    /// The default implementation of row factory.
    /// </summary>
    /// <remarks>
    /// Ported from org.h2.result.RowFactory.DefaultRowFactory (H2 2.5.x).
    /// </remarks>
    public sealed class DefaultRowFactory : RowFactory
    {
        private readonly RowDataType dataType;
        private readonly int columnCount;
        private readonly int[]? indexes;
        private readonly TypeInfo[]? columnTypes;
        private readonly int[]? map;

        public static readonly DefaultRowFactory Instance = new DefaultRowFactory();

        internal DefaultRowFactory()
        {
            dataType = new RowDataType(null, CompareMode.GetInstance(null, 0), null, null, null, 0, true);
            columnCount = 0;
            indexes = null;
            columnTypes = null;
            map = null;
        }

        private DefaultRowFactory(
            RowDataType dataType,
            int columnCount,
            int[]? indexes,
            TypeInfo[]? columnTypes)
        {
            this.dataType = dataType;
            this.columnCount = columnCount;
            this.indexes = indexes;
            if (indexes == null)
            {
                map = null;
            }
            else
            {
                map = new int[columnCount];
                for (int i = 0, len = indexes.Length; i < len;)
                {
                    map[indexes[i]] = ++i;
                }
            }

            this.columnTypes = columnTypes;
        }

        /// <summary>
        /// Create a new row factory from columns and index columns.
        /// </summary>
        /// <remarks>
        /// Reference: org.h2.result.RowFactory.DefaultRowFactory.java:110-140
        /// Extracts indexes, sortTypes, and columnTypes from the inputs, then delegates
        /// to the lower-level createRowFactory.
        /// </remarks>
        /// <param name="provider">the cast provider</param>
        /// <param name="compareMode">the compare mode</param>
        /// <param name="handler">the data handler</param>
        /// <param name="columns">the columns</param>
        /// <param name="indexColumns">the index columns</param>
        /// <param name="storeKeys">whether row keys are stored</param>
        /// <returns>the (possibly new) row factory</returns>
        public override RowFactory CreateRowFactory(
            CastDataProvider? provider,
            CompareMode compareMode,
            DataHandler? handler,
            Typed[]? columns,
            IndexColumn[]? indexColumns,
            bool storeKeys)
        {
            int[]? indexes = null;
            int[]? sortTypes = null;
            TypeInfo[]? columnTypes = null;
            int columnCount = 0;
            if (columns is not null)
            {
                columnCount = columns.Length;
                if (indexColumns is null)
                {
                    sortTypes = new int[columnCount];
                    for (int i = 0; i < columnCount; i++)
                    {
                        sortTypes[i] = SortOrder.Ascending;
                    }
                }
                else
                {
                    int len = indexColumns.Length;
                    indexes = new int[len];
                    sortTypes = new int[len];
                    for (int i = 0; i < len; i++)
                    {
                        IndexColumn indexColumn = indexColumns[i];
                        indexes[i] = indexColumn.GetColumn().GetColumnId();
                        sortTypes[i] = indexColumn.SortType;
                    }
                }

                columnTypes = new TypeInfo[columnCount];
                for (int i = 0; i < columnCount; i++)
                {
                    columnTypes[i] = columns[i].GetTypeInfo();
                }
            }

            return CreateRowFactory(
                provider, compareMode, handler, sortTypes, indexes, columnTypes, columnCount, storeKeys);
        }

        /// <summary>
        /// Create a new row factory.
        /// </summary>
        /// <remarks>
        /// Reference: org.h2.result.RowFactory.DefaultRowFactory.java:155-162
        /// Builds RowDataType, creates DefaultRowFactory, and wires the factory back
        /// into RowDataType so that reads produce the correct row type.
        /// </remarks>
        /// <param name="provider">the cast provider</param>
        /// <param name="compareMode">the compare mode</param>
        /// <param name="handler">the data handler</param>
        /// <param name="sortTypes">the sort types</param>
        /// <param name="indexes">the list of indexed columns</param>
        /// <param name="columnTypes">the list of column data type information</param>
        /// <param name="columnCount">the number of columns</param>
        /// <param name="storeKeys">whether row keys are stored</param>
        /// <returns>the (possibly new) row factory</returns>
        public RowFactory CreateRowFactory(
            CastDataProvider? provider,
            CompareMode compareMode,
            DataHandler? handler,
            int[]? sortTypes,
            int[]? indexes,
            TypeInfo[]? columnTypes,
            int columnCount,
            bool storeKeys)
        {
            RowDataType rowDataType = new RowDataType(
                provider, compareMode, handler, sortTypes, indexes, columnCount, storeKeys);
            RowFactory rowFactory = new DefaultRowFactory(rowDataType, columnCount, indexes, columnTypes);
            rowDataType.SetRowFactory(rowFactory);
            return rowFactory;
        }

        public override Row CreateRow(Value[] data, int memory)
        {
            ArgumentNullException.ThrowIfNull(data);
            return new DefaultRow(data, memory);
        }

        /// <summary>
        /// Creates a search row.
        /// </summary>
        /// <remarks>
        /// Three branches match the reference:
        /// - indexes == null → DefaultRow (all columns)
        /// - indexes.Length == 1 → SimpleRowValue (single indexed column)
        /// - otherwise → Sparse (multiple indexed columns)
        /// Reference: org.h2.result.RowFactory.DefaultRowFactory.java:170-178
        /// </remarks>
        public override SearchRow CreateRow()
        {
            if (indexes is null)
            {
                return new DefaultRow(columnCount);
            }
            else if (indexes.Length == 1)
            {
                return new SimpleRowValue(columnCount, indexes[0]);
            }
            else
            {
                return new Sparse(columnCount, indexes.Length, map!);
            }
        }

        public override RowDataType GetRowDataType()
        {
            return dataType;
        }

        public override int[]? GetIndexes()
        {
            return indexes;
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
