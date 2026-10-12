/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.index.Index.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * An index. Indexes are used to speed up searching data.
 *
 * Partially ported, and the split is the reference's own: everything that describes an index —
 * its columns, their order, whether it is unique, the CREATE that builds it — is here, and
 * everything that moves data through it is left to the subclass and refuses here. That split is
 * what lets MVTable and MVPrimaryIndex be written against this class before either of them
 * exists.
 *
 * Two things are worth knowing about the unique columns. They are always the *first* columns of
 * the list, which is why the count alone is enough to find them; and the index carries two row
 * factories rather than one, because a unique index also needs to read rows in the order of its
 * unique columns alone, which for a composite index is a shorter key than the index itself.
 */
using System;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using H2Sharp.Util;

namespace H2Sharp.Indexes;

/// <summary>
/// An index. Indexes are used to speed up searching data.
/// </summary>
public abstract class Index : SchemaObject
{
    /// <summary>
    /// Check that the index columns are not CLOB or BLOB.
    /// </summary>
    /// <param name="columns">the columns</param>
    protected static void CheckIndexColumnTypes(IndexColumn[] columns)
    {
        foreach (IndexColumn c in columns)
        {
            if (!IsIndexable(c.GetColumn()))
            {
                throw DbException.GetUnsupportedException(
                    "Index on column: " + c.GetColumn().GetSql(HasSql.DefaultSqlFlags));
            }
        }
    }

    private static bool IsIndexable(Column column)
    {
        int type = column.GetValueType();
        return type is not Values.Value.Blob and not Values.Value.Clob and not Values.Value.Row and not Values.Value.JavaObject;
    }

    /// <summary>
    /// Columns of this index.
    /// </summary>
    protected IndexColumn[] indexColumns = [];

    /// <summary>
    /// Table columns used in this index.
    /// </summary>
    protected Column[] columns = [];

    /// <summary>
    /// Identities of table columns.
    /// </summary>
    protected int[] columnIds = [];

    /// <summary>
    /// Count of unique columns. Unique columns, if any, are always first columns
    /// in the lists.
    /// </summary>
    protected readonly int uniqueColumnColumn;

    /// <summary>
    /// The table.
    /// </summary>
    protected readonly Table? table;

    /// <summary>
    /// The index type.
    /// </summary>
    protected readonly IndexType? indexType;

    private readonly RowFactory? rowFactory;

    private readonly RowFactory? uniqueRowFactory;

    /// <summary>
    /// Initialize the index.
    /// </summary>
    /// <param name="newTable">the table</param>
    /// <param name="id">the object id</param>
    /// <param name="name">the index name</param>
    /// <param name="newIndexColumns">the columns that are indexed or null if this is not yet known</param>
    /// <param name="uniqueColumnCount">count of unique columns</param>
    /// <param name="newIndexType">the index type</param>
    protected Index(
        Table? newTable,
        int id,
        string name,
        IndexColumn[]? newIndexColumns,
        int uniqueColumnCount,
        IndexType? newIndexType)
        : base(newTable?.GetSchema() ?? throw new ArgumentNullException(nameof(newTable)), id, name, Trace.Index)
    {
        uniqueColumnColumn = uniqueColumnCount;
        indexType = newIndexType;
        table = newTable;
        if (newIndexColumns is not null)
        {
            indexColumns = newIndexColumns;
            columns = new Column[newIndexColumns.Length];
            columnIds = new int[columns.Length];
            for (int i = 0; i < columns.Length; i++)
            {
                Column col = newIndexColumns[i].GetColumn();
                columns[i] = col;
                columnIds[i] = col.GetColumnId();
            }
        }

        // The row factories need the database's compare mode and its table columns, and the
        // database is the one thing this constructor does not have yet: a schema in this port
        // does not know its database (Schema.GetDatabase exists but returns nothing usable yet).
        // They are filled in by the subclass once it has a session.
        rowFactory = null;
        uniqueRowFactory = null;
    }

    /// <summary>
    /// Gets the row factory that reads rows in the order of this index.
    /// </summary>
    protected RowFactory? GetRowFactory()
    {
        return rowFactory;
    }

    /// <summary>
    /// Gets the row factory that reads rows in the order of the unique columns alone.
    /// </summary>
    protected RowFactory? GetUniqueRowFactory()
    {
        return uniqueRowFactory;
    }

    /// <inheritdoc/>
    public override int GetTypeId()
    {
        return DbObject.Index;
    }

    /// <inheritdoc/>
    public override void RemoveChildrenAndResources(SessionLocal session)
    {
        table?.RemoveIndex(this);
        Remove(session);
        // database.removeMeta(session, getId());
    }

    /// <inheritdoc/>
    public override string GetCreateSql()
    {
        StringBuilder builder = new("CREATE ");
        _ = builder.Append(indexType is null ? "INDEX" : indexType.GetSql(true));
        _ = builder.Append(' ').Append(GetSql(HasSql.DefaultSqlFlags)).Append(" ON ");
        _ = table!.GetSql(builder, HasSql.DefaultSqlFlags);
        if (comment is not null)
        {
            _ = builder.Append(" COMMENT ");
            StringUtils.QuoteStringSql(builder, comment);
        }

        return GetColumnListSql(builder, HasSql.DefaultSqlFlags).ToString();
    }

    /// <summary>
    /// Get the list of columns as a string.
    /// </summary>
    /// <param name="builder">the builder</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the list of columns</returns>
    private StringBuilder GetColumnListSql(StringBuilder builder, int sqlFlags)
    {
        _ = builder.Append('(');
        int length = indexColumns.Length;
        if (uniqueColumnColumn > 0 && uniqueColumnColumn < length)
        {
            _ = IndexColumn.WriteColumns(builder, indexColumns, 0, uniqueColumnColumn, sqlFlags).Append(") INCLUDE(");
            _ = IndexColumn.WriteColumns(builder, indexColumns, uniqueColumnColumn, length, sqlFlags);
        }
        else
        {
            _ = IndexColumn.WriteColumns(builder, indexColumns, 0, length, sqlFlags);
        }

        return builder.Append(')');
    }

    /// <inheritdoc/>
    public override string? GetDropSql()
    {
        return "DROP INDEX " + GetSql(HasSql.DefaultSqlFlags);
    }

    /// <summary>
    /// Get the message to show in an EXPLAIN statement.
    /// </summary>
    /// <returns>the plan</returns>
    public virtual string GetPlanSql()
    {
        return GetSql(HasSql.TraceSqlFlags | HasSql.AddPlanInformation);
    }

    /// <summary>
    /// Gets the columns of this index.
    /// </summary>
    /// <returns>the columns</returns>
    public IndexColumn[] GetColumns()
    {
        return indexColumns;
    }

    /// <summary>
    /// Gets the table this index belongs to.
    /// </summary>
    /// <returns>the table</returns>
    public Table? GetTable() => table;

    /// <summary>
    /// Whether finding through this index always reads the whole table.
    /// </summary>
    /// <remarks>
    /// False, unless a subclass says otherwise. It is the planner's shortcut around an index
    /// that cannot serve a lookup: asking such an index for rows would scan the table once per
    /// condition, and the one scan the filter would have done anyway is cheaper than all of
    /// them.
    /// </remarks>
    /// <returns>true when every find is a full scan</returns>
    public virtual bool IsFindUsingFullTableScan() => false;

    /// <summary>
    /// Finds the position of a column in this index.
    /// </summary>
    /// <remarks>
    /// -1 when the column is not in the index, and that is not a failure to report: it is the
    /// answer the planner asks for, because an index that does not contain the column is an
    /// index that cannot serve the condition on it. Equals is on the table and the name, so a
    /// column that was created and later widened is still found.
    /// </remarks>
    /// <param name="col">the column</param>
    /// <returns>the position, or -1</returns>
    public int GetColumnIndex(Column col)
    {
        for (int i = 0, len = columns.Length; i < len; i++)
        {
            if (columns[i].Equals(col))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Gets the column at the given position.
    /// </summary>
    /// <param name="column">the column position</param>
    /// <returns>the column</returns>
    public IndexColumn GetColumn(int column)
    {
        return indexColumns[column];
    }

    /// <summary>
    /// Whether the column at the given position is read in ascending order.
    /// </summary>
    /// <param name="column">the column position</param>
    /// <returns>true if it is</returns>
    public bool IsColumnAscending(int column)
    {
        return indexColumns[column].IsAscending();
    }

    /// <summary>
    /// Gets the number of columns in this index.
    /// </summary>
    /// <returns>the number of columns</returns>
    public int GetColumnCount()
    {
        return indexColumns.Length;
    }

    /// <summary>
    /// Gets the first column of this index that belongs to the given table.
    /// </summary>
    /// <param name="table">the table</param>
    /// <returns>the column, or null</returns>
    public Column? GetFirstColumn(Table? table)
    {
        for (int i = 0; i < indexColumns.Length; i++)
        {
            if (indexColumns[i].GetColumn().GetTable() == table)
            {
                return indexColumns[i].GetColumn();
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the index type.
    /// </summary>
    /// <returns>the index type</returns>
    public IndexType? GetIndexType()
    {
        return indexType;
    }

    /// <summary>
    /// Whether this index is unique.
    /// </summary>
    /// <returns>true if it is</returns>
    public bool IsUnique()
    {
        return uniqueColumnColumn > 0 || (indexType is not null && indexType.IsUnique());
    }

    /// <summary>
    /// Whether this index is the primary key index of its table.
    /// </summary>
    /// <returns>true if it is</returns>
    public bool IsPrimaryKey()
    {
        return indexType is not null && indexType.IsPrimaryKey();
    }

    /// <summary>
    /// Whether this index is a table scan pseudo-index.
    /// </summary>
    /// <returns>true if it is</returns>
    public bool IsScan()
    {
        return indexType is not null && indexType.IsScan();
    }

    /// <summary>
    /// Gets the number of rows in this index, or -1 when that is not known.
    /// </summary>
    /// <returns>the row count</returns>
    public virtual long GetRowCount()
        => throw new TODOException("Index.getRowCount: the storage is not ported");

    /// <summary>
    /// The number of unique columns in this index.
    /// </summary>
    /// <returns>the count</returns>
    public int GetUniqueColumnCount()
    {
        return uniqueColumnColumn;
    }

    /// <summary>
    /// The columns this index is unique by.
    /// </summary>
    /// <returns>the columns, or an empty array</returns>
    public IndexColumn[] GetUniqueColumns()
    {
        if (uniqueColumnColumn == 0)
        {
            return [];
        }

        IndexColumn[] result = new IndexColumn[uniqueColumnColumn];
        Array.Copy(indexColumns, result, uniqueColumnColumn);
        return result;
    }

    /// <summary>
    /// Closes this index.
    /// </summary>
    /// <param name="session">the session used to write data</param>
    public abstract void Close(SessionLocal session);

    /// <summary>
    /// Adds a row to the index.
    /// </summary>
    /// <param name="session">the session to use</param>
    /// <param name="row">the row to add</param>
    public virtual void Add(SessionLocal session, Row row)
        => throw new TODOException("Index.add");

    /// <summary>
    /// Removes a row from the index.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="row">the row</param>
    public virtual void Remove(SessionLocal session, Row row)
        => throw new TODOException("Index.remove");

    /// <summary>
    /// Updates the index after a row change.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="oldRow">the row before the update</param>
    /// <param name="newRow">the row after the update</param>
    public virtual void Update(SessionLocal session, SearchRow oldRow, SearchRow newRow)
        => throw new TODOException("Index.update");

    /// <summary>
    /// Removes this index from the database.
    /// </summary>
    /// <param name="session">the session</param>
    public virtual void Remove(SessionLocal session)
        => throw new TODOException("Index.remove: the schema is not ported");

    /// <summary>
    /// Finds a row in this index.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="row">the row to find</param>
    /// <returns>whether it was found</returns>
    public virtual bool Find(SessionLocal session, SearchRow row)
        => throw new TODOException("Index.find: the storage is not ported");

    /// <summary>
    /// Returns a cursor over this index.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="first">the first row, or null to start at the beginning</param>
    /// <param name="last">the last row, or null to read to the end</param>
    /// <returns>the cursor</returns>
    public virtual IndexCursor? GetCursor(SessionLocal session, SearchRow? first, SearchRow? last)
        => throw new TODOException("Index.getCursor: the storage is not ported");

    /// <summary>
    /// Returns a cursor that writes its changes back to this index.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the cursor</returns>
    public virtual IndexCursor? GetWriteCursor(SessionLocal session)
        => throw new TODOException("Index.getWriteCursor: the storage is not ported");

    /// <summary>
    /// Truncates this index.
    /// </summary>
    /// <param name="session">the session</param>
    public virtual void Truncate(SessionLocal session)
        => throw new TODOException("Index.truncate: the storage is not ported");

    /// <summary>
    /// Renames this index.
    /// </summary>
    /// <param name="newName">the new name</param>
    public virtual void Rename(StringBuilder newName)
        => throw new TODOException("Index.rename");

    /// <summary>
    /// Reinitializes this index.
    /// </summary>
    /// <param name="session">the session</param>
    public virtual void Reinitialize()
        => throw new TODOException("Index.reinitialize");

    /// <summary>
    /// The columns of this index that belong to the given filters.
    /// </summary>
    private static void UnusedColumnsPlaceholder()
    {
    }
}
