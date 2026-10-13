/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.table.TableFilter.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A table as a query sees it: under an alias, at a position in FROM, read by a plan.
 *
 * There is one of these per *use* of a table, not per table. "SELECT * FROM TEST T1, TEST T2"
 * has two filters over one table, each with its own alias, its own position and its own cursor,
 * because each is read independently and a column reference has to say which of them it means.
 * This is why a query holds filters and not tables: T1.NAME and T2.NAME are different columns,
 * and the filter is what tells them apart.
 *
 * The constructor asks the session to check the right to read the table unless the caller says
 * the right was already checked. That check is where a statement that names a table it may not
 * read fails, which is before the table would be opened — a check that happened at run time
 * would have half-read the table first.
 *
 * The hash is an object id of the session, not of the database, because it is not an identity
 * among the catalogue's objects: it tells one filter of a query from another, and it must not
 * spend an id a table would need.
 *
 * This file is the foundation of the class: its fields, its constructor, and the members that
 * only read them. Everything that plans or moves — bestPlanItem, the cursor loop, the joins —
 * arrives member by member, each read from the reference before it is written.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Conditions;
using H2Sharp.Result;
using H2Sharp.Values;

namespace H2Sharp.Tables;

/// <summary>
/// A table filter represents a table that is used in a query. There is one such object for
/// every use of a table. For example, for the query "SELECT * FROM TEST T1, TEST T2", this will
/// contain two filter objects, one for table TEST T1 and one for table TEST T2.
/// </summary>
public class TableFilter : ColumnResolver
{
    private const int BeforeFirst = 0;

    private const int Found = 1;

    private const int AfterLast = 2;

    private const int NullRow = 3;

    private Result.SearchRow? currentSearchRow;

    private Result.Row? current;

    private int state;

    private bool foundOne;

    private int scanCount;

    /// <summary>
    /// Starts a query on this filter and everything joined to it.
    /// </summary>
    /// <remarks>
    /// The session is handed down because the filter that was built for one session may run on
    /// another: a prepared statement outlives the session that prepared it, and a filter holding
    /// the old one would read with the wrong transaction. The scan count starts over for the
    /// same reason a bound does — a leftover count would time out this run for the last one's
    /// rows.
    /// </remarks>
    /// <param name="s">the session</param>
    public void StartQuery(SessionLocal s)
    {
        ArgumentNullException.ThrowIfNull(s);
        session = s;
        scanCount = 0;
        if (nestedJoin is not null)
        {
            nestedJoin.StartQuery(s);
        }

        if (join is not null)
        {
            join.StartQuery(s);
        }
    }

    /// <summary>
    /// Resets to the current position.
    /// </summary>
    /// <remarks>
    /// The nested join and the join are reset first, because a scan that restarts in the middle
    /// would re-read the rows it already answered: the position is only "current" when every
    /// filter of the chain agrees where it is.
    /// </remarks>
    public void Reset()
    {
        if (nestedJoin is not null)
        {
            nestedJoin.Reset();
        }

        if (join is not null)
        {
            join.Reset();
        }

        state = BeforeFirst;
        foundOne = false;
    }

    /// <summary>
    /// Sets the null row, for the outer side of an outer join that matched nothing.
    /// </summary>
    /// <remarks>
    /// It is protected because only the scan itself may produce it: a caller that set a null
    /// row from outside would make the filter answer rows the join never saw. The nested join
    /// is visited rather than set, since it has its own null row to produce.
    /// </remarks>
    protected void SetNullRow()
    {
        state = NullRow;
        current = table.GetNullRow();
        currentSearchRow = current;
        if (nestedJoin is not null)
        {
            nestedJoin.Visit(new NullRowVisitor());
        }
    }

    /// <summary>
    /// Marks every filter of a join as holding the null row.
    /// </summary>
    private sealed class NullRowVisitor : TableFilterVisitor
    {
        public void Accept(TableFilter f)
        {
            ArgumentNullException.ThrowIfNull(f);
            f.SetNullRow();
        }
    }

    /// <summary>
    /// Whether the current value of the condition is true, or there is no condition.
    /// </summary>
    /// <param name="condition">the condition (null for no condition)</param>
    /// <returns>true if yes</returns>
    internal bool IsOk(Expression? condition)
    {
        return condition is null || condition.GetBooleanValue(GetSession());
    }

    /// <summary>
    /// Advances to the next row that survives the conditions.
    /// </summary>
    /// <remarks>
    /// The join is tried first when there is a row to continue from: a joined table that still
    /// has rows pairs them with the current one before anything moves, because moving on would
    /// drop pairs nobody asked to drop. Otherwise the cursor walks, and every row it finds is
    /// asked — the filter condition, then the join condition — before the join is walked for it.
    /// A row that fails either is skipped, not answered: answering it would hand the query a row
    /// its own WHERE refused.
    ///
    /// The null row is produced once, when the scan is over and the join is outer and nothing
    /// matched: it is the row the outer join exists to keep, and producing it twice would double
    /// every unmatched row.
    /// </remarks>
    /// <returns>true when there is a row</returns>
    public bool Next()
    {
        if (state == AfterLast)
        {
            return false;
        }

        if (state == BeforeFirst)
        {
            cursor.Find(GetSession(), indexConditions);
            if (!cursor.IsAlwaysFalse())
            {
                if (nestedJoin is not null)
                {
                    nestedJoin.Reset();
                }

                if (join is not null)
                {
                    join.Reset();
                }
            }
        }
        else
        {
            // state == FOUND || NULL_ROW
            // the last row was ok - try next row of the join
            if (join is not null && join.Next())
            {
                return true;
            }
        }

        while (true)
        {
            // go to the next row
            if (state == NullRow)
            {
                break;
            }

            if (cursor.IsAlwaysFalse())
            {
                state = AfterLast;
            }
            else if (nestedJoin is not null)
            {
                if (state == BeforeFirst)
                {
                    state = Found;
                }
            }
            else
            {
                if ((++scanCount & 4095) == 0)
                {
                    CheckTimeout();
                }

                if (cursor.Next())
                {
                    currentSearchRow = cursor.GetSearchRow();
                    current = null;
                    state = Found;
                }
                else
                {
                    state = AfterLast;
                }
            }

            if (nestedJoin is not null && state == Found)
            {
                if (!nestedJoin.Next())
                {
                    state = AfterLast;
                    if (joinOuter && !foundOne)
                    {
                        // possibly null row
                    }
                    else
                    {
                        continue;
                    }
                }
            }

            // if no more rows found, try the null row (for outer joins only)
            if (state == AfterLast)
            {
                if (joinOuter && !foundOne)
                {
                    SetNullRow();
                }
                else
                {
                    break;
                }
            }

            if (!IsOk(filterCondition))
            {
                continue;
            }

            bool joinConditionOk = IsOk(joinCondition);
            if (state == Found)
            {
                if (joinConditionOk)
                {
                    foundOne = true;
                }
                else
                {
                    continue;
                }
            }

            if (join is not null)
            {
                join.Reset();
                if (!join.Next())
                {
                    continue;
                }
            }

            // check if it's ok
            if (state == NullRow || joinConditionOk)
            {
                return true;
            }
        }

        state = AfterLast;
        return false;
    }

    /// <summary>
    /// Throws when the statement running this scan was canceled.
    /// </summary>
    /// <remarks>
    /// Empty, because there is nothing to cancel yet: the reference checks a deadline the
    /// cancel call sets, and neither the deadline nor the throttling around it is ported. A
    /// check that threw here would end every scan at its 4096th row, which is not what
    /// "not ported" means — it would be a scan that fails for being long.
    /// </remarks>
    private void CheckTimeout()
    {
    }

    /// <summary>
    /// Whether the cursor is on the null row of an outer join.
    /// </summary>
    /// <remarks>
    /// It is the state and nothing else: a null row is not an empty row and not a missing one,
    /// it is the row an outer join produces where the inner side had nothing. Anything that
    /// reads it as "no row" would drop the very rows the outer join exists to keep.
    /// </remarks>
    /// <returns>true when it is on the null row</returns>
    public bool IsNullRow() => state == NullRow;

    /// <summary>
    /// Gets the row the cursor is on.
    /// </summary>
    /// <remarks>
    /// The search row is enough when it is all there is; the full row is read from the cursor
    /// only when the cursor is actually standing on it. Reading it eagerly would fetch every
    /// row twice — once to find it, once to read it — and a scan that fetches twice runs half
    /// as fast for no reason.
    /// </remarks>
    /// <returns>the row, or null when there is none</returns>
    public Result.Row? Get()
    {
        if (current is null && currentSearchRow is not null)
        {
            current = cursor.Get();
        }

        return current;
    }

    /// <summary>
    /// Sets the row the cursor is on, and the search row with it.
    /// </summary>
    /// <remarks>
    /// Both at once, because they are two views of one position: the search row is what the
    /// index found, the row is what the table holds. Setting one without the other would leave
    /// the cursor standing between two different rows.
    /// </remarks>
    /// <param name="current">the row, or null</param>
    public void Set(Result.Row? current)
    {
        this.current = current;
        currentSearchRow = current;
    }

    /// <summary>
    /// Comparator that uses order in FROM clause as a sort key.
    /// </summary>
    public static readonly Comparison<TableFilter> OrderInFromComparator =
        (left, right) => left.orderInFrom.CompareTo(right.orderInFrom);

    /// <summary>
    /// Whether this filter is reached only through another one, which is what makes an outer
    /// join's outer side outer.
    /// </summary>
    protected bool joinOuterIndirect;

    private SessionLocal session;

    private readonly Table table;

    private readonly Command.Query.Select? select;

    private string? alias;

    private readonly IndexHints? indexHints;

    private int[]? masks;

    /// <summary>
    /// Gets the best plan item for this filter: the index to read and what it costs.
    /// </summary>
    /// <remarks>
    /// The scan is priced twice, on purpose. When nothing is fixed the scan-only plan
    /// <c>item1</c> is the fallback the mask-priced plan has to beat; when something is fixed
    /// there is no fallback and the mask-priced plan stands however it compares. The discount
    /// for many conditions pulls the heavily-fixed table earlier, so that joins without
    /// indexes still run quickly (issue 113). The join cost is the reference's own
    /// approximation — cost multiplied by the join's cost — kept as it stands, including its
    /// TODO to price row counts separately.
    /// </remarks>
    /// <param name="s">the session</param>
    /// <param name="filters">the filters of the query</param>
    /// <param name="filter">the position of this filter among them</param>
    /// <param name="allColumnsSet">the columns each table reads, or null</param>
    /// <param name="isSelectCommand">whether the caller is a select</param>
    /// <returns>the plan item</returns>
    public PlanItem GetBestPlanItem(
        SessionLocal s,
        TableFilter[] filters,
        int filter,
        Command.Query.AllColumnsForPlan? allColumnsSet,
        bool isSelectCommand)
    {
        PlanItem? item1 = null;
        SortOrder? sortOrder = null;
        if (select != null)
        {
            sortOrder = select.GetSortOrder();
        }
        if (indexConditions.Count == 0)
        {
            item1 = new PlanItem();
            item1.SetIndex(table.GetScanIndex(s, null, filters, filter,
                sortOrder, allColumnsSet));
            item1.Cost = item1.GetIndex()!.GetCost(s, null, filters, filter,
                sortOrder, allColumnsSet, isSelectCommand);
        }
        int len = table.GetColumns().Length;
        int[]? masks = new int[len];
        foreach (Indexes.IndexCondition condition in indexConditions)
        {
            if (condition.IsEvaluatable())
            {
                if (condition.IsAlwaysFalse())
                {
                    masks = null;
                    break;
                }
                if (condition.IsCompoundColumns())
                {
                    // Set the op mask in case of compound columns as well.
                    Column[]? columns = condition.GetColumns();
                    foreach (Column column in columns!)
                    {
                        int id = column.GetColumnId();
                        if (id >= 0)
                        {
                            masks[id] |= condition.GetMask(indexConditions);
                        }
                    }
                }
                else
                {
                    int id = condition.GetColumn()!.GetColumnId();
                    if (id >= 0)
                    {
                        masks[id] |= condition.GetMask(indexConditions);
                    }
                }
            }
        }
        PlanItem item = table.GetBestPlanItem(s, masks, filters, filter, sortOrder, allColumnsSet, isSelectCommand);
        item.SetMasks(masks);
        // The more index conditions, the earlier the table.
        // This is to ensure joins without indexes run quickly:
        // x (x.a=10); y (x.b=y.b) - see issue 113
        item.Cost -= item.Cost * indexConditions.Count / 100 / (filter + 1);
        if (item1 != null && item1.Cost < item.Cost)
        {
            item = item1;
        }
        if (nestedJoin != null)
        {
            SetEvaluatable(true);
            item.SetNestedJoinPlan(nestedJoin.GetBestPlanItem(s, filters, filter, allColumnsSet, isSelectCommand));
            // TODO optimizer: calculate cost of a join: should use separate
            // expected row number and lookup cost
            item.Cost += item.Cost * item.GetNestedJoinPlan()!.Cost;
        }
        if (join != null)
        {
            SetEvaluatable(true);
            do
            {
                filter++;
            } while (filters[filter] != join);
            item.SetJoinPlan(join.GetBestPlanItem(s, filters, filter, allColumnsSet, isSelectCommand));
            // TODO optimizer: calculate cost of a join: should use separate
            // expected row number and lookup cost
            item.Cost += item.Cost * item.GetJoinPlan()!.Cost;
        }
        return item;
    }
    /// <remarks>
    /// A null plan is not an error here but a plan that cannot be used — most likely because a
    /// column was not found — and the exception comes later, when the statement runs without
    /// one. Failing here would fail the statement that is still being read, for a table it has
    /// not finished naming.
    /// </remarks>
    /// <param name="item">the plan item, or null</param>
    public void SetPlanItem(PlanItem? item)
    {
        if (item is null)
        {
            // invalid plan, most likely because a column wasn't found
            // this will result in an exception later on
            return;
        }

        SetIndex(item.GetIndex()!, reverse: false);
        masks = item.GetMasks();
        if (nestedJoin is not null)
        {
            if (item.GetNestedJoinPlan() is not null)
            {
                nestedJoin.SetPlanItem(item.GetNestedJoinPlan());
            }
            else
            {
                nestedJoin.SetScanIndexes();
            }
        }

        if (join is not null)
        {
            if (item.GetJoinPlan() is not null)
            {
                join.SetPlanItem(item.GetJoinPlan());
            }
            else
            {
                join.SetScanIndexes();
            }
        }
    }

    /// <summary>
    /// Gets the masks of the columns the query has fixed.
    /// </summary>
    /// <returns>the masks, or null when the plan chose none</returns>
    public int[]? GetMasks() => masks;

    /// <summary>
    /// Points every filter without an index at a full scan, down the whole chain.
    /// </summary>
    /// <remarks>
    /// Only the filters that have none: a filter with a plan keeps it, since a plan the
    /// planner chose beats a scan by definition. This is the fallback the planner itself
    /// calls, not a default — a filter that scanned when it had a plan would answer slower
    /// than it was told to.
    /// </remarks>
    private void SetScanIndexes()
    {
        if (index is null)
        {
            SetIndex(table.GetScanIndex(GetSession())!, reverse: false);
        }

        if (join is not null)
        {
            join.SetScanIndexes();
        }

        if (nestedJoin is not null)
        {
            nestedJoin.SetScanIndexes();
        }
    }

    private H2Sharp.Indexes.Index? index;

    /// <summary>
    /// Sets the index this filter reads, and points the cursor at it.
    /// </summary>
    /// <remarks>
    /// Two assignments and neither is optional. The filter answers which index it reads and the
    /// cursor does the reading — a filter that kept one and let the cursor walk another would
    /// answer about an index it never read.
    /// </remarks>
    /// <param name="index">the index</param>
    /// <param name="reverse">whether to walk it backwards</param>
    public void SetIndex(H2Sharp.Indexes.Index index, bool reverse)
    {
        ArgumentNullException.ThrowIfNull(index);
        this.index = index;
        cursor.SetIndex(index, reverse);
    }

    /// <summary>
    /// Forgets the index conditions that cannot use the chosen index.
    /// </summary>
    /// <remarks>
    /// The list may be modified here, which is why it is walked by index and not by enumerator:
    /// removing while enumerating would fail, and copying it first would double every prepare.
    /// A compound condition found first takes the index and everything after it goes, because a
    /// compound lookup and another lookup cannot share one cursor -- the cursor holds one list.
    /// A simple condition stays when it names the index's first column, since that is the only
    /// column a scan can start from; anything else is a filter, not a bound.
    /// </remarks>
    public void Prepare()
    {
        // forget all unused index conditions
        // the indexConditions list may be modified here
        bool compoundIndexConditionFound = false;
        for (int i = 0; i < indexConditions.Count; i++)
        {
            Indexes.IndexCondition condition = indexConditions[i];
            if (!condition.IsAlwaysFalse())
            {
                if (compoundIndexConditionFound)
                {
                    // A compound index condition is already found. We cannot use other indexes with it, so removing
                    // everything else. The compound condition was added first.
                    // See: ConditionIn#createIndexConditions(SessionLocal, TableFilter)
                    indexConditions.RemoveAt(i);
                    i--;
                }
                else if (condition.IsCompoundColumns())
                {
                    if (index is not null && index.GetIndexType()?.IsScan() == true)
                    {
                        // This is only a pseudo index.
                        indexConditions.RemoveAt(i);
                        i--;
                        continue;
                    }

                    // Checking the columns match with the index.
                    if (IndexCursor.CanUseIndexForIn(index, condition.GetColumns()))
                    {
                        // The condition uses the exact columns in the right order.
                        compoundIndexConditionFound = true;
                        continue;
                    }

                    // Trying to fix the order of the condition columns.
                    Indexes.IndexCondition? fixedCondition = condition.CloneWithIndexColumns(index!);
                    if (fixedCondition is not null)
                    {
                        indexConditions[i] = fixedCondition;
                        compoundIndexConditionFound = true;
                        continue;
                    }

                    // Index condition cannot be used.
                    indexConditions.RemoveAt(i);
                    i--;
                }
                else
                {
                    Tables.Column? col = condition.GetColumn();
                    if (col is not null && col.GetColumnId() >= 0)
                    {
                        int columnIndex = index?.GetColumnIndex(col) ?? -1;
                        if (columnIndex == 0)
                        {
                            // The first column of the index always matches.
                            continue;
                        }

                        if (columnIndex < 0 || condition.GetCompareType() == H2Sharp.Expressions.Conditions.Comparison.InList)
                        {
                            // The index does not contain the column, or this is an IN() condition which can be used
                            // only if the first index column is the searched one.
                            // See: IndexCursor#canUseIndexFor(column)
                            indexConditions.RemoveAt(i);
                            i--;
                        }
                    }
                }
            }
        }

        if (nestedJoin is not null)
        {
            if (ReferenceEquals(nestedJoin, this))
            {
                throw DbException.GetInternalError("self join");
            }

            nestedJoin.Prepare();
        }

        if (join is not null)
        {
            if (ReferenceEquals(join, this))
            {
                throw DbException.GetInternalError("self join");
            }

            join.Prepare();
        }
    }
    /// <returns>the index, or null when the plan has not chosen one</returns>
    public H2Sharp.Indexes.Index? GetIndex() => index;

    /// <summary>
    /// The filter used to walk through the index.
    /// </summary>
    /// <remarks>
    /// Built here and not when the plan arrives, because the reference builds it in the
    /// constructor for the same reason: a filter without a cursor cannot be walked, and a plan
    /// that arrived before the cursor would have nowhere to point it.
    /// </remarks>
    private readonly IndexCursor cursor;

    /// <summary>
    /// The index conditions used for direct index lookup (start or end).
    /// </summary>
    private readonly List<Indexes.IndexCondition> indexConditions = [];

    /// <summary>
    /// Adds an index condition, to be used for direct index lookup.
    /// </summary>
    /// <remarks>
    /// It is only held here. Whether it becomes a lookup or a scan is decided later, when the
    /// plan is chosen — holding it now and using it then is what keeps a condition on an indexed
    /// column from being answered by reading the whole table.
    /// </remarks>
    /// <param name="condition">the condition</param>
    public void AddIndexCondition(Indexes.IndexCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        indexConditions.Add(condition);
    }

    private readonly int hashCode;

    private readonly int orderInFrom;

    /// <summary>
    /// Creates a filter over a table.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="table">the table</param>
    /// <param name="alias">the alias, or null</param>
    /// <param name="rightsChecked">whether the right to read the table was already checked</param>
    /// <param name="select">the select this filter belongs to, or null</param>
    /// <param name="orderInFrom">the position of this table in the FROM clause</param>
    /// <param name="indexHints">which indexes the caller allowed, or null</param>
    public TableFilter(
        SessionLocal session,
        Table table,
        string? alias,
        bool rightsChecked,
        Command.Query.Select? select,
        int orderInFrom,
        IndexHints? indexHints)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(table);
        this.session = session;
        this.table = table;
        this.alias = alias;
        this.select = select;
        this.cursor = new IndexCursor();

        if (!rightsChecked)
        {
            session.CheckReadRight(table);
        }

        hashCode = session.NextObjectId();
        this.orderInFrom = orderInFrom;
        this.indexHints = indexHints;
    }

    /// <summary>
    /// Get the order number (index) of this table filter in the "from" clause of the query.
    /// </summary>
    /// <returns>the index (0, 1, 2,...)</returns>
    public int GetOrderInFrom() => orderInFrom;

    /// <summary>
    /// Get the select statement this table filter belongs to, or null.
    /// </summary>
    /// <returns>the select statement, or null</returns>
    public Command.Query.Select? GetSelect() => select;

    /// <summary>
    /// Get the table.
    /// </summary>
    /// <returns>the table</returns>
    public Table GetTable() => table;

    /// <summary>
    /// Whether this filter has an alias.
    /// </summary>
    /// <returns>true if it has</returns>
    public bool HasAlias() => alias is not null;

    /// <summary>
    /// Get the table alias: the alias, or the table's own name when there is none.
    /// </summary>
    /// <remarks>
    /// The fallback is not a convenience. "SELECT NAME FROM TEST" reads through the same path
    /// as "SELECT T.NAME FROM TEST T": a column that was written without a qualifier still has
    /// to be answered as a column *of this table*, and the table's own name is the only name
    /// that must always mean it.
    /// </remarks>
    /// <returns>the alias</returns>
    public string GetTableAlias()
    {
        if (alias is not null)
        {
            return alias;
        }

        return table.GetTableName();
    }

    /// <summary>
    /// Whether this is the outer side of an outer join, directly or through a nested one.
    /// </summary>
    /// <returns>true if it is</returns>
    public bool IsJoinOuterIndirect() => joinOuterIndirect;

    /// <inheritdoc/>
    /// <remarks>
    /// The columns of the table, as in the reference. A derived table contributes its own, and
    /// that is the only case where this is answered differently — see <see cref="FindColumn"/>
    /// for why the two go together.
    /// </remarks>
    public Column[] GetColumns() => table.GetColumns();

    /// <summary>
    /// Finds a column by name: a derived column first, then the table's own.
    /// </summary>
    /// <remarks>
    /// Not ported. The derived map is where a view's own names live, and it is filled while the
    /// view is read; without it a name that only exists inside a view would fall through to the
    /// table behind it and be answered with the wrong column.
    /// </remarks>
    /// <param name="name">the name</param>
    /// <returns>the column</returns>
    public Column? FindColumn(string? name)
        => throw new TODOException("TableFilter.findColumn: the derived map is not filled yet");

    /// <summary>
    /// Reads a column out of the row the cursor is on.
    /// </summary>
    /// <remarks>
    /// Not ported. It is the row's own value, and answering it needs the search row the cursor
    /// found and the rules for a rowid — which is <c>ValueBigint.get(currentSearchRow.getKey())</c>
    /// when the column asks for the key rather than a value. Until that is here, a column read
    /// outside a scan would give the same answer as a failed read inside one, which is the
    /// failure this refuses rather than reports.
    /// </remarks>
    /// <param name="column">the column</param>
    /// <returns>the value</returns>
    public Value GetValue(Column column)
        => throw new TODOException("TableFilter.getValue: the cursor's row is not readable yet");

    /// <summary>
    /// Finds the rowid column of the table this filter reads.
    /// </summary>
    /// <remarks>
    /// Not ported. It is <c>table.getRowIdColumn()</c>, and that method is not ported either:
    /// answering without it would need knowing which column is the rowid, and there are three
    /// candidates the reference treats differently depending on the table.
    /// </remarks>
    /// <returns>the column</returns>
    public Column? GetRowIdColumn()
        => throw new TODOException("TableFilter.getRowIdColumn: Table.getRowIdColumn is not ported");

    /// <summary>
    /// Gets the filter this resolver reads from: itself.
    /// </summary>
    /// <returns>this filter</returns>
    public TableFilter? GetTableFilter() => this;

    /// <inheritdoc/>
    public override int GetHashCode() => hashCode;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => ReferenceEquals(this, obj);

    private Expression? filterCondition;

    private Expression? joinCondition;

    /// <summary>
    /// Adds a filter condition.
    /// </summary>
    /// <remarks>
    /// The first one is kept as it was written; every later one is ANDed onto it. That is not
    /// an optimisation, it is how the filter answers: there is one condition to check per row,
    /// and two conditions on one row are true together or not at all. A join condition and a
    /// filter condition are kept separately because they are answered at different moments — the
    /// join decides which rows pair up, the filter decides which of the pairs survive.
    /// </remarks>
    /// <param name="condition">the condition</param>
    /// <param name="isJoin">whether this is part of the join</param>
    public void AddFilterCondition(Expression? condition, bool isJoin)
    {
        if (isJoin)
        {
            joinCondition = joinCondition is null
                ? condition
                : new ConditionAndOr(ConditionAndOr.And, joinCondition, condition!);
        }
        else
        {
            filterCondition = filterCondition is null
                ? condition
                : new ConditionAndOr(ConditionAndOr.And, filterCondition, condition!);
        }
    }

    private bool evaluatable;

    /// <summary>
    /// Marks this filter and everything reachable from it as evaluatable or not.
    /// </summary>
    /// <remarks>
    /// The flag goes to the filter itself, to both of its conditions, to the nested join — but
    /// only when this *is* the filter being marked, because a nested join belongs to the join
    /// it sits in and enabling a filter of a joined filter must not switch it on — and then to
    /// the join. That asymmetry is the whole content of this method: a blanket "mark
    /// everything" would enable filters the query never asked to evaluate.
    /// </remarks>
    /// <param name="filter">the filter the marking started from</param>
    /// <param name="b">whether it is evaluatable</param>
    public void SetEvaluatable(TableFilter filter, bool b)
    {
        ArgumentNullException.ThrowIfNull(filter);
        filter.SetEvaluatable(b);
        if (filterCondition is not null)
        {
            filterCondition.SetEvaluatable(filter, b);
        }

        if (joinCondition is not null)
        {
            joinCondition.SetEvaluatable(filter, b);
        }

        if (nestedJoin is not null)
        {
            // don't enable / disable the nested join filters
            // if enabling a filter in a joined filter
            if (this == filter)
            {
                nestedJoin.SetEvaluatable(nestedJoin, b);
            }
        }

        if (join is not null)
        {
            join.SetEvaluatable(filter, b);
        }
    }

    /// <summary>
    /// Sets whether this filter can be evaluated without reading a row.
    /// </summary>
    /// <param name="evaluatable">true when it can</param>
    public void SetEvaluatable(bool evaluatable)
    {
        this.evaluatable = evaluatable;
    }
    /// <remarks>
    /// It is kept apart from the filter condition because the two are answered at different
    /// moments: the join decides which rows pair up, the filter which of the pairs survive. A
    /// caller that wants "the condition" without saying which one would get an answer that is
    /// true for the wrong half of the question.
    /// </remarks>
    /// <returns>the join condition, or null</returns>
    public Expression? GetJoinCondition() => joinCondition;

    /// <summary>
    /// Removes the joined table.
    /// </summary>
    /// <remarks>
    /// It only drops the link. The filter that was joined keeps its own conditions, because
    /// removing a join is not removing what was read through it — a query that forgets which
    /// rows paired up but remembers the pairing would answer from half a plan.
    /// </remarks>
    public void RemoveJoin() => join = null;

    /// <summary>
    /// Removes the join condition.
    /// </summary>
    /// <remarks>
    /// The filter stays joined; only the condition goes. That is for the planner, which tries a
    /// plan, measures it, and takes the condition back when the plan loses — the pairing is
    /// kept so that the next try starts from the same tables.
    /// </remarks>
    public void RemoveJoinCondition() => joinCondition = null;

    /// <summary>
    /// Pushes the condition over the whole join down into the filters that can answer it.
    /// </summary>
    /// <remarks>
    /// Only when this is not the outer side: the null row an outer join produces has no values
    /// to filter by, and a condition pushed into it would unmake rows the join was told to keep.
    /// The walk goes to the nested join and to the join for the same reason the condition was
    /// set on all of them — it constrains rows no single filter sees alone.
    /// </remarks>
    internal void OptimizeFullCondition()
    {
        if (!joinOuter && fullCondition is not null)
        {
            fullCondition.AddFilterConditions(this);
            if (nestedJoin is not null)
            {
                nestedJoin.OptimizeFullCondition();
            }

            if (join is not null)
            {
                join.OptimizeFullCondition();
            }
        }
    }
    /// <returns>the filter condition, or null</returns>
    public Expression? GetFilterCondition() => filterCondition;

    /// <summary>
    /// Forgets the filter condition.
    /// </summary>
    /// <remarks>
    /// It is the planner that does this, when it decides the condition is better answered by the
    /// index than by the filter: a condition left in both places would be asked twice, and a
    /// condition asked twice is asked once too many.
    /// </remarks>
    public void RemoveFilterCondition() => filterCondition = null;

    /// <summary>
    /// Joins another filter to this one.
    /// </summary>
    /// <remarks>
    /// The ON condition is mapped against both sides first, because a name inside it may belong
    /// to either table and reading it against one of them would answer with the other's column.
    /// The filter lands at the end of the join chain rather than here: "A JOIN B JOIN C" pairs
    /// A with B before C is even seen, and putting C anywhere but last would pair it with the
    /// wrong one.
    /// </remarks>
    /// <param name="filter">the joined filter</param>
    /// <param name="outer">whether the join is an outer one</param>
    /// <param name="on">the join condition</param>
    public void AddJoin(TableFilter filter, bool outer, Expression? on)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (on is not null)
        {
            MapColumnsVisitor visitor = new(on);
            Visit(visitor);
            filter.Visit(visitor);
        }

        if (join is null)
        {
            join = filter;
            filter.joinOuter = outer;
            if (outer)
            {
                filter.Visit(new JoinOuterVisitor());
            }

            if (on is not null)
            {
                filter.AddFilter(on);
            }
        }
        else
        {
            join.AddJoin(filter, outer, on);
        }
    }

    /// <summary>
    /// Adds a condition that is answered while joining.
    /// </summary>
    /// <remarks>
    /// It lands on this filter and on every filter it is joined to, because a join condition
    /// names columns of both sides and neither side can answer it alone. A condition that stayed
    /// on one side would be asked of rows it cannot see.
    /// </remarks>
    /// <param name="on">the condition</param>
    public void AddFilter(Expression? on)
    {
        AddFilterCondition(on, true);
        if (join is not null)
        {
            join.AddFilter(on);
        }
    }

    /// <summary>
    /// Something that is done to every filter of a join.
    /// </summary>
    public interface TableFilterVisitor
    {
        /// <summary>
        /// Called for each nested or joined table filter.
        /// </summary>
        /// <param name="f">the filter</param>
        void Accept(TableFilter f);
    }

    /// <summary>
    /// Visits this filter and every filter joined to it.
    /// </summary>
    /// <remarks>
    /// The nested join is visited before the chain moves on, because a filter nested inside a
    /// join belongs to the join it sits in — and visiting it after the chain would hand it to a
    /// query that has already walked past.
    /// </remarks>
    /// <param name="visitor">the visitor</param>
    public void Visit(TableFilterVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        TableFilter f = this;
        do
        {
            visitor.Accept(f);
            TableFilter? n = f.nestedJoin;
            if (n is not null)
            {
                n.Visit(visitor);
            }

            f = f.join!;
        }
        while (f is not null);
    }

    private TableFilter? join;

    private bool joinOuter;

    private TableFilter? nestedJoin;

    private Expression? fullCondition;

    /// <summary>
    /// Sets the condition over the whole join.
    /// </summary>
    /// <remarks>
    /// It lands here and on every filter this one is joined to, because a condition over the
    /// whole join constrains rows no single filter can see alone. A condition that stayed on one
    /// filter would be asked of rows outside its join, which is a different question with a
    /// different answer.
    /// </remarks>
    /// <param name="condition">the condition</param>
    public void SetFullCondition(Expression? condition)
    {
        fullCondition = condition;
        if (join is not null)
        {
            join.SetFullCondition(condition);
        }
    }

    /// <summary>
    /// Sets the filter this one is nested inside.
    /// </summary>
    /// <param name="filter">the filter</param>
    public void SetNestedJoin(TableFilter? filter)
    {
        nestedJoin = filter;
    }

    /// <summary>
    /// Get the filter this one is joined to.
    /// </summary>
    /// <returns>the joined filter, or null</returns>
    public TableFilter? GetJoin() => join;

    /// <summary>
    /// Whether this is the outer side of an outer join.
    /// </summary>
    /// <returns>true if it is</returns>
    public bool IsJoinOuter() => joinOuter;

    /// <summary>
    /// Maps an ON condition against the columns it names.
    /// </summary>
    /// <summary>
    /// Marks every filter of a join as the outer side of one.
    /// </summary>
    private sealed class JoinOuterVisitor : TableFilterVisitor
    {
        public void Accept(TableFilter f)
        {
            ArgumentNullException.ThrowIfNull(f);
            f.joinOuterIndirect = true;
        }
    }

    private sealed class MapColumnsVisitor : TableFilterVisitor
    {
        private readonly Expression on;

        internal MapColumnsVisitor(Expression on)
        {
            this.on = on;
        }

        public void Accept(TableFilter f)
        {
            ArgumentNullException.ThrowIfNull(f);
            on.MapColumns(f, 0, Expression.MapInitial);
        }
    }
    /// <summary>
    /// Maps the join condition's columns against the filters they name.
    /// </summary>
    /// <remarks>
    /// An outer filter reached from the inside is skipped, because its columns belong to rows
    /// the inner side never sees: mapping them would bind a name to a column of a row that does
    /// not exist yet. The nested join and the join are mapped after, in that order, since a name
    /// inside the condition may belong to either side and both have to be asked.
    /// </remarks>
    /// <param name="resolver">where unqualified names are looked up</param>
    /// <param name="level">the query level</param>
    /// <param name="outer">whether this call comes from outside the join</param>
    public void MapColumns(ColumnResolver resolver, int level, bool outer)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        if (!outer && joinOuter)
        {
            return;
        }

        if (joinCondition is not null)
        {
            joinCondition.MapColumns(resolver, level, Expressions.Expression.MapInitial);
        }

        if (nestedJoin is not null)
        {
            nestedJoin.MapColumns(resolver, level, outer);
        }

        if (join is not null)
        {
            join.MapColumns(resolver, level, outer);
        }
    }

    /// <summary>
    /// Creates the index conditions of the join condition, here and everywhere it reaches.
    /// </summary>
    /// <remarks>
    /// The condition is optimized first, because an index condition is only worth creating for
    /// a condition in the shape the index understands — and an unoptimized condition would
    /// create lookups for comparisons the scan would have answered cheaper. The nested join is
    /// asked with the same condition since it names columns of both sides, and the join after
    /// it, so that every filter holds the conditions it can serve.
    /// </remarks>
    public void CreateIndexConditions()
    {
        if (joinCondition is not null)
        {
            joinCondition = joinCondition.OptimizeCondition(GetSession());
            if (joinCondition is not null)
            {
                joinCondition.CreateIndexConditions(GetSession(), this);
                if (nestedJoin is not null)
                {
                    joinCondition.CreateIndexConditions(GetSession(), nestedJoin);
                }
            }
        }

        if (join is not null)
        {
            join.CreateIndexConditions();
        }

        if (nestedJoin is not null)
        {
            nestedJoin.CreateIndexConditions();
        }
    }

    /// <summary>
    /// Sets the alias of this filter.
    /// </summary>
    /// <remarks>
    /// It is set, not built, because the alias is what was written and not what was resolved:
    /// a filter renamed after it was read would answer column references against a name nobody
    /// wrote.
    /// </remarks>
    /// <param name="alias">the alias, or null</param>
    public void SetAlias(string? alias) => this.alias = alias;

    /// <inheritdoc/>
    /// <remarks>
    /// The alias when there is one, the table otherwise. It is the name this filter is known by
    /// in the query it belongs to — and a filter that printed its table when it has an alias
    /// would print a name the query never used.
    /// </remarks>
    public override string ToString() => alias ?? table.ToString()!;
    /// <remarks>
    /// They are kept, not applied, because applying them is the planner's job and not the
    /// filter's: a filter that chose its own index would answer about a plan it has not seen,
    /// and the hint that named one index would become a demand for it.
    /// </remarks>
    /// <returns>the hints, or null when the caller allowed everything</returns>
    public IndexHints? GetIndexHints() => indexHints;

    /// <summary>
    /// The names a derived table gives its own columns, and which column each name means.
    /// </summary>
    /// <remarks>
    /// A dictionary rather than the reference's linked map, with the same caveat as the shared
    /// columns above: insertion order is only read when the select list is built, which is not
    /// ported, and a reader that needs it will revisit this choice.
    /// </remarks>
    private Dictionary<Column, string>? derivedColumnMap;

    /// <summary>
    /// Whether this filter reads through names of its own rather than the table's.
    /// </summary>
    /// <returns>true when it has a derived column list</returns>
    public bool HasDerivedColumnList() => derivedColumnMap is not null;

    /// <summary>
    /// Sets the names this filter's columns are known by.
    /// </summary>
    /// <remarks>
    /// The count must match the columns, and no two names may be the same: a derived list that
    /// renamed two columns to one name would make both unfindable, and a list of the wrong
    /// length would leave columns unnamed. Both are the reference's errors, raised here rather
    /// than when the missing name is looked up — by then the query would have read rows it
    /// cannot name.
    /// </remarks>
    /// <param name="derivedColumnNames">the names, in column order</param>
    public void SetDerivedColumns(List<string> derivedColumnNames)
    {
        ArgumentNullException.ThrowIfNull(derivedColumnNames);
        Column[] columns = GetColumns();
        int count = columns.Length;
        if (count != derivedColumnNames.Count)
        {
            throw DbException.Get(Api.ErrorCode.ColumnCountDoesNotMatch);
        }

        Dictionary<Column, string> map = [];
        for (int i = 0; i < count; i++)
        {
            string alias = derivedColumnNames[i];
            for (int j = 0; j < i; j++)
            {
                if (alias.Equals(derivedColumnNames[j], StringComparison.Ordinal))
                {
                    throw DbException.Get(Api.ErrorCode.DuplicateColumnName1, alias);
                }
            }

            map[columns[i]] = alias;
        }

        derivedColumnMap = map;
    }

    /// <summary>
    /// The columns two sides of a NATURAL join or USING share, and what replaces them.
    /// </summary>
    /// <remarks>
    /// A dictionary rather than the reference's linked map, and the difference is on record:
    /// the reference iterates it in the order the columns were added, which is the order a
    /// NATURAL join's select list prints them in. Until that list is built, no reader depends
    /// on the order, and a reader that does will have to revisit this choice.
    /// </remarks>
    private Dictionary<Column, Column>? commonJoinColumns;

    private TableFilter? commonJoinColumnsFilter;

    private List<Column>? commonJoinColumnsToExclude;

    /// <summary>
    /// Adds a column shared by a NATURAL join or USING, and what replaces it.
    /// </summary>
    /// <remarks>
    /// The first call also fixes which filter the replacements come from, and every later call
    /// must name the same one: a shared column replaced by two different filters is two
    /// different columns, and the assert that catches it is the reference's, kept as an
    /// explicit check rather than a debug-only one.
    /// </remarks>
    /// <param name="leftColumn">the shared column</param>
    /// <param name="replacementColumn">what replaces it</param>
    /// <param name="replacementFilter">where the replacement comes from</param>
    public void AddCommonJoinColumns(Column leftColumn, Column replacementColumn, TableFilter replacementFilter)
    {
        ArgumentNullException.ThrowIfNull(leftColumn);
        ArgumentNullException.ThrowIfNull(replacementColumn);
        ArgumentNullException.ThrowIfNull(replacementFilter);
        if (commonJoinColumns is null)
        {
            commonJoinColumns = [];
            commonJoinColumnsFilter = replacementFilter;
        }
        else if (!ReferenceEquals(commonJoinColumnsFilter, replacementFilter))
        {
            throw DbException.GetInternalError("common join columns from two filters");
        }

        commonJoinColumns[leftColumn] = replacementColumn;
    }

    /// <summary>
    /// Adds a column to exclude from the shared columns.
    /// </summary>
    /// <param name="columnToExclude">the column to exclude</param>
    public void AddCommonJoinColumnToExclude(Column columnToExclude)
    {
        ArgumentNullException.ThrowIfNull(columnToExclude);
        commonJoinColumnsToExclude ??= [];
        commonJoinColumnsToExclude.Add(columnToExclude);
    }

    /// <summary>
    /// Returns the shared columns and their replacements.
    /// </summary>
    /// <returns>the map, or null when there is none</returns>
    public Dictionary<Column, Column>? GetCommonJoinColumns() => commonJoinColumns;

    /// <summary>
    /// Returns the filter the replacements come from.
    /// </summary>
    /// <returns>the filter, or null when there is none</returns>
    public TableFilter? GetCommonJoinColumnsFilter() => commonJoinColumnsFilter;

    /// <summary>
    /// Whether the column is excluded from the shared columns.
    /// </summary>
    /// <param name="c">the column</param>
    /// <returns>true when it is excluded</returns>
    public bool IsCommonJoinColumnToExclude(Column? c)
    {
        return c is not null && commonJoinColumnsToExclude is not null && commonJoinColumnsToExclude.Contains(c);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The derived name when the filter has one, the table's own otherwise. A view's columns
    /// hide the table's behind them: answering with the table's name when the view renamed the
    /// column would name something the query cannot see.
    /// </remarks>
    public string GetColumnName(Column column)
    {
        ArgumentNullException.ThrowIfNull(column);
        Dictionary<Column, string>? map = derivedColumnMap;
        return map is not null && map.TryGetValue(column, out string? name) ? name : column.GetName();
    }
    /// <summary>
    /// Forgets the index conditions no index can serve.
    /// </summary>
    /// <remarks>
    /// A condition with no mask names no column of the index, and one that cannot be evaluated
    /// without a row cannot narrow a scan: keeping either would make the planner price a lookup
    /// that reads every row, which is a scan with extra steps.
    /// </remarks>
    internal void RemoveUnusableIndexConditions()
    {
        // the indexConditions list may be modified here
        for (int i = 0; i < indexConditions.Count; i++)
        {
            Indexes.IndexCondition cond = indexConditions[i];
            if (cond.GetMask(indexConditions) == 0 || !cond.IsEvaluatable())
            {
                indexConditions.RemoveAt(i--);
            }
        }
    }

    /// <summary>
    /// Locks the table this filter reads, and everything joined to it.
    /// </summary>
    /// <remarks>
    /// The join is locked too, because a query that reads two tables while another statement
    /// writes one of them would join rows from two different moments. Locking only this filter
    /// would protect half the join and leave the other half to move.
    /// </remarks>
    /// <param name="s">the session</param>
    public virtual void Lock(SessionLocal s)
    {
        ArgumentNullException.ThrowIfNull(s);
        table.Lock(s, Table.ReadLock);
        if (join is not null)
        {
            join.Lock(s);
        }
    }

    /// <returns>the session</returns>
    public SessionLocal GetSession() => session;

    /// <summary>
    /// Whether this filter is used in the plan.
    /// </summary>
    /// <remarks>
    /// A filter that no plan uses is a table the query named and never read — which is legal SQL
    /// and a wasted scan, and the planner is the one that decides. Until it does, every filter
    /// is unused, and that is the honest default rather than a guess about the query.
    /// </remarks>
    /// <returns>true if it is used</returns>
    public bool IsUsed() => used;

    /// <summary>
    /// Sets whether this filter is used in the plan.
    /// </summary>
    /// <param name="used">true when the plan uses it</param>
    public void SetUsed(bool used) => this.used = used;
    private bool used;

    /// <summary>
    /// Gets the filter this one is nested inside.
    /// </summary>
    /// <returns>the nested filter, or null</returns>
    public TableFilter? GetNestedJoin() => nestedJoin;

    /// <summary>
    /// Whether this filter can be evaluated without reading a row.
    /// </summary>
    /// <remarks>
    /// It is a flag and not a question, because the answer is decided elsewhere — by the query
    /// that marks what it will read, and by the planner that takes conditions away. Asking the
    /// filter to decide would make it answer about a plan it has not seen.
    /// </remarks>
    /// <returns>true when it can</returns>
    public bool IsEvaluatable() => evaluatable;

    /// <param name="s">the session</param>
    public void SetSession(SessionLocal s)
    {
        ArgumentNullException.ThrowIfNull(s);
        session = s;
    }
}
