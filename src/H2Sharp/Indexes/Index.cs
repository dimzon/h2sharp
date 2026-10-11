/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.index.Index.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A stub, and the whole class is one: this is the port's next working block, and the members
 * below refuse with TODOException because the table layer they stand on (Table, Column,
 * IndexColumn, SessionLocal) is not ported yet. Nothing here guesses behaviour — a guess here
 * would be a second engine wearing the reference's names, and the tests that are meant to guide
 * the port would pass against it.
 *
 * The naming is the collision PORTING.md warns about: a type Index inside a namespace
 * H2Sharp.Indexes. It is kept, because renaming the namespace breaks the mirror rule and renaming
 * the type breaks the 1:1 rule, and the mirror rule is the cheaper of the two to repair — a
 * using-alias at the use sites, which is what the rest of the port does for org.h2.store.FileStore.
 */
using System;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Util;

namespace H2Sharp.Indexes;

/// <summary>
/// An index on a table.
/// </summary>
public abstract class Index
{
    private readonly int id;

    private readonly Table? table;

    private readonly string name;

    private readonly IndexType? indexType;

    private bool created;

    /// <summary>
    /// Initializes the index.
    /// </summary>
    /// <param name="id">the object id</param>
    /// <param name="table">the table</param>
    /// <param name="name">the index name</param>
    /// <param name="indexType">the index type</param>
    protected Index(int id, Table? table, string name, IndexType? indexType)
    {
        this.id = id;
        this.table = table;
        this.name = name;
        this.indexType = indexType;
        created = true;
    }

    /// <summary>
    /// Gets the object id.
    /// </summary>
    public int GetId()
    {
        return id;
    }

    /// <summary>
    /// Gets the index name.
    /// </summary>
    public string GetName()
    {
        return name;
    }

    /// <summary>
    /// Gets the table this index is on.
    /// </summary>
    public Table? GetTable()
    {
        return table;
    }

    /// <summary>
    /// Gets the index type.
    /// </summary>
    public IndexType? GetIndexType()
    {
        return indexType;
    }

    /// <summary>
    /// Checks whether this index is created and initialized.
    /// </summary>
    public bool IsCreated()
    {
        return created;
    }

    /// <summary>
    /// Returns whether this index is on the given column.
    /// </summary>
    /// <param name="column">the column</param>
    /// <returns>true if it is</returns>
    public bool ContainsColumn(Column? column)
        => throw new TODOException("Index.ContainsColumn");

    /// <summary>
    /// Gets the number of columns in this index.
    /// </summary>
    public int GetColumnCount()
        => throw new TODOException("Index.GetColumnCount");

    /// <summary>
    /// Gets the columns of this index.
    /// </summary>
    public IndexColumn[] GetColumns()
        => throw new TODOException("Index.GetColumns");

    /// <summary>
    /// Gets the column at the given position.
    /// </summary>
    /// <param name="column">the column position</param>
    /// <returns>the column</returns>
    public IndexColumn GetColumn(int column)
        => throw new TODOException("Index.GetColumn");

    /// <summary>
    /// Gets the columns of this index that belong to the given table.
    /// </summary>
    /// <param name="refColumns">the reference for the result array</param>
    /// <param name="refCount">the reference for the result count</param>
    public void GetColumns(TableFilter?[] refColumns, int[] refCount)
        => throw new TODOException("Index.GetColumns");

    /// <summary>
    /// Gets the first column of this index that belongs to the given table.
    /// </summary>
    /// <param name="table">the table</param>
    /// <returns>the column, or null</returns>
    public Column? GetFirstColumn(Table? table)
        => throw new TODOException("Index.GetFirstColumn");

    /// <summary>
    /// Gets the number of rows in this index, or -1 when that is not known.
    /// </summary>
    public long GetRowCount()
        => throw new TODOException("Index.GetRowCount");

    /// <summary>
    /// Gets the row count estimate for this index.
    /// </summary>
    public long GetRowCountEstimate()
        => throw new TODOException("Index.GetRowCountEstimate");

    /// <summary>
    /// Gets the cost of an index lookup, in a form the query planner can compare.
    /// </summary>
    public long GetCostRangeIndex(MvccSnapshot? snapshot, IndexCondition? condition)
        => throw new TODOException("Index.GetCostRangeIndex");

    /// <summary>
    /// Finds a row in the index.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="row">the row to find</param>
    /// <returns>whether it was found</returns>
    public bool Find(H2Sharp.Engine.SessionLocal session, SearchRow row)
        => throw new TODOException("Index.Find");

    /// <summary>
    /// Adds a row to this index.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="row">the row to add</param>
    public void Add(H2Sharp.Engine.SessionLocal session, Row row)
        => throw new TODOException("Index.Add");

    /// <summary>
    /// Removes a row from this index.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="row">the row to remove</param>
    public void Remove(H2Sharp.Engine.SessionLocal session, Row row)
        => throw new TODOException("Index.Remove");

    /// <summary>
    /// Returns a cursor over this index, optionally starting at a row.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="first">the first row, or null to start at the beginning</param>
    /// <param name="last">the last row, or null to read to the end</param>
    /// <returns>the cursor</returns>
    public IndexCursor GetCursor(H2Sharp.Engine.SessionLocal session, SearchRow? first, SearchRow? last)
        => throw new TODOException("Index.GetCursor");

    /// <summary>
    /// Returns a cursor that writes its changes back to the index.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the cursor</returns>
    public IndexCursor GetWriteCursor(H2Sharp.Engine.SessionLocal session)
        => throw new TODOException("Index.GetWriteCursor");

    /// <summary>
    /// Closes this index and releases its resources.
    /// </summary>
    public virtual void Close()
    {
        created = false;
    }

    /// <summary>
    /// Renames this index.
    /// </summary>
    /// <param name="newName">the new name</param>
    public void Rename(StringBuilder newName)
        => throw new TODOException("Index.Rename");

    /// <summary>
    /// Gets the CREATE ... SQL statement for this index.
    /// </summary>
    public string GetCreateSql()
        => throw new TODOException("Index.GetCreateSQL");

    /// <summary>
    /// Gets the DROP ... SQL statement for this index.
    /// </summary>
    public string GetDropSql()
        => throw new TODOException("Index.GetDropSQL");

    /// <summary>
    /// Gets the SQL for this index, quoted with the given flags.
    /// </summary>
    /// <param name="sqlFlags">the SQL flags</param>
    /// <returns>the SQL</returns>
    public string GetSql(int sqlFlags)
        => throw new TODOException("Index.GetSQL");

    /// <summary>
    /// Checks if this index is a scan pseudo-index.
    /// </summary>
    public bool IsScan()
        => throw new TODOException("Index.IsScan");

    /// <summary>
    /// Checks whether this index needs to be rebuilt before use.
    /// </summary>
    /// <param name="session">the session</param>
    public void RebuildIfRequired(H2Sharp.Engine.SessionLocal session)
        => throw new TODOException("Index.RebuildIfRequired");
}
