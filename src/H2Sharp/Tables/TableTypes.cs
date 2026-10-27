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
using System.Collections.Generic;
using System.Text;
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Util;
using H2Sharp.Values;
using Index = H2Sharp.Indexes.Index;
using ResultCursor = H2Sharp.Indexes.Cursor;

namespace H2Sharp.Tables;


/// <summary>
/// An index column: a column plus the sort direction it is read in.
/// </summary>
/// <remarks>
/// The reference stores the name and the column separately, and a statement that reads
/// columns before its table exists — CREATE TABLE's constraints — carries name-only index
/// columns until the table is there and the statement maps them onto it. This port keeps
/// the same two shapes; the column is the nullable one, because it is the one that arrives
/// late.
/// </remarks>
public class IndexColumn
{
    /// <summary>
    /// Do not append ordering.
    /// </summary>
    /// <remarks>
    /// Port rename: SQL_NO_ORDER → SqlNoOrder per C# constant naming convention.
    /// Reference: org.h2.table.IndexColumn.java:21
    /// </remarks>
    public const int SqlNoOrder = unchecked((int)0x8000_0000);

    private readonly string columnName;

    private readonly Column? column;

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
        columnName = column.GetName();
        this.ascending = ascending;
        SortType = ascending ? Result.SortOrder.Ascending : Result.SortOrder.Descending;
    }

    /// <summary>
    /// Creates an ascending index column over the given column.
    /// </summary>
    /// <param name="column">the column</param>
    public IndexColumn(Column column)
        : this(column, ascending: true)
    {
    }

    /// <summary>
    /// Creates an index column over the given column with the given sort mask.
    /// </summary>
    /// <remarks>
    /// The direction flag follows the mask, because the boolean exists only as the mask's
    /// shadow.
    /// </remarks>
    /// <param name="column">the column</param>
    /// <param name="sortType">the sort type mask</param>
    public IndexColumn(Column column, int sortType)
    {
        this.column = column;
        columnName = column.GetName();
        ascending = (sortType & Result.SortOrder.Descending) == 0;
        SortType = sortType;
    }

    /// <summary>
    /// Creates an ascending index column by name, with the column to be mapped onto a table
    /// later.
    /// </summary>
    /// <param name="columnName">the column name</param>
    public IndexColumn(string columnName)
    {
        this.columnName = columnName;
        ascending = true;
        SortType = Result.SortOrder.Ascending;
    }

    /// <summary>
    /// Creates an index column by name with an explicit sort type, with the column to be
    /// mapped onto a table later.
    /// </summary>
    /// <param name="columnName">the column name</param>
    /// <param name="sortType">the sort order and NULLS placement</param>
    public IndexColumn(string columnName, int sortType)
    {
        this.columnName = columnName;
        ascending = (sortType & Result.SortOrder.Descending) == 0;
        SortType = sortType;
    }

    /// <summary>
    /// Wraps each column in an ascending index column.
    /// </summary>
    /// <param name="columns">the columns</param>
    /// <returns>the wrapped columns</returns>
    public static IndexColumn[] Wrap(Column[] columns)
    {
        IndexColumn[] list = new IndexColumn[columns.Length];
        for (int i = 0; i < list.Length; i++)
        {
            list[i] = new IndexColumn(columns[i]);
        }
        return list;
    }

    /// <summary>
    /// Resolves named index columns against a table.
    /// </summary>
    /// <remarks>
    /// The reference assigns the resolved column into each index column in place. The port's
    /// index column is immutable, so the array is rebuilt and the sort mask carried over.
    /// A name the table does not know is COLUMN_NOT_FOUND_1, which is where a CREATE INDEX
    /// over a column that was never declared fails.
    /// </remarks>
    /// <param name="indexColumns">the columns to map</param>
    /// <param name="table">the table</param>
    /// <returns>the mapped columns</returns>
    public static IndexColumn[] MapColumns(IndexColumn[] indexColumns, Table table)
    {
        Column[] tableColumns = table.GetColumns();
        IndexColumn[] mapped = new IndexColumn[indexColumns.Length];
        for (int i = 0; i < indexColumns.Length; i++)
        {
            string name = indexColumns[i].GetName();
            Column? match = null;
            foreach (Column candidate in tableColumns)
            {
                if (candidate.GetName().Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    match = candidate;
                    break;
                }
            }

            if (match is null)
            {
                throw Message.DbException.Get(Api.ErrorCode.ColumnNotFound1, name);
            }

            mapped[i] = new IndexColumn(match, indexColumns[i].SortType);
        }

        return mapped;
    }

    /// <summary>
    /// Gets the column.
    /// </summary>
    /// <remarks>
    /// A name-only index column has none until its statement maps it onto the table it runs
    /// against; reading it before that is the reference's exception too, a null dereference
    /// at the caller.
    /// </remarks>
    public Column GetColumn()
    {
        return column!;
    }

    /// <summary>
    /// Gets the column name.
    /// </summary>
    public string GetName()
    {
        return columnName;
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
    /// Appends the SQL snippet for this index column to the specified string builder.
    /// </summary>
    /// <remarks>
    /// Ported from org.h2.table.IndexColumn.java:149-159.
    /// Prints the column name (or the column itself), then the sort order suffix
    /// (DESC, NULLS FIRST, NULLS LAST) unless SqlNoOrder is set.
    /// </remarks>
    /// <param name="builder">the builder</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the builder</returns>
    public StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        if (column is not null)
        {
            _ = column.GetSql(builder, sqlFlags);
        }
        else
        {
            _ = ParserUtil.QuoteIdentifier(builder, columnName, sqlFlags);
        }

        if ((sqlFlags & SqlNoOrder) == 0)
        {
            SortOrder.TypeToString(builder, SortType);
        }

        return builder;
    }

    /// <summary>
    /// Writes all index columns.
    /// </summary>
    /// <remarks>
    /// Delegates to the range version with the full span.
    /// Reference: org.h2.table.IndexColumn.java:50-51
    /// </remarks>
    /// <param name="builder">the builder</param>
    /// <param name="columns">the columns</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the builder</returns>
    public static StringBuilder WriteColumns(
        StringBuilder builder,
        IndexColumn[] columns,
        int sqlFlags)
    {
        return WriteColumns(builder, columns, 0, columns.Length, sqlFlags);
    }

    /// <summary>
    /// Writes a range of index columns, each qualified by its table when the index spans more
    /// than one.
    /// </summary>
    /// <remarks>
    /// Reference: org.h2.table.IndexColumn.java:69-78
    /// </remarks>
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

            _ = columns[i].GetSql(builder, sqlFlags);
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
/// <remarks>
/// It implements <see cref="H2Sharp.Indexes.Cursor"/>, as the reference's IndexCursor does: a
/// cursor that walks an index is walked through that contract, and anything else would make the
/// filter hold a cursor it cannot read through. The walk itself — find, next, previous — is not
/// ported yet; what is here is everything around it, which the reference keeps on the same class.
/// </remarks>
public class IndexCursor : H2Sharp.Indexes.Cursor
{
    private H2Sharp.Indexes.Index? index;

    private bool reverse;

    private Table? table;

    private IndexColumn?[]? indexColumns;

    private Engine.SessionLocal? session;

    private bool alwaysFalse;

    private Result.SearchRow? start;

    private Result.SearchRow? end;

    private Result.SearchRow? intersects;

    private ResultCursor? cursor;

    /// <summary>
    /// Contains a <see cref="Column"/> or a <c>Column[]</c> depending on the condition type.
    /// </summary>
    private object? inColumn;

    private Values.Value?[]? inList;

    private Result.ResultInterface? inResult;

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

    /// <summary>
    /// Prepares this cursor to answer the given conditions.
    /// </summary>
    /// <remarks>
    /// Everything is reset first, because a cursor is prepared again for every run and a bound
    /// left over from the last one would answer this run's questions with last run's rows. The
    /// conditions that cannot use this index are skipped rather than failed on — a full-table
    /// index asked for a lookup would scan the table once per condition, and the one scan the
    /// filter would have done anyway is cheaper than all of them.
    ///
    /// An equality narrows both bounds, and a narrower bound replaces a wider one: that is why
    /// each bound is built with GetSearchRow rather than set, and why a second X=? on the same
    /// column keeps the tighter of the two instead of the later.
    /// </remarks>
    /// <param name="s">the session</param>
    /// <param name="indexConditions">the conditions</param>
    public void Prepare(Engine.SessionLocal s, List<H2Sharp.Indexes.IndexCondition> indexConditions)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(indexConditions);
        session = s;
        alwaysFalse = false;
        start = end = null;
        inList = null;
        inColumn = null;
        inResult = null;
        intersects = null;
        foreach (H2Sharp.Indexes.IndexCondition condition in indexConditions)
        {
            if (condition.IsAlwaysFalse())
            {
                alwaysFalse = true;
                break;
            }

            // If index can perform only full table scan do not try to use it for regular
            // lookups, each such lookup will perform an own table scan.
            if (index is not null && index.IsFindUsingFullTableScan())
            {
                continue;
            }

            if (condition.IsCompoundColumns())
            {
                Column[]? columns = condition.GetColumns();
                if (condition.GetCompareType() == H2Sharp.Expressions.Conditions.Comparison.InList)
                {
                    if (start is null && end is null)
                    {
                        if (CanUseIndexForIn(columns!))
                        {
                            inColumn = columns;
                            inList = condition.GetCurrentValueList(s, BuildSortTypes(columns!));
                            inListIndex = 0;
                        }
                    }

                    continue;
                }

                throw DbException.GetInternalError("Multiple columns can only be used with compound IN lists.");
            }

            Column? column = condition.GetColumn();
            switch (condition.GetCompareType())
            {
                case H2Sharp.Expressions.Conditions.Comparison.InList:
                case H2Sharp.Expressions.Conditions.Comparison.InArray:
                    if (start is null && end is null)
                    {
                        if (column is not null && CanUseIndexForIn(column))
                        {
                            inColumn = column;
                            inList = condition.GetCurrentValueList(s, BuildSortTypes([column]));
                            inListIndex = 0;
                        }
                    }

                    break;
                case H2Sharp.Expressions.Conditions.Comparison.InQuery:
                    if (start is null && end is null)
                    {
                        if (column is not null && CanUseIndexForIn(column))
                        {
                            inColumn = column;
                            inResult = condition.GetCurrentResult(session!, BuildSortTypes([column]));
                        }
                    }

                    break;
                default:
                    Values.Value? v = condition.GetCurrentValue(s);
                    bool isStart = condition.IsStart();
                    bool isEnd = condition.IsEnd();
                    bool isIntersects = condition.IsSpatialIntersects();
                    int columnId = column?.GetColumnId() ?? Result.SearchRow.RowidIndex;
                    if (columnId != Result.SearchRow.RowidIndex)
                    {
                        IndexColumn? idxCol = indexColumns?[columnId];
                        if (idxCol is not null && (idxCol.SortType & Result.SortOrder.Descending) != 0)
                        {
                            // if the index column is sorted the other way, we swap
                            // end and start NULLS_FIRST / NULLS_LAST is not a
                            // problem, as nulls never match anyway
                            bool temp = isStart;
                            isStart = isEnd;
                            isEnd = temp;
                        }
                    }

                    if (isStart)
                    {
                        start = GetSearchRow(start, columnId, v, true);
                    }

                    if (isEnd)
                    {
                        end = GetSearchRow(end, columnId, v, false);
                    }

                    if (isIntersects)
                    {
                        intersects = GetSpatialSearchRow(intersects, columnId, v);
                    }

                    // An X=? condition will produce less rows than
                    // an X IN(..) condition, unless the X IN condition can use the index.
                    if ((isStart || isEnd) && (inColumn is not Column inCol || !CanUseIndexFor(inCol)))
                    {
                        inColumn = null;
                        inList = null;
                        inResult = null;
                    }

                    break;
            }
        }

        if (inColumn is not null)
        {
            start = table?.GetTemplateRow();
        }
    }

    /// <summary>
    /// Gets the lower bound of the scan.
    /// </summary>
    /// <returns>the bound, or null when there is none</returns>
    public Result.SearchRow? GetStart() => start;

    /// <summary>
    /// Gets the upper bound of the scan.
    /// </summary>
    /// <returns>the bound, or null when there is none</returns>
    public Result.SearchRow? GetEnd() => end;

    /// <summary>
    /// Whether the result is empty for sure.
    /// </summary>
    /// <returns>true when nothing can match</returns>
    public bool IsAlwaysFalse() => alwaysFalse;

    /// <summary>
    /// Whether the given column can start an IN lookup on this index.
    /// </summary>
    /// <remarks>
    /// Only one IN condition can be used at a time: a second one would need a second list, and
    /// the cursor holds one. The column itself still has to fit the index, which is the other
    /// half of the question.
    /// </remarks>
    /// <param name="column">the column</param>
    /// <returns>true when it can</returns>
    private bool CanUseIndexForIn(Column column)
    {
        if (inColumn is not null)
        {
            // only one IN(..) condition can be used at the same time
            return false;
        }

        return CanUseIndexFor(column);
    }

    /// <summary>
    /// Whether the given columns can start an IN lookup on this index.
    /// </summary>
    /// <param name="columns">the columns</param>
    /// <returns>true when they can</returns>
    private bool CanUseIndexForIn(Column[] columns)
    {
        if (inColumn is not null)
        {
            // only one IN(..) condition can be used at the same time
            return false;
        }

        return CanUseIndexForIn(index, columns);
    }

    /// <summary>
    /// Whether the given column can narrow a scan on this index.
    /// </summary>
    /// <remarks>
    /// The first column of the index must match, or the index must be one built over a view
    /// where the column is null: a condition on a later column without one on the first is a
    /// filter, not a bound, and the index cannot start from it.
    /// </remarks>
    /// <param name="column">the column</param>
    /// <returns>true when it can</returns>
    private bool CanUseIndexFor(Column column)
    {
        // The first column of the index must match this column,
        // or it must be a VIEW index (where the column is null).
        // Multiple IN conditions with views are not supported, see
        // IndexCondition.getMask.
        IndexColumn[]? cols = index?.GetColumns();
        if (cols is null)
        {
            return true;
        }

        IndexColumn idxCol = cols[0];
        return idxCol is null || ReferenceEquals(idxCol.GetColumn(), column);
    }

    /// <summary>
    /// Builds a search row for one bound of the scan.
    /// </summary>
    /// <remarks>
    /// A null row means "no bound yet", so a fresh template is made; a row that is already
    /// there keeps the tighter of the two values. The rowid is not a value but the key, which
    /// is why it is set apart: a key of NULL would be no key at all, so a NULL bound becomes
    /// the smallest key there is.
    /// </remarks>
    /// <param name="row">the row so far, or null</param>
    /// <param name="columnId">which column the bound is on</param>
    /// <param name="v">the bound</param>
    /// <param name="max">whether the larger value wins</param>
    /// <returns>the row</returns>
    private Result.SearchRow GetSearchRow(Result.SearchRow? row, int columnId, Values.Value? v, bool max)
    {
        if (row is null)
        {
            row = table?.GetTemplateRow() ?? throw new TODOException("IndexCursor.getSearchRow: no table to template from");
        }
        else
        {
            v = GetMax(row.GetValue(columnId), v, max);
        }

        if (columnId == Result.SearchRow.RowidIndex)
        {
            row.SetKey(v == Values.ValueNull.Instance ? long.MinValue : v!.GetLong());
        }
        else
        {
            // The reference passes v as it is, null included; this port's SetValue takes a
            // non-nullable value, so a null becomes the NULL value here. It is the same answer:
            // null in a search row means SQL NULL, and ValueNull is what that is.
            row.SetValue(columnId, v ?? Values.ValueNull.Instance);
        }

        return row;
    }

    /// <summary>
    /// Picks the tighter of two bounds.
    /// </summary>
    /// <remarks>
    /// Null loses to everything, and NULL loses to everything but is remembered: "IS NULL must
    /// be checked later" is the reference's own comment, because a NULL bound is not a bound at
    /// all but a question the scan still has to ask. Equal values answer with the first, so that
    /// a bound written twice does not move.
    /// </remarks>
    /// <param name="a">the bound so far</param>
    /// <param name="b">the new bound</param>
    /// <param name="bigger">whether the larger value wins</param>
    /// <returns>the tighter bound</returns>
    private Values.Value? GetMax(Values.Value? a, Values.Value? b, bool bigger)
    {
        if (a is null)
        {
            return b;
        }

        if (b is null)
        {
            return a;
        }

        // IS NULL must be checked later
        if (ReferenceEquals(a, Values.ValueNull.Instance))
        {
            return b;
        }

        if (ReferenceEquals(b, Values.ValueNull.Instance))
        {
            return a;
        }

        // The session compares, because two values of different types compare by the session's
        // rules and not by either value's own: it is the same Compare the filter's map uses.
        int comp = session is null
            ? throw new TODOException("IndexCursor.getMax: no session to compare with")
            : session.Compare(a, b);
        if (comp == 0)
        {
            return a;
        }

        return (comp > 0) == bigger ? a : b;
    }

    /// <summary>
    /// Builds a search row for a spatial bound.
    /// </summary>
    private int inListIndex;

    /// <summary>
    /// Skips to the next row if one is available.
    /// </summary>
    /// <remarks>
    /// A cursor that has none starts one, and a cursor that is spent is dropped before the next
    /// is asked for: holding a spent cursor would answer the same last row twice, and starting
    /// a new one while the old still stands would read two scans as one.
    /// </remarks>
    /// <returns>true if another row is available</returns>
    public bool Next()
    {
        while (true)
        {
            if (cursor is null)
            {
                NextCursor();
                if (cursor is null)
                {
                    return false;
                }
            }

            if (cursor.Next())
            {
                return true;
            }

            cursor = null;
        }
    }

    /// <summary>
    /// Points the cursor at the next entry of the IN list or result.
    /// </summary>
    /// <remarks>
    /// NULL entries are skipped without asking: NULL never matches, so starting a scan for one
    /// would read rows no answer can come from. The list and the result are exclusive — a cursor
    /// holds one — because an IN over a list and an IN over a query at once would need two
    /// positions, and there is one.
    /// </remarks>
    private void NextCursor()
    {
        if (inList is not null)
        {
            while (inListIndex < inList.Length)
            {
                Values.Value? v = inList[inListIndex++];
                if (!ReferenceEquals(v, Values.ValueNull.Instance))
                {
                    Find(v!);
                    break;
                }
            }
        }
        else if (inResult is not null)
        {
            while (inResult.Next())
            {
                Values.Value? v = inResult.CurrentRow()[0];
                if (!ReferenceEquals(v, Values.ValueNull.Instance))
                {
                    if (inColumn is Column[] columns)
                    {
                        v = Column.Convert(session?.CastDataProvider, columns, (Values.ValueRow)v!);
                    }
                    else
                    {
                        v = ((Column)inColumn!).Convert(session?.CastDataProvider, v!);
                    }

                    Find(v!);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Points the scan at one value of the IN list.
    /// </summary>
    /// <remarks>
    /// A compound value is converted to the columns' types first, because a list read from
    /// storage holds values as they were written and the index keeps them as its columns do: an
    /// unconverted value would miss rows whose stored form differs. A single value goes through
    /// its column for the same reason.
    /// </remarks>
    /// <param name="v">the value</param>
    private void Find(Values.Value v)
    {
        ArgumentNullException.ThrowIfNull(v);
        if (inColumn is Column[] columns)
        {
            Values.ValueRow converted = Column.Convert(session?.CastDataProvider, columns, (Values.ValueRow)v);
            Values.Value[] values = converted.GetList();
            for (int i = columns.Length; --i >= 0;)
            {
                start?.SetValue(columns[i].GetColumnId(), values[i]);
            }
        }
        else
        {
            Column column = (Column)inColumn!;
            v = column.Convert(session?.CastDataProvider, v);
            int id = column.GetColumnId();
            start?.SetValue(id, v);
        }

        cursor = index?.GetCursor(session!, start, start, reverse);
    }

    /// <summary>
    /// Skips to the previous row if one is available.
    /// </summary>
    /// <remarks>
    /// It throws, and that is the answer rather than a stub: the reference throws an internal
    /// error here because a cursor that prepared forward bounds cannot walk them backwards. A
    /// Previous that answered would walk bounds built for the other direction.
    /// </remarks>
    /// <returns>nothing, it throws</returns>
    public bool Previous()
    {
        throw DbException.GetInternalError(ToString() ?? "IndexCursor");
    }

    /// <summary>
    /// Builds a search row for a spatial bound.
    /// </summary>
    /// <remarks>
    /// Not ported. An object that must overlap two others must overlap their union, not their
    /// intersection — which needs the geometry envelope, and there is no geometry here to take
    /// one from.
    /// </remarks>
    /// <param name="row">the row so far, or null</param>
    /// <param name="columnId">which column the bound is on</param>
    /// <param name="v">the bound</param>
    /// <returns>the row</returns>
    private Result.SearchRow? GetSpatialSearchRow(Result.SearchRow? row, int columnId, Values.Value? v)
        => throw new TODOException("IndexCursor.getSpatialSearchRow: geometry is not ported");

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
    /// Finds the rows the prepared conditions describe and points the cursor at them.
    /// </summary>
    /// <remarks>
    /// An IN list short-circuits everything: it is answered from the list, and no scan is
    /// started for it. Otherwise, unless the conditions already ruled everything out, the scan
    /// runs from the first bound to the last — swapped when the cursor walks backwards, so that
    /// "backwards" means the same rows in the other order and not a different set of them.
    /// </remarks>
    /// <param name="s">the session</param>
    /// <param name="indexConditions">the conditions</param>
    public void Find(Engine.SessionLocal s, List<H2Sharp.Indexes.IndexCondition> indexConditions)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(indexConditions);
        Prepare(s, indexConditions);
        if (inColumn is not null)
        {
            return;
        }

        if (!alwaysFalse)
        {
            Result.SearchRow? first;
            Result.SearchRow? last;
            if (reverse)
            {
                first = end;
                last = start;
            }
            else
            {
                first = start;
                last = end;
            }

            if (intersects is not null && index is H2Sharp.Indexes.SpatialIndex spatial)
            {
                cursor = spatial.FindByGeometry(session!, first, last, reverse, intersects);
            }
            else if (index is not null)
            {
                cursor = index.GetCursor(session!, first, last, reverse);
            }
        }
    }

    /// <summary>
    /// Gets the complete current row. All columns are available.
    /// </summary>
    /// <remarks>
    /// Whatever the index walk stands on: this cursor only frames it with bounds, and the row
    /// itself comes from the walk. Null when the walk has nothing — before any Next, or past
    /// the end.
    /// </remarks>
    /// <returns>the complete row</returns>
    public Result.Row? Get() => cursor?.Get();

    /// <summary>
    /// Gets the current row. Only the data for indexed columns is available in this row.
    /// </summary>
    /// <remarks>
    /// Same delegation as <see cref="Get"/>.
    /// </remarks>
    /// <returns>the search row</returns>
    public Result.SearchRow? GetSearchRow() => cursor?.GetSearchRow();

    /// <summary>
    /// Inserts a row through this cursor.
    /// </summary>
    /// <param name="row">the row</param>
    public virtual void Add(Result.Row row)
        => throw new TODOException("IndexCursor.add");

    /// <summary>
    /// Removes the current row through this cursor.
    /// </summary>
    public virtual void Remove()
        => throw new TODOException("IndexCursor.remove");
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
    /// A system table.
    /// </summary>
    /// <remarks>
    /// The numbering is local to this hierarchy and not the reference's TableType ordinals:
    /// it only has to tell the kinds apart where they are compared.
    /// </remarks>
    public const int SYSTEM_TABLE = 5;

    /// <summary>
    /// The managed (cached) storage of a table, as SET DEFAULT_TABLE_TYPE writes it.
    /// </summary>
    public const int TYPE_CACHED = 0;

    /// <summary>
    /// The in-memory storage of a table, as SET DEFAULT_TABLE_TYPE writes it.
    /// </summary>
    public const int TYPE_MEMORY = 1;

    /// <summary>
    /// A view, a table whose rows are the rows of a query.
    /// </summary>
    public const int VIEW = 6;

    /// <summary>
    /// Whether this table is temporary.
    /// </summary>
    /// <remarks>
    /// In the reference the flag lives on DbObject, which this hierarchy does not share: Table
    /// stands on its own. The member is the reference's, only the shelf moved, and construction
    /// leaves it cleared, as tables here are regular until temporary ones are ported.
    /// </remarks>
    private bool temporary;

    private bool onCommitDrop;

    private bool onCommitTruncate;

    /// <summary>Whether this temporary table is global.</summary>
    private bool globalTemporary;

    /// <summary>
    /// Check if this object is temporary (for example, a temporary table).
    /// </summary>
    /// <returns>true if temporary</returns>
    public bool IsTemporary()
    {
        return temporary;
    }

    /// <summary>Whether this table is dropped at commit.</summary>
    /// <returns>true if it is dropped at commit</returns>
    public bool GetOnCommitDrop() => onCommitDrop;

    /// <summary>Sets whether this table is dropped at commit.</summary>
    /// <param name="value">the new value</param>
    public void SetOnCommitDrop(bool value) => onCommitDrop = value;

    /// <summary>Whether this table is truncated at commit.</summary>
    /// <returns>true if it is truncated at commit</returns>
    public bool GetOnCommitTruncate() => onCommitTruncate;

    /// <summary>Sets whether this table is truncated at commit.</summary>
    /// <param name="value">the new value</param>
    public void SetOnCommitTruncate(bool value) => onCommitTruncate = value;

    /// <summary>
    /// Tell this table that it is temporary or not.
    /// </summary>
    /// <param name="temporary">the new value</param>
    public void SetTemporary(bool temporary)
    {
        this.temporary = temporary;
    }

    /// <summary>
    /// The comment of this table, or null.
    /// </summary>
    /// <remarks>
    /// In the reference the field lives on DbObject, which this hierarchy does not share:
    /// Table stands on its own. The member is the reference's, only the shelf moved, and an
    /// empty comment reads back as none.
    /// </remarks>
    private string? comment;

    /// <summary>
    /// Change the comment of this table.
    /// </summary>
    /// <param name="newComment">the comment, or null for no comment</param>
    public void SetComment(string? newComment)
    {
        comment = string.IsNullOrEmpty(newComment) ? null : newComment;
    }

    /// <summary>
    /// Get the comment of this table.
    /// </summary>
    /// <returns>the comment, or null</returns>
    public string? GetComment() => comment;

    /// <summary>
    /// Whether this is a global temporary table.
    /// </summary>
    /// <returns>true for a global temporary table</returns>
    public bool IsGlobalTemporary() => globalTemporary;

    /// <summary>Sets whether this temporary table is global.</summary>
    public void SetGlobalTemporary(bool value) => globalTemporary = value;

    /// <summary>
    /// Read lock: the table may be read but not changed.
    /// </summary>
    public const int ReadLock = 0;

    /// <summary>
    /// Write lock: the table may be read and changed.
    /// </summary>
    public const int WriteLock = 1;

    /// <summary>
    /// Exclusive lock: nobody else may even read the table.
    /// </summary>
    public const int ExclusiveLock = 2;

    /// <summary>
    /// Lock the table for the given session. This method waits until the lock is granted.
    /// </summary>
    /// <remarks>
    /// False here, as in the reference: the base table has nothing to lock, and a lock on it
    /// would serialize statements that share nothing. The tables that do share something — the
    /// ones with rows — override this with a real lock, and the filter never knows which kind
    /// it holds.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="lockType">the type of lock</param>
    /// <returns>true if the table was already exclusively locked by this session</returns>
    public virtual bool Lock(H2Sharp.Engine.SessionLocal session, int lockType) => false;

    /// <summary>Releases the locks this session holds on the table.</summary>
    /// <param name="session">the session</param>
    public virtual void Unlock(H2Sharp.Engine.SessionLocal session)
    {
    }

    /// <summary>Whether the table is exclusively locked by this session.</summary>
    public virtual bool IsLockedExclusivelyBy(SessionLocal session) => false;

    /// <summary>Whether this table supports row-level locks.</summary>
    /// <returns>true if the table supports row-level locks</returns>
    public virtual bool IsRowLockable() => false;

    /// <summary>Locks a row, preventing any updates except from the given session.</summary>
    /// <param name="session">the session</param>
    /// <param name="row">the row to lock</param>
    /// <param name="timeoutMillis">the timeout in milliseconds, -1 for default, -2 to skip
    /// locked rows</param>
    /// <returns>the locked row, or null if it no longer exists or was skipped</returns>
    public virtual Result.Row? LockRow(SessionLocal session, Result.Row row, int timeoutMillis)
        => throw DbException.GetUnsupportedException("lockRow()");

    /// <summary>
    /// Get the row id column if this table has one.
    /// </summary>
    /// <remarks>
    /// The base table has none, as in the reference: a view or a range table has no rows of
    /// its own to identify. A table that stores rows overrides this with the pseudo column
    /// that names the row, which is what MERGE ... USING remembers duplicated target rows by.
    /// </remarks>
    /// <returns>the row id column, or null</returns>
    public virtual Column? GetRowIdColumn() => null;

    /// <summary>
    /// The estimated number of bytes this table occupies.
    /// </summary>
    /// <param name="total">whether to include the indexes</param>
    /// <param name="approximate">true to return a quick approximation</param>
    /// <returns>the estimated number of bytes</returns>
    public virtual long GetDiskSpaceUsed(bool total, bool approximate) => 0L;

    /// <summary>
    /// Whether this table is a view.
    /// </summary>
    /// <remarks>
    /// The reference's Table.isView, which the SQL table type of the metadata catalogue is
    /// built from: a view is a VIEW there, and anything else is a BASE TABLE.
    /// </remarks>
    /// <returns>true if this table is a view</returns>
    public virtual bool IsView() => false;

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
    /// Finds a column by name, as the reference's findColumn does.
    /// </summary>
    /// <remarks>
    /// The reference keeps a map of the columns by name, and the map compares the names the
    /// way the database compares identifiers; here the columns are looked at one by one, and
    /// the comparison is the database's.
    /// </remarks>
    /// <param name="columnName">the name of the column</param>
    /// <returns>the column, or null when the table has no column of that name</returns>
    public virtual Column? FindColumn(string columnName)
    {
        Engine.Database? database = GetSchema()?.GetDatabase();
        foreach (Column column in GetColumns())
        {
            if (database is null
                ? column.GetName().Equals(columnName, StringComparison.Ordinal)
                : database.EqualsIdentifiers(column.GetName(), columnName))
            {
                return column;
            }
        }

        return null;
    }

    /// <summary>Gets a column, allowing a missing name for IF EXISTS.</summary>
    public Column? GetColumn(string columnName, bool ifExists)
    {
        Column? column = FindColumn(columnName);
        if (column is null && !ifExists) throw DbException.Get(Api.ErrorCode.ColumnNotFound1, columnName);
        return column;
    }

    /// <summary>Renames a column after checking for a duplicate name.</summary>
    public void RenameColumn(Column column, string newName)
    {
        foreach (Column c in GetColumns())
        {
            if (!ReferenceEquals(c, column) && c.GetName().Equals(newName, StringComparison.Ordinal))
                throw DbException.Get(Api.ErrorCode.DuplicateColumnName1, newName);
        }
        // This table hierarchy resolves columns from the array; no separate name map exists.
        column.Rename(newName);
    }

    /// <summary>Checks whether the table supports ALTER TABLE.</summary>
    public virtual void CheckSupportAlter() => throw DbException.GetUnsupportedException("ALTER TABLE");

    /// <summary>Renames this schema object.</summary>
    public virtual void Rename(string newName) => throw new TODOException("Table.rename: this table kind is not ported");

    /// <summary>Gets first-level schema children, in the reference's order.</summary>
    public List<DbObject> GetChildren()
    {
        List<DbObject> children = [.. GetIndexes()];
        if (constraints is not null) children.AddRange(constraints);
        if (sequences is not null) children.AddRange(sequences);
        // Triggers, synonyms and rights do not exist in this hierarchy yet.
        return children;
    }

    /// <summary>Gets the columns included in an implicit INSERT column list.</summary>
    public Column[] GetVisibleColumns()
    {
        List<Column> columns = [];
        foreach (Column column in GetColumns()) if (column.GetVisible()) columns.Add(column);
        return columns.ToArray();
    }

    /// <summary>Gets the SQL used by the metadata table.</summary>
    public virtual string? GetCreateSqlForMeta() => GetCreateSql();

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

    private List<Constraint>? constraints;

    /// <summary>
    /// The sequences that belong to this table: the sequences of its identity columns.
    /// </summary>
    private List<Schemas.Sequence>? sequences;

    /// <summary>
    /// The views that read this table.
    /// </summary>
    private List<TableView>? dependentViews;

    private List<TableSynonym>? synonyms;

    /// <summary>
    /// Add a constraint to the table.
    /// </summary>
    /// <remarks>
    /// The list is kept from the first constraint on, as in the reference, and an already
    /// present constraint is not added twice: the table's cleanup removes every member, and a
    /// list that could hold the same object twice would try to remove it twice.
    /// </remarks>
    /// <param name="constraint">the constraint to add</param>
    public void AddConstraint(Constraint constraint)
    {
        if (constraints is null)
        {
            constraints = [constraint];
        }
        else if (!constraints.Contains(constraint))
        {
            constraints.Add(constraint);
        }
    }

    /// <summary>
    /// Gets the constraints of this table.
    /// </summary>
    /// <returns>the constraints; empty when there are none</returns>
    public List<Constraint> GetConstraints()
    {
        return constraints ?? [];
    }

    /// <summary>
    /// Remove the given constraint from the list.
    /// </summary>
    /// <param name="constraint">the constraint to remove</param>
    public void RemoveConstraint(Constraint constraint)
    {
        _ = constraints?.Remove(constraint);
    }

    /// <summary>
    /// Drops the constraints and indexes that are fully covered by the columns being removed.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="columnsToDrop">the columns being removed</param>
    public void DropMultipleColumnsConstraintsAndIndexes(SessionLocal session, List<Column> columnsToDrop)
    {
        HashSet<Column> columnSetToDrop = [.. columnsToDrop];
        HashSet<Constraint> constraintsToDrop = [];
        foreach (Constraint constraint in GetConstraints())
        {
            bool? partiallyCovered = IsPartiallyCovered(columnSetToDrop, constraint.GetReferencedColumns(this));
            if (partiallyCovered is null)
            {
                constraintsToDrop.Add(constraint);
            }
            else if (partiallyCovered.Value)
            {
                throw DbException.Get(Api.ErrorCode.ColumnIsReferenced1,
                    constraint.GetCreateSql() ?? constraint.GetName());
            }
        }

        HashSet<Indexes.Index> indexesToDrop = [];
        foreach (Indexes.Index index in GetIndexes())
        {
            if (index.GetCreateSql() is null)
            {
                continue;
            }

            List<Column> indexColumns = [];
            foreach (IndexColumn indexColumn in index.GetColumns())
            {
                indexColumns.Add(indexColumn.GetColumn());
            }

            bool? partiallyCovered = IsPartiallyCovered(columnSetToDrop, indexColumns);
            if (partiallyCovered is null)
            {
                indexesToDrop.Add(index);
            }
            else if (partiallyCovered.Value)
            {
                throw DbException.Get(Api.ErrorCode.ColumnIsReferenced1,
                    index.GetCreateSql() ?? index.GetName());
            }
        }

        foreach (Constraint constraint in constraintsToDrop)
        {
            if (constraint.IsValid())
            {
                session.GetDatabase().RemoveSchemaObject(session, constraint);
            }
        }

        foreach (Indexes.Index index in indexesToDrop)
        {
            if (Array.IndexOf(GetIndexes(), index) >= 0)
            {
                session.GetDatabase().RemoveSchemaObject(session, index);
            }
        }
    }

    private static bool? IsPartiallyCovered<T>(HashSet<T> cover, IEnumerable<T> covered)
    {
        bool containsNone = true;
        bool containsAll = true;
        foreach (T item in covered)
        {
            if (cover.Contains(item))
            {
                containsNone = false;
            }
            else
            {
                containsAll = false;
            }
        }

        return containsAll ? null : !containsNone;
    }

    /// <summary>
    /// If the index is still required by a constraint, transfer the ownership to it.
    /// Otherwise, the index is removed.
    /// </summary>
    /// <remarks>
    /// The reference updates the meta table for each constraint that took the index over; that
    /// write is deferred with the meta table.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="index">the index that is no longer required</param>
    public void RemoveIndexOrTransferOwnership(H2Sharp.Engine.SessionLocal session, Indexes.Index index)
    {
        bool stillNeeded = false;
        foreach (Constraint cons in GetConstraints())
        {
            if (cons.UsesIndex(index))
            {
                cons.SetIndexOwner(index);
                stillNeeded = true;
            }
        }

        if (!stillNeeded)
        {
            (GetSchema() ?? throw new TODOException("Table.removeIndexOrTransferOwnership: no schema"))
                .GetDatabase().RemoveSchemaObject(session, index);
        }
    }

    /// <summary>
    /// Whether the foreign key constraints of this table are checked.
    /// </summary>
    /// <remarks>
    /// The reference's field, with the reference's default: on. What the reference's setter
    /// does with it — checking the existing rows when it is turned back on — is below, in
    /// <see cref="SetCheckForeignKeyConstraints"/>. The database-wide switch of the reference
    /// (<c>SET REFERENTIAL_INTEGRITY</c>) is not ported, so this is the only switch.
    /// </remarks>
    private bool checkForeignKeyConstraints = true;

    /// <summary>
    /// Whether any constraint or trigger of this table can fire.
    /// </summary>
    /// <remarks>
    /// The reference also asks the triggers, and there are none here, so the answer is the
    /// constraints. The statements ask this before they ask <see cref="FireBeforeRow"/>: when
    /// nothing can fire, the row goes in without a call, as it does on the JVM.
    /// </remarks>
    /// <returns>true when a constraint could fire</returns>
    public bool FireRow() => constraints is not null && constraints.Count > 0;

    /// <summary>
    /// Fires the constraints that run before the row is written.
    /// </summary>
    /// <remarks>
    /// The reference fires the before-action triggers first and answers whether one of them
    /// handled the row instead; there are no triggers here, so the answer is always false and
    /// the row is always written by the caller. The constraints fire either way: the caller
    /// asks <see cref="FireRow"/> first, and a table with none never gets here.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="oldRow">the row as it was, or null for an insert</param>
    /// <param name="newRow">the row as it becomes, or null for a delete</param>
    /// <returns>false, always: no trigger handled the row instead</returns>
    public bool FireBeforeRow(Engine.SessionLocal session, Row? oldRow, Row? newRow)
    {
        FireConstraints(session, oldRow, newRow, before: true);
        return false;
    }

    /// <summary>
    /// Fires the constraints that run after the row was written.
    /// </summary>
    /// <remarks>
    /// Referential constraints are after-constraints, so this is where a row that points at
    /// nothing fails: the row is already written, and the failure rolls it back through the
    /// savepoint the write took, as it does on the JVM.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="oldRow">the row as it was, or null for an insert</param>
    /// <param name="newRow">the row as it becomes, or null for a delete</param>
    /// <param name="rollback">whether the write is being rolled back</param>
    public void FireAfterRow(Engine.SessionLocal session, Row? oldRow, Row? newRow, bool rollback)
    {
        if (!rollback)
        {
            FireConstraints(session, oldRow, newRow, before: false);
        }
    }

    /// <summary>
    /// Fires the constraints that belong to the given side of the write.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="oldRow">the row as it was, or null for an insert</param>
    /// <param name="newRow">the row as it becomes, or null for a delete</param>
    /// <param name="before">whether the before-constraints fire</param>
    private void FireConstraints(Engine.SessionLocal session, Row? oldRow, Row? newRow, bool before)
    {
        if (constraints is null)
        {
            return;
        }

        foreach (Constraint constraint in constraints)
        {
            if (constraint.IsBefore() == before)
            {
                constraint.CheckRow(session, this, oldRow!, newRow!);
            }
        }
    }

    /// <summary>
    /// Turns the foreign key checks of this table on or off, checking the existing rows first
    /// when asked.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="enabled">whether the checks run</param>
    /// <param name="checkExisting">whether the existing rows are checked when turning them on</param>
    public void SetCheckForeignKeyConstraints(Engine.SessionLocal session, bool enabled, bool checkExisting)
    {
        if (enabled && checkExisting)
        {
            foreach (Constraint constraint in GetConstraints())
            {
                if (constraint.GetConstraintType() == Constraint.Type.REFERENTIAL)
                {
                    constraint.CheckExistingData(session);
                }
            }
        }

        checkForeignKeyConstraints = enabled;
    }

    /// <summary>
    /// Whether the foreign key constraints of this table are checked.
    /// </summary>
    /// <returns>true when they are</returns>
    public bool GetCheckForeignKeyConstraints() => checkForeignKeyConstraints;

    /// <summary>
    /// Gets the table type.
    /// </summary>
    public abstract int GetTableType();

    /// <summary>
    /// Gets the number of columns.
    /// </summary>
    public abstract int GetColumnCount();

    /// <summary>
    /// Gets the column id of the main index column: the rowid, unless a table says otherwise.
    /// </summary>
    /// <remarks>
    /// The rowid, because that is what every table is ordered by until it says it is ordered
    /// by something else. A covering-index check that asked for another column would compare
    /// the query's columns against the wrong key.
    /// </remarks>
    /// <returns>the column id</returns>
    public virtual int GetMainIndexColumn() => Result.SearchRow.RowidIndex;

    /// <summary>
    /// Gets the column at the given index.
    /// </summary>
    /// <param name="index">the column index</param>
    /// <returns>the column</returns>
    public abstract Column GetColumn(int index);

    /// <summary>
    /// Gets a row filled with NULL, for the outer side of an outer join that matched nothing.
    /// </summary>
    /// <remarks>
    /// Made once and kept, because every null row of this table is the same row: it has no
    /// values to differ by. Two threads may make two of them at the same moment, and that is
    /// fine for the same reason — neither has anything in it that the other lacks.
    /// </remarks>
    /// <returns>the null row</returns>
    public virtual Row GetNullRow()
    {
        Row? row = nullRow;
        if (row is null)
        {
            // Here can be concurrently produced more than one row, but it must
            // be ok.
            Value?[] values = new Value?[GetColumnCount()];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = ValueNull.Instance;
            }

            nullRow = row = CreateRow(values!, 1);
        }

        return row;
    }

    private Row? nullRow;

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

    /// <summary>
    /// Prepares the specified row for INSERT: defaults are filled in, all values are
    /// converted to target data types and validated.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="row">the row</param>
    /// <param name="overridingSystem">whether system values are overridden, null when not said</param>
    public virtual void ConvertInsertRow(
        H2Sharp.Engine.SessionLocal session, Row row, bool? overridingSystem)
    {
        Column[] columns = GetColumns();
        int generated = 0;
        for (int i = 0; i < columns.Length; i++)
        {
            Value? value = row.GetValue(i);
            Column column = columns[i];
            if (value is ValueNull && column.IsDefaultOnNull())
            {
                value = null;
            }

            if (column.IsIdentity())
            {
                if (overridingSystem is not null)
                {
                    if (!overridingSystem.Value)
                    {
                        value = null;
                    }
                }
                else if (value is not null && column.IsGeneratedAlways())
                {
                    throw Message.DbException.Get(
                        Api.ErrorCode.GeneratedColumnCannotBeAssigned1,
                        column.GetSqlWithTable(new StringBuilder(), Util.HasSql.TraceSqlFlags).ToString());
                }
            }
            else if (column.IsGeneratedAlways())
            {
                if (value is not null)
                {
                    throw Message.DbException.Get(
                        Api.ErrorCode.GeneratedColumnCannotBeAssigned1,
                        column.GetSqlWithTable(new StringBuilder(), Util.HasSql.TraceSqlFlags).ToString());
                }

                generated++;
                continue;
            }

            Value converted = column.ValidateConvertUpdateSequence(session, value, row);
            if (!ReferenceEquals(converted, value))
            {
                row.SetValue(i, converted);
            }
        }

        if (generated > 0)
        {
            for (int i = 0; i < columns.Length; i++)
            {
                if (row.GetValue(i) is null)
                {
                    row.SetValue(i, columns[i].ValidateConvertUpdateSequence(session, null, row));
                }
            }
        }
    }

    /// <summary>
    /// Prepares the specified row for UPDATE operation. Default and generated values are
    /// evaluated, all values are converted to target data types and validated.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="row">the row</param>
    /// <param name="fromTrigger">true if the row was modified by an INSERT or UPDATE trigger</param>
    public virtual void ConvertUpdateRow(
        H2Sharp.Engine.SessionLocal session, Row row, bool fromTrigger)
    {
        Column[] columns = GetColumns();
        int generated = 0;
        for (int i = 0; i < columns.Length; i++)
        {
            Value? value = row.GetValue(i);
            Column column = columns[i];
            if (column.IsGenerated())
            {
                if (value is not null)
                {
                    if (!fromTrigger)
                    {
                        throw Message.DbException.Get(
                            Api.ErrorCode.GeneratedColumnCannotBeAssigned1,
                            column.GetSqlWithTable(new StringBuilder(), Util.HasSql.TraceSqlFlags).ToString());
                    }

                    // A missing value reads as null here: the template row holds nulls, not NULLs.
                    row.SetValue(i, null!);
                }

                generated++;
                continue;
            }

            Value converted = column.ValidateConvertUpdateSequence(session, value, row);
            if (!ReferenceEquals(converted, value))
            {
                row.SetValue(i, converted);
            }
        }

        if (generated > 0)
        {
            for (int i = 0; i < columns.Length; i++)
            {
                if (row.GetValue(i) is null)
                {
                    row.SetValue(i, columns[i].ValidateConvertUpdateSequence(session, null, row));
                }
            }
        }
    }

    private readonly RowFactory rowFactory = RowFactory.GetDefaultRowFactory();

    /// <summary>
    /// Gets all columns.
    /// </summary>
    /// <returns>the columns</returns>
    public abstract Column[] GetColumns();

    /// <summary>
    /// Gets the column with the given name.
    /// </summary>
    /// <remarks>
    /// The reference keeps a map from the name as the parser read it, which for an unquoted
    /// name is the upper case one; this walks the columns and compares the same way. A quoted
    /// name keeps its case on both sides, so the comparison stays ordinal rather than
    /// case-insensitive.
    /// </remarks>
    /// <param name="columnName">the column name</param>
    /// <returns>the column</returns>
    public Column GetColumn(string columnName)
    {
        foreach (Column column in GetColumns())
        {
            if (column.GetName().Equals(columnName, StringComparison.Ordinal))
            {
                return column;
            }
        }

        throw DbException.Get(Api.ErrorCode.ColumnNotFound1, columnName);
    }

    /// <summary>
    /// Gets the index a full scan reads, priced for the given query.
    /// </summary>
    /// <remarks>
    /// The base answer ignores everything but the session: a scan reads the whole table however
    /// it was asked, and masks, filters, order and columns do not change what "everything"
    /// costs. The tables that read less override this; the base is what they fall back to, not
    /// what they compute with.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="masks">which columns the query fixed, by column id</param>
    /// <param name="filters">the filters of the query</param>
    /// <param name="filter">the position of this table among them</param>
    /// <param name="sortOrder">the order the query wants, or null</param>
    /// <param name="allColumnsSet">the columns each table reads, or null</param>
    /// <returns>the scan index</returns>
    public virtual H2Sharp.Indexes.Index? GetScanIndex(
        Engine.SessionLocal session,
        int[]? masks,
        TableFilter[]? filters,
        int filter,
        Result.SortOrder? sortOrder,
        H2Sharp.Command.Query.AllColumnsForPlan? allColumnsSet)
    {
        return GetScanIndex(session);
    }

    /// <summary>
    /// Prices every index the table owns for the given query and returns the cheapest plan.
    /// </summary>
    /// <remarks>
    /// The scan is always priced first and wins by default; every other index is priced only
    /// when the query fixed columns, and only when the query's hints do not forbid it. An
    /// unfixed query never leaves the scan, however many indexes the table has.
    /// The reference logs each candidate through the session trace here; that logging is
    /// skipped, because <see cref="Engine.SessionLocal.GetTrace"/> refuses until tracing is
    /// ported, and a plan must not depend on diagnostics being available.
    /// </remarks>
    /// <summary>
    /// Returns the main index column when the index is a primary key over one _ROWID_-compatible
    /// column, or the row id when it is anything else.
    /// </summary>
    /// <remarks>
    /// In the reference this static lives on TableBase, which this port does not have: the
    /// table hierarchy here is Table with MVTable directly under it. The signature is the
    /// reference's verbatim; only the shelf moved. A descending key never qualifies, and
    /// neither does a second column — only an ascending single integer key reuses the primary
    /// index, everything else gets its own.
    /// </remarks>
    /// <summary>
    /// Whether conditions on this table can use an index.
    /// </summary>
    /// <remarks>
    /// True unless a table kind that stores no order says otherwise; none of those kinds is
    /// ported, so nothing overrides this yet.
    /// </remarks>
    /// <returns>true</returns>
    public virtual bool IsQueryComparable() => true;

    /// <param name="indexType">the type of the index</param>
    /// <param name="cols">the columns of the index</param>
    /// <returns>the main index column, or <see cref="SearchRow.RowidIndex"/></returns>
    public static int GetMainIndexColumn(H2Sharp.Indexes.IndexType indexType, IndexColumn[] cols)
    {
        if (!indexType.IsPrimaryKey() || cols.Length != 1)
        {
            return SearchRow.RowidIndex;
        }
        IndexColumn first = cols[0];
        if ((first.SortType & SortOrder.Descending) != 0)
        {
            return SearchRow.RowidIndex;
        }
        switch (first.GetColumn().GetTypeInfo().GetValueType())
        {
            case Value.Tinyint:
            case Value.Smallint:
            case Value.Integer:
            case Value.Bigint:
                return first.GetColumn().GetColumnId();
            default:
                return SearchRow.RowidIndex;
        }
    }

    /// <param name="session">the session</param>
    /// <param name="masks">which columns the query fixed, by column id, or null</param>
    /// <param name="filters">the filters of the query</param>
    /// <param name="filter">the position of this table among them</param>
    /// <param name="sortOrder">the order the query wants, or null</param>
    /// <param name="allColumnsSet">the columns each table reads, or null</param>
    /// <param name="isSelectCommand">whether a SELECT is being planned</param>
    /// <returns>the cheapest plan item</returns>
    public virtual PlanItem GetBestPlanItem(
        Engine.SessionLocal session,
        int[]? masks,
        TableFilter[]? filters,
        int filter,
        Result.SortOrder? sortOrder,
        H2Sharp.Command.Query.AllColumnsForPlan? allColumnsSet,
        bool isSelectCommand)
    {
        PlanItem item = new();
        H2Sharp.Indexes.Index? scanIndex = GetScanIndex(session);
        item.SetIndex(scanIndex);
        item.Cost = item.GetIndex()!.GetCost(session, null, filters, filter, null, allColumnsSet, isSelectCommand);
        if (masks != null)
        {
            IndexHints? indexHints = GetIndexHints(filters, filter);
            foreach (H2Sharp.Indexes.Index index in GetIndexes())
            {
                if (index == scanIndex || IsIndexExcludedByHints(indexHints, index))
                {
                    continue;
                }

                double cost = index.GetCost(session, masks, filters, filter,
                    sortOrder, allColumnsSet, isSelectCommand);
                if (cost < item.Cost)
                {
                    item.Cost = cost;
                    item.SetIndex(index);
                }
            }
        }
        return item;
    }

    private static bool IsIndexExcludedByHints(IndexHints? indexHints, H2Sharp.Indexes.Index index)
    {
        return indexHints is not null && !indexHints.AllowIndex(index.GetName());
    }

    private static IndexHints? GetIndexHints(TableFilter[]? filters, int filter)
    {
        return filters is null ? null : filters[filter].GetIndexHints();
    }

    /// <summary>
    /// Gets the index a full scan reads.
    /// </summary>
    /// <remarks>
    /// Abstract because "the index a full scan reads" depends on the table: a regular table
    /// scans its primary index, and any other table answers with whatever it walks in full. A
    /// scan that read another index would skip rows or read them twice, depending on which.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <returns>the scan index</returns>
    public abstract H2Sharp.Indexes.Index? GetScanIndex(Engine.SessionLocal session);

    /// <summary>
    /// Gets the primary key index if there is one, or null if there is none.
    /// </summary>
    /// <returns>the primary key index or null</returns>
    public virtual Index? FindPrimaryKey()
    {
        foreach (Index idx in GetIndexes())
        {
            if (idx.GetIndexType() is { } indexType && indexType.IsPrimaryKey())
            {
                return idx;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the primary key index, failing when the table has none.
    /// </summary>
    /// <remarks>
    /// The name in the error is the reference's literal: the index that is missing is the one
    /// named by the PRIMARY_KEY_ prefix, whether or not a name was ever handed out.
    /// </remarks>
    /// <returns>the primary key index</returns>
    public virtual Index GetPrimaryKey()
    {
        Index? index = FindPrimaryKey();
        if (index is not null)
        {
            return index;
        }

        throw DbException.Get(Api.ErrorCode.IndexNotFound1, Constants.PrefixPrimaryKey);
    }

    /// <summary>
    /// Whether the indexes of this table are persisted.
    /// </summary>
    /// <remarks>
    /// In the reference the flag is a field of Table, set by the constructor from the statement
    /// that created the table. This hierarchy keeps no state in the base, so the answer lives
    /// with the implementations and the base refuses, as it does for the other table state.
    /// </remarks>
    /// <returns>true if the indexes are persisted</returns>
    public virtual bool IsPersistIndexes()
        => throw new TODOException("Table.isPersistIndexes");

    /// <summary>
    /// Tells the table that it was modified.
    /// </summary>
    /// <remarks>
    /// In the reference this is DbObject's, and every schema object answers it; a table here
    /// does not share that base, so the member stands on the table and the state lives with
    /// the implementation.
    /// </remarks>
    public virtual void SetModified()
        => throw new TODOException("Table.setModified");

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
    /// Gets the index that has the given column as the first element.
    /// This method returns null if no matching index is found.
    /// </summary>
    /// <param name="column">the column</param>
    /// <param name="needGetFirstOrLast">if the returned index must be able to do first-or-last lookups</param>
    /// <param name="needFindNext">if the returned index must be able to do next-row lookups</param>
    /// <returns>the index or null</returns>
    public virtual Index? GetIndexForColumn(Column column, bool needGetFirstOrLast, bool needFindNext)
    {
        Index? result = null;
        foreach (Index index in GetIndexes())
        {
            if (needGetFirstOrLast && !index.CanGetFirstOrLast())
            {
                continue;
            }

            if (needFindNext && !index.CanFindNext())
            {
                continue;
            }

            // Choose the minimal covering index with the needed first
            // column to work consistently with execution plan from
            // Optimizer
            if (index.IsFirstColumn(column) && (result is null
                || result.GetColumns().Length > index.GetColumns().Length))
            {
                result = index;
            }
        }

        return result;
    }

    /// <summary>
    /// Finds the index usable for the given row.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="row">the row to search for</param>
    /// <returns>the index, or null</returns>
    public abstract Index? GetIndexFor(H2Sharp.Engine.SessionLocal session, SearchRow row);

    /// <summary>
    /// Replaces the rows with the same keys.
    /// </summary>
    /// <remarks>
    /// The reference's updateRows: the old rows go first and the new ones after, so that two
    /// rows swapping their keys do not collide halfway. A failure rolls back to the savepoint
    /// the call took; the statement around it rolls back further, to its own.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="rows">the old and new rows, in pairs</param>
    public virtual void UpdateRows(H2Sharp.Engine.SessionLocal session, List<(Row OldRow, Row NewRow)> rows)
    {
        H2Sharp.MvStore.Tx.Transaction transaction = session.GetTransaction();
        long savepoint = transaction.SetSavepoint();
        try
        {
            foreach ((Row oldRow, Row _) in rows)
            {
                RemoveRow(session, oldRow.GetKey());
            }

            foreach ((Row _, Row newRow) in rows)
            {
                _ = AddRow(session, newRow);
            }
        }
        catch (H2SharpException)
        {
            try
            {
                transaction.RollbackToSavepoint(savepoint);
            }
            catch
            {
                // The reference records the rollback failure as suppressed; a plain .NET
                // exception carries none, and the write's own failure is what the caller sees.
            }

            throw;
        }
    }

    /// <summary>
    /// Adds a row to this table.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="row">the row</param>
    /// <returns>the key of the added row</returns>
    public virtual long AddRow(H2Sharp.Engine.SessionLocal session, Row row)
        => throw new TODOException("Table.addRow");

    /// <summary>
    /// Replaces the row with the same key.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="oldRow">the row as it was</param>
    /// <param name="newRow">the row as it is now</param>
    public virtual void UpdateRow(H2Sharp.Engine.SessionLocal session, Row oldRow, Row newRow)
        => throw new TODOException("Table.updateRow");

    /// <summary>
    /// Removes the row with the given key.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="key">the key</param>
    public virtual void RemoveRow(H2Sharp.Engine.SessionLocal session, long key)
        => throw new TODOException("Table.removeRow");

    /// <summary>
    /// Adds an index to this table.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="indexName">the index name</param>
    /// <param name="indexId">the object id</param>
    /// <param name="cols">the columns that are indexed</param>
    /// <param name="uniqueColumnCount">count of unique columns</param>
    /// <param name="indexType">the index type</param>
    /// <param name="create">whether this call creates the index</param>
    /// <param name="indexComment">the comment, or null</param>
    /// <returns>the added index</returns>
    public virtual Index AddIndex(
        H2Sharp.Engine.SessionLocal session,
        string? indexName,
        int indexId,
        IndexColumn[] cols,
        int uniqueColumnCount,
        H2Sharp.Indexes.IndexType indexType,
        bool create,
        string? indexComment)
        => throw new TODOException("Table.addIndex");

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
    /// <param name="session">the session</param>
    public abstract long GetRowCountApproximation(SessionLocal session);

    /// <summary>
    /// Check if the row count can be retrieved quickly.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>true if it can</returns>
    public abstract bool CanGetRowCount(H2Sharp.Engine.SessionLocal session);

    /// <summary>
    /// Get the row count for this table.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the row count</returns>
    public abstract long GetRowCount(H2Sharp.Engine.SessionLocal session);

    /// <summary>
    /// Gets the SQL statement that creates this table.
    /// </summary>
    public abstract string? GetCreateSql();

    /// <summary>
    /// The SQL to name this table in a trace or an error, as the reference's HasSQL default.
    /// </summary>
    /// <returns>the SQL</returns>
    public string GetTraceSql()
        => GetSql(Util.HasSql.TraceSqlFlags);

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
    /// <remarks>
    /// The constraints go first, and the order is not cosmetic: dropping the table's indexes
    /// before the constraints that own them would leave a constraint naming an index that is
    /// gone. The reference's base body also removes dependent views, synonyms, triggers, rights
    /// and sequences around this loop; none of those exist here, so what runs is the loop the
    /// reference has in the middle of them.
    /// </remarks>
    /// <param name="session">the session</param>
    public virtual void RemoveChildrenAndResources(H2Sharp.Engine.SessionLocal session)
    {
        while (dependentViews is not null && dependentViews.Count > 0)
        {
            TableView view = dependentViews[0];
            dependentViews.RemoveAt(0);
            (GetSchema() ?? throw new TODOException("Table.removeChildrenAndResources: no schema"))
                .GetDatabase().RemoveSchemaObject(session, view);
        }

        while (synonyms is not null && synonyms.Count > 0)
        {
            TableSynonym synonym = synonyms[0];
            synonyms.RemoveAt(0);
            (GetSchema() ?? throw new TODOException("Table.removeChildrenAndResources: no schema"))
                .GetDatabase().RemoveSchemaObject(session, synonym);
        }

        bool sessionTemporary = IsTemporary() && !IsGlobalTemporary();
        while (constraints is not null && constraints.Count > 0)
        {
            Constraint constraint = constraints[0];
            constraints.RemoveAt(0);
            if (sessionTemporary)
            {
                session.RemoveLocalTempTableConstraint(constraint);
            }
            else
            {
                (GetSchema() ?? throw new TODOException("Table.removeChildrenAndResources: no schema"))
                    .GetDatabase().RemoveSchemaObject(session, constraint);
            }
        }

        // The sequences of the identity columns belong to the table and go with it.
        while (sequences is not null && sequences.Count > 0)
        {
            Schemas.Sequence sequence = sequences[0];
            sequences.RemoveAt(0);
            (GetSchema() ?? throw new TODOException("Table.removeChildrenAndResources: no schema"))
                .GetDatabase().RemoveSchemaObject(session, sequence);
        }
    }

    /// <summary>
    /// Adds a sequence that belongs to this table, which is the sequence of an identity column.
    /// </summary>
    /// <param name="sequence">the sequence</param>
    public void AddSequence(Schemas.Sequence sequence)
    {
        sequences ??= [];
        sequences.Add(sequence);
    }

    /// <summary>
    /// Gets the sequences that belong to this table.
    /// </summary>
    /// <returns>the sequences</returns>
    public List<Schemas.Sequence> GetSequences() => sequences is null ? [] : [.. sequences];

    /// <summary>
    /// Removes a sequence that belonged to this table.
    /// </summary>
    /// <param name="sequence">the sequence</param>
    public void RemoveSequence(Schemas.Sequence sequence) => sequences?.Remove(sequence);

    /// <summary>Adds a view that reads this table.</summary>
    /// <param name="view">the view</param>
    public void AddDependentView(TableView view)
    {
        dependentViews ??= [];
        if (!dependentViews.Contains(view))
        {
            dependentViews.Add(view);
        }
    }

    /// <summary>Removes a view that no longer reads this table.</summary>
    /// <param name="view">the view</param>
    public void RemoveDependentView(TableView view) => dependentViews?.Remove(view);

    /// <summary>Gets the views that read this table.</summary>
    /// <returns>the views; empty when there are none</returns>
    public List<TableView> GetDependentViews() => dependentViews is null ? [] : [.. dependentViews];

    /// <summary>Adds a synonym of this table.</summary>
    /// <param name="synonym">the synonym</param>
    public void AddSynonym(TableSynonym synonym)
    {
        synonyms ??= [];
        if (!synonyms.Contains(synonym))
        {
            synonyms.Add(synonym);
        }
    }

    /// <summary>Removes a synonym of this table.</summary>
    /// <param name="synonym">the synonym</param>
    public void RemoveSynonym(TableSynonym synonym) => synonyms?.Remove(synonym);

    /// <summary>Gets the synonyms of this table.</summary>
    /// <returns>the synonyms; empty when there are none</returns>
    public List<TableSynonym> GetSynonyms() => synonyms is null ? [] : [.. synonyms];

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
