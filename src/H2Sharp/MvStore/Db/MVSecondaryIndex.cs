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
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.MvStore.Tx;
using H2Sharp.MvStore.Type;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;
using TMIterator = H2Sharp.MvStore.Tx.TransactionMap<H2Sharp.Result.SearchRow,
    H2Sharp.Values.Value>.TMIterator<H2Sharp.Result.SearchRow, H2Sharp.Values.Value,
    H2Sharp.Result.SearchRow>;

namespace H2Sharp.MvStore.Db;

/// <summary>
/// A regular index that stores the data in an MVStore.
/// </summary>
public sealed class MVSecondaryIndex : MVIndex<SearchRow, Value>
{
    /// <summary>
    /// The multi-value table.
    /// </summary>
    private readonly MVTable mvTable;

    private readonly TransactionMap<SearchRow, Value> dataMap;

    /// <summary>
    /// Creates a secondary index.
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
        mvTable = table;
        if (!db.IsStarting())
        {
            CheckIndexColumnTypes(columns);
        }

        string mapName = "index." + GetId();
        RowDataType keyType = GetRowFactory().GetRowDataType();
        Transaction? t = mvTable.GetTransactionBegin();
        dataMap = t is null
            ? throw new TODOException("MVSecondaryIndex: the table has no transaction yet")
            : t.OpenMap(mapName, keyType, NullValueDataType.Instance);
        dataMap.InnerMap.SetVolatile(!table.IsPersistData || indexType is null || !indexType.IsPersistent());
        if (!db.IsStarting())
        {
            dataMap.Clear();
        }

        t.Commit();

        // The reference also compares the declared key type with the one the map reports. In
        // this port the map reports the object-level adapter (see ObjectMapDataType), so the
        // check would compare a RowDataType with that adapter and always fail.
    }

    /// <inheritdoc/>
    public override void AddRowsToBuffer(List<Row> rows, string bufferName)
    {
        MVMap<SearchRow, Value> map = OpenMap(bufferName);
        foreach (Row row in rows)
        {
            SearchRow r = GetRowFactory().CreateRow();
            r.CopyFrom(row);
            map.Append(r, ValueNull.Instance);
        }
    }

    private sealed class Source
    {
        private readonly IEnumerator<SearchRow> iterator;

        internal SearchRow CurrentRowData;

        internal Source(IEnumerator<SearchRow> iterator)
        {
            this.iterator = iterator;
            CurrentRowData = iterator.Current;
        }

        internal bool HasNext()
        {
            bool result = iterator.MoveNext();
            if (result)
            {
                CurrentRowData = iterator.Current;
            }

            return result;
        }

        internal SearchRow Next() => CurrentRowData;

        internal sealed class RowComparator : IComparer<Source>
        {
            private readonly DataType<SearchRow> type;

            internal RowComparator(DataType<SearchRow> type)
            {
                this.type = type;
            }

            public int Compare(Source? one, Source? two)
                => type.Compare(one!.CurrentRowData, two!.CurrentRowData);
        }
    }

    /// <inheritdoc/>
    public override void AddBufferedRows(List<string> bufferNames)
    {
        int buffersCount = bufferNames.Count;
        PriorityQueue<Source, Source> queue = new(buffersCount,
            new Source.RowComparator(GetRowFactory().GetRowDataType()));
        foreach (string bufferName in bufferNames)
        {
            IEnumerator<SearchRow> iter = OpenMap(bufferName).KeyIterator(null);
            if (iter.MoveNext())
            {
                Source source = new(iter);
                queue.Enqueue(source, source);
            }
        }

        try
        {
            while (queue.TryDequeue(out Source? s, out _))
            {
                SearchRow row = s!.Next();

                if (NeedsUniqueCheck(row))
                {
                    CheckUnique(false, dataMap, row, long.MinValue);
                }

                dataMap.PutCommitted(row, ValueNull.Instance);

                if (s.HasNext())
                {
                    queue.Enqueue(s, s);
                }
            }
        }
        finally
        {
            MvStore.MVStore mvStore = mvTable.GetSchema()!.GetDatabase().GetStore().GetMvStore();
            foreach (string tempMapName in bufferNames)
            {
                mvStore.RemoveMap(tempMapName);
            }
        }
    }

    private MVMap<SearchRow, Value> OpenMap(string mapName)
    {
        RowDataType keyType = GetRowFactory().GetRowDataType();
        MVMap<SearchRow, Value>.Builder<SearchRow, Value> builder =
            new MVMap<SearchRow, Value>.Builder<SearchRow, Value>()
                .SingleWriter()
                .KeyType(keyType)
                .ValueType(NullValueDataType.Instance);
        return mvTable.GetSchema()!.GetDatabase().GetStore().GetMvStore().OpenMap(mapName, builder);
    }

    /// <inheritdoc/>
    public override void Close(SessionLocal session)
    {
        // ok
    }

    /// <inheritdoc/>
    public override void Add(SessionLocal session, Row row)
    {
        if (TryAdd(session, row, out SearchRow? conflict))
        {
            return;
        }

        throw GetDuplicateKeyException(conflict!.ToString());
    }

    internal override bool TryAdd(SessionLocal session, Row row, out SearchRow? conflict)
    {
        TransactionMap<SearchRow, Value> map = GetMap(session);
        SearchRow key = ConvertToKey(row, null)!;
        bool checkRequired = NeedsUniqueCheck(row);
        if (checkRequired)
        {
            bool repeatableRead = !session.GetTransaction().AllowNonRepeatableRead();
            conflict = FindUniqueConflict(repeatableRead, map, row, long.MinValue);
            if (conflict is not null)
            {
                return false;
            }
        }

        try
        {
            map.Put(key, ValueNull.Instance);
        }
        catch (MvStoreException e)
        {
            throw mvTable.ConvertException(e);
        }

        if (checkRequired)
        {
            conflict = FindUniqueConflict(false, map, row, row.GetKey());
            if (conflict is not null)
            {
                _ = map.Remove(key);
                return false;
            }
        }

        conflict = null;
        return true;
    }

    private void CheckUnique(bool repeatableRead, TransactionMap<SearchRow, Value> map,
        SearchRow row, long newKey)
    {
        SearchRow? conflict = FindUniqueConflict(repeatableRead, map, row, newKey);
        if (conflict is not null)
        {
            throw GetDuplicateKeyException(conflict.ToString());
        }
    }

    private SearchRow? FindUniqueConflict(bool repeatableRead, TransactionMap<SearchRow, Value> map,
        SearchRow row, long newKey)
    {
        RowFactory uniqueRowFactory = GetUniqueRowFactory();
        SearchRow from = uniqueRowFactory.CreateRow();
        from.CopyFrom(row);
        from.SetKey(long.MinValue);
        SearchRow to = uniqueRowFactory.CreateRow();
        to.CopyFrom(row);
        to.SetKey(long.MaxValue);
        if (repeatableRead)
        {
            // In order to guarantee repeatable reads, snapshot taken at the beginning of the
            // statement or transaction need to be checked additionally, because existence of
            // the key should be accounted for, even if since then, it was already deleted by
            // another (possibly committed) transaction.
            TMIterator it = map.KeyIterator(from, to);
            while (it.TryFetchNext(out SearchRow k))
            {
                if (newKey != k.GetKey() && !map.IsDeletedByCurrentTransaction(k))
                {
                    return k;
                }
            }
        }

        TMIterator uncommitted = map.KeyIteratorUncommitted(from, to);
        while (uncommitted.TryFetchNext(out SearchRow k))
        {
            if (newKey != k.GetKey())
            {
                if (map.GetImmediate(k) is not null)
                {
                    // committed
                    return k;
                }

                throw DbException.Get(ErrorCode.ConcurrentUpdate1, mvTable.Name);
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public override void Remove(SessionLocal session, Row row)
    {
        SearchRow searchRow = ConvertToKey(row, null)!;
        TransactionMap<SearchRow, Value> map = GetMap(session);
        try
        {
            if (map.Remove(searchRow) is null)
            {
                StringBuilder builder = new();
                _ = GetSql(builder, HasSql.TraceSqlFlags).Append(": ").Append(row.GetKey());
                throw DbException.Get(ErrorCode.RowNotFoundWhenDeleting1, builder.ToString());
            }
        }
        catch (MvStoreException e)
        {
            throw mvTable.ConvertException(e);
        }
    }

    /// <inheritdoc/>
    public override void Update(SessionLocal session, Row oldRow, Row newRow)
    {
        if (!AreRowsEquivalent(oldRow, newRow))
        {
            base.Update(session, oldRow, newRow);
        }
    }

    /// <inheritdoc/>
    public override Cursor? GetCursor(SessionLocal session, SearchRow? first, SearchRow? last, bool reverse)
    {
        return GetCursor(session, first, false, last, reverse);
    }

    private Cursor GetCursor(SessionLocal session, SearchRow? first, bool bigger,
        SearchRow? last, bool reverse)
    {
        SearchRow? min = ConvertToKey(first, bigger ^ reverse);
        SearchRow? max = ConvertToKey(last, !reverse);
        return new MVStoreCursor(session, GetMap(session).KeyIterator(min, max, reverse), mvTable);
    }

    private SearchRow? ConvertToKey(SearchRow? r, bool? minMax)
    {
        if (r is null)
        {
            return null;
        }

        SearchRow row = GetRowFactory().CreateRow();
        row.CopyFrom(r);
        if (minMax is not null)
        {
            row.SetKey(minMax.Value ? long.MaxValue : long.MinValue);
        }

        return row;
    }

    /// <inheritdoc/>
    public override double GetCost(
        SessionLocal session,
        int[]? masks,
        TableFilter[]? filters,
        int filter,
        SortOrder? sortOrder,
        AllColumnsForPlan? allColumnsSet,
        bool isSelectCommand)
    {
        try
        {
            return 10 * GetCostRangeIndex(masks, dataMap.SizeAsLongMax(),
                filters, filter, sortOrder, isScanIndex: false, allColumnsSet, isSelectCommand);
        }
        catch (MvStoreException e)
        {
            throw DbException.Get(ErrorCode.ObjectClosed, e);
        }
    }

    /// <inheritdoc/>
    public override void Remove(SessionLocal session)
    {
        TransactionMap<SearchRow, Value> map = GetMap(session);
        if (!map.IsClosed())
        {
            Transaction t = session.GetTransaction();
            t.RemoveMap(map);
        }
    }

    /// <inheritdoc/>
    public override void Truncate(SessionLocal session)
    {
        GetMap(session).Clear();
    }

    /// <inheritdoc/>
    public override bool CanGetFirstOrLast() => true;

    /// <inheritdoc/>
    public override Cursor FindFirstOrLast(SessionLocal session, bool first)
    {
        TMIterator iter = GetMap(session).KeyIterator(null, !first);
        while (iter.TryFetchNext(out SearchRow key))
        {
            if (key.GetValue(columnIds[0]) != ValueNull.Instance)
            {
                return new SingleRowCursor(mvTable.GetRow(session, key.GetKey()) as Row);
            }
        }

        return SingleRowCursor.Empty;
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
            throw DbException.Get(ErrorCode.ObjectClosed, e);
        }
    }

    /// <inheritdoc/>
    public override long GetRowCount(SessionLocal session)
    {
        TransactionMap<SearchRow, Value> map = GetMap(session);
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
            throw DbException.Get(ErrorCode.ObjectClosed, e);
        }
    }

    /// <inheritdoc/>
    public override bool CanFindNext() => true;

    /// <summary>
    /// Get the map to store the data.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the map</returns>
    private TransactionMap<SearchRow, Value> GetMap(SessionLocal? session)
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
    /// Not ported: the reference returns the map that the transaction map wraps, typed with the
    /// row key and versioned value. This port's transaction map wraps an object-keyed map (see
    /// <see cref="TransactionStore"/>), and that object is not an instance of the typed map the
    /// member promises.
    /// </remarks>
    public override MVMap<SearchRow, VersionedValue<Value>> GetMVMap()
        => throw new TODOException("MVSecondaryIndex.getMVMap: the transactional map is object-typed");

    /// <inheritdoc/>
    /// <remarks>
    /// Reads the inner map's root page directly, for the same reason as in
    /// <see cref="MVPrimaryIndex.GetDiskSpaceUsed(bool)"/>.
    /// </remarks>
    public override long GetDiskSpaceUsed(bool approximate)
        => dataMap.InnerMap.GetRootPage().GetDiskSpaceUsed(approximate);

    private sealed class MVStoreCursor : Cursor
    {
        private readonly SessionLocal session;

        private readonly TMIterator it;

        private readonly MVTable mvTable;

        private SearchRow? current;

        private Row? row;

        internal MVStoreCursor(SessionLocal session,
            TMIterator it, MVTable mvTable)
        {
            this.session = session;
            this.it = it;
            this.mvTable = mvTable;
        }

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

        public SearchRow? GetSearchRow() => current;

        public bool Next()
        {
            current = it.TryFetchNext(out SearchRow next) ? next : null;
            row = null;
            return current is not null;
        }

        public bool Previous()
            => throw DbException.GetUnsupportedException("previous");
    }
}
