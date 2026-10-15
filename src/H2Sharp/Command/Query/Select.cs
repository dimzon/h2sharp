/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.query.Select.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * SELECT.
 *
 * The largest single class of the reference's command layer — over 1800 lines — and it is large
 * for a reason: it is the plan as well as the statement. It reads its own text, builds a list of
 * expressions, finds the tables behind them, asks the optimizer what to do, and runs the plan it
 * was given into a LocalResult. The optimizer is a separate class and the interesting part of the
 * whole thing.
 *
 * That is why nothing here is faked. A SELECT that answered rows without a plan would be a
 * different program that happens to share a name, and the first thing it would get wrong is the
 * one nobody checks first: which table it reads from, and in what order.
 *
 * The setters are the reference's, one per clause. They are here rather than one method with a
 * flag because the clauses are read in a fixed order and each one decides what the next may be:
 * HAVING before GROUP BY is a syntax error, and a parser that sets fields in any order would
 * accept it.
 *
 * Not ported: every member refuses.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions.Conditions;
using H2Sharp.Message;
using H2Sharp.Expressions;
using H2Sharp.Result;

using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Command.Query;

/// <summary>
/// A SELECT statement.
/// </summary>
public sealed class Select : Query
{
    private SessionLocal session;

    /// <summary>
    /// The select list, with hidden ORDER BY and grouping expressions appended past the
    /// visible columns by <see cref="Init"/>, as the reference grows its own list.
    /// </summary>
    private List<Expression> expressions = [];

    private bool isQuickAggregateQuery;

    private bool distinct;

    private bool isDistinct;

    private Expression? condition;

    private TableFilter? topTableFilter;

    private Expression? having;

    private List<Expression>? groupBy;

    private List<Expression>? windows;

    private bool isOrderByTop;

    /// <summary>
    /// Whether this is a grouping query: aggregates, GROUP BY or HAVING. The parser says so;
    /// the grouping machinery that reads it is not ported yet.
    /// </summary>
    private bool isGroupQuery;

    /// <summary>
    /// Whether init ran: a second run would resolve the order list twice and append its
    /// expressions twice, so it is refused as the reference refuses it.
    /// </summary>
    private bool checkInit;

    private long limit = -1;

    private long offset;

    private bool isFetchDistinct;

    private string? forUpdate;

    private readonly List<TableFilter> filters = [];

    private readonly List<TableFilter> topFilters = [];

    /// <summary>
    /// How many leading columns come from the index in order. Resolution builds it with the
    /// sort; until then every sort reads the whole result.
    /// </summary>
    private int indexSortedColumns = 0;

    /// <summary>
    /// Whether tied rows ride along past the limit.
    /// </summary>
    private bool withTies = false;

    /// <summary>
    /// The cost of the built plan.
    /// </summary>
    private double cost;

    /// <summary>
    /// Creates the statement for a session.
    /// </summary>
    /// <param name="session">the session</param>
    public Select(SessionLocal session)
        : base(session, string.Empty)
    {
        this.session = session;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The order block of the reference's init: the select list snippets the order is
    /// matched against, then the resolution of names to positions. The rest of init waits:
    /// star expansion, the distinct indexes, the having and qualify moves, the grouping
    /// indexes, and the column mapping pass — each needs machinery that is not here, and a
    /// half-mapped select would answer wrong rows rather than refuse.
    /// </remarks>
    public override void Init()
    {
        if (checkInit)
        {
            throw DbException.GetInternalError();
        }

        List<string>? expressionSQL = null;
        if (orderList is not null)
        {
            expressionSQL = new(visibleColumnCount);
            for (int i = 0; i < visibleColumnCount; i++)
            {
                Expression expr = expressions[i].GetNonAliasExpression();
                expressionSQL.Add(expr.GetSql(HasSql.DefaultSqlFlags, Expression.WithoutParentheses));
            }
        }

        if (orderList is not null)
        {
            _ = InitOrder(expressionSQL, IsAnyDistinct(), filters, expressions);
        }

        // map columns in select list and condition
        foreach (TableFilter f in filters)
        {
            MapColumns(f, 0, false);
        }

        checkInit = true;
    }

    /// <summary>
    /// Whether this query is a DISTINCT or DISTINCT ON query.
    /// </summary>
    /// <remarks>
    /// The reference also counts its distinct-expression list; that list is not ported, and
    /// the DISTINCT ON marker stands in its place until it is.
    /// </remarks>
    /// <returns>true for either distinct flavor</returns>
    public bool IsAnyDistinct() => distinct || isDistinct;

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, bool outer)
    {
        foreach (Expression e in expressions)
        {
            e.MapColumns(resolver, level, Expression.MapInitial);
        }

        if (condition is not null)
        {
            condition.MapColumns(resolver, level, Expression.MapInitial);
        }

        foreach (TableFilter tableFilter in topFilters)
        {
            tableFilter.MapColumns(resolver, level, outer);
        }
    }

    /// <inheritdoc/>
    public override void Optimize(SessionLocal session, TableFilter? targetTableFilter)
        => throw new TODOException("Select.optimize: the optimizer is not ported");

    /// <inheritdoc/>
    /// <remarks>
    /// What runs here is the reference's shape — outer joins keep their conditions out of
    /// the indexes, then the optimizer prices and prepares, then a sorted index replaces
    /// the chosen one when it serves the order. The distinct fast path between the costing
    /// and the sort waits for the per-column index lookup and selectivity.
    /// </remarks>
    public override void PreparePlan(SessionLocal session, TableFilter? targetTableFilter)
    {
        if (condition is not null)
        {
            foreach (TableFilter f in filters)
            {
                // outer joins: must not add index conditions such as
                // "c is null" - example:
                // create table parent(p int primary key) as select 1;
                // create table child(c int primary key, pc int);
                // insert into child values(2, 1);
                // select p, c from parent
                // left outer join child on p = pc where c is null;
                if (!f.IsJoinOuter() && !f.IsJoinOuterIndirect())
                {
                    condition.CreateIndexConditions(session, f);
                }
            }
        }

        cost = PreparePlan(session.IsParsingCreateView());

        if (sort is not null && !isQuickAggregateQuery && !isGroupQuery)
        {
            List<Indexes.IndexSort>? sortIndexes = GetIndexSorts();
            Indexes.Index? current = topTableFilter?.GetIndex();
            if (sortIndexes is not null && current is not null && topTableFilter is not null)
            {
                foreach (Indexes.IndexSort sortIndex in sortIndexes)
                {
                    Indexes.Index? index = sortIndex.GetIndex();
                    // A null index never materializes here — every table has its scan — but
                    // the list can hold one where the reference holds a scan, so it is
                    // skipped rather than handed to a setter that refuses nulls.
                    if (index is null)
                    {
                        continue;
                    }

                    bool reverse = sortIndex.IsReverse();
                    if (current.GetIndexType()?.IsScan() == true || current == index)
                    {
                        topTableFilter.SetIndex(index, reverse);
                        indexSortedColumns = sortIndex.GetSortedColumns();
                        break;
                    }
                    else
                    {
                        IndexColumn[] sortColumns = index.GetColumns();
                        IndexColumn[] currentColumns = current.GetColumns();
                        if (sortColumns.Length >= currentColumns.Length)
                        {
                            bool swapIndex = false;
                            bool sameColumns = true;
                            for (int i = 0; i < currentColumns.Length; i++)
                            {
                                if (!ReferenceEquals(sortColumns[i].GetColumn(), currentColumns[i].GetColumn()))
                                {
                                    sameColumns = false;
                                    break;
                                }

                                if (sortColumns[i].SortType != currentColumns[i].SortType)
                                {
                                    swapIndex = true;
                                }
                            }

                            if (sameColumns && swapIndex)
                            {
                                topTableFilter.SetIndex(index, reverse);
                                indexSortedColumns = sortIndex.GetSortedColumns();
                                break;
                            }
                        }
                    }
                }
            }

            if (indexSortedColumns > 0 && forUpdate is not null
                && topTableFilter?.GetIndex()?.IsRowIdIndex() == false)
            {
                indexSortedColumns = 0;
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Not ported. In the reference this walks the expressions, the condition, the having and
    /// the tables, answering per visitor kind -- and each branch needs machinery that is not
    /// here (table determinism, modification ids). A stub that answered true would mark every
    /// query cacheable and optimizable whether it is or not.
    /// </remarks>
    public override bool IsEverything(ExpressionVisitor visitor)
        => throw new TODOException("Select.isEverything: the branches need unported machinery");

    /// <inheritdoc/>
    /// <remarks>
    /// A select is cacheable unless it locks: FOR UPDATE takes row locks that belong to one run of
    /// the statement, and handing back a cached command would hand back locks nobody asked for.
    /// </remarks>
    public override bool IsCacheable() => forUpdate is null;

    /// <inheritdoc/>
    /// <remarks>
    /// The hierarchy types plan costs as whole numbers, so the fraction is dropped. Nothing
    /// prices on it yet; the field keeps the full answer for when something does.
    /// </remarks>
    public override long GetCost() => (long)cost;

    /// <inheritdoc/>
    public override HashSet<DbObject> GetDependencies()
        => throw new TODOException("Select.getDependencies: the plan is not built");

    /// <inheritdoc/>
    public override bool IsUnion() => false;

    /// <inheritdoc/>
    public override int GetUnionType()
        => throw new TODOException("Select.getUnionType: a select is not a union");

    /// <inheritdoc/>
    public override StringBuilder GetPlanSql(StringBuilder builder, int sqlFlags)
        => throw new TODOException("Select.getPlanSQL: the plan is not built");

    /// <inheritdoc/>
    /// <remarks>
    /// The visible columns, not the whole list: <see cref="Init"/> may append hidden ORDER
    /// BY expressions past them, and those are not columns of the query.
    /// </remarks>
    public override Expression[] GetExpressions() => [.. expressions];

    /// <inheritdoc/>
    public override int GetColumnCount() => visibleColumnCount;

    /// <inheritdoc/>
    public override long GetRowCount(SessionLocal session, List<TableFilter> filters, long limit)
        => throw new TODOException("Select.getRowCount: the plan is not built");

    /// <inheritdoc/>
    /// <remarks>
    /// The order half of the reference's method: the order list becomes the sort, and the
    /// sort drops what optimization proved constant. The expression half waits for the
    /// mode machinery: the reference folds the select list through
    /// <c>optimizeExpressionsAndPreserveAliases</c> in two of its modes and through a plain
    /// optimize in the rest, and only the plain path is taken here. The condition is
    /// optimized as there. The quick aggregate shortcut and the expression array snapshot
    /// wait for their own machinery.
    /// </remarks>
    public override void PrepareExpressions()
    {
        if (orderList is not null)
        {
            PrepareOrder(orderList, expressions.Count);
        }

        for (int i = 0; i < expressions.Count; i++)
        {
            expressions[i] = expressions[i].Optimize(session);
        }

        if (sort is not null)
        {
            CleanupOrder(expressions);
        }

        if (condition is not null)
        {
            condition = condition.OptimizeCondition(session);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Flat queries only: grouped, sorted, distinct, windowed and locking shapes refuse below,
    /// each waiting for its own machinery. What runs here is the reference's flat path — skip
    /// the offset, fill the result, trim it — with lazy execution left out: every result here
    /// is materialized, and a lazy one would hand back a walk over tables the caller may close.
    /// </remarks>
    public override ResultInterface Run(long limit)
    {
        if (groupBy is not null || windows is not null || distinct
            || isDistinct || forUpdate is not null)
        {
            throw new TODOException("Select.query: grouped, distinct, windowed and locking queries are not ported");
        }

        long offset = this.offset;
        long fetch = limit >= 0 ? limit : this.limit;
        int columnCount = expressions.Count;
        Expression[] expressionArray = [.. expressions];
        LocalResult result = new(session, expressionArray, visibleColumnCount, expressionArray.Length);
        topTableFilter!.StartQuery(session);
        topTableFilter.Reset();
        topTableFilter.Lock(session);
        QuickOffset quickOffset = QuickOffset.Yes;
        if (sort is not null && indexSortedColumns != Indexes.IndexSort.FullySorted)
        {
            result.SetSortOrder(sort);
            // PARTIAL waits for index-sort resolution: without a sorted index the offset cannot
            // be skipped up front, the result trims it after the sort.
            quickOffset = QuickOffset.No;
        }

        QueryFlat(columnCount, result, offset, fetch, withTies, quickOffset);
        if (quickOffset == QuickOffset.Yes)
        {
            offset = 0;
        }

        if (fetch >= 0)
        {
            result.SetLimit(fetch);
        }

        if (offset != 0)
        {
            result.SetOffset(offset);
        }

        result.Done();
        return result;
    }

    /// <summary>
    /// Reads the flat rows into the result, skipping the offset on the way when allowed.
    /// </summary>
    /// <param name="columnCount">how many columns each row has</param>
    /// <param name="result">where the rows go, or null for a lazy walk</param>
    /// <param name="offset">how many rows to skip</param>
    /// <param name="limitRows">how many rows to read, or negative for all</param>
    /// <param name="withTies">whether tied rows ride along past the limit</param>
    /// <param name="quickOffset">how the offset is skipped</param>
    /// <returns>the lazy walk, when no result was given</returns>
    private LazyResult? QueryFlat(
        int columnCount,
        ResultTarget? result,
        long offset,
        long limitRows,
        bool withTies,
        QuickOffset quickOffset)
    {
        if (limitRows > 0 && offset > 0 && quickOffset != QuickOffset.Yes)
        {
            limitRows += offset;
            if (limitRows < 0)
            {
                // Overflow
                limitRows = long.MaxValue;
            }
        }

        LazyResultQueryFlat lazyResult = new(this, [.. expressions], columnCount, forUpdate is not null);
        SkipOffset(lazyResult, offset, quickOffset == QuickOffset.Yes);
        if (result is null)
        {
            return lazyResult;
        }

        if (limitRows == long.MaxValue || limitRows < 0 || sort is not null && indexSortedColumns == 0
            || withTies && quickOffset == QuickOffset.No)
        {
            while (lazyResult.Next())
            {
                result.AddRow(lazyResult.CurrentRow());
            }
        }
        else
        {
            ReadWithLimit(result, limitRows, withTies, lazyResult);
        }

        return null;
    }

    /// <summary>
    /// Reads at most so many rows, plus the ties when they ride along.
    /// </summary>
    /// <param name="result">where the rows go</param>
    /// <param name="limitRows">how many rows to read</param>
    /// <param name="withTies">whether tied rows ride along past the limit</param>
    /// <param name="lazyResult">the walk</param>
    private void ReadWithLimit(ResultTarget result, long limitRows, bool withTies, LazyResultQueryFlat lazyResult)
    {
        Value?[]? last = null;
        while (result.GetRowCount() < limitRows && lazyResult.Next())
        {
            last = lazyResult.CurrentRow();
            result.AddRow(last);
        }

        if (sort is not null && last is not null)
        {
            if (indexSortedColumns < Indexes.IndexSort.FullySorted)
            {
                while (lazyResult.Next())
                {
                    Value?[] row = lazyResult.CurrentRow();
                    if (sort.Compare(last, row, indexSortedColumns) != 0)
                    {
                        break;
                    }

                    result.AddRow(row);
                }
            }
            else if (withTies)
            {
                while (lazyResult.Next())
                {
                    Value?[] row = lazyResult.CurrentRow();
                    if (sort.Compare(last, row) != 0)
                    {
                        break;
                    }

                    result.AddRow(row);
                }

                result.LimitsWereApplied();
            }
        }
    }

    /// <summary>
    /// Whether the offset was skipped while walking rather than trimmed afterwards.
    /// </summary>
    private enum QuickOffset
    {
        No,
        Yes,
        Partial,
    }

    /// <summary>
    /// Skips the offset rows of a lazy walk.
    /// </summary>
    /// <param name="lazyResult">the walk</param>
    /// <param name="offset">how many rows to skip</param>
    /// <param name="quickOffset">whether skipping while walking is allowed</param>
    private static void SkipOffset(LazyResultSelect lazyResult, long offset, bool quickOffset)
    {
        if (quickOffset)
        {
            while (offset > 0 && lazyResult.Skip())
            {
                offset--;
            }
        }
    }

    /// <summary>
    /// Lazy execution support for a select: the filter walk plus the row number.
    /// </summary>
    /// <remarks>
    /// Nested as in the reference, reading the statement it runs for: the filter to walk, the
    /// visible count to report, and the row number the ROWNUM function reads. C# nested classes
    /// do not capture the outer instance, so it arrives as a constructor argument where Java
    /// takes it for granted.
    /// </remarks>
    private abstract class LazyResultSelect : LazyResult
    {
        protected long rowNumber;

        protected int columnCount;

        protected readonly Select select;

        protected LazyResultSelect(Select select, Expression[] expressions, int columnCount)
            : base(select.GetSession(), expressions)
        {
            this.select = select;
            this.columnCount = columnCount;
            select.SetCurrentRowNumber(0);
        }

        /// <inheritdoc/>
        public override int GetVisibleColumnCount() => select.visibleColumnCount;

        /// <inheritdoc/>
        public override void Reset()
        {
            base.Reset();
            select.topTableFilter!.Reset();
            select.SetCurrentRowNumber(0);
            rowNumber = 0;
        }
    }

    /// <summary>
    /// Lazy execution for a flat query.
    /// </summary>
    private sealed class LazyResultQueryFlat : LazyResultSelect
    {
        private readonly bool forUpdate;

        internal LazyResultQueryFlat(Select select, Expression[] expressions, int columnCount, bool forUpdate)
            : base(select, expressions, columnCount) => this.forUpdate = forUpdate;

        /// <inheritdoc/>
        protected override Value?[]? FetchNextRow()
        {
            TableFilter top = select.topTableFilter!;
            while (top.Next())
            {
                select.SetCurrentRowNumber(rowNumber + 1);
                // This method may lock rows
                if (forUpdate ? select.IsConditionMetForUpdate() : select.IsConditionMet())
                {
                    ++rowNumber;
                    Value?[] row = new Value?[columnCount];
                    for (int i = 0; i < columnCount; i++)
                    {
                        Expression expr = expressions[i];
                        row[i] = expr.GetValue(select.GetSession());
                    }

                    return row;
                }
            }

            return null;
        }

        /// <inheritdoc/>
        protected override bool SkipNextRow()
        {
            TableFilter top = select.topTableFilter!;
            while (top.Next())
            {
                select.SetCurrentRowNumber(rowNumber + 1);
                // This method does not lock rows
                if (select.IsConditionMet())
                {
                    ++rowNumber;
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc/>
        public override void Dispose() => Close();
    }

    /// <summary>
    /// Whether the current row survives the WHERE clause.
    /// </summary>
    /// <returns>true when there is no clause or it holds</returns>
    private bool IsConditionMet() => condition is null || condition.GetBooleanValue(session);

    /// <summary>
    /// Whether the current row survives locking for update.
    /// </summary>
    /// <remarks>
    /// Not ported: it locks the row in every table and re-checks the condition on what came
    /// back, and row locking does not exist here yet.
    /// </remarks>
    /// <returns>never returns</returns>
    private bool IsConditionMetForUpdate()
        => throw new TODOException("Select.isConditionMetForUpdate: row locking is not ported");

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.Select;

    /// <inheritdoc/>
    public override ResultInterface? QueryMeta() => null;

    /// <inheritdoc/>
    public override bool IsQuery() => true;

    /// <inheritdoc/>
    public override bool IsTransactional() => true;

    /// <summary>Sets the condition on the grouped rows.</summary>
    /// <param name="having">the condition</param>
    public void SetHaving(Expression? having) => this.having = having;

    /// <summary>Sets the columns to group by.</summary>
    /// <param name="groupBy">the expressions</param>
    public void SetGroupBy(List<Expression>? groupBy) => this.groupBy = groupBy;

    /// <summary>
    /// Set when SELECT statement contains (non-window) aggregate functions,
    /// GROUP BY clause or HAVING clause.
    /// </summary>
    public void SetGroupQuery() => isGroupQuery = true;

    /// <summary>Sets the window functions.</summary>
    /// <param name="windows">the expressions</param>
    public void SetWindows(List<Expression>? windows) => this.windows = windows;

    /// <summary>Sets whether this is ORDER BY ... TOP.</summary>
    /// <param name="isOrderByTop">true for ORDER BY ... TOP</param>
    public void SetOrderByTop(bool isOrderByTop) => this.isOrderByTop = isOrderByTop;

    /// <summary>Sets the maximum number of rows.</summary>
    /// <param name="limit">the limit, -1 for no limit</param>
    public void SetLimit(long limit) => this.limit = limit;

    /// <summary>Sets the number of rows to skip.</summary>
    /// <param name="offset">the offset</param>
    public void SetOffset(long offset) => this.offset = offset;

    /// <summary>Sets whether FETCH is DISTINCT.</summary>
    /// <param name="isFetchDistinct">true for FETCH DISTINCT</param>
    public void SetFetchDistinct(bool isFetchDistinct) => this.isFetchDistinct = isFetchDistinct;

    /// <summary>Sets the table this statement locks.</summary>
    /// <param name="forUpdate">the table name</param>
    public void SetForUpdate(string? forUpdate) => this.forUpdate = forUpdate;

    /// <summary>Sets the table filters of the plan.</summary>
    /// <param name="filters">the filters</param>
    public void SetFilters(TableFilter[]? filters)
    {
        this.filters.Clear();
        if (filters is not null)
        {
            this.filters.AddRange(filters);
        }
    }

    /// <summary>Sets the expressions this statement selects.</summary>
    /// <param name="expressions">the expressions</param>
    public void SetExpressions(Expression[] expressions)
    {
        this.expressions = [.. expressions];
        visibleColumnCount = expressions.Length;
    }

    /// <summary>
    /// How many of the expressions are visible columns. Fixed here because the parser hands
    /// over the finished select list; <see cref="Init"/> may append hidden ORDER BY
    /// expressions past them, as the reference grows its own list in init.
    /// </summary>
    private int visibleColumnCount;

    /// <summary>Sets whether this is an aggregate over the whole table.</summary>
    /// <param name="isQuick">true for a quick aggregate</param>
    public void SetQuickAggregateQuery(bool isQuick) => isQuickAggregateQuery = isQuick;

    /// <summary>Sets whether duplicate rows are removed.</summary>
    /// <param name="distinct">true for DISTINCT</param>
    public void SetDistinct(bool distinct) => this.distinct = distinct;

    /// <summary>Sets whether the columns of this statement are distinct.</summary>
    /// <param name="isDistinct">true for DISTINCT ON</param>
    public void SetIsDistinct(bool isDistinct) => this.isDistinct = isDistinct;

    /// <summary>
    /// Adds a table filter to this query.
    /// </summary>
    /// <remarks>
    /// Two filters with the same alias are refused, unless neither has one: tables without
    /// aliases may share a name across schemas, and an alias is a promise that the name means
    /// one table. That promise is checked here, when the filter arrives, rather than when the
    /// query runs — by then the query would have read rows it cannot tell apart.
    /// </remarks>
    /// <param name="filter">the filter</param>
    /// <param name="isTop">whether it reads a top-level table</param>
    public void AddTableFilter(TableFilter filter, bool isTop)
    {
        ArgumentNullException.ThrowIfNull(filter);
        Database db = session.GetDatabase();
        string alias = filter.GetTableAlias();
        foreach (TableFilter f in filters)
        {
            // Tables without aliases are not checked, they may have the same name in different schemas
            if ((filter.HasAlias() || f.HasAlias()) && db.EqualsIdentifiers(alias, f.GetTableAlias()))
            {
                throw DbException.Get(Api.ErrorCode.DuplicateTableAlias1, alias);
            }
        }

        filters.Add(filter);
        if (isTop)
        {
            topFilters.Add(filter);
        }
    }

    /// <summary>
    /// Adds a condition to the list of conditions.
    /// </summary>
    /// <remarks>
    /// The first one is kept as written and every later one ANDs onto it, for the same reason
    /// the filter keeps one condition per row: there is one WHERE clause, and two conditions on
    /// one row are true together or not at all.
    /// </remarks>
    /// <param name="cond">the condition to add</param>
    public void AddCondition(Expression cond)
    {
        ArgumentNullException.ThrowIfNull(cond);
        condition = condition is null
            ? cond
            : new ConditionAndOr(ConditionAndOr.And, cond, condition);
    }

    /// <summary>
    /// Gets the condition of the WHERE clause.
    /// </summary>
    /// <returns>the condition, or null</returns>
    public Expression? GetCondition() => condition;

    /// <summary>
    /// Builds the plan and marks everything in it evaluatable.
    /// </summary>
    /// <remarks>
    /// The index conditions are created first, because the plan is priced on lookups and there
    /// is nothing to price before they exist. Then the optimizer picks the order, the top
    /// filter is where the scan starts, and everything is marked evaluatable — except when this
    /// is only parsing a view, in which case the filters are joined but never prepared: a view
    /// parsed now runs later, against tables that may have changed since.
    /// </remarks>
    /// <param name="parse">whether this is only parsing, without running</param>
    /// <returns>the cost of the plan</returns>
    public double PreparePlan(bool parse)
    {
        List<TableFilter> topArray = [.. topFilters];
        foreach (TableFilter t in topArray)
        {
            t.CreateIndexConditions();
            t.SetFullCondition(condition);
        }

        Optimizer optimizer = new([.. topArray], condition, session);
        optimizer.Optimize(parse, isSelectCommand: true);
        topTableFilter = optimizer.GetTopFilter();
        double planCost = optimizer.GetCost();

        SetEvaluatableRecursive(topTableFilter);

        if (!parse)
        {
            topTableFilter!.Prepare();
        }

        return planCost;
    }

    /// <summary>
    /// Returns possible index-sorting operations (better first) if they exist.
    /// </summary>
    /// <returns>possible index-sorting operations, or null if unavailable</returns>
    private List<Indexes.IndexSort>? GetIndexSorts()
    {
        if (sort is null)
        {
            return null;
        }

        List<Column?> sortColumns = [];
        int[] queryColumnIndexes = sort.GetQueryColumnIndexes();
        int queryIndexesLength = queryColumnIndexes.Length;
        int[] sortIndex = new int[queryIndexesLength];
        int sortedColumns = 0;
        bool needMore = false;
        for (int i = 0; i < queryIndexesLength; i++)
        {
            int idx = queryColumnIndexes[i];
            if (idx < 0 || idx >= expressions.Count)
            {
                throw DbException.GetInvalidValueException("ORDER BY", idx + 1);
            }

            Expression expr = expressions[idx].GetNonAliasExpression();
            if (expr.IsConstant())
            {
                continue;
            }

            if (expr is not ExpressionColumn exprCol)
            {
                needMore = true;
                break;
            }

            if (exprCol.GetTableFilter() != topTableFilter)
            {
                needMore = true;
                break;
            }

            Column? sortColumn = exprCol.GetColumn();
            sortColumns.Add(sortColumn);
            sortIndex[sortedColumns++] = i;
        }

        if (sortedColumns == 0)
        {
            if (needMore)
            {
                // Can't sort using index
                return null;
            }

            // sort just on constants - can use scan index
            return [new Indexes.IndexSort(topTableFilter!.GetTable().GetScanIndex(session), false)];
        }

        Column?[] sortCols;
        int[] sortTypes = sort.GetSortTypesWithNullOrdering();
        if (sortedColumns == 1)
        {
            Column? column = sortColumns[0];
            if (column is not null && column.GetColumnId() == SearchRow.RowidIndex)
            {
                // special case: order by _ROWID_
                Indexes.Index? index = topTableFilter!.GetTable().GetScanIndex(session);
                if (index?.IsRowIdIndex() == true)
                {
                    return [new Indexes.IndexSort(
                        index, needMore ? sortedColumns : Indexes.IndexSort.FullySorted,
                        (sortTypes[sortIndex[0]] & SortOrder.Descending) != 0)];
                }
            }

            sortCols = [column];
        }
        else
        {
            sortCols = [.. sortColumns];
        }

        DefaultNullOrdering defaultNullOrdering = session.GetDatabase().GetDefaultNullOrdering();
        List<Indexes.IndexSort> indexSorts = [];
        foreach (Indexes.Index index in topTableFilter!.GetTable().GetIndexes())
        {
            if (index.GetCreateSql() is null || index.GetIndexType()?.IsHash() == true)
            {
                // can't use scan or hash indexes
                continue;
            }

            IndexColumn[] indexCols = index.GetColumns();
            int count = Math.Min(indexCols.Length, sortedColumns);
            bool reverse = false;
            bool matched = true;
            for (int j = 0; j < count; j++)
            {
                // the index and the sort order must start
                // with the exact same columns
                IndexColumn idxCol = indexCols[j];
                Column? sortCol = sortCols[j];
                // A null sort column matches nothing: the reference compares the two
                // references, where null is never the index's column.
                bool mismatch = sortCol is null || !ReferenceEquals(idxCol.GetColumn(), sortCol);
                if (!mismatch)
                {
                    // Not null: a null sort column mismatches above.
                    if (sortCol!.IsNullable() == Column.Nullable)
                    {
                        int o1 = DefaultNullOrderingRules.AddExplicitNullOrdering(
                            defaultNullOrdering, idxCol.SortType);
                        int o2 = sortTypes[sortIndex[j]];
                        if (j == 0)
                        {
                            if (o1 != o2)
                            {
                                if (o1 == SortOrder.Inverse(o2))
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
                            if (o1 != (reverse ? SortOrder.Inverse(o2) : o2))
                            {
                                mismatch = true;
                            }
                        }
                    }
                    else
                    {
                        bool different = (idxCol.SortType & SortOrder.Descending)
                            != (sortTypes[sortIndex[j]] & SortOrder.Descending);
                        if (j == 0)
                        {
                            reverse = different;
                        }
                        else
                        {
                            mismatch = different != reverse;
                        }
                    }
                }

                if (mismatch)
                {
                    if (j > 0)
                    {
                        indexSorts.Add(new Indexes.IndexSort(index, j, reverse));
                    }

                    matched = false;
                    break;
                }
            }

            if (!matched)
            {
                continue;
            }

            indexSorts.Add(new Indexes.IndexSort(
                index, needMore || count < sortedColumns ? count : Indexes.IndexSort.FullySorted,
                reverse));
        }

        indexSorts.Sort();
        return indexSorts;
    }

    /// <summary>
    /// Marks a filter and everything under it as evaluatable.
    /// </summary>
    /// <param name="f">the filter, or null</param>
    private void SetEvaluatableRecursive(TableFilter? f)
    {
        for (; f is not null; f = f.GetJoin())
        {
            f.SetEvaluatable(f, true);
            if (condition is not null)
            {
                condition.SetEvaluatable(f, true);
            }

            TableFilter? n = f.GetNestedJoin();
            if (n is not null)
            {
                SetEvaluatableRecursive(n);
            }

            Expression? on = f.GetJoinCondition();
            if (on is not null)
            {
                if (!on.IsEverything(ExpressionVisitor.EvaluableVisitor))
                {
                    // need to check that all added are bound to a table
                    on = on.Optimize(session);
                    if (!f.IsJoinOuter() && !f.IsJoinOuterIndirect())
                    {
                        f.RemoveJoinCondition();
                        AddCondition(on);
                    }
                }
            }

            on = f.GetFilterCondition();
            if (on is not null)
            {
                if (!on.IsEverything(ExpressionVisitor.EvaluableVisitor))
                {
                    f.RemoveFilterCondition();
                    AddCondition(on);
                }
            }

            // this is only important for subqueries, so they know
            // the result columns are evaluatable
            foreach (Expression e in expressions)
            {
                e.SetEvaluatable(f, true);
            }
        }
    }

    /// <summary>
    /// Gets the filter the whole plan hangs off.
    /// </summary>
    /// <returns>the top filter, or null before the plan is built</returns>
    public TableFilter? GetTopFilter() => topTableFilter;
    /// <returns>the session</returns>
    public new SessionLocal GetSession() => session;

    /// <summary>Sets the session this statement runs on.</summary>
    /// <param name="session">the session</param>
    public new void SetSession(SessionLocal session) => this.session = session;
}
