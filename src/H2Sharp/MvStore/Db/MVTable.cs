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
 * A stub: the members the primary index stands on exist and refuse, because a table that
 * answered them would be a table that stored rows somewhere other than an MVStore — and the
 * index is written against a real one. What is here is the *shape*: which members exist, what
 * they mean, and the two rules the index depends on.
 *
 * The first rule is that a table has no row keys of its own — the primary index hands them out.
 * The second is that a table which does not persist its data keeps it in memory, and the index
 * has to know which it is before it opens its map, because that is what decides whether the map
 * is marked volatile.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.MvStore.Tx;
using H2Sharp.Result;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using H2Sharp.Util;
using Index = H2Sharp.Indexes.Index;

namespace H2Sharp.MvStore.Db;

/// <summary>
/// A table stored in a MVStore.
/// </summary>
public sealed class MVTable : Table
{
    /// <summary>
    /// Creates a table.
    /// </summary>
    /// <param name="schema">the schema</param>
    /// <param name="id">the object id</param>
    /// <param name="name">the table name</param>
    /// <param name="persistData">whether the data is persisted</param>
    public MVTable(Schemas.Schema? schema, int id, string name, bool persistData)
    {
        Schema = schema;
        Id = id;
        Name = name;
        PersistData = persistData;
    }

    /// <summary>Gets the schema of this table, if any.</summary>
    public Schemas.Schema? Schema { get; }

    /// <summary>Gets the object id.</summary>
    public int Id { get; }

    /// <summary>Gets the table name.</summary>
    public string Name { get; }

    /// <summary>Whether the data is persisted; an in-memory table keeps it in memory.</summary>
    public bool PersistData { get; }

    /// <summary>Whether any column of this table holds a large object.</summary>
    public bool ContainsLargeObject
        => throw new TODOException("MVTable.getContainsLargeObject");

    /// <summary>
    /// Gets the row factory that reads the rows of this table.
    /// </summary>
    public RowFactory GetRowFactory()
        => throw new TODOException("MVTable.getRowFactory");

    /// <summary>
    /// Begins a transaction on this table's store.
    /// </summary>
    public Transaction? GetTransactionBegin()
        => throw new TODOException("MVTable.getTransactionBegin");

    /// <summary>
    /// Converts a store failure into the error the caller expects.
    /// </summary>
    /// <param name="e">the store failure</param>
    public Exception ConvertException(Exception e)
        => throw new TODOException("MVTable.convertException: " + e.Message);

    /// <inheritdoc/>
    public override string GetTableName()
    {
        return Name;
    }

    /// <inheritdoc/>
    public override int GetTableType()
    {
        return TABLE;
    }

    /// <inheritdoc/>
    public override int GetColumnCount()
        => throw new TODOException("MVTable.getColumnCount");

    /// <inheritdoc/>
    public override Column GetColumn(int index)
        => throw new TODOException("MVTable.getColumn");

    /// <inheritdoc/>
    public override Column[] GetColumns()
        => throw new TODOException("MVTable.getColumns");

    /// <inheritdoc/>
    public override H2Sharp.Indexes.Index? GetPrimaryKey()
        => throw new TODOException("MVTable.getPrimaryKey");

    /// <inheritdoc/>
    public override H2Sharp.Indexes.Index? GetIndexFor(SessionLocal session, SearchRow row)
        => throw new TODOException("MVTable.getIndexFor");

    /// <inheritdoc/>
    public override long GetRowCountApproximation()
        => throw new TODOException("MVTable.getRowCountApproximation");

    /// <inheritdoc/>
    public override string GetCreateSql()
        => throw new TODOException("MVTable.getCreateSQL");

    /// <inheritdoc/>
    public override string GetSql(int sqlFlags)
        => throw new TODOException("MVTable.getSQL");

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => throw new TODOException("MVTable.getSQL");

    /// <inheritdoc/>
    public override void Truncate(SessionLocal session)
        => throw new TODOException("MVTable.truncate");

    /// <inheritdoc/>
    public override void RemoveChildrenAndResources(SessionLocal session)
        => throw new TODOException("MVTable.removeChildrenAndResources");

    /// <inheritdoc/>
    public override void Close()
    {
    }

    /// <inheritdoc/>
    public override bool IsClosed()
    {
        return false;
    }

    /// <inheritdoc/>
    public override string? GetSchemaName()
        => Schema?.GetName();
}
