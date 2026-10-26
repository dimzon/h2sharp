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
using H2Sharp.Expressions.Analysis;
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

    /// <summary>
    /// The select list as an array, snapshotted once in <see cref="PrepareExpressions"/>
    /// and reused by every run, as the reference snapshots its own list there. Null until
    /// then; runs that never prepared fall back to a fresh copy.
    /// </summary>
    private Expression[]? expressionArray;

    private bool isQuickAggregateQuery;

    private bool isDistinct;

    private Expression? condition;

    private TableFilter? topTableFilter;

    private Expression? having;

    private List<Expression>? groupBy;

    private List<Expression>? windows;

    private bool isOrderByTop;

    /// <summary>
    /// Whether this is a grouping query: aggregates, GROUP BY or HAVING. The parser says so;
    /// the quick aggregate shortcut reads it, grouped execution still refuses below.
    /// </summary>
    private bool isGroupQuery;
    private bool isWindowQuery;
    private bool isGroupWindowStage2;
    private Dictionary<string, Expressions.Analysis.Window>? windowClause;
    private Select? parentSelect = null;

    /// <summary>Marks a query that uses window operations.</summary>
    public void SetWindowQuery() => isWindowQuery = true;
    /// <summary>Whether window operations are present.</summary>
    public bool IsWindowQuery() => isWindowQuery;
    /// <summary>Whether the second grouped-window stage is active.</summary>
    public bool IsGroupWindowStage2() => isGroupWindowStage2;
    /// <summary>Gets a named window.</summary>
    public Expressions.Analysis.Window? GetWindow(string name) => windowClause?.GetValueOrDefault(name);
    /// <summary>Adds a named window (true when newly registered).</summary>
    public bool AddWindow(string name, Expressions.Analysis.Window window)
    {
        windowClause ??= new Dictionary<string, Expressions.Analysis.Window>(StringComparer.Ordinal);
        return windowClause.TryAdd(name, window);
    }
    /// <summary>Returns the parent select.</summary>
    public Select? GetParentSelect() => parentSelect;

    /// <summary>
    /// Where the grouping key columns land in the result, or null when nothing groups.
    /// </summary>
    private int[]? groupIndex = null;

    /// <summary>
    /// How many columns result rows carry: the visible columns plus the ORDER BY
    /// appendages the sort still reads. HAVING and GROUP BY appendages never reach a
    /// result row; the reference trims them at the same point.
    /// </summary>
    private int resultColumnCount;

    /// <summary>
    /// Which select expressions are grouping keys, by position.
    /// </summary>
    private bool[]? groupByExpression;

    /// <summary>
    /// For a select expression that repeats an earlier one, the earlier position; -2 for
    /// an expression that is a source or has no copies. Null when nothing repeats.
    /// </summary>
    private int[]? groupByCopies;

    /// <summary>
    /// Where the HAVING clause moved in the select list, or -1 when there is none.
    /// </summary>
    private int havingIndex = -1;

    /// <summary>
    /// Where the QUALIFY clause moved in the select list, or -1 when there is none.
    /// </summary>
    private int qualifyIndex = -1;

    /// <summary>
    /// The group data grouped execution walks, or null outside it.
    /// </summary>
    private SelectGroups? groupData = null;

    /// <summary>
    /// Whether init ran: a second run would resolve the order list twice and append its
    /// expressions twice, so it is refused as the reference refuses it.
    /// </summary>

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

    /// <inheritdoc/>
    public override void SetWithTies(bool withTies) => this.withTies = withTies;

    /// <inheritdoc/>
    public override bool IsWithTies() => withTies;

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
    /// The order and group blocks of the reference's init: the select list snippets names
    /// resolve against, then positions for ORDER BY and grouping indexes for GROUP BY, with
    /// the HAVING move in between. Star expansion, the distinct indexes and the qualify move
    /// wait: each needs machinery that is not here, and a half-mapped select would answer
    /// wrong rows rather than refuse.
    /// </remarks>
    public override void Init()
    {
        if (checkInit)
        {
            throw DbException.GetInternalError();
        }

        List<string>? expressionSQL = null;
        if (orderList is not null || groupBy is not null)
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

        resultColumnCount = expressions.Count;

        if (having is not null)
        {
            expressions.Add(having);
            havingIndex = expressions.Count - 1;
            having = null;
        }
        else
        {
            havingIndex = -1;
        }

        // first the select list (visible columns), then ORDER BY expressions,
        // then HAVING expressions, and GROUP BY expressions at the end
        if (groupBy is not null)
        {
            Database db = session.GetDatabase();
            int size = groupBy.Count;
            groupIndex = new int[size];
            for (int i = 0; i < size; i++)
            {
                Expression expr = groupBy[i];
                string sql = expr.GetSql(HasSql.DefaultSqlFlags, Expression.WithoutParentheses);
                int found = -1;
                for (int j = 0; j < visibleColumnCount; j++)
                {
                    if (db.EqualsIdentifiers(expressionSQL![j], sql))
                    {
                        found = MergeGroupByExpressions(db, j, expressionSQL, false);
                        break;
                    }
                }

                if (found < 0)
                {
                    // special case: GROUP BY a column alias
                    for (int j = 0; j < visibleColumnCount; j++)
                    {
                        Expression e = expressions[j];
                        if (db.EqualsIdentifiers(sql, e.GetAlias(session, j)))
                        {
                            found = MergeGroupByExpressions(db, j, expressionSQL!, true);
                            break;
                        }

                        sql = expr.GetAlias(session, j);
                        if (db.EqualsIdentifiers(sql, e.GetAlias(session, j)))
                        {
                            found = MergeGroupByExpressions(db, j, expressionSQL!, true);
                            break;
                        }
                    }
                }

                if (found < 0)
                {
                    int index = expressions.Count;
                    groupIndex[i] = index;
                    expressions.Add(expr);
                }
                else
                {
                    groupIndex[i] = found;
                }
            }

            if (groupByCopies is not null)
            {
                bool used = false;
                foreach (int c in groupByCopies)
                {
                    if (c >= 0)
                    {
                        used = true;
                        break;
                    }
                }

                if (!used)
                {
                    groupByCopies = null;
                }
            }

            groupByExpression = new bool[expressions.Count];
            foreach (int gi in groupIndex)
            {
                groupByExpression[gi] = true;
            }

            groupBy = null;
        }

        // map columns in select list and condition
        foreach (TableFilter f in filters)
        {
            MapColumns(f, 0, false);
        }

        MapCondition(havingIndex);
        MapCondition(qualifyIndex);

        checkInit = true;
    }

    private void MapCondition(int index)
    {
        if (index < 0) return;
        Expression expression = expressions[index];
        SelectListColumnResolver resolver = new(this);
        expression.MapColumns(resolver, 0, Expression.MapInitial);
    }

    /// <summary>
    /// Whether this query is a DISTINCT or DISTINCT ON query.
    /// </summary>
    /// <remarks>
    /// The reference also counts its distinct-expression list; that list is not ported, and
    /// the DISTINCT ON marker stands in its place until it is.
    /// </remarks>
    /// <returns>true for either distinct flavor</returns>
    public override bool IsAnyDistinct() => distinct || isDistinct;

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
    public override void SetEvaluatable(TableFilter tableFilter, bool value)
    {
        foreach (Expression expression in expressions)
        {
            expression.SetEvaluatable(tableFilter, value);
        }

        condition?.SetEvaluatable(tableFilter, value);
    }

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal current, int stage)
    {
        foreach (Expression expression in expressions)
        {
            expression.UpdateAggregate(current, stage);
        }

        condition?.UpdateAggregate(current, stage);
        having?.UpdateAggregate(current, stage);
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
    /// The column-collecting visitors walk the select list, the join conditions, the condition
    /// and the HAVING clause, which is what the planner needs to price an index. The other
    /// visitor kinds answer with machinery that is not ported (table determinism, modification
    /// ids, dependency sets, evaluatable-subquery setting), so they still refuse by name rather
    /// than answering a question they cannot answer. The reference's QUALIFY clause is not
    /// ported, so its expression has nowhere to be walked from.
    /// </remarks>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        switch (visitor.GetVisitorType())
        {
            case ExpressionVisitor.GetColumns1:
            case ExpressionVisitor.GetColumns2:
                break;
            case ExpressionVisitor.Deterministic:
            case ExpressionVisitor.SetMaxDataModificationId:
                throw new TODOException("Select.isEverything: this visitor needs unported table metadata");
            case ExpressionVisitor.GetDependencies:
                foreach (TableFilter filter in filters) visitor.AddDependency(filter.GetTable());
                break;
            default:
                // The reference's optimizeEvaluatableSubqueries setting defaults to true;
                // DbSettings is not ported, so the default traversal is used here.
                break;
        }

        ExpressionVisitor v2 = visitor.IncrementQueryLevel(1);
        foreach (Expression e in expressions)
        {
            if (!e.IsEverything(v2))
            {
                return false;
            }
        }

        foreach (TableFilter f in filters)
        {
            Expression? c = f.GetJoinCondition();
            if (c is not null && !c.IsEverything(v2))
            {
                return false;
            }
        }

        if (condition is not null && !condition.IsEverything(v2))
        {
            return false;
        }

        if (having is not null && !having.IsEverything(v2))
        {
            return false;
        }

        return true;
    }

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

    private bool HasFromClause() => topTableFilter is not null ? topTableFilter.HasFromClause()
        : topFilters.Count == 1 ? topFilters[0].HasFromClause() : true;
    public override bool IsConstantQuery()
    {
        if (!base.IsConstantQuery() || isDistinct || condition is not null || isGroupQuery || isWindowQuery || HasFromClause()) return false;
        for (int i = 0; i < visibleColumnCount; i++) if (!expressions[i].IsConstant()) return false;
        return true;
    }
    public override Expression? GetIfSingleRow()
    {
        if (GetOffset() is not null || GetFetch() is not null || condition is not null || isGroupQuery || isWindowQuery || HasFromClause()) return null;
        return visibleColumnCount == 1 ? expressions[0] : new ExpressionList(expressions.GetRange(0, visibleColumnCount).ToArray(), false);
    }

    /// <inheritdoc/>
    public override int GetUnionType()
        => throw new TODOException("Select.getUnionType: a select is not a union");

    /// <inheritdoc/>
    public override StringBuilder GetPlanSql(StringBuilder builder, int sqlFlags)
    {
        // Ported getPlanSQL over this port's existing fields. Explicit TABLE, DISTINCT ON,
        // QUALIFY, and WITH-list fields are not present in this Select representation yet.
        Expression[] exprList = GetExpressions();
        builder.Append("SELECT");
        if (IsAnyDistinct()) builder.Append(" DISTINCT");
        for (int i = 0; i < visibleColumnCount; i++)
        {
            if (i > 0) builder.Append(',');
            StringUtils.Indent(builder.Append('\n'), exprList[i].GetSql(sqlFlags, Expression.WithoutParentheses), 4, false);
        }
        TableFilter? filter = topTableFilter;
        if (filter is null)
        {
            if (topFilters.Count != 1 || topFilters[0].GetTable() is not DualTable)
            {
                builder.Append("\nFROM ");
                bool isJoin = false;
                foreach (TableFilter top in topFilters) isJoin = GetPlanFromFilter(builder, sqlFlags, top, isJoin);
            }
        }
        else if (filter.GetTable() is not DualTable) GetPlanFromFilter(builder.Append("\nFROM "), sqlFlags, filter, false);
        if (condition is not null) condition.GetNonAliasExpression().GetUnenclosedSql(builder.Append("\nWHERE "), sqlFlags);
        if (groupIndex is not null)
        {
            builder.Append("\nGROUP BY ");
            for (int i = 0; i < groupIndex.Length; i++)
            {
                if (i > 0) builder.Append(", ");
                exprList[groupIndex[i]].GetNonAliasExpression().GetUnenclosedSql(builder, sqlFlags);
            }
        }
        else if (groupBy is not null)
        {
            builder.Append("\nGROUP BY ");
            Expression.WriteExpressions(builder, [.. groupBy], sqlFlags);
        }
        else if (isGroupQuery && having is null && havingIndex < 0)
        {
            bool aggregate = false;
            for (int i = 0; i < visibleColumnCount; i++) aggregate |= ContainsAggregate(exprList[i]);
            if (!aggregate) builder.Append("\nGROUP BY ()");
        }
        Expression? havingExpression = having ?? (havingIndex >= 0 ? exprList[havingIndex] : null);
        if (havingExpression is not null) havingExpression.GetNonAliasExpression().GetUnenclosedSql(builder.Append("\nHAVING "), sqlFlags);
        AppendEndOfQueryToSql(builder, sqlFlags, exprList, withTies);
        if (forUpdate is not null) builder.Append('\n').Append(forUpdate);
        if ((sqlFlags & HasSql.AddPlanInformation) != 0)
        {
            if (isQuickAggregateQuery) builder.Append("\n/* direct lookup */");
            if (isDistinct) builder.Append("\n/* distinct */");
            if (indexSortedColumns == Indexes.IndexSort.FullySorted) builder.Append("\n/* index sorted */");
            else if (indexSortedColumns > 0) builder.Append("\n/* index sorted: ").Append(indexSortedColumns).Append(" of ")
                .Append(sort!.GetOrderList()!.Count).Append(" columns */");
        }
        return builder;
    }

    private static bool GetPlanFromFilter(StringBuilder builder, int sqlFlags, TableFilter filter, bool isJoin)
    {
        do
        {
            if (isJoin) builder.Append('\n');
            filter.GetPlanSql(builder, isJoin, sqlFlags);
            isJoin = true;
            filter = filter.GetJoin()!;
        } while (filter is not null);
        return isJoin;
    }

    private static bool ContainsAggregate(Expression expression)
    {
        if (expression is Expressions.Analysis.DataAnalysisOperation operation && operation.IsAggregate()) return true;
        for (int i = 0; i < expression.GetSubexpressionCount(); i++)
            if (ContainsAggregate(expression.GetSubexpression(i)!)) return true;
        return false;
    }

    /// <summary>
    /// Marks duplicate select expressions for the group walk: the first occurrence becomes
    /// the source the copies read from.
    /// </summary>
    /// <remarks>
    /// -1: uniqueness of expression is not known yet.
    /// -2: expression that is used as a source for a copy or does not have copies.
    /// >=0: expression is a copy of expression at this index.
    /// </remarks>
    /// <param name="db">the database</param>
    /// <param name="index">the position of the expression</param>
    /// <param name="expressionSQL">the select list snippets</param>
    /// <param name="scanPrevious">whether expressions before this one may be identical</param>
    /// <returns>the source position</returns>
    private int MergeGroupByExpressions(Database db, int index, List<string> expressionSQL, bool scanPrevious)
    {
        if (groupByCopies is not null)
        {
            int c = groupByCopies[index];
            if (c >= 0)
            {
                return c;
            }
            else if (c == -2)
            {
                return index;
            }
        }
        else
        {
            groupByCopies = new int[expressionSQL.Count];
            Array.Fill(groupByCopies, -1);
        }

        string sql = expressionSQL[index];
        if (scanPrevious)
        {
            // If expression was matched using an alias previous expressions may
            // be identical.
            for (int i = 0; i < index; i++)
            {
                if (db.EqualsIdentifiers(sql, expressionSQL[i]))
                {
                    index = i;
                    break;
                }
            }
        }

        int l = expressionSQL.Count;
        for (int i = index + 1; i < l; i++)
        {
            if (db.EqualsIdentifiers(sql, expressionSQL[i]))
            {
                groupByCopies[i] = index;
            }
        }

        groupByCopies[index] = -2;
        return index;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The visible columns, not the whole list: <see cref="Init"/> may append hidden ORDER
    /// BY expressions past them, and those are not columns of the query. The array itself is
    /// the snapshot <see cref="PrepareExpressions"/> took, as the reference hands out its
    /// own list rather than a copy.
    /// </remarks>
    public override Expression[] GetExpressions() => expressionArray ?? [.. expressions];

    /// <inheritdoc/>
    public override HashSet<Tables.Table> GetTables()
    {
        HashSet<Tables.Table> set = [];
        foreach (Tables.TableFilter filter in filters)
        {
            _ = set.Add(filter.GetTable());
        }

        return set;
    }

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
    /// optimized as there. The quick aggregate shortcut waits for its own machinery. The
    /// select list is snapshotted last, as the reference snapshots its array here.
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

        if (isGroupQuery && groupIndex is null && havingIndex < 0 && qualifyIndex < 0 && condition is null
            && filters.Count == 1)
        {
            isQuickAggregateQuery = IsOptimizableAggregate(filters[0]);
        }

        if (withTies && sort is null)
        {
            throw DbException.Get(Api.ErrorCode.WithTiesWithoutOrderBy);
        }

        expressionArray = [.. expressions];
    }

    /// <summary>
    /// Whether every select expression is optimizable against this table: the reference's
    /// quick-aggregate check, which is what lets COUNT(*) skip the row walk. Joins, HAVING
    /// and QUALIFY travel with their ports; none of them can appear here yet.
    /// </summary>
    /// <param name="filter">the table filter</param>
    /// <returns>true when the aggregate can be answered without walking rows</returns>
    private bool IsOptimizableAggregate(TableFilter filter)
    {
        Tables.Table? table = filter.GetTable();
        if (table is null)
        {
            return false;
        }

        ExpressionVisitor visitor = ExpressionVisitor.GetOptimizableVisitor(table);
        foreach (Expression e in expressions)
        {
            if (!e.IsEverything(visitor))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Flat, quick aggregate and grouped queries run; windowed, DISTINCT ON and locking
    /// shapes refuse below, each waiting for its own machinery. What runs here is the
    /// reference's flat path — skip the offset, fill the result, trim it — with lazy
    /// execution left out: every result here is materialized, and a lazy one would hand
    /// back a walk over tables the caller may close. Plain DISTINCT runs: the result itself
    /// drops duplicates as rows arrive. The index fast path that would walk them in order
    /// waits for secondary indexes, which are still a stub.
    /// </remarks>
    public override ResultInterface? Run(long limit, ResultTarget? target)
    {
        if (windows is not null || isDistinct || forUpdate is not null)
        {
            throw new TODOException("Select.query: windowed, distinct on and locking queries are not ported");
        }

        long offset;
        long fetch;
        bool fetchPercent;
        {
            OffsetFetch offsetFetch = GetOffsetFetch(limit);
            offset = offsetFetch.Offset;
            fetch = offsetFetch.Fetch;
            fetchPercent = offsetFetch.FetchPercent;
        }
        Expression[] array = expressionArray ?? [.. expressions];
        int columnCount = array.Length;
        int resultWidth = resultColumnCount > 0 ? resultColumnCount : array.Length;
        // H2 queryWithoutCache: with optimizeInsertFromSelect (its default) a flat query
        // writes straight to the target. Ordering, grouping, distinct, windows and
        // offset/fetch still need a LocalResult first.
        bool needResult = target is null
            || (sort is not null && indexSortedColumns != Indexes.IndexSort.FullySorted)
            || distinct || isWindowQuery || isGroupQuery
            || fetch >= 0 || offset > 0;
        LocalResult? result = needResult
            ? new LocalResult(session, array, visibleColumnCount, resultWidth) : null;
        ResultTarget to = result ?? target!;
        topTableFilter!.StartQuery(session);
        topTableFilter.Reset();
        topTableFilter.Lock(session);
        QuickOffset quickOffset = QuickOffset.Yes;
        if (sort is not null && indexSortedColumns != Indexes.IndexSort.FullySorted)
        {
            result!.SetSortOrder(sort);
            // PARTIAL waits for index-sort resolution: without a sorted index the offset cannot
            // be skipped up front, the result trims it after the sort.
            quickOffset = QuickOffset.No;
        }

        if (distinct)
        {
            result!.SetDistinct();
            // A deduplicated row is not the row that was read: skipping the offset up front
            // would skip raw rows and trim the wrong ones, so the result trims afterwards.
            quickOffset = QuickOffset.No;
        }

        if (isQuickAggregateQuery)
        {
            QueryQuick(array, to, columnCount, offset, fetch);
            offset = 0;
        }
        else if (isWindowQuery)
        {
            if (isGroupQuery) QueryGroupWindow(columnCount, result!);
            else QueryWindow(columnCount, result!);
            quickOffset = QuickOffset.No;
        }
        else if (isGroupQuery)
        {
            // Groups form after the walk, so the offset cannot be skipped up front: the
            // result trims it after the sort, like the reference trims its own.
            QueryGroup(to);
            quickOffset = QuickOffset.No;
        }
        else
        {
            QueryFlat(array, to, offset, fetch, withTies, quickOffset);
            if (quickOffset == QuickOffset.Yes)
            {
                offset = 0;
            }
        }

        return result is null ? null : FinishResult(result, offset, fetch, fetchPercent, target);
    }

    /// <summary>
    /// Reads one aggregate row without walking the table: each expression answers off the
    /// table itself, which is what makes COUNT(*) a lookup rather than a scan.
    /// </summary>
    /// <remarks>
    /// The reference's queryQuick, minus the skip flag it threads through: an offset skips
    /// the one row there is, and a zero fetch reads none, so both just mean no row here.
    /// </remarks>
    /// <param name="array">the select list, shared with the result and the walk</param>
    /// <param name="result">where the row goes</param>
    /// <param name="columnCount">how many columns the row has</param>
    /// <param name="offset">how many rows to skip</param>
    /// <param name="fetch">how many rows to read, or negative for all</param>
    private void QueryQuick(Expression[] array, ResultTarget result, int columnCount, long offset, long fetch)
    {
        if (fetch != 0 && offset <= 0)
        {
            Value?[] row = new Value?[columnCount];
            for (int i = 0; i < columnCount; i++)
            {
                row[i] = expressions[i].GetValue(session);
            }

            result.AddRow(RowForResult(row));
        }
    }

    /// <summary>
    /// Reads the grouped rows into the result: one pass accumulates the aggregates per
    /// group key, another turns each group into a row.
    /// </summary>
    /// <param name="result">where the rows go</param>
    private void QueryGroup(ResultTarget result)
    {
        int columnCount = expressions.Count;
        InitGroupData(columnCount);
        try
        {
            GatherGroup(columnCount);
            ProcessGroupResult(result, columnCount);
        }
        finally
        {
            groupData!.Reset();
        }
    }

    /// <summary>
    /// Prepares the group data, reusing it across runs of the same prepared statement.
    /// </summary>
    /// <param name="columnCount">the number of columns</param>
    private void InitGroupData(int columnCount)
    {
        if (groupData is null)
        {
            SetGroupData(SelectGroups.GetInstance(session, [.. expressions], isGroupQuery, groupIndex));
        }
        else
        {
            UpdateAgg(columnCount, DataAnalysisOperation.StageReset);
        }

        groupData!.Reset();
    }

    /// <summary>
    /// Sets the group data this query walks.
    /// </summary>
    /// <remarks>
    /// The reference also hands it to the filters of sub-selects; flat selects have none,
    /// so only the field is set.
    /// </remarks>
    /// <param name="groupData">the group data</param>
    private void SetGroupData(SelectGroups groupData) => this.groupData = groupData;

    private void QueryWindow(int columnCount, ResultTarget result)
    {
        InitGroupData(columnCount);
        try
        {
            GatherGroup(columnCount, DataAnalysisOperation.StageWindow);
            ProcessGroupResult(result, columnCount, false);
        }
        finally { groupData!.Reset(); }
    }

    private void QueryGroupWindow(int columnCount, ResultTarget result)
    {
        InitGroupData(columnCount);
        try
        {
            GatherGroup(columnCount, DataAnalysisOperation.StageGroup);
            try
            {
                isGroupWindowStage2 = true;
                while (groupData!.Next() is not null)
                {
                    if (havingIndex < 0 || expressions[havingIndex].GetBooleanValue(session))
                        UpdateAgg(columnCount, DataAnalysisOperation.StageWindow);
                    else groupData.Remove();
                }
                groupData.Done();
                ProcessGroupResult(result, columnCount, false);
            }
            finally { isGroupWindowStage2 = false; }
        }
        finally { groupData!.Reset(); }
    }

    /// <summary>
    /// Walks the table once, filing every surviving row into its group.
    /// </summary>
    /// <param name="columnCount">the number of columns</param>
    private void GatherGroup(int columnCount, int stage = DataAnalysisOperation.StageGroup)
    {
        long rowNumber = 0;
        SetCurrentRowNumber(0);
        while (topTableFilter!.Next())
        {
            SetCurrentRowNumber(rowNumber + 1);
            if (IsConditionMet())
            {
                rowNumber++;
                groupData!.NextSource();
                UpdateAgg(columnCount, stage);
            }
        }

        groupData!.Done();
    }

    /// <summary>
    /// Update any aggregate expressions with the query stage.
    /// </summary>
    /// <param name="columnCount">the number of columns</param>
    /// <param name="stage">the select stage</param>
    private void UpdateAgg(int columnCount, int stage)
    {
        for (int i = 0; i < columnCount; i++)
        {
            if ((groupByExpression is null || !groupByExpression[i])
                && (groupByCopies is null || groupByCopies[i] < 0))
            {
                Expression expr = expressions[i];
                expr.UpdateAggregate(session, stage);
            }
        }
    }

    /// <summary>
    /// Turns each group into a result row, dropping the ones HAVING refuses.
    /// </summary>
    /// <param name="result">where the rows go</param>
    /// <param name="columnCount">how many columns each row has</param>
    private void ProcessGroupResult(ResultTarget result, int columnCount, bool withHaving = true)
    {
        while (groupData!.Next() is ValueRow currentGroupsKey)
        {
            Value?[] row = ConstructGroupResultRow(currentGroupsKey.GetList(), columnCount);
            if (withHaving && IsHavingNullOrFalse(row))
            {
                continue;
            }

            result.AddRow(RowForResult(row));
        }
    }

    /// <summary>
    /// Whether the HAVING clause refuses this group row.
    /// </summary>
    /// <param name="row">the group row</param>
    /// <returns>true when HAVING is present and not true</returns>
    private bool IsHavingNullOrFalse(Value?[] row)
        => havingIndex >= 0 && !row[havingIndex]!.IsTrue();

    /// <summary>
    /// Builds one group row: the key columns from the key, the rest by evaluation.
    /// </summary>
    /// <param name="keyValues">the group key values</param>
    /// <param name="columnCount">how many columns the row has</param>
    /// <returns>the row</returns>
    private Value?[] ConstructGroupResultRow(Value?[] keyValues, int columnCount)
    {
        Value?[] row = new Value?[columnCount];
        if (groupIndex is not null)
        {
            for (int i = 0, l = groupIndex.Length; i < l; i++)
            {
                row[groupIndex[i]] = keyValues[i];
            }
        }

        for (int i = 0; i < columnCount; i++)
        {
            if (groupByExpression is not null && groupByExpression[i])
            {
                continue;
            }

            if (groupByCopies is not null)
            {
                int original = groupByCopies[i];
                if (original >= 0)
                {
                    row[i] = row[original];
                    continue;
                }
            }

            row[i] = expressions[i].GetValue(session);
        }

        return row;
    }

    /// <summary>
    /// Removes HAVING and GROUP BY appendages from the row.
    /// </summary>
    /// <remarks>
    /// A select that never ran init keeps its rows whole: with no tracked width there is
    /// nothing to trim to, and its rows are exactly what was built.
    /// </remarks>
    /// <param name="row">the complete row</param>
    /// <returns>the same or the truncated row</returns>
    private Value?[] RowForResult(Value?[] row)
    {
        if (resultColumnCount <= 0 || row.Length == resultColumnCount)
        {
            return row;
        }

        Value?[] kept = new Value?[resultColumnCount];
        Array.Copy(row, kept, resultColumnCount);
        return kept;
    }

    /// <summary>
    /// Reads the flat rows into the result, skipping the offset on the way when allowed.
    /// </summary>
    /// <param name="expressionArray">the select list, shared with the result and the walk</param>
    /// <param name="result">where the rows go, or null for a lazy walk</param>
    /// <param name="offset">how many rows to skip</param>
    /// <param name="limitRows">how many rows to read, or negative for all</param>
    /// <param name="withTies">whether tied rows ride along past the limit</param>
    /// <param name="quickOffset">how the offset is skipped</param>
    /// <returns>the lazy walk, when no result was given</returns>
    private LazyResult? QueryFlat(
        Expression[] expressionArray,
        ResultTarget? result,
        long offset,
        long limitRows,
        bool withTies,
        QuickOffset quickOffset)
    {
        int columnCount = expressionArray.Length;
        if (limitRows > 0 && offset > 0 && quickOffset != QuickOffset.Yes)
        {
            limitRows += offset;
            if (limitRows < 0)
            {
                // Overflow
                limitRows = long.MaxValue;
            }
        }

        LazyResultQueryFlat lazyResult = new(this, expressionArray, columnCount, forUpdate is not null);
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

    /// <summary>
    /// Whether this is an aggregate answered without walking rows, such as COUNT(*) over a
    /// table the row count is known for.
    /// </summary>
    /// <returns>true for a quick aggregate</returns>
    public bool IsQuickAggregateQuery() => isQuickAggregateQuery;

    /// <summary>
    /// Whether this query groups: aggregates, GROUP BY or HAVING.
    /// </summary>
    /// <returns>true for a grouping query</returns>
    public bool IsGroupQuery() => isGroupQuery;

    /// <summary>
    /// Get the group data if there is currently a group-by active.
    /// </summary>
    /// <remarks>
    /// Grouped execution is not ported, and only the quick aggregate path runs, which never
    /// touches group data: this refuses with the missing piece named.
    /// </remarks>
    /// <param name="window">is this a window function</param>
    /// <returns>the grouped data</returns>
    public SelectGroups? GetGroupDataIfCurrent(bool window)
    {
        SelectGroups? data = groupData;
        if (data is not null && data.IsCurrentGroup())
        {
            return data;
        }

        return null;
    }

    /// <summary>Set the distinct flag.</summary>
    /// <remarks>
    /// No boolean: the reference's setter takes none, and DISTINCT ON arrives through its
    /// own setter. The exclusion will matter when that list is ported; until then the guard
    /// reads the marker that stands in its place.
    /// </remarks>
    public void SetDistinct()
    {
        if (isDistinct)
        {
            throw DbException.GetUnsupportedException("DISTINCT ON together with DISTINCT");
        }

        distinct = true;
    }

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

    /// <inheritdoc/>
    public override void AddGlobalCondition(Parameter param, int columnId, int comparisonType)
    {
        AddParameter(param);
        Expression col = expressions[columnId].GetNonAliasExpression();
        Expression comp = col.IsEverything(ExpressionVisitor.QueryComparableVisitor)
            ? new Comparison(comparisonType, col, param, false)
            // This condition will always evaluate to true, but the parameter is still added so
            // that the caller can set it later.
            : new Comparison(Comparison.EqualNullSafe, param, param, false);
        comp = comp.Optimize(session);
        if (isWindowQuery)
        {
            throw new TODOException("Select.addGlobalCondition: QUALIFY is not ported");
        }

        if (isGroupQuery)
        {
            for (int i = 0; groupIndex is not null && i < groupIndex.Length; i++)
            {
                if (groupIndex[i] == columnId)
                {
                    condition = AddGlobalCondition(condition, comp);
                    return;
                }
            }

            if (havingIndex >= 0)
            {
                having = expressions[havingIndex];
            }

            having = AddGlobalCondition(having, comp);
        }
        else
        {
            condition = AddGlobalCondition(condition, comp);
        }
    }

    /// <inheritdoc/>
    public override bool AllowGlobalConditions() => GetOffset() is null && GetFetch() is null;

    private static Expression AddGlobalCondition(Expression? condition, Expression additional)
    {
        if (condition is not ConditionLocalAndGlobal)
        {
            return new ConditionLocalAndGlobal(condition, additional);
        }

        Expression? oldLocal, oldGlobal;
        if (condition.GetSubexpressionCount() == 1)
        {
            oldLocal = null;
            oldGlobal = condition.GetSubexpression(0)!;
        }
        else
        {
            oldLocal = condition.GetSubexpression(0);
            oldGlobal = condition.GetSubexpression(1)!;
        }

        return new ConditionLocalAndGlobal(
            oldLocal,
            new ConditionAndOr(ConditionAndOr.And, oldGlobal, additional));
    }

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
        TableFilter[] topArray = [.. topFilters];
        foreach (TableFilter t in topArray)
        {
            t.CreateIndexConditions();
            t.SetFullCondition(condition);
        }

        Optimizer optimizer = new(topArray, condition, session);
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
    public override void SetSession(SessionLocal session)
    {
        base.SetSession(session);
        this.session = session;
    }
}
