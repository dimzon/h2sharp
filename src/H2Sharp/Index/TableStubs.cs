/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Stubs for the table layer, which the index layer stands on.
 *
 * These are declarations with no behaviour: every member refuses with TODOException, because
 * the members' answers are the table engine and inventing them here would produce a second
 * engine that passes the port's own tests. They exist so that IndexSort and IndexType — which
 * are real ports — have something to point at while the rest of the layer is ported.
 *
 * The types themselves are the reference's, with the reference's fields, so that a ported
 * subclass fits without reshaping.
 */
using System;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Values;

namespace H2Sharp.Index;

/// <summary>
/// A column of a table or an index.
/// </summary>
public class Column
{
    private readonly Table table;

    private readonly int columnId;

    private readonly string name;

    /// <summary>
    /// Creates a column.
    /// </summary>
    /// <param name="table">the table</param>
    /// <param name="columnId">the column id</param>
    /// <param name="name">the column name</param>
    protected Column(Table table, int columnId, string name)
    {
        this.table = table;
        this.columnId = columnId;
        this.name = name;
    }

    /// <summary>
    /// Gets the table this column belongs to.
    /// </summary>
    public Table GetTable()
    {
        return table;
    }

    /// <summary>
    /// Gets the column id.
    /// </summary>
    public int GetColumnId()
    {
        return columnId;
    }

    /// <summary>
    /// Gets the column name.
    /// </summary>
    public string GetName()
    {
        return name;
    }

    /// <summary>
    /// Gets the value type of this column.
    /// </summary>
    public int GetValueType()
        => throw new TODOException("Column.getValueType");

    /// <summary>
    /// Gets the type name of this column.
    /// </summary>
    public string GetTypeName()
        => throw new TODOException("Column.getTypeName");

    /// <summary>
    /// Gets the declared precision of this column.
    /// </summary>
    public int GetPrecision()
        => throw new TODOException("Column.getPrecision");

    /// <summary>
    /// Gets the declared scale of this column.
    /// </summary>
    public int GetScale()
        => throw new TODOException("Column.getScale");

    /// <summary>
    /// Whether this column may be null.
    /// </summary>
    public bool IsNullable()
        => throw new TODOException("Column.isNullable");

    /// <summary>
    /// Whether this column is part of the primary key.
    /// </summary>
    public bool IsPrimaryKey()
        => throw new TODOException("Column.isPrimaryKey");

    /// <summary>
    /// Renames this column.
    /// </summary>
    /// <param name="newName">the new name</param>
    public void Rename(StringBuilder newName)
        => throw new TODOException("Column.rename");

    /// <summary>
    /// Gets the SQL for this column.
    /// </summary>
    /// <param name="sqlFlags">the SQL flags</param>
    /// <returns>the SQL</returns>
    public string GetSql(int sqlFlags)
        => throw new TODOException("Column.getSQL");
}

/// <summary>
/// An index column: a column plus the sort direction it is read in.
/// </summary>
public class IndexColumn
{
    private readonly Column column;

    private readonly bool ascending;

    /// <summary>
    /// Creates an index column.
    /// </summary>
    /// <param name="column">the column</param>
    /// <param name="ascending">the sort direction</param>
    public IndexColumn(Column column, bool ascending)
    {
        this.column = column;
        this.ascending = ascending;
    }

    /// <summary>
    /// Gets the column.
    /// </summary>
    public Column GetColumn()
    {
        return column;
    }

    /// <summary>
    /// Gets the column name.
    /// </summary>
    public string GetName()
    {
        return column.GetName();
    }

    /// <summary>
    /// Whether this column is read in ascending order.
    /// </summary>
    public bool IsAscending()
    {
        return ascending;
    }

    /// <summary>
    /// Gets the sort order of this column as the query planner spells it.
    /// </summary>
    public int GetSortOrder()
    {
        return ascending ? ASCENDING : DESCENDING;
    }

    /// <summary>Ascending sort order.</summary>
    public const int ASCENDING = 0;

    /// <summary>Descending sort order.</summary>
    public const int DESCENDING = 1;
}

/// <summary>
/// A filter on a table, which is a table plus the state of the current row.
/// </summary>
public sealed class TableFilter
{
    private readonly Table table;

    /// <summary>
    /// Creates a table filter.
    /// </summary>
    /// <param name="table">the table</param>
    public TableFilter(Table table)
    {
        this.table = table;
    }

    /// <summary>
    /// Gets the table.
    /// </summary>
    public Table GetTable()
    {
        return table;
    }
}

/// <summary>
/// A cursor over an index.
/// </summary>
public abstract class IndexCursor
{
    /// <summary>
    /// Advances to the next row.
    /// </summary>
    public abstract void Next();

    /// <summary>
    /// Moves to the previous row.
    /// </summary>
    public abstract void Previous();

    /// <summary>
    /// Moves to the first row.
    /// </summary>
    public abstract void First();

    /// <summary>
    /// Moves to the last row.
    /// </summary>
    public abstract void Last();

    /// <summary>
    /// Whether there is a current row.
    /// </summary>
    public abstract bool HasNext();

    /// <summary>
    /// Gets the current row.
    /// </summary>
    public abstract Row GetRow();

    /// <summary>
    /// Inserts a row through this cursor.
    /// </summary>
    /// <param name="row">the row</param>
    public virtual void Add(Row row)
        => throw new TODOException("IndexCursor.add");

    /// <summary>
    /// Removes the current row through this cursor.
    /// </summary>
    public virtual void Remove()
        => throw new TODOException("IndexCursor.remove");
}

/// <summary>
/// A condition on an index, used to estimate the cost of a lookup.
/// </summary>
public sealed class IndexCondition
{
    /// <summary>
    /// Creates an index condition.
    /// </summary>
    /// <param name="column">the column</param>
    /// <param name="index">the index</param>
    public IndexCondition(Column column, Index index)
    {
        Column = column;
        Index = index;
    }

    /// <summary>Gets the column the condition is on.</summary>
    public Column Column { get; }

    /// <summary>Gets the index the condition applies to.</summary>
    public Index Index { get; }
}

/// <summary>
/// A snapshot of the visible rows, which is what a cost estimate is asked about.
/// </summary>
public interface MvccSnapshot
{
}

/// <summary>
/// A table of rows.
/// </summary>
public abstract class Table
{
    /// <summary>
    /// A regular table.
    /// </summary>
    public const int TABLE = 0;

    /// <summary>
    /// A table backed by an external table engine.
    /// </summary>
    public const int EXTERNAL_TABLE_ENGINE = 1;

    /// <summary>
    /// A materialized view.
    /// </summary>
    public const int MATERIALIZED_VIEW = 2;

    /// <summary>
    /// A global temporary table.
    /// </summary>
    public const int GLOBAL_TEMPORARY = 3;

    /// <summary>
    /// A local temporary table.
    /// </summary>
    public const int LOCAL_TEMPORARY = 4;

    /// <summary>
    /// Gets the table name.
    /// </summary>
    public abstract string GetTableName();

    /// <summary>
    /// Gets the table type.
    /// </summary>
    public abstract int GetTableType();

    /// <summary>
    /// Gets the number of columns.
    /// </summary>
    public abstract int GetColumnCount();

    /// <summary>
    /// Gets the column at the given index.
    /// </summary>
    /// <param name="index">the column index</param>
    /// <returns>the column</returns>
    public abstract Column GetColumn(int index);

    /// <summary>
    /// Gets all columns.
    /// </summary>
    /// <returns>the columns</returns>
    public abstract Column[] GetColumns();

    /// <summary>
    /// Gets the primary key index.
    /// </summary>
    public abstract Index? GetPrimaryKey();

    /// <summary>
    /// Gets the index at the given position.
    /// </summary>
    /// <param name="index">the index position</param>
    /// <returns>the index</returns>
    public virtual Index? GetIndex(int index)
        => throw new TODOException("Table.getIndex");

    /// <summary>
    /// Gets the number of indexes.
    /// </summary>
    public virtual int GetIndexCount()
        => throw new TODOException("Table.getIndexCount");

    /// <summary>
    /// Gets all indexes.
    /// </summary>
    /// <returns>the indexes</returns>
    public virtual Index[] GetIndexes()
        => throw new TODOException("Table.getIndexes");

    /// <summary>
    /// Finds the index usable for the given row.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="row">the row to search for</param>
    /// <returns>the index, or null</returns>
    public abstract Index? GetIndexFor(H2Sharp.Engine.SessionLocal session, SearchRow row);

    /// <summary>
    /// Gets the primary key columns.
    /// </summary>
    /// <returns>the columns</returns>
    public virtual IndexColumn[] GetPrimaryKeyColumns()
        => throw new TODOException("Table.getPrimaryKeyColumns");

    /// <summary>
    /// Whether this table has a primary key.
    /// </summary>
    public virtual bool HasPrimaryKey()
        => throw new TODOException("Table.hasPrimaryKey");

    /// <summary>
    /// Gets the row count, or -1 when not known.
    /// </summary>
    public abstract long GetRowCountApproximation();

    /// <summary>
    /// Gets the SQL statement that creates this table.
    /// </summary>
    public abstract string GetCreateSql();

    /// <summary>
    /// Gets the SQL for this table with the given flags.
    /// </summary>
    /// <param name="sqlFlags">the SQL flags</param>
    /// <returns>the SQL</returns>
    public abstract string GetSql(int sqlFlags);

    /// <summary>
    /// Drops all rows of this table.
    /// </summary>
    /// <param name="session">the session</param>
    public abstract void Truncate(H2Sharp.Engine.SessionLocal session);

    /// <summary>
    /// Removes all dependent objects and resources.
    /// </summary>
    /// <param name="session">the session</param>
    public abstract void RemoveChildrenAndResources(H2Sharp.Engine.SessionLocal session);

    /// <summary>
    /// Whether this table depends on the given object.
    /// </summary>
    /// <param name="other">the other object</param>
    /// <returns>true if it does</returns>
    public virtual bool DependsOn(object other)
        => throw new TODOException("Table.dependsOn");

    /// <summary>
    /// Closes this table and releases its resources.
    /// </summary>
    public abstract void Close();

    /// <summary>
    /// Checks whether this table is closed.
    /// </summary>
    /// <returns>true if it is</returns>
    public abstract bool IsClosed();
}
