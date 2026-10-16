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
using System.Collections.Generic;
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

        // The row factories need the database's row factory, its compare mode and its table
        // columns, and the database is the one thing this constructor does not have yet: a
        // schema in this port does not know its database (Schema.GetDatabase exists but returns
        // nothing usable yet). They stay null until that path is ported, and the accessors
        // below refuse instead of handing a null that would fail later far from the cause.
        rowFactory = null;
        uniqueRowFactory = null;
    }

    /// <summary>
    /// Gets the row factory that reads rows in the order of this index.
    /// </summary>
    protected RowFactory GetRowFactory()
    {
        return rowFactory ?? throw new TODOException("Index.getRowFactory: the database row factory is not ported");
    }

    /// <summary>
    /// Gets the row factory that reads rows in the order of the unique columns alone.
    /// </summary>
    protected RowFactory GetUniqueRowFactory()
    {
        return uniqueRowFactory ?? throw new TODOException("Index.getUniqueRowFactory: the database row factory is not ported");
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
    /// <remarks>
    /// A scan index answers null, as the reference's primary index does: there is no
    /// statement that creates it. <see cref="MvStore.Db.MVPrimaryIndex"/> says so.
    /// </remarks>
    public override string? GetCreateSql()
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
    /// Does this index support lookup by row id?
    /// </summary>
    /// <returns>true if it does</returns>
    public virtual bool IsRowIdIndex() => false;

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
    public virtual int GetColumnIndex(Column col)
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
    /// Whether the given column is the first for this index.
    /// </summary>
    /// <remarks>
    /// For scan indexes of tables with row identifiers their row id column counts as first,
    /// although it is not in the list, because such scans walk in that virtual column's order.
    /// </remarks>
    /// <param name="column">the column</param>
    /// <returns>true when it is first</returns>
    public virtual bool IsFirstColumn(Column column)
    {
        return column.Equals(columns[0]);
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
    /// Gets the cost of using this index for the given query: how many rows it will read.
    /// </summary>
    /// <remarks>
    /// Abstract, because no two kinds of index answer it the same way: a scan reads everything
    /// it holds, and a lookup reads what its bounds describe. What they share is below, in
    /// <see cref="GetCostRangeIndex"/>.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="masks">which columns the query fixed, by column id</param>
    /// <param name="filters">the filters of the query</param>
    /// <param name="filter">the position of this index's filter among them</param>
    /// <param name="sortOrder">the order the query wants, or null</param>
    /// <param name="allColumnsSet">the columns each table reads, or null</param>
    /// <param name="isSelectCommand">whether the caller is a select</param>
    /// <returns>the cost</returns>
    public abstract double GetCost(
        Engine.SessionLocal session,
        int[]? masks,
        Tables.TableFilter[]? filters,
        int filter,
        Result.SortOrder? sortOrder,
        Command.Query.AllColumnsForPlan? allColumnsSet,
        bool isSelectCommand);

    /// <summary>
    /// Prices a range scan over this index's own columns.
    /// </summary>
    /// <remarks>
    /// An equality on all of a unique key costs 3 rows, because that is what a lookup that
    /// stops at the first match reads. A range costs a quarter of the rows, a start or an end
    /// a third: those fractions are not measured anywhere, they are what the reference charges,
    /// and changing them reorders every plan with a range in it. An ORDER BY the index already
    /// keeps is cheaper by exactly the sort it saves, and a covering index by the table it
    /// never reads.
    /// </remarks>
    /// <param name="masks">which columns the query fixed, by column id</param>
    /// <param name="rowCount">how many rows the index holds</param>
    /// <param name="filters">the filters of the query</param>
    /// <param name="filter">the position of this index's filter among them</param>
    /// <param name="sortOrder">the order the query wants, or null</param>
    /// <param name="isScanIndex">whether this index is the scan index</param>
    /// <param name="allColumnsSet">the columns each table reads, or null</param>
    /// <param name="isSelectCommand">whether the caller is a select</param>
    /// <returns>the cost</returns>
    protected long GetCostRangeIndex(
        int[]? masks,
        long rowCount,
        Tables.TableFilter[]? filters,
        int filter,
        Result.SortOrder? sortOrder,
        bool isScanIndex,
        Command.Query.AllColumnsForPlan? allColumnsSet,
        bool isSelectCommand)
    {
        rowCount += Engine.Constants.CostRowOffset;
        int totalSelectivity = 0;
        long rowsCost = rowCount;
        if (masks is not null)
        {
            int i = 0;
            int len = columns.Length;
            bool tryAdditional = false;
            while (i < len)
            {
                Tables.Column column = columns[i++];
                int index = column.GetColumnId();
                int mask = masks[index];
                if ((mask & H2Sharp.Indexes.IndexCondition.Equality) == H2Sharp.Indexes.IndexCondition.Equality)
                {
                    if (i > 0 && i == uniqueColumnColumn)
                    {
                        rowsCost = 3;
                        break;
                    }

                    totalSelectivity = 100 - ((100 - totalSelectivity) * (100 - column.GetSelectivity()) / 100);
                    long distinctRows = rowCount * totalSelectivity / 100;
                    if (distinctRows <= 0)
                    {
                        distinctRows = 1;
                    }

                    rowsCost = 2 + Math.Max(rowCount / distinctRows, 1);
                }
                else if ((mask & H2Sharp.Indexes.IndexCondition.Range) == H2Sharp.Indexes.IndexCondition.Range)
                {
                    rowsCost = 2 + rowsCost / 4;
                    tryAdditional = true;
                    break;
                }
                else if ((mask & H2Sharp.Indexes.IndexCondition.Start) == H2Sharp.Indexes.IndexCondition.Start)
                {
                    rowsCost = 2 + rowsCost / 3;
                    tryAdditional = true;
                    break;
                }
                else if ((mask & H2Sharp.Indexes.IndexCondition.End) == H2Sharp.Indexes.IndexCondition.End)
                {
                    rowsCost = rowsCost / 3;
                    tryAdditional = true;
                    break;
                }
                else if ((mask & H2Sharp.Indexes.IndexCondition.SpatialIntersects) == H2Sharp.Indexes.IndexCondition.SpatialIntersects)
                {
                    rowsCost = 2 + rowsCost / 4;
                    tryAdditional = true;
                    break;
                }
                else
                {
                    if (mask == 0)
                    {
                        // Adjust counter of used columns (i)
                        i--;
                    }

                    break;
                }
            }

            // Some additional columns can still be used
            if (tryAdditional)
            {
                while (i < len && masks[columns[i].GetColumnId()] != 0)
                {
                    i++;
                    rowsCost--;
                }
            }

            // Increase cost of indexes with additional unused columns
            rowsCost += len - i;
        }

        // If the ORDER BY clause matches the ordering of this index,
        // it will be cheaper than another index, so adjust the cost
        // accordingly.
        long sortingCost = 0;
        if (sortOrder is not null)
        {
            sortingCost = 100 + rowCount / 10;
        }

        if (sortOrder is not null && !isScanIndex && filters is not null && filter == 0)
        {
            int coveringCount = 0;
            // Unlike running out of index columns, or a direction/null-ordering mismatch on
            // an otherwise-matching column (still usable via a reverse index scan), landing
            // on a sort column the index doesn't have at that position at all means the rows
            // still need a real sort from that point on, so it must disqualify the discount
            // below rather than just cap coveringCount at whatever matched so far.
            bool mismatched = false;
            int[] sortTypes = sortOrder.GetSortTypesWithNullOrdering();
            int sortOrderLength = sortTypes.Length;
            Tables.TableFilter tableFilter = filters[0];
            Engine.DefaultNullOrdering defaultNullOrdering = GetDatabase()!.GetDefaultNullOrdering();
            bool reverse = false;
            Tables.IndexColumn[] idxColumns = GetColumns();
            for (int i = 0; i < sortOrderLength; i++)
            {
                if (i >= idxColumns.Length)
                {
                    // We can still use this index if we are sorting by more
                    // than it's columns, it's just that the coveringCount
                    // is lower than with an index that contains
                    // more of the order by columns.
                    break;
                }

                Tables.Column? col = sortOrder.GetColumn(i, tableFilter);
                if (col is null)
                {
                    mismatched = true;
                    break;
                }

                Tables.IndexColumn idxCol = idxColumns[i];
                if (!col.Equals(idxCol.GetColumn()))
                {
                    mismatched = true;
                    break;
                }

                bool mismatch = false;
                if (col.IsNullable() == Tables.Column.Nullable)
                {
                    int o1 = Engine.DefaultNullOrderingRules.AddExplicitNullOrdering(defaultNullOrdering, idxCol.SortType);
                    int o2 = sortTypes[i];
                    if (i == 0)
                    {
                        if (o1 != o2)
                        {
                            if (o1 == Result.SortOrder.Inverse(o2))
                            {
                                reverse = true;
                            }
                            else
                            {
                                mismatch = true;
                            }
                        }
                    }
                    else
                    {
                        if (o1 != (reverse ? Result.SortOrder.Inverse(o2) : o2))
                        {
                            mismatch = true;
                        }
                    }
                }
                else
                {
                    bool different = (idxCol.SortType & Result.SortOrder.Descending)
                        != (sortTypes[i] & Result.SortOrder.Descending);
                    if (i == 0)
                    {
                        reverse = different;
                    }
                    else
                    {
                        mismatch = different != reverse;
                    }
                }

                if (mismatch)
                {
                    break;
                }

                coveringCount++;
            }

            if (!mismatched && coveringCount > 0)
            {
                // "coveringCount" makes sure that when we have two
                // or more covering indexes, we choose the one
                // that covers more.
                sortingCost = 100 - coveringCount;
            }
        }

        // If we have two indexes with the same cost, and one of the indexes can
        // satisfy the query without needing to read from the primary table
        // (scan index), make that one slightly lower cost.
        // For INSERT or UPDATE commands, we have to touch the primary table anyway
        // so this makes no difference.
        bool needsToReadFromScanIndex;
        if (isSelectCommand && !isScanIndex && allColumnsSet is not null)
        {
            needsToReadFromScanIndex = false;
            List<Tables.Column>? foundCols = allColumnsSet.Get(GetTable()!);
            if (foundCols is not null)
            {
                Tables.Table? table = GetTable();
                int main = table?.GetMainIndexColumn() ?? Result.SearchRow.RowidIndex;
                foreach (Tables.Column c in foundCols)
                {
                    int id = c.GetColumnId();
                    if (id == Result.SearchRow.RowidIndex || id == main)
                    {
                        continue;
                    }

                    bool covered = false;
                    foreach (Tables.Column c2 in columns)
                    {
                        if (ReferenceEquals(c, c2))
                        {
                            covered = true;
                            break;
                        }
                    }

                    if (!covered)
                    {
                        needsToReadFromScanIndex = true;
                        break;
                    }
                }
            }
        }
        else
        {
            needsToReadFromScanIndex = true;
        }

        long rc;
        if (!isSelectCommand)
        {
            // For UPDATE or INSERT, we have to touch the primary table
            // so the covering index calculations below are irrelevant.
            rc = rowsCost + sortingCost;
        }
        else if (isScanIndex)
        {
            rc = rowsCost + sortingCost + 20;
        }
        else if (needsToReadFromScanIndex)
        {
            rc = rowsCost + rowsCost + sortingCost + 20;
        }
        else
        {
            // covering index
            // The "+ 20" terms above, and the "+ columns.length" term here,
            // makes sure that when we pick a covering index,
            // we pick the covering index that has the smallest number of
            // columns (the more columns we have in index - the higher cost).
            // This is faster because a smaller index will fit into fewer data
            // blocks.
            rc = rowsCost + sortingCost + columns.Length;
        }

        return rc;
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
    /// Whether this index needs rebuilding after opening.
    /// </summary>
    /// <remarks>
    /// This method is called after opening an index.
    /// </remarks>
    /// <returns>true if a rebuild is required</returns>
    public abstract bool NeedRebuild();

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
    /// Checks if the index can directly look up the lowest or highest value of a column.
    /// </summary>
    /// <returns>true if it can</returns>
    public virtual bool CanGetFirstOrLast() => false;

    /// <summary>
    /// Checks if the index can get the next higher value.
    /// </summary>
    /// <returns>true if it can</returns>
    public virtual bool CanFindNext() => false;

    /// <summary>
    /// Finds the first (or last) value of this index. The cursor returned is positioned
    /// on the correct row, or on null if no row has been found.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="first">true if the first (lowest for ascending indexes) or last value should be returned</param>
    /// <returns>a cursor (never null)</returns>
    public virtual Cursor FindFirstOrLast(SessionLocal session, bool first)
        => throw DbException.GetInternalError(ToString());

    /// <summary>
    /// Returns a cursor over this index.
    /// </summary>
    /// <remarks>
    /// The interface rather than the table filter's cursor: what walks here is the index's own
    /// walk, and the filter wraps it with bounds and conditions of its own.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="first">the first row, or null to start at the beginning</param>
    /// <param name="last">the last row, or null to read to the end</param>
    /// <param name="reverse">whether to walk backwards</param>
    /// <returns>the cursor</returns>
    public virtual Cursor? GetCursor(SessionLocal session, SearchRow? first, SearchRow? last, bool reverse)
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
