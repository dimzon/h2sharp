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

    private readonly TransactionStore store;

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
    /// <param name="store">the transaction store the rows live in</param>
    /// <param name="persistData">whether the data is persisted</param>
    public MVTable(
        Engine.Database database,
        Schemas.Schema? schema,
        int id,
        string name,
        Column[] columns,
        TransactionStore store,
        bool persistData)
    {
        this.database = database;
        Schema = schema;
        Id = id;
        Name = name;
        this.columns = columns;
        this.store = store;
        this.persistData = persistData;
        primaryIndex = new MVPrimaryIndex(null!, this, id, null, IndexType.CreatePrimaryKey(persistData, false));
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
    public override Index? GetPrimaryKey()
    {
        return primaryIndex;
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
    public override Index? GetIndexFor(SessionLocal session, SearchRow row)
    {
        return primaryIndex;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A snapshot, not the live list: the port widened the reference's list answer to an
    /// array, and handing the list itself out would let callers add or drop indexes behind
    /// the table's back. See <see cref="indexes"/> for who writes it.
    /// </remarks>
    public override Index[] GetIndexes()
    {
        return indexes.ToArray();
    }

    /// <summary>
    /// Gets the row factory that reads this table's rows.
    /// </summary>
    public RowFactory GetRowFactory()
    {
        return new TableRowFactory(columns.Length);
    }

    /// <summary>
    /// Gets the type of this table's rows: the reference's, so that what is written to a page
    /// is the format the reference writes.
    /// </summary>
    public RowDataType GetRowDataType()
    {
        return new RowDataType(null, CompareMode.GetInstance(null, 0), null, null, null, columns.Length, true);
    }

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
    public long AddRow(SessionLocal session, Row row)
    {
        ArgumentNullException.ThrowIfNull(row);
        using TransactionScope scope = new(this, session);
        primaryIndex.Add(session, row);
        return row.GetKey();
    }

    /// <summary>
    /// Removes the row with the given key.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="key">the key</param>
    public void RemoveRow(SessionLocal session, long key)
    {
        using TransactionScope scope = new(this, session);
        SearchRow? existing = primaryIndex.GetRowForKey(scope.Transaction, key);
        if (existing is not Row row)
        {
            throw DbException.Get(ErrorCode.RowNotFoundWhenDeleting1, Name + ": " + key);
        }

        primaryIndex.Remove(session, row);
    }

    /// <summary>
    /// Replaces the row with the same key.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="oldRow">the row as it was</param>
    /// <param name="newRow">the row as it is now</param>
    public void UpdateRow(SessionLocal session, SearchRow oldRow, SearchRow newRow)
    {
        using TransactionScope scope = new(this, session);
        primaryIndex.Update(session, oldRow, newRow);
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
    {
        using TransactionScope scope = new(this, session);
        return primaryIndex.GetRowForKey(scope.Transaction, key);
    }

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
    {
        using TransactionScope scope = new(this, session);
        return [.. primaryIndex.Scan(scope.Transaction)];
    }

    /// <summary>
    /// Runs one statement's worth of work in a transaction of this table's store, on the session.
    /// </summary>
    /// <remarks>
    /// A statement is one transaction: it either is in the map or is not. The scope commits on
    /// the way out whether the work succeeded or threw, because a transaction that was rolled back
    /// after an exception and one that was left open both make the next statement see a different
    /// table than the last one did.
    /// </remarks>
    private sealed class TransactionScope : IDisposable
    {
        private readonly SessionLocal session;

        private readonly MVTable.TransactionScope? previous;

        private readonly Transaction transaction;

        internal TransactionScope(MVTable table, SessionLocal session)
        {
            this.session = session;
            previous = session.TransactionScope as MVTable.TransactionScope;
            transaction = table.store.Begin();
            session.Transaction = transaction;
            session.TransactionScope = this;
        }

        /// <summary>The transaction this scope opened.</summary>
        internal Transaction Transaction => transaction;

        public void Dispose()
        {
            // The previous scope is restored as well as the transaction, so that work nested on
            // this session keeps its own transaction instead of the one being closed.
            session.Transaction = previous?.Transaction;
            session.TransactionScope = previous;
            transaction.Commit();
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
    public override long GetRowCountApproximation()
    {
        Transaction? t = GetTransactionBegin();
        if (t is null)
        {
            return 0;
        }

        long count = primaryIndex.GetRowCount(t);
        t.Commit();
        return count;
    }

    /// <inheritdoc/>
    public override string GetCreateSql()
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
    public override void RemoveChildrenAndResources(SessionLocal session)
    {
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
            => new(null, CompareMode.GetInstance(null, 0), null, null, null, columnCount, true);

        public override int[]? GetIndexes() => null;

        public override TypeInfo[]? GetColumnTypes() => null;

        public override int GetColumnCount() => columnCount;
    }
}
