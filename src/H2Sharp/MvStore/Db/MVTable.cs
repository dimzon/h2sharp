/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.mvstore.db.MVTable.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A table stored in a MVStore.
 *
 * Partially ported, and the part that is here is the part the statements need: the columns, the
 * rows, and the rule that hands out row keys.
 *
 * **The table does not have keys; the primary index does.** That is the reference's shape and it
 * is the reason the index is not an accessory here: the index opens the map, hands out the keys
 * in increasing order and never twice, and the table asks it. A table that numbered its own rows
 * would have two counters that could disagree, and the disagreement would surface as a duplicate
 * key error on an insert that had inserted nothing.
 *
 * **A row is stored by its values, and nothing else.** The key is outside the row, so a row read
 * back is exactly the values that were written — no key smuggled in, nothing to strip. The row
 * type is the reference's `RowDataType`, which reads a row that is narrower than the table (an
 * index stores only the columns it keys on) by setting only the positions it holds.
 *
 * What is not here and refuses: the meta table (`INFORMATION_SCHEMA`), scanning by index
 * condition, and the many column types beyond what the statements below need.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.MvStore.Tx;
using H2Sharp.Result;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;
using Index = H2Sharp.Indexes.Index;

namespace H2Sharp.MvStore.Db;

/// <summary>
/// A table stored in a MVStore.
/// </summary>
public sealed class MVTable : Table
{
    private readonly Column[] columns;

    private readonly MVPrimaryIndex primaryIndex;

    /// <summary>
    /// Every index of this table, the primary first.
    /// </summary>
    /// <remarks>
    /// In the reference this is a copy-on-write list the table hands out live; here the list
    /// stays inside and <see cref="GetIndexes"/> hands out a snapshot, because the port widened
    /// the answer to an array. Until <c>addIndex</c> is ported the constructor is the only
    /// writer, so the list holds the primary and nothing else.
    /// </remarks>
    private readonly List<Index> indexes;

    /// <summary>
    /// The id of the database state this table was last changed in.
    /// </summary>
    /// <remarks>
    /// In the reference this lives on DbObject; MVTable carries its own database here, so the
    /// member lives with it. Same null-tolerant body.
    /// </remarks>
    private long modificationId;

    /// <summary>
    /// Tell the table that it was modified.
    /// </summary>
    public override void SetModified()
    {
        modificationId = database is null ? -1 : database.GetNextModificationMetaId();
    }

    private readonly TransactionStore store;

    private readonly bool persistIndexes;

    private readonly bool persistData;

    private bool closed;

    /// <summary>
    /// Opens a table on the given store.
    /// </summary>
    /// <param name="database">the database the table belongs to</param>
    /// <param name="schema">the schema, when the table has one</param>
    /// <param name="id">the object id</param>
    /// <param name="name">the table name</param>
    /// <param name="columns">the columns, in order</param>
    /// <param name="persistIndexes">whether the indexes are persisted</param>
    /// <param name="persistData">whether the data is persisted</param>
    public MVTable(
        Engine.Database database,
        Schemas.Schema? schema,
        int id,
        string name,
        Column[] columns,
        bool persistIndexes,
        bool persistData)
    {
        this.database = database;
        Schema = schema;
        Id = id;
        Name = name;
        this.columns = columns;
        // The database's own store, as in the reference: a table on another store would be
        // invisible to every transaction but its own.
        store = database.GetStore();
        this.persistIndexes = persistIndexes;
        this.persistData = persistData;
        // The scan index is always a scan, and scan indexes are always persistent, as the
        // reference's MVTable constructor says with createScan(true). It is not a primary key
        // index: a table without a PRIMARY KEY has no key index at all, and FindPrimaryKey
        // relies on that to tell a keyed table from a keyless one.
        primaryIndex = new MVPrimaryIndex(null!, this, id, IndexColumn.Wrap(this.columns), IndexType.CreateScan(true));
        indexes = new List<Index> { primaryIndex };
    }

    /// <summary>Gets the schema of this table, if any.</summary>
    public Schemas.Schema? Schema { get; private set; }

    /// <summary>Gets the object id.</summary>
    public int Id { get; }

    /// <summary>Gets the table name.</summary>
    public string Name { get; }

    /// <summary>Whether the data is persisted; an in-memory table keeps it in memory.</summary>
    public bool IsPersistData => persistData;

    /// <inheritdoc/>
    public override bool IsPersistIndexes() => persistIndexes;

    /// <summary>Whether any column of this table holds a large object.</summary>
    public bool ContainsLargeObject => false;

    /// <summary>
    /// Gets the schema this table belongs to.
    /// </summary>
    /// <remarks>
    /// A table created without one is put in PUBLIC, which is what the reference does for the
    /// default schema: an object with no schema has to live somewhere, and the qualified names
    /// that reach a client have to name it.
    /// </remarks>
    /// <returns>the schema</returns>
    public override Schemas.Schema? GetSchema()
    {
        if (Schema is null)
        {
            if (database is null)
            {
                throw new TODOException("MVTable.getSchema: the table was created without a database");
            }

            Schema = new Schemas.Schema(database, 0, DefaultSchemaName, system: false);
        }

        return Schema;
    }

    /// <summary>The schema a table belongs to when it was created without one.</summary>
    public const string DefaultSchemaName = "PUBLIC";

    /// <summary>Gets the database this table belongs to.</summary>
    private Engine.Database? database;

    /// <inheritdoc/>
    public override string GetTableName()
    {
        return Name;
    }

    /// <inheritdoc/>
    public override int GetId()
    {
        return Id;
    }

    /// <inheritdoc/>
    public override int GetTableType()
    {
        return TABLE;
    }

    /// <inheritdoc/>
    public override int GetColumnCount()
    {
        return columns.Length;
    }

    /// <inheritdoc/>
    public override Column GetColumn(int index)
    {
        return columns[index];
    }

    /// <inheritdoc/>
    public override Column[] GetColumns()
    {
        return columns;
    }

    /// <inheritdoc/>
    public override string? GetSchemaName()
    {
        return Schema?.GetName();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The primary index, as in the reference. A full scan of this table walks the rows in
    /// key order, which is what the primary index keeps.
    /// </remarks>
    public override Index? GetScanIndex(SessionLocal session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return primaryIndex;
    }

    /// <inheritdoc/>
    public override int GetMainIndexColumn()
    {
        return primaryIndex.GetMainIndexColumn();
    }

    /// <inheritdoc/>
    public override Index? GetIndexFor(SessionLocal session, SearchRow row)
    {
        return primaryIndex;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A snapshot, not the live list: the port widened the reference's list answer to an
    /// array, and handing the list itself out would let callers add or drop indexes behind
    /// the table's back. The snapshot is cached and rebuilt when an index is added, so the
    /// planner stops paying a copy per candidate.
    /// </remarks>
    public override Index[] GetIndexes()
    {
        return indexCache ??= [.. indexes];
    }

    private Index[]? indexCache;

    /// <inheritdoc/>
    /// <remarks>
    /// The reference's body removes the index from the table's list and clears the primary-key
    /// flag on its columns. The list is the live one here, not the snapshot
    /// <see cref="GetIndexes"/> hands out, so the member lives where the list does; a snapshot
    /// taken before this call is rebuilt on the next one.
    /// </remarks>
    public override void RemoveIndex(Index index)
    {
        _ = indexes.Remove(index);
        indexCache = null;
        if (index.GetIndexType()!.IsPrimaryKey())
        {
            foreach (IndexColumn col in index.GetColumns())
            {
                col.GetColumn().SetPrimaryKey(false);
            }
        }
    }

    /// <summary>
    /// Gets the row factory that reads this table's rows.
    /// </summary>
    /// <remarks>
    /// One per table: the column count never changes after construction, and the factory
    /// itself holds no state, so sharing it is safe and a second instance would only be
    /// garbage.
    /// </remarks>
    public RowFactory GetRowFactory()
    {
        return rowFactory ??= new TableRowFactory(columns.Length);
    }

    private RowFactory? rowFactory;

    /// <summary>
    /// Adds an index to this table: a delegate over the primary for a single integer key,
    /// an index of its own for everything else.
    /// </summary>
    /// <remarks>
    /// The branches are the reference's. The spatial branch refuses: naming it needs the
    /// spatial key type, which is not ported, and a class with invented type arguments would
    /// be worse than an honest refusal. The schema half after a built index — the comment and
    /// the registry write — runs only for tables the reference itself does not exempt, and
    /// waits for the meta table with them. Rebuilding a non-empty table waits for the buffered
    /// and block-merge builders below.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="indexName">the index name</param>
    /// <param name="indexId">the object id</param>
    /// <param name="cols">the columns that are indexed</param>
    /// <param name="uniqueColumnCount">count of unique columns</param>
    /// <param name="indexType">the index type</param>
    /// <param name="create">whether this call creates the index</param>
    /// <param name="indexComment">the comment, or null</param>
    /// <returns>the added index</returns>
    public override Index AddIndex(
        SessionLocal session,
        string? indexName,
        int indexId,
        IndexColumn[] cols,
        int uniqueColumnCount,
        IndexType indexType,
        bool create,
        string? indexComment)
    {
        ArgumentNullException.ThrowIfNull(database);
        cols = PrepareColumns(database, cols, indexType);
        bool isSessionTemporary = IsTemporary() && !IsGlobalTemporary();
        if (!isSessionTemporary)
        {
            database.LockMeta(session);
        }
        Index index;
        int mainIndexColumn = primaryIndex.GetMainIndexColumn() != SearchRow.RowidIndex
            ? SearchRow.RowidIndex : GetMainIndexColumn(indexType, cols);
        if (database.IsStarting())
        {
            // if index does exist as a separate map it can't be a delegate
            if (store.HasMap("index." + indexId))
            {
                // we can not reuse primary index
                mainIndexColumn = SearchRow.RowidIndex;
            }
        }
        else if (primaryIndex.GetRowCountMax() != 0)
        {
            mainIndexColumn = SearchRow.RowidIndex;
        }

        if (mainIndexColumn != SearchRow.RowidIndex)
        {
            primaryIndex.SetMainIndexColumn(mainIndexColumn);
            index = new MVDelegateIndex(this, indexId, indexName, primaryIndex, indexType);
        }
        else if (indexType.IsSpatial())
        {
            throw new TODOException("MVTable.addIndex: spatial indexes need the spatial key type");
        }
        else
        {
            index = new MVSecondaryIndex(session.GetDatabase(), this, indexId,
                indexName, cols, uniqueColumnCount, indexType);
        }
        if (index.NeedRebuild())
        {
            RebuildIndex(session, index, indexName);
        }
        index.SetTemporary(IsTemporary());
        if (Id != 0 && index.GetCreateSql() != null)
        {
            index.SetComment(indexComment);
            if (isSessionTemporary)
            {
                session.AddLocalTempTableIndex(index);
            }
            else
            {
                database.AddSchemaObject(session, index);
            }
        }
        indexes.Add(index);
        indexCache = null;
        SetModified();
        return index;
    }

    /// <summary>
    /// Prepares the index columns: primary-key columns must not be nullable, and plain columns
    /// gain the default's explicit null ordering.
    /// </summary>
    /// <remarks>
    /// Nullability here is three-state, and anything but definitely-not-nullable is rejected:
    /// a primary key over a column that may hold NULL is not a key. The rewritten column keeps
    /// its resolved column and only trades the mask, because nameless columns do not exist here.
    /// The null-ordering rule lives on DefaultNullOrderingRules rather than the ordering enum;
    /// same pure function, only the shelf moved.
    /// </remarks>
    /// <param name="database">the database</param>
    /// <param name="cols">the columns</param>
    /// <param name="indexType">the index type</param>
    /// <returns>the prepared columns</returns>
    private static IndexColumn[] PrepareColumns(
        Engine.Database database,
        IndexColumn[] cols,
        IndexType indexType)
    {
        if (indexType.IsPrimaryKey())
        {
            foreach (IndexColumn c in cols)
            {
                Column column = c.GetColumn();
                if (column.IsNullable() != Column.NotNullable)
                {
                    throw Message.DbException.Get(Api.ErrorCode.ColumnMustNotBeNullable1, column.GetName());
                }
            }
            foreach (IndexColumn c in cols)
            {
                c.GetColumn().SetPrimaryKey(true);
            }
        }
        else if (!indexType.IsSpatial())
        {
            int i = 0, l = cols.Length;
            while (i < l && (cols[i].SortType & (SortOrder.NullsFirst | SortOrder.NullsLast)) != 0)
            {
                i++;
            }
            if (i != l)
            {
                cols = (IndexColumn[])cols.Clone();
                DefaultNullOrdering defaultNullOrdering = database.GetDefaultNullOrdering();
                for (; i < l; i++)
                {
                    IndexColumn oldColumn = cols[i];
                    int sortTypeOld = oldColumn.SortType;
                    int sortTypeNew = DefaultNullOrderingRules.AddExplicitNullOrdering(defaultNullOrdering, sortTypeOld);
                    if (sortTypeNew != sortTypeOld)
                    {
                        IndexColumn newColumn = new IndexColumn(oldColumn.GetColumn(), sortTypeNew);
                        cols[i] = newColumn;
                    }
                }
            }
        }
        return cols;
    }

    /// <summary>
    /// Rebuilds the index from the table's rows, removing the half-built index when it fails.
    /// </summary>
    /// <remarks>
    /// The cleanup is thinner than the reference's: freeing the reserved name waits for the
    /// schema's name registry, and the failure log waits for tracing. What stays is the shape
    /// that matters — a failed rebuild leaves no half index behind and the failure travels on.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="index">the index</param>
    /// <param name="indexName">the index name</param>
    private void RebuildIndex(SessionLocal session, Index index, string? indexName)
    {
        try
        {
            if (!session.GetDatabase().IsPersistent() || index is MVSecondaryIndex)
            {
                // in-memory
                RebuildIndexBuffered(session, index);
            }
            else
            {
                RebuildIndexBlockMerge(session, index);
            }
        }
        catch (H2SharpException)
        {
            try
            {
                index.Remove(session);
            }
            catch (H2SharpException)
            {
                // this could happen, for example on failure in the storage
                // but if that is not the case it means
                // there is something wrong with the database
                throw;
            }
            throw;
        }
    }

    /// <summary>
    /// Rebuilds the index by buffering the rows first.
    /// </summary>
    /// <remarks>
    /// Not ported. It sorts the rows into temporary buffers and merges them into the index,
    /// which needs row cursors over the table and the buffer lifecycle — both unported.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="index">the index</param>
    private void RebuildIndexBuffered(SessionLocal session, Index index)
        => throw new TODOException("MVTable.rebuildIndexBuffered");

    /// <summary>
    /// Rebuilds the index by merging sorted blocks.
    /// </summary>
    /// <remarks>
    /// Not ported. It is the persistent twin of the buffered rebuild, with the same needs.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="index">the index</param>
    private void RebuildIndexBlockMerge(SessionLocal session, Index index)
        => throw new TODOException("MVTable.rebuildIndexBlockMerge");

    /// <summary>
    /// Begins a transaction on the store this table's rows live in.
    /// </summary>
    public Transaction? GetTransactionBegin()
    {
        return store.Begin();
    }

    /// <summary>
    /// Converts a store failure into the error a caller expects.
    /// </summary>
    /// <remarks>
    /// The reference rethrows an MVStore failure as the database-level error with the file and
    /// the key in it, because "the page could not be written" is not what a user can act on.
    /// </remarks>
    /// <param name="e">the store failure</param>
    /// <returns>the error to raise</returns>
    public Exception ConvertException(Exception e)
        => DbException.GetInternalError(Name + ": " + e.Message);

    /// <summary>
    /// Adds a row and returns the key it was given.
    /// </summary>
    /// <remarks>
    /// The key comes from the primary index and not from here — see the file header. The value
    /// that goes in the map is the row itself, so a row read back is exactly the values that
    /// were written.
    /// <para>
    /// The transaction is opened here and put on the session for the duration, which is the
    /// reference's arrangement: the session carries the transaction a statement runs on, and a
    /// caller that took one of its own would be writing on a transaction the statement cannot see.
    /// </para>
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="row">the row to add</param>
    /// <returns>the key</returns>
    public override long AddRow(SessionLocal session, Row row)
    {
        ArgumentNullException.ThrowIfNull(row);
        MvStore.Tx.Transaction transaction = session.GetTransaction();
        long savepoint = transaction.SetSavepoint();
        try
        {
            primaryIndex.Add(session, row);
            return row.GetKey();
        }
        catch (Exception e)
        {
            RollbackSavepoint(transaction, savepoint);
            throw Message.DbException.Convert(e);
        }
    }

    /// <summary>
    /// Removes the row with the given key.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="key">the key</param>
    public override void RemoveRow(SessionLocal session, long key)
    {
        MvStore.Tx.Transaction transaction = session.GetTransaction();
        long savepoint = transaction.SetSavepoint();
        try
        {
            SearchRow? existing = primaryIndex.GetRowForKey(transaction, key);
            if (existing is not Row row)
            {
                throw DbException.Get(ErrorCode.RowNotFoundWhenDeleting1, Name + ": " + key);
            }

            primaryIndex.Remove(session, row);
        }
        catch (Exception e)
        {
            RollbackSavepoint(transaction, savepoint);
            throw Message.DbException.Convert(e);
        }
    }

    /// <summary>
    /// Replaces the row with the same key.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="oldRow">the row as it was</param>
    /// <param name="newRow">the row as it is now</param>
    public override void UpdateRow(SessionLocal session, SearchRow oldRow, SearchRow newRow)
    {
        MvStore.Tx.Transaction transaction = session.GetTransaction();
        long savepoint = transaction.SetSavepoint();
        try
        {
            primaryIndex.Update(session, oldRow, newRow);
        }
        catch (Exception e)
        {
            RollbackSavepoint(transaction, savepoint);
            throw Message.DbException.Convert(e);
        }
    }

    /// <summary>
    /// Reads the row with the given key.
    /// </summary>
    /// <remarks>
    /// A read is a statement's worth of work too, so it runs in a transaction like the writes
    /// do. Opening none would mean reading on a snapshot of nothing, and the row could come from
    /// a state no statement ever committed.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="key">the key</param>
    /// <returns>the row, or null</returns>
    public SearchRow? GetRow(SessionLocal session, long key)
        => primaryIndex.GetRowForKey(session.GetTransaction(), key);

    /// <summary>
    /// Every row of this table, in key order.
    /// </summary>
    /// <remarks>
    /// In key order because that is the order the index stores them in, and a scan that returned
    /// rows in a different order would give a different answer to a question with no ORDER BY —
    /// which is not something a caller can detect.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <returns>the rows</returns>
    public List<SearchRow> Scan(SessionLocal session)
        => [.. primaryIndex.Scan(session.GetTransaction())];

    /// <summary>
    /// Rolls a transaction back to a savepoint, swallowing a failure to roll back.
    /// </summary>
    /// <remarks>
    /// The reference adds the nested failure as suppressed; a plain .NET exception carries
    /// none, so a failure to roll back is dropped rather than invented.
    /// </remarks>
    /// <param name="transaction">the transaction</param>
    /// <param name="savepoint">the savepoint to roll back to</param>
    private static void RollbackSavepoint(MvStore.Tx.Transaction transaction, long savepoint)
    {
        try
        {
            transaction.RollbackToSavepoint(savepoint);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Gets the transaction store this table's rows live in.
    /// </summary>
    public TransactionStore GetStore()
    {
        return store;
    }

    /// <inheritdoc/>
    public override long GetRowCountApproximation(SessionLocal session)
        => primaryIndex.GetRowCount(session.GetTransaction());

    /// <inheritdoc/>
    public override bool CanGetRowCount(SessionLocal session) => true;

    /// <inheritdoc/>
    public override long GetRowCount(SessionLocal session)
    {
        // The session transaction, not a fresh one: a count taken on another transaction would
        // not see this statement's own uncommitted rows.
        Transaction t = session.GetTransaction();
        return primaryIndex.GetRowCount(t);
    }

    /// <inheritdoc/>
    public override string? GetCreateSql()
    {
        StringBuilder builder = new("CREATE TABLE ");
        _ = ParserUtil.QuoteIdentifier(builder, Name, HasSql.DefaultSqlFlags);
        _ = builder.Append("(");
        for (int i = 0; i < columns.Length; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            _ = builder.Append(columns[i].GetCreateSql());
        }

        return builder.Append(')').ToString();
    }

    /// <inheritdoc/>
    public override string GetSql(int sqlFlags)
    {
        return GetSql(new StringBuilder(), sqlFlags).ToString();
    }

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        string? schemaName = GetSchemaName();
        if (schemaName is not null)
        {
            _ = ParserUtil.QuoteIdentifier(builder, schemaName, sqlFlags).Append('.');
        }

        return ParserUtil.QuoteIdentifier(builder, Name, sqlFlags);
    }

    /// <inheritdoc/>
    public override void Truncate(SessionLocal session)
    {
        primaryIndex.Truncate(session);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The reference body starts with the table's LOB storage and ends with the base's
    /// children — dependent views, synonyms, triggers, constraints, rights and sequences — and
    /// none of those exist here yet, so what runs is the middle: every index but the scan is
    /// removed, and each one that has a name is removed from the schema with it. That schema
    /// removal is not bookkeeping: it releases the name, which is what lets a table dropped and
    /// created again receive the same PRIMARY_KEY_ name, as it does on the JVM.
    /// The scan index is closed last, as in the reference; removing its map is deferred with
    /// the transaction store's removeTable.
    /// </remarks>
    public override void RemoveChildrenAndResources(SessionLocal session)
    {
        // remove scan index (at position 0 on the list) last
        while (indexes.Count > 1)
        {
            Index index = indexes[1];
            index.Remove(session);
            if (index.GetName() is not null)
            {
                database!.RemoveSchemaObject(session, index);
            }

            // needed for session temporary indexes
            _ = indexes.Remove(index);
        }

        indexes.Clear();
        Close();
    }

    /// <inheritdoc/>
    public override void Close()
    {
        closed = true;
    }

    /// <inheritdoc/>
    public override bool IsClosed()
    {
        return closed;
    }

    /// <summary>
    /// The row factory of one table: rows with exactly as many columns as the table has.
    /// </summary>
    /// <remarks>
    /// A distinct type per table rather than one shared, because the row a reader must build
    /// depends on the table: a row of an index has fewer columns than the table it belongs to,
    /// and a factory that guessed the width would fill the wrong number of values.
    /// </remarks>
    private sealed class TableRowFactory : RowFactory
    {
        private readonly int columnCount;

        internal TableRowFactory(int columnCount)
        {
            this.columnCount = columnCount;
        }

        public override Row CreateRow(Value[] data, int memory) => new DefaultRow(data, memory);

        public override SearchRow CreateRow() => new DefaultRow(columnCount);

        public override RowDataType GetRowDataType()
            => rowDataType ??= new(null, CompareMode.GetInstance(null, 0), null, null, null, columnCount, true);

        private RowDataType? rowDataType;

        public override int[]? GetIndexes() => null;

        public override TypeInfo[]? GetColumnTypes() => null;

        public override int GetColumnCount() => columnCount;
    }
}
