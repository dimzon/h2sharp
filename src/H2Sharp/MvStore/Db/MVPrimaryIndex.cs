/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.mvstore.db.MVPrimaryIndex.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The primary index of a table, which is also where the table's rows live.
 *
 * This class hands out row keys, and the rule it uses is the reason it exists at all. A table
 * has no keys of its own: the index gives every row one, and it gives them in **increasing
 * order and never twice** — `lastKey` only ever moves forward, and the update loop at the end of
 * add() is a compare-and-set loop precisely because the key can be set from the outside through
 * the `_rowid_` syntax, which may write a key that is not the one the index would have chosen.
 * Two threads adding rows at once must not both pick the same number, and the second one has to
 * find its own number instead of reusing the first.
 *
 * The other job is the duplicate key error, and its wording depends on *when* the duplicate was
 * found: a row already committed is a duplicate key, and a row another transaction is putting in
 * right now is a concurrent update. Those are different problems for the caller, so the two
 * cases are read apart rather than collapsed into one error.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.MvStore.Tx;
using H2Sharp.MvStore.Type;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Db;

/// <summary>
/// A table stored in a MVStore: the index that holds the rows.
/// </summary>
public sealed class MVPrimaryIndex : MVIndex<long, SearchRow>
{
    private readonly MVTable mvTable;

    private readonly TransactionMap<long, SearchRow> dataMap;

    private readonly AtomicLong lastKey = new();

    private int mainIndexColumn = SearchRow.RowidIndex;

    /// <summary>
    /// Opens the primary index of the given table.
    /// </summary>
    /// <param name="database">the database</param>
    /// <param name="table">the table</param>
    /// <param name="id">the object id</param>
    /// <param name="columns">the index columns</param>
    /// <param name="indexType">the index type</param>
    public MVPrimaryIndex(Database database, MVTable table, int id, IndexColumn[]? columns, IndexType? indexType)
        : base(table, id, table.Name + "_DATA", columns, 0, indexType)
    {
        mvTable = table;
        RowDataType valueType = table.GetRowFactory().GetRowDataType();
        string mapName = "table." + GetId();
        Transaction? t = table.GetTransactionBegin();
        dataMap = t is null
            ? throw new TODOException("MVPrimaryIndex: the table has no transaction yet")
            : t.OpenMap(mapName, LongDataType.Instance, valueType);

        // include uncommitted keys as well: the map's last key is where the counter resumes,
        // and a key that is still in flight is a key that will be taken.
        long? k = LastKeyOf(dataMap.InnerMap);
        lastKey.Set(k ?? 0);
        _ = database;
    }

    /// <summary>
    /// Sets the column whose value becomes the row key, or <see cref="SearchRow.RowidIndex"/>
    /// when the index hands out keys itself.
    /// </summary>
    /// <param name="mainIndexColumn">the column position, or -1</param>
    public void SetMainIndexColumn(int mainIndexColumn)
    {
        this.mainIndexColumn = mainIndexColumn;
    }

    /// <summary>
    /// Gets the column whose value becomes the row key, or -1.
    /// </summary>
    /// <returns>the column position, or -1</returns>
    public int GetMainIndexColumn()
    {
        return mainIndexColumn;
    }

    /// <summary>
    /// Gets the cost of using this index: ten times the range price, because every row it
    /// returns has to be read from the table itself as well.
    /// </summary>
    /// <remarks>
    /// The factor of ten is not measured anywhere, it is what the reference charges: a primary
    /// lookup that stopped at the index would be free of the table, and this one is not. A
    /// closed store fails the sizing first, and that failure is reported as OBJECT_CLOSED
    /// rather than whatever the store said — a caller that asked for a cost gets an answer
    /// about the database, not about the file.
    /// </remarks>
    /// <inheritdoc/>
    public override double GetCost(
        SessionLocal session,
        int[]? masks,
        Tables.TableFilter[]? filters,
        int filter,
        SortOrder? sortOrder,
        H2Sharp.Command.Query.AllColumnsForPlan? allColumnsSet,
        bool isSelectCommand)
    {
        try
        {
            return 10 * GetCostRangeIndex(
                masks, dataMap.SizeAsLongMax(), filters, filter, sortOrder,
                isScanIndex: true, allColumnsSet, isSelectCommand);
        }
        catch (MvStoreException e)
        {
            throw DbException.Get(ErrorCode.ObjectClosed, e);
        }
    }

    /// <inheritdoc/>
    public override void Close(SessionLocal session)
    {
        // ok
    }

    /// <inheritdoc/>
    public override bool NeedRebuild()
    {
        return false;
    }

    /// <inheritdoc/>
    public override void Add(SessionLocal session, Row row)
    {
        if (mainIndexColumn == SearchRow.RowidIndex)
        {
            if (row.GetKey() == 0)
            {
                row.SetKey(lastKey.IncrementAndGet());
            }
        }
        else
        {
            row.SetKey(row.GetValue(mainIndexColumn)!.GetLong());
        }

        TransactionMap<long, SearchRow> map = GetMap(session.Transaction);
        long rowKey = row.GetKey();
        try
        {
            Row? old = (Row?)map.PutIfAbsent(rowKey, row);
            if (old is not null)
            {
                int errorCode = ErrorCode.ConcurrentUpdate1;
                if (map.GetImmediate(rowKey) is not null || map.GetFromSnapshot(rowKey) is not null)
                {
                    // committed
                    errorCode = ErrorCode.DuplicateKey1;
                }

                throw DbException.Get(errorCode, GetDuplicatePrimaryKeyMessage(mainIndexColumn).Append(' ').Append(old).ToString());
            }
        }
        catch (Exception e) when (e is not H2SharpException)
        {
            throw mvTable.ConvertException(e);
        }

        // Because it is possible to directly update the key using the _rowid_ syntax, the key
        // just written may be past the counter; move the counter to it, and only if nobody else
        // moved it further in the meantime.
        long last;
        while (rowKey > (last = lastKey.Get()))
        {
            if (lastKey.CompareAndSet(last, rowKey))
            {
                break;
            }
        }
    }

    /// <inheritdoc/>
    public override void Remove(SessionLocal session, Row row)
    {
        TransactionMap<long, SearchRow> map = GetMap(session.Transaction);
        Row? existing;
        try
        {
            existing = (Row?)map.Remove(row.GetKey());
        }
        catch (Exception e) when (e is not H2SharpException)
        {
            throw mvTable.ConvertException(e);
        }

        if (existing is null)
        {
            StringBuilder builder = new();
            _ = GetSql(builder, HasSql.TraceSqlFlags).Append(": ").Append(row.GetKey());
            throw DbException.Get(ErrorCode.RowNotFoundWhenDeleting1, builder.ToString());
        }
    }

    /// <inheritdoc/>
    public override void Update(SessionLocal session, SearchRow oldRow, SearchRow newRow)
    {
        long key = oldRow.GetKey();
        if (key != newRow.GetKey())
        {
            throw DbException.GetInternalError(key + " != " + newRow.GetKey());
        }

        TransactionMap<long, SearchRow> map = GetMap(session.Transaction);
        object? existing;
        try
        {
            existing = map.Put(key, newRow);
        }
        catch (Exception e) when (e is not H2SharpException)
        {
            throw mvTable.ConvertException(e);
        }

        if (existing is null)
        {
            StringBuilder builder = new();
            _ = GetSql(builder, HasSql.TraceSqlFlags).Append(": ").Append(key);
            throw DbException.Get(ErrorCode.RowNotFoundInPrimaryIndex, builder.ToString());
        }

        // Because it is possible to directly update the key using the _rowid_ syntax.
        if (newRow.GetKey() > lastKey.Get())
        {
            lastKey.Set(newRow.GetKey());
        }
    }

    /// <inheritdoc/>
    public override void Truncate(SessionLocal session)
    {
        dataMap.Clear();
        lastKey.Set(0);
    }

    /// <inheritdoc/>
    public override MVMap<long, VersionedValue<SearchRow>> GetMVMap()
    {
        throw new TODOException("MVPrimaryIndex.getMVMap: the map is wrapped in a transaction");
    }

    /// <inheritdoc/>
    public override void AddRowsToBuffer(List<Row> rows, string bufferName)
        => throw new TODOException("MVPrimaryIndex.addRowsToBuffer");

    /// <inheritdoc/>
    public override void AddBufferedRows(List<string> bufferNames)
        => throw new TODOException("MVPrimaryIndex.addBufferedRows");

    /// <summary>
    /// Reads the row stored under the given key, on the given transaction.
    /// </summary>
    /// <param name="transaction">the transaction to read on</param>
    /// <param name="key">the key</param>
    /// <returns>the row, or null when there is none</returns>
    public SearchRow? GetRowForKey(Transaction? transaction, long key)
    {
        return GetMap(transaction).Get(key);
    }

    /// <summary>
    /// Every row, in key order, on the given transaction.
    /// </summary>
    /// <remarks>
    /// In key order because that is the order the map stores them in. A scan that returned rows
    /// in any other order would answer a question with no ORDER BY differently from one call to
    /// the next, and nothing in the result would show it.
    /// </remarks>
    /// <param name="transaction">the transaction to read on</param>
    /// <returns>the rows</returns>
    public IEnumerable<SearchRow> Scan(Transaction? transaction)
    {
        foreach (KeyValuePair<long, SearchRow> entry in GetMap(transaction).EntrySet())
        {
            yield return entry.Value;
        }
    }

    /// <summary>
    /// A cursor over the rows from the first bound to the last, or over everything when no
    /// bounds were given.
    /// </summary>
    /// <remarks>
    /// The bounds resolve to keys the way the reference resolves them: no bound is open-ended,
    /// otherwise the key when the row id is indexed or the value is missing, and otherwise the
    /// long the value names, with unrepresentable and infinite values ending or emptying the
    /// walk as they do there. A lookup whose bounds meet on one key reads that one row.
    /// Reverse never arrives here — the table filter's cursor swaps the bounds instead.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="first">the first row, or null to start at the beginning</param>
    /// <param name="last">the last row, or null to read to the end</param>
    /// <returns>the cursor</returns>
    public override Indexes.Cursor? GetCursor(SessionLocal session, SearchRow? first, SearchRow? last)
    {
        long? min, max;
        Values.Value? v;
        if (first is null)
        {
            min = null;
        }
        else if (mainIndexColumn == SearchRow.RowidIndex || (v = first.GetValue(mainIndexColumn)) is null)
        {
            min = first.GetKey();
        }
        else
        {
            switch (v.GetValueType())
            {
                case Values.Value.Null:
                    return SingleRowCursor.Empty;
                case Values.Value.Real:
                case Values.Value.Double:
                    {
                        double d = v.GetDouble();
                        if (double.IsNaN(d))
                        {
                            return SingleRowCursor.Empty;
                        }

                        min = (long)d;
                        break;
                    }
                case Values.Value.Decfloat:
                    if (!((Values.ValueDecfloat)v).IsFinite())
                    {
                        if (ReferenceEquals(v, Values.ValueDecfloat.NegativeInfinity))
                        {
                            min = null;
                        }
                        else
                        {
                            return SingleRowCursor.Empty;
                        }

                        break;
                    }

                    goto case Values.Value.Numeric;
                case Values.Value.Numeric:
                    {
                        Util.BigDecimal bd = v.GetBigDecimal();
                        if (bd.CompareTo(Values.Value.MaxLongDecimal) > 0)
                        {
                            return SingleRowCursor.Empty;
                        }

                        if (bd.CompareTo(Values.Value.MinLongDecimal) < 0)
                        {
                            min = null;
                        }
                        else
                        {
                            min = bd.LongValue();
                        }

                        break;
                    }
                default:
                    min = v.GetLong();
                    break;
            }
        }

        if (last is null)
        {
            max = null;
        }
        else if (mainIndexColumn == SearchRow.RowidIndex || (v = last.GetValue(mainIndexColumn)) is null)
        {
            max = last.GetKey();
        }
        else
        {
            switch (v.GetValueType())
            {
                case Values.Value.Null:
                    return SingleRowCursor.Empty;
                case Values.Value.Real:
                case Values.Value.Double:
                    {
                        double d = v.GetDouble();
                        if (double.IsNaN(d))
                        {
                            max = null;
                        }
                        else
                        {
                            max = (long)d;
                        }

                        break;
                    }
                case Values.Value.Decfloat:
                    if (!((Values.ValueDecfloat)v).IsFinite())
                    {
                        if (ReferenceEquals(v, Values.ValueDecfloat.NegativeInfinity))
                        {
                            return SingleRowCursor.Empty;
                        }

                        max = null;
                        break;
                    }

                    goto case Values.Value.Numeric;
                case Values.Value.Numeric:
                    {
                        Util.BigDecimal bd = v.GetBigDecimal();
                        if (bd.CompareTo(Values.Value.MaxLongDecimal) > 0)
                        {
                            max = null;
                        }
                        else if (bd.CompareTo(Values.Value.MinLongDecimal) < 0)
                        {
                            return SingleRowCursor.Empty;
                        }
                        else
                        {
                            max = bd.LongValue();
                        }

                        break;
                    }
                default:
                    max = v.GetLong();
                    break;
            }
        }

        // A read opens the session's transaction on demand, as getInstance does behind
        // session.getTransaction there: the table's store is the session's store here.
        TransactionMap<long, SearchRow> map = GetMap(session.Transaction ??= mvTable.GetTransactionBegin());
        if (min.HasValue && max.HasValue && min.Value == max.Value)
        {
            return new SingleRowCursor(SetRowKey((Row)map.GetFromSnapshot(min.Value)!, min.Value));
        }

        return new MVStoreCursor(map.EntryIterator(min, max, false));
    }

    /// <summary>
    /// The row with the given key, or null when there is none.
    /// </summary>
    /// <remarks>
    /// Rows read back carry no key of their own until it is set: a row whose key is zero takes
    /// the key it was read under, which is how a stored row finds its identity again.
    /// </remarks>
    /// <param name="row">the row, or null</param>
    /// <param name="key">the key it was read under</param>
    /// <returns>the row</returns>
    private static Row? SetRowKey(Row? row, long key)
    {
        if (row is not null && row.GetKey() == 0)
        {
            row.SetKey(key);
        }

        return row;
    }

    /// <summary>
    /// A cursor over map entries in key order.
    /// </summary>
    private sealed class MVStoreCursor : Indexes.Cursor
    {
        private readonly TransactionMap<long, SearchRow>.TMIterator<long, SearchRow, KeyValuePair<long, SearchRow>> iterator;

        private KeyValuePair<long, SearchRow>? current;

        private Row? row;

        internal MVStoreCursor(
            TransactionMap<long, SearchRow>.TMIterator<long, SearchRow, KeyValuePair<long, SearchRow>> iterator)
            => this.iterator = iterator;

        /// <inheritdoc/>
        public Row? Get()
        {
            if (row is null && current.HasValue)
            {
                row = (Row)current.Value.Value;
                if (row.GetKey() == 0)
                {
                    row.SetKey(current.Value.Key);
                }
            }

            return row;
        }

        /// <inheritdoc/>
        public SearchRow? GetSearchRow() => Get();

        /// <inheritdoc/>
        public bool Next()
        {
            if (!iterator.TryFetchNext(out KeyValuePair<long, SearchRow> entry))
            {
                current = null;
                row = null;
                return false;
            }

            current = entry;
            row = null;
            return true;
        }

        /// <inheritdoc/>
        public bool Previous()
        {
            throw Message.DbException.GetUnsupportedException("previous");
        }
    }

    /// <summary>
    /// The number of rows in this index.
    /// </summary>
    /// <remarks>
    /// Counted on the given transaction rather than on a session, because a row count is asked
    /// of a table by anything that opened it and does not necessarily have a session; the
    /// reference reaches the same number through the database's own session.
    /// </remarks>
    /// <param name="transaction">the transaction to read on</param>
    /// <returns>the row count</returns>
    public long GetRowCount(Transaction transaction)
    {
        return dataMap.GetInstance(transaction).SizeAsLong();
    }

    /// <summary>
    /// The largest row count this index can report: the size of the underlying map.
    /// </summary>
    /// <returns>the row count maximum</returns>
    public long GetRowCountMax()
    {
        return dataMap.SizeAsLongMax();
    }

    /// <summary>
    /// The approximate row count, for the planner's pricing.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the row count maximum</returns>
    public long GetRowCountApproximation(SessionLocal session)
    {
        return GetRowCountMax();
    }

    private TransactionMap<long, SearchRow> GetMap(Transaction? transaction)
    {
        if (transaction is null)
        {
            throw new TODOException(
                "MVPrimaryIndex: the session has no transaction; a statement opens one before it writes");
        }

        return dataMap.GetInstance(transaction);
    }

    /// <summary>
    /// The last key of the underlying map, whatever version it belongs to. The map stores
    /// objects, so the key arrives boxed and has to be asked what it is; the counter resumes
    /// from it because a key that is still in flight is a key that will be taken.
    /// </summary>
    private static long? LastKeyOf(MVMap<object, VersionedValue<object>> map)
    {
        object? last = map.LastKey();
        return last switch
        {
            null => null,
            long l => l,
            _ => throw new TODOException("MVPrimaryIndex: the map's last key is not a long"),
        };
    }

    private StringBuilder GetDuplicatePrimaryKeyMessage(int mainIndexColumn)
    {
        return mainIndexColumn < 0
            ? new StringBuilder("Unique index or primary key violation:")
            : new StringBuilder("Unique index or primary key violation: \"")
                .Append(mvTable.GetTableName())
                .Append("_INDEX_")
                .Append(mainIndexColumn + 1);
    }
}
