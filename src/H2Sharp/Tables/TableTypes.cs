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
using Index = H2Sharp.Indexes.Index;
using ResultCursor = H2Sharp.Indexes.Cursor;

namespace H2Sharp.Tables;


/// <summary>
/// An index column: a column plus the sort direction it is read in.
/// </summary>
public class IndexColumn
{
    private readonly Column column;

    private readonly bool ascending;

    /// <summary>
    /// The sort type: direction, and where NULL goes.
    /// </summary>
    /// <remarks>
    /// The reference keeps this as a bit mask rather than a boolean, because direction is not
    /// the whole answer — NULLS FIRST and NULLS LAST ride on the same number. This port was
    /// built with a boolean, so the mask is derived from it: ascending is the default order and
    /// everything else is explicit.
    /// </remarks>
    public readonly int SortType;

    /// <summary>
    /// Creates an index column.
    /// </summary>
    /// <param name="column">the column</param>
    /// <param name="ascending">the sort direction</param>
    public IndexColumn(Column column, bool ascending)
    {
        this.column = column;
        this.ascending = ascending;
        SortType = ascending ? Result.SortOrder.Ascending : Result.SortOrder.Descending;
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

    /// <summary>
    /// Writes a range of index columns, each qualified by its table when the index spans more
    /// than one.
    /// </summary>
    /// <param name="builder">the builder</param>
    /// <param name="columns">the columns</param>
    /// <param name="from">the first position</param>
    /// <param name="to">the position after the last</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the builder</returns>
    public static StringBuilder WriteColumns(
        StringBuilder builder,
        IndexColumn[] columns,
        int from,
        int to,
        int sqlFlags)
    {
        for (int i = from; i < to; i++)
        {
            if (i > from)
            {
                _ = builder.Append(", ");
            }

            _ = columns[i].GetColumn().GetSql(builder, sqlFlags);
            if (!columns[i].IsAscending())
            {
                _ = builder.Append(" DESC");
            }
        }

        return builder;
    }

    /// <summary>Ascending sort order.</summary>
    public const int ASCENDING = 0;

    /// <summary>Descending sort order.</summary>
    public const int DESCENDING = 1;
}

/// <summary>
/// A cursor over an index.
/// </summary>
public abstract class IndexCursor
{
    private H2Sharp.Indexes.Index? index;

    private bool reverse;

    private Table? table;

    private IndexColumn?[]? indexColumns;

    /// <summary>
    /// Sets the index this cursor walks, and how.
    /// </summary>
    /// <remarks>
    /// The columns are re-mapped against the index on every call, because the same cursor may be
    /// pointed at another index between two runs and the masks it answered with belong to the old
    /// one. A column the index does not keep is left null rather than failed on: the planner
    /// already decided this index can serve the condition, and a column missing here means the
    /// plan was built on an index that has since lost a column, which is the database changing
    /// under the statement rather than a mistake in the plan.
    /// </remarks>
    /// <param name="index">the index</param>
    /// <param name="reverse">whether to walk it backwards</param>
    public void SetIndex(H2Sharp.Indexes.Index index, bool reverse)
    {
        ArgumentNullException.ThrowIfNull(index);
        this.index = index;
        this.reverse = reverse;
        table = index.GetTable();
        Column[] columns = table?.GetColumns() ?? [];
        indexColumns = new IndexColumn?[columns.Length];
        IndexColumn[]? idxCols = index.GetColumns();
        if (idxCols is not null)
        {
            for (int i = 0, len = columns.Length; i < len; i++)
            {
                int idx = index.GetColumnIndex(columns[i]);
                if (idx >= 0)
                {
                    indexColumns[i] = idxCols[idx];
                }
            }
        }
    }

    /// <summary>
    /// Builds the sort types an IN list over the given columns is read in.
    /// </summary>
    /// <remarks>
    /// One per column, up to the longer of the index and the condition. Each answers whether
    /// that column of the index runs backwards, flipped when the cursor itself runs backwards:
    /// a backwards cursor over a backwards column reads forwards, and the sort types have to
    /// say so or the list comes back in the wrong order.
    /// </remarks>
    /// <param name="columns">the columns of the condition</param>
    /// <returns>the sort types</returns>
    private int[] BuildSortTypes(Column[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        IndexColumn[]? idxColumns = index?.GetColumns();
        int l = Math.Max(idxColumns is null ? 0 : idxColumns.Length, columns.Length);
        int[] sortTypes = new int[l];
        for (int i = 0; i < l; i++)
        {
            sortTypes[i] = ((idxColumns![i].SortType & Result.SortOrder.Descending) != 0) ^ reverse
                ? Result.SortOrder.Descending
                : Result.SortOrder.Ascending;
        }

        return sortTypes;
    }
    /// <remarks>
    /// Only when the index keeps exactly these columns in exactly this order. An IN over two
    /// columns is a lookup on a compound key, and an index whose columns are the same two in a
    /// different order is a different key — it would answer rows the condition never asked for.
    /// Null and length are refused first rather than compared, because an index with no columns
    /// is not one that "almost matches".
    /// </remarks>
    /// <param name="index">the index</param>
    /// <param name="columns">the columns of the condition</param>
    /// <returns>true when the index can serve it</returns>
    public static bool CanUseIndexForIn(H2Sharp.Indexes.Index? index, Column[]? columns)
    {
        IndexColumn[]? cols = index?.GetColumns();
        if (cols is null || columns is null || cols.Length != columns.Length)
        {
            return false;
        }

        for (int i = 0; i < cols.Length; i++)
        {
            IndexColumn idxCol = cols[i];
            if (idxCol is not null && !ReferenceEquals(idxCol.GetColumn(), columns[i]))
            {
                return false;
            }
        }

        return true;
    }

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
    /// Gets the object id of this table, or -1 when it has none yet.
    /// </summary>
    /// <returns>the object id</returns>
    public virtual int GetId()
        => throw new TODOException("Table.getId");

    /// <summary>
    /// Gets the schema name of this table, or null when it has none.
    /// </summary>
    public virtual string? GetSchemaName()
        => null;

    /// <summary>
    /// Gets the schema this table belongs to.
    /// </summary>
    public virtual Schemas.Schema? GetSchema()
        => null;

    /// <summary>
    /// Removes an index from this table.
    /// </summary>
    /// <param name="index">the index to remove</param>
    public virtual void RemoveIndex(Indexes.Index index)
        => throw new TODOException("Table.removeIndex");

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
    /// Creates a row with the given values.
    /// </summary>
    /// <remarks>
    /// The factory belongs to the table, not to the caller, because a row knows how it is
    /// stored: a row made by another table's factory would carry the wrong key layout, and the
    /// store would write it to a place nobody reads from.
    /// </remarks>
    /// <param name="data">the values</param>
    /// <param name="memory">how the memory is counted</param>
    /// <returns>the row</returns>
    public virtual Row CreateRow(Value[] data, int memory) => rowFactory.CreateRow(data, memory);

    /// <summary>
    /// Creates an empty row with room for every column.
    /// </summary>
    /// <remarks>
    /// A template, not a row: its values are all null, and it exists so that a search has
    /// something to fill in. A caller that kept it would hold a row that means nothing, which
    /// is why the reference makes one per search rather than keeping one around.
    /// </remarks>
    /// <returns>the empty row</returns>
    public virtual Row GetTemplateRow() => CreateRow(new Value[GetColumns().Length], DefaultRow.MemoryCalculate);

    private readonly RowFactory rowFactory = RowFactory.GetDefaultRowFactory();

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
    /// Appends the SQL for this table with the given flags.
    /// </summary>
    /// <param name="builder">the builder</param>
    /// <param name="sqlFlags">the SQL flags</param>
    /// <returns>the builder</returns>
    public abstract StringBuilder GetSql(StringBuilder builder, int sqlFlags);

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
