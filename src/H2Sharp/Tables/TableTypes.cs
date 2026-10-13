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
using H2Sharp.Engine;
using H2Sharp.Message;
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
    /// Creates an ascending index column over the given column.
    /// </summary>
    /// <remarks>
    /// The reference keeps the name and resolves the column later; here the column arrives
    /// resolved, so this is the ascending creation with the column already in hand.
    /// </remarks>
    /// <param name="column">the column</param>
    public IndexColumn(Column column)
        : this(column, ascending: true)
    {
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

        cursor = index?.GetCursor(session!, start, start);
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
                cursor = index.GetCursor(session!, first, last);
            }
        }
    }

    /// <summary>
    /// Gets the complete current row. All columns are available.
    /// </summary>
    /// <remarks>
    /// Not ported. It is the row the walk stands on, and the walk is what is missing: a Get
    /// without Next would answer the same row forever, which is a result set that never ends.
    /// </remarks>
    /// <returns>the complete row</returns>
    public Result.Row? Get()
        => throw new TODOException("IndexCursor.get: the walk is not ported");

    /// <summary>
    /// Gets the current row. Only the data for indexed columns is available in this row.
    /// </summary>
    /// <remarks>
    /// Not ported, for the same reason as <see cref="Get"/>.
    /// </remarks>
    /// <returns>the search row</returns>
    public Result.SearchRow? GetSearchRow()
        => throw new TODOException("IndexCursor.getSearchRow: the walk is not ported");

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

    private readonly RowFactory rowFactory = RowFactory.GetDefaultRowFactory();

    /// <summary>
    /// Gets all columns.
    /// </summary>
    /// <returns>the columns</returns>
    public abstract Column[] GetColumns();

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
