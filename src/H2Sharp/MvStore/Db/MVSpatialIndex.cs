/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.mvstore.db.MVSpatialIndex.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.MvStore.RTree;
using H2Sharp.MvStore.Tx;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Util.Geometry;
using H2Sharp.Values;
using TMIterator = H2Sharp.MvStore.Tx.TransactionMap<H2Sharp.MvStore.RTree.Spatial,
    H2Sharp.Values.Value>.TMIterator<H2Sharp.MvStore.RTree.Spatial, H2Sharp.Values.Value,
    H2Sharp.MvStore.RTree.Spatial>;
using SpatialMapCursor = H2Sharp.MvStore.RTree.MVRTreeMap<H2Sharp.Values.VersionedValue<object>>.RTreeCursor<
    H2Sharp.Values.VersionedValue<object>>;

namespace H2Sharp.MvStore.Db;

/// <summary>
/// This is an index based on a <see cref="MVRTreeMap{V}"/>.
/// </summary>
public sealed class MVSpatialIndex : MVIndex<Spatial, Value>, Indexes.SpatialIndex
{
    /// <summary>
    /// The multi-value table.
    /// </summary>
    private readonly MVTable mvTable;

    private readonly TransactionMap<Spatial, Value> dataMap;

    private readonly MVRTreeMap<VersionedValue<object>> spatialMap;

    /// <summary>
    /// Creates a spatial index.
    /// </summary>
    /// <param name="db">the database</param>
    /// <param name="table">the table instance</param>
    /// <param name="id">the index id</param>
    /// <param name="indexName">the index name</param>
    /// <param name="columns">the indexed columns (only one geometry column allowed)</param>
    /// <param name="uniqueColumnCount">count of unique columns (0 or 1)</param>
    /// <param name="indexType">the index type (only spatial index)</param>
    public MVSpatialIndex(Database db, MVTable table, int id, string? indexName, IndexColumn[] columns,
        int uniqueColumnCount, IndexType? indexType)
        : base(table, id, indexName!, columns, uniqueColumnCount, indexType)
    {
        if (columns.Length != 1)
        {
            throw DbException.GetUnsupportedException("Can only index one column");
        }

        IndexColumn col = columns[0];
        if ((col.SortType & SortOrder.Descending) != 0)
        {
            throw DbException.GetUnsupportedException("Cannot index in descending order");
        }

        if ((col.SortType & SortOrder.NullsFirst) != 0)
        {
            throw DbException.GetUnsupportedException("Nulls first is not supported");
        }

        if ((col.SortType & SortOrder.NullsLast) != 0)
        {
            throw DbException.GetUnsupportedException("Nulls last is not supported");
        }

        if (col.GetColumn().GetTypeInfo().GetValueType() != Value.Geometry)
        {
            throw DbException.GetUnsupportedException(
                "Spatial index on non-geometry column, " + col.GetColumn().GetCreateSql());
        }

        mvTable = table;
        if (!db.IsStarting())
        {
            CheckIndexColumnTypes(columns);
        }

        string mapName = "index." + GetId();
        spatialMap = db.GetStore().GetMvStore().OpenMap(mapName,
            new MVRTreeMap<VersionedValue<object>>.Builder<VersionedValue<object>>()
                .ValueType(new VersionedValueType<object, Database>(
                    new ObjectMapDataType<Value>(NullValueDataType.Instance))));
        Transaction? t = mvTable.GetTransactionBegin();
        dataMap = t is null
            ? throw new TODOException("MVSpatialIndex: the table has no transaction yet")
            : t.OpenMapX<Spatial, Value>(spatialMap);
        dataMap.InnerMap.SetVolatile(!table.IsPersistData || indexType is null || !indexType.IsPersistent());
        t.Commit();
    }

    /// <inheritdoc/>
    public override void AddRowsToBuffer(List<Row> rows, string bufferName)
        => throw DbException.GetInternalError();

    /// <inheritdoc/>
    public override void AddBufferedRows(List<string> bufferNames)
        => throw DbException.GetInternalError();

    /// <inheritdoc/>
    public override void Close(SessionLocal session)
    {
        // ok
    }

    /// <inheritdoc/>
    public override void Add(SessionLocal session, Row row)
    {
        TransactionMap<Spatial, Value> map = GetMap(session);
        Spatial key = GetKey(row);

        if (key.IsNull())
        {
            return;
        }

        if (GetUniqueColumnCount() > 0)
        {
            // this will detect committed entries only
            SpatialKeyIterator it = new(map, spatialMap.FindContainedKeys(key), false);
            while (it.HasNext())
            {
                Spatial k = it.Next()!;
                if (k.EqualsIgnoringId(key))
                {
                    throw GetDuplicateKeyException(k.ToString());
                }
            }
        }

        try
        {
            _ = map.Put(key, ValueNull.Instance);
        }
        catch (MvStoreException e)
        {
            throw mvTable.ConvertException(e);
        }

        if (GetUniqueColumnCount() > 0)
        {
            // check if there is another (uncommitted) entry
            SpatialKeyIterator it = new(map, spatialMap.FindContainedKeys(key), true);
            while (it.HasNext())
            {
                Spatial k = it.Next()!;
                if (k.EqualsIgnoringId(key))
                {
                    if (map.IsSameTransaction(k))
                    {
                        continue;
                    }

                    _ = map.Remove(key);
                    if (map.GetImmediate(k) is not null)
                    {
                        // committed
                        throw GetDuplicateKeyException(k.ToString());
                    }

                    throw DbException.Get(Api.ErrorCode.ConcurrentUpdate1, mvTable.GetTableName());
                }
            }
        }
    }

    /// <inheritdoc/>
    public override void Remove(SessionLocal session, Row row)
    {
        Spatial key = GetKey(row);

        if (key.IsNull())
        {
            return;
        }

        TransactionMap<Spatial, Value> map = GetMap(session);
        try
        {
            Value? old = map.Remove(key);
            if (old is null)
            {
                StringBuilder builder = new();
                _ = GetSql(builder, HasSql.TraceSqlFlags).Append(": ").Append(row.GetKey());
                throw DbException.Get(Api.ErrorCode.RowNotFoundWhenDeleting1, builder.ToString());
            }
        }
        catch (MvStoreException e)
        {
            throw mvTable.ConvertException(e);
        }
    }

    /// <inheritdoc/>
    public override Cursor? GetCursor(SessionLocal session, SearchRow? first, SearchRow? last, bool reverse)
    {
        TransactionMap<Spatial, Value> map = GetMap(session);
        SpatialKeyIterator it = new(map, reverse ? map.KeyIterator(null, true) : map.KeyIterator(null), false);
        return new MVStoreCursor(session, it, mvTable);
    }

    /// <inheritdoc/>
    public Cursor? FindByGeometry(SessionLocal session, SearchRow? first, SearchRow? last, bool reverse,
        SearchRow? intersection)
    {
        if (intersection is null)
        {
            return GetCursor(session, first, last, reverse);
        }

        SpatialMapCursor cursor = spatialMap.FindIntersectingKeys(GetKey(intersection));
        return new MVStoreCursor(session, new SpatialKeyIterator(GetMap(session), cursor, false), mvTable);
    }

    /// <summary>
    /// Returns the minimum bounding box that encloses all keys.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the minimum bounding box that encloses all keys, or null</returns>
    public Value? GetBounds(SessionLocal session)
    {
        FindBoundsCursor cursor = new(spatialMap.GetRootPage(), new DefaultSpatial(0), session,
            mvTable, GetMap(session), columnIds[0]);
        while (cursor.HasNext())
        {
            _ = cursor.Next();
        }

        return cursor.GetBounds();
    }

    /// <summary>
    /// Returns the estimated minimum bounding box that encloses all keys.
    /// </summary>
    /// <remarks>
    /// The returned value may be incorrect.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <returns>the estimated minimum bounding box that encloses all keys, or null</returns>
    public Value GetEstimatedBounds(SessionLocal session)
    {
        Page<object, VersionedValue<object>> p = spatialMap.GetRootPage();
        int count = p.GetKeyCount();
        if (count > 0)
        {
            Spatial key = (Spatial)p.GetKey(0)!;
            float bminxf = key.Min(0), bmaxxf = key.Max(0), bminyf = key.Min(1), bmaxyf = key.Max(1);
            for (int i = 1; i < count; i++)
            {
                key = (Spatial)p.GetKey(i)!;
                float minxf = key.Min(0), maxxf = key.Max(0), minyf = key.Min(1), maxyf = key.Max(1);
                if (minxf < bminxf)
                {
                    bminxf = minxf;
                }

                if (maxxf > bmaxxf)
                {
                    bmaxxf = maxxf;
                }

                if (minyf < bminyf)
                {
                    bminyf = minyf;
                }

                if (maxyf > bmaxyf)
                {
                    bmaxyf = maxyf;
                }
            }

            return ValueGeometry.FromEnvelope(new double[] { bminxf, bmaxxf, bminyf, bmaxyf });
        }

        return ValueNull.Instance;
    }

    private Spatial GetKey(SearchRow row)
    {
        Value? v = row.GetValue(columnIds[0]);
        double[]? env;
        if (v is ValueNull || (env = v!.ConvertToGeometry(null).GetEnvelopeNoCopy()) is null)
        {
            return new DefaultSpatial(row.GetKey());
        }

        return new DefaultSpatial(row.GetKey(),
            (float)env[GeometryUtils.MinX], (float)env[GeometryUtils.MaxX],
            (float)env[GeometryUtils.MinY], (float)env[GeometryUtils.MaxY]);
    }

    /// <inheritdoc/>
    public override double GetCost(SessionLocal session, int[]? masks, TableFilter[]? filters,
        int filter, SortOrder? sortOrder, AllColumnsForPlan? allColumnsSet, bool isSelectCommand)
    {
        // Never use spatial tree index without spatial filter
        if (columns.Length == 0)
        {
            return long.MaxValue;
        }

        foreach (Column column in columns)
        {
            int index = column.GetColumnId();
            int mask = masks![index];
            if ((mask & IndexCondition.SpatialIntersects) != IndexCondition.SpatialIntersects)
            {
                return long.MaxValue;
            }
        }

        return 10 * GetCostRangeIndex(masks, dataMap.SizeAsLongMax(), filters, filter, sortOrder, true,
            allColumnsSet, isSelectCommand);
    }

    /// <inheritdoc/>
    public override void Remove(SessionLocal session)
    {
        TransactionMap<Spatial, Value> map = GetMap(session);
        if (!map.IsClosed())
        {
            Transaction t = session.GetTransaction();
            t.RemoveMap(map);
        }
    }

    /// <inheritdoc/>
    public override void Truncate(SessionLocal session)
    {
        TransactionMap<Spatial, Value> map = GetMap(session);
        map.Clear();
    }

    /// <inheritdoc/>
    public override bool NeedRebuild()
    {
        try
        {
            return dataMap.SizeAsLongMax() == 0;
        }
        catch (MvStoreException e)
        {
            throw DbException.Get(Api.ErrorCode.ObjectClosed, e);
        }
    }

    /// <inheritdoc/>
    public override long GetRowCount(SessionLocal session)
    {
        TransactionMap<Spatial, Value> map = GetMap(session);
        return map.SizeAsLong();
    }

    /// <inheritdoc/>
    public override long GetRowCountApproximation(SessionLocal session)
    {
        try
        {
            return dataMap.SizeAsLongMax();
        }
        catch (MvStoreException e)
        {
            throw DbException.Get(Api.ErrorCode.ObjectClosed, e);
        }
    }

    /// <summary>
    /// Get the map to store the data.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the map</returns>
    private TransactionMap<Spatial, Value> GetMap(SessionLocal? session)
    {
        if (session is null)
        {
            return dataMap;
        }

        Transaction t = session.GetTransaction();
        return dataMap.GetInstance(t);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The transactional map is object-typed in this port, as for the secondary index: the
    /// typed map the member promises is not what the store holds.
    /// </remarks>
    public override MVMap<Spatial, VersionedValue<Value>> GetMVMap()
        => throw new TODOException("MVSpatialIndex.getMVMap: the transactional map is object-typed");

    /// <summary>
    /// A cursor.
    /// </summary>
    private sealed class MVStoreCursor : Cursor
    {
        private readonly SessionLocal session;
        private readonly SpatialKeyIterator it;
        private readonly MVTable mvTable;
        private Spatial? current;
        private SearchRow? searchRow;
        private Row? row;

        internal MVStoreCursor(SessionLocal session, SpatialKeyIterator it, MVTable mvTable)
        {
            this.session = session;
            this.it = it;
            this.mvTable = mvTable;
        }

        /// <inheritdoc/>
        public Row? Get()
        {
            if (row is null)
            {
                SearchRow? r = GetSearchRow();
                if (r is not null)
                {
                    row = mvTable.GetRow(session, r.GetKey()) as Row;
                }
            }

            return row;
        }

        /// <inheritdoc/>
        public SearchRow? GetSearchRow()
        {
            if (searchRow is null)
            {
                if (current is not null)
                {
                    searchRow = mvTable.GetTemplateRow();
                    searchRow.SetKey(current.GetId());
                }
            }

            return searchRow;
        }

        /// <inheritdoc/>
        public bool Next()
        {
            current = it.HasNext() ? it.Next() : null;
            searchRow = null;
            row = null;
            return current is not null;
        }

        /// <inheritdoc/>
        public bool Previous()
            => throw DbException.GetUnsupportedException("previous");
    }

    /// <summary>
    /// Filters the keys an R-tree or key iterator yields down to the ones the transaction map
    /// answers for, as the reference's iterator of the same name does.
    /// </summary>
    private sealed class SpatialKeyIterator
    {
        private readonly TransactionMap<Spatial, Value> map;
        private readonly SpatialMapCursor? spatialCursor;
        private readonly TMIterator? keyIterator;
        private readonly bool includeUncommitted;
        private Spatial? current;

        internal SpatialKeyIterator(TransactionMap<Spatial, Value> map,
            SpatialMapCursor iterator, bool includeUncommitted)
        {
            this.map = map;
            spatialCursor = iterator;
            this.includeUncommitted = includeUncommitted;
        }

        internal SpatialKeyIterator(TransactionMap<Spatial, Value> map,
            TMIterator iterator, bool includeUncommitted)
        {
            this.map = map;
            keyIterator = iterator;
            this.includeUncommitted = includeUncommitted;
        }

        internal bool HasNext()
        {
            if (current is null)
            {
                FetchNext();
            }

            return current is not null;
        }

        internal Spatial? Next()
        {
            Spatial? result = current;
            current = null;
            return result;
        }

        private void FetchNext()
        {
            while (FetchFromSource())
            {
                if (includeUncommitted || map.ContainsKey(current))
                {
                    return;
                }
            }

            current = null;
        }

        private bool FetchFromSource()
        {
            if (spatialCursor is not null)
            {
                current = spatialCursor.HasNext() ? spatialCursor.Next() : null;
            }
            else
            {
                current = keyIterator!.TryFetchNext(out Spatial next) ? next : null;
            }

            return current is not null;
        }
    }

    /// <summary>
    /// A cursor for the getBounds method.
    /// </summary>
    private sealed class FindBoundsCursor : SpatialMapCursor
    {
        private readonly SessionLocal session;

        private readonly MVTable mvTable;

        private readonly TransactionMap<Spatial, Value> map;

        private readonly int columnId;

        private bool hasBounds;

        private float bminxf, bmaxxf, bminyf, bmaxyf;

        private double bminxd, bmaxxd, bminyd, bmaxyd;

        internal FindBoundsCursor(Page<object, VersionedValue<object>> root, Spatial filter,
            SessionLocal session, MVTable mvTable, TransactionMap<Spatial, Value> map, int columnId)
            : base(root, filter)
        {
            this.session = session;
            this.mvTable = mvTable;
            this.map = map;
            this.columnId = columnId;
        }

        /// <inheritdoc/>
        protected override bool Check(bool leaf, Spatial key, Spatial test)
        {
            float minxf = key.Min(0), maxxf = key.Max(0), minyf = key.Min(1), maxyf = key.Max(1);
            if (leaf)
            {
                if (hasBounds)
                {
                    if ((minxf <= bminxf || maxxf >= bmaxxf || minyf <= bminyf || maxyf >= bmaxyf)
                            && map.ContainsKey(key))
                    {
                        double[] env = ((ValueGeometry)mvTable.GetRow(session, key.GetId())!
                                .GetValue(columnId)!).GetEnvelopeNoCopy()!;
                        double minxd = env[GeometryUtils.MinX], maxxd = env[GeometryUtils.MaxX],
                            minyd = env[GeometryUtils.MinY], maxyd = env[GeometryUtils.MaxY];
                        if (minxd < bminxd)
                        {
                            bminxf = minxf;
                            bminxd = minxd;
                        }

                        if (maxxd > bmaxxd)
                        {
                            bmaxxf = maxxf;
                            bmaxxd = maxxd;
                        }

                        if (minyd < bminyd)
                        {
                            bminyf = minyf;
                            bminyd = minyd;
                        }

                        if (maxyd > bmaxyd)
                        {
                            bmaxyf = maxyf;
                            bmaxyd = maxyd;
                        }
                    }
                }
                else if (map.ContainsKey(key))
                {
                    hasBounds = true;
                    double[] env = ((ValueGeometry)mvTable.GetRow(session, key.GetId())!
                            .GetValue(columnId)!).GetEnvelopeNoCopy()!;
                    bminxf = minxf;
                    bminxd = env[GeometryUtils.MinX];
                    bmaxxf = maxxf;
                    bmaxxd = env[GeometryUtils.MaxX];
                    bminyf = minyf;
                    bminyd = env[GeometryUtils.MinY];
                    bmaxyf = maxyf;
                    bmaxyd = env[GeometryUtils.MaxY];
                }
            }
            else if (hasBounds)
            {
                if (minxf <= bminxf || maxxf >= bmaxxf || minyf <= bminyf || maxyf >= bmaxyf)
                {
                    return true;
                }
            }
            else
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// The bounding box found, or null.
        /// </summary>
        /// <returns>the bounds</returns>
        public Value? GetBounds()
            => hasBounds
                ? ValueGeometry.FromEnvelope(new double[] { bminxd, bmaxxd, bminyd, bmaxyd })
                : ValueNull.Instance;
    }
}
