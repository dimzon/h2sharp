/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.query.Query.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A query: something that produces rows.
 *
 * It is the base of Select, of the set operations over it, and of the few statements that are
 * shaped like a query — INSERT INTO ... SELECT, CREATE TABLE AS. What all of them share is that
 * they are asked for rows and that those rows are counted before they are read, because the
 * optimizer needs the count to decide between reading an index and reading the table.
 *
 * The optimizer is the part that is genuinely large in the reference and it is not ported, so
 * every member here refuses. The shape is kept because it is the shape the command layer and
 * the data change statements are written against.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Command.Query;

/// <summary>
/// A query, which is anything that produces rows.
/// </summary>
/// <summary>
/// A query, which is anything that produces rows.
/// </summary>
/// <remarks>
/// A <see cref="Prepared"/>, as in the reference: a query is what the parser produced, and it is
/// run through a <see cref="CommandContainer"/> rather than being handed to a caller as a
/// command itself.
/// </remarks>
public abstract class Query : Prepared
{
    private int orderByIndex = -1;

    private int limitOffset = -1;

    /// <summary>
    /// The expression that says how many rows to skip, or null.
    /// </summary>
    private Expression? offsetExpr;

    /// <summary>
    /// The expression that says how many rows to read, or null.
    /// </summary>
    private Expression? fetchExpr;

    /// <summary>
    /// Whether the fetch is a percentage of the rows rather than a number of them.
    /// </summary>
    private bool fetchPercent;

    /// <summary>Whether duplicate rows are removed.</summary>
    protected bool distinct;

    /// <summary>The ordering of an IN predicate result, or null for an ordinary result.</summary>
    protected int[]? inPredicateSortTypes;

    private bool neverLazy;

    /// <summary>Whether preparation has completed.</summary>
    protected bool isPrepared;

    /// <summary>Requires a materialized result. This port currently materializes all queries.</summary>
    public void SetNeverLazy(bool value) => neverLazy = value;

    /// <summary>Whether this query must not return a lazy result.</summary>
    public bool IsNeverLazy() => neverLazy;

    /// <summary>Marks this result as an IN predicate value list.</summary>
    public void SetInPredicateResult() => inPredicateSortTypes ??= [];

    /// <summary>Whether this query produces an IN predicate value list.</summary>
    public bool IsInPredicateResult() => inPredicateSortTypes is not null;

    /// <summary>Sets the ordering of an IN predicate value list.</summary>
    public void SetInPredicateResultSortTypes(int[] sortTypes) => inPredicateSortTypes = sortTypes;

    /// <summary>Whether this query removes duplicates.</summary>
    public virtual bool IsAnyDistinct() => distinct;

    /// <summary>
    /// Enables DISTINCT only when it cannot change which rows OFFSET / FETCH retain.
    /// </summary>
    public void SetDistinctIfPossible()
    {
        if (!IsAnyDistinct() && offsetExpr is null && fetchExpr is null)
        {
            distinct = true;
        }
    }

    /// <inheritdoc/>
    public override void Prepare()
    {
        if (!checkInit)
        {
            throw DbException.GetInternalError("not initialized");
        }

        if (isPrepared)
        {
            return;
        }

        PrepareExpressions();
        PreparePlan(GetSession(), null);
        isPrepared = true;
    }

    /// <summary>Returns the cost bounded as an expression cost, as in Query.getCostAsExpression.</summary>
    public int GetCostAsExpression() => (int)Math.Min(1_000_000d, 10d + 10d * GetCost());

    /// <summary>Returns the scalar type for one column, or the ROW type for multiple columns.</summary>
    public TypeInfo GetRowDataType()
    {
        Expression[] expressions = GetExpressions();
        int count = GetColumnCount();
        if (count == 1)
        {
            return expressions[0].GetTypeInfo();
        }

        // ExtTypeInfoRow's existing port takes TypeInfo[] in place of Java's Typed[].
        TypeInfo[] types = new TypeInfo[count];
        for (int i = 0; i < count; i++)
        {
            types[i] = expressions[i].GetTypeInfo();
        }

        return TypeInfo.GetTypeInfo(Value.Row, -1L, -1, new ExtTypeInfoRow(types, count));
    }

    /// <summary>Propagates evaluatability into the expressions of this query.</summary>
    public abstract void SetEvaluatable(TableFilter tableFilter, bool value);

    /// <summary>Updates aggregate expressions, including those in nested queries.</summary>
    public abstract void UpdateAggregate(SessionLocal session, int stage);

    /// <summary>
    /// Creates a query.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="sql">the statement text</param>
    protected Query(SessionLocal session, string sql)
        : base(session)
    {
        SetSql(sql, null);
    }

    /// <summary>
    /// Optimizes this query.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="targetTableFilter">the table the rows are written into, or null</param>
    public virtual void Optimize(SessionLocal session, TableFilter? targetTableFilter)
        => throw new TODOException("Query.optimize: the optimizer is not ported");

    /// <summary>
    /// Prepares this query for the second execution and beyond.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="targetTableFilter">the table the rows are written into, or null</param>
    public virtual void PreparePlan(SessionLocal session, TableFilter? targetTableFilter)
        => throw new TODOException("Query.preparePlan: the optimizer is not ported");

    /// <summary>
    /// Whether this query is read only.
    /// </summary>
    /// <returns>true if it is</returns>
    public override bool IsReadOnly() => true;

    /// <summary>
    /// Whether this query is a SELECT.
    /// </summary>
    /// <returns>true if it is</returns>
    public virtual bool IsEverything(ExpressionVisitor visitor) => true;

    /// <summary>
    /// Whether this query can be cached.
    /// </summary>
    /// <returns>true if it can</returns>
    public override bool IsCacheable() => false;

    /// <summary>
    /// Gets the cost of this query.
    /// </summary>
    /// <returns>the cost</returns>
    public virtual long GetCost() => throw new TODOException("Query.getCost: not ported");

    /// <summary>
    /// Gets the tables this query reads.
    /// </summary>
    /// <returns>the dependencies</returns>
    public virtual HashSet<DbObject> GetDependencies()
        => throw new TODOException("Query.getDependencies: not ported");

    /// <summary>
    /// Whether this query is a union of queries.
    /// </summary>
    /// <returns>true if it is</returns>
    public virtual bool IsUnion() => false;

    /// <summary>
    /// Gets the union type, one of the reference's UNION_* constants.
    /// </summary>
    /// <returns>the union type</returns>
    public virtual int GetUnionType() => throw new TODOException("Query.getUnionType: not ported");

    /// <summary>
    /// Gets the name of this query for the plan.
    /// </summary>
    /// <returns>the name</returns>
    public virtual string GetPlanSQL(StringBuilder builder, int sqlFlags)
        => throw new TODOException("Query.getPlanSQL: not ported");

    /// <summary>
    /// Gets the expressions of the columns this query produces.
    /// </summary>
    /// <returns>the expressions</returns>
    public abstract Expression[] GetExpressions();

    /// <summary>
    /// Gets the tables this query reads.
    /// </summary>
    /// <returns>the tables</returns>
    public abstract HashSet<Tables.Table> GetTables();

    /// <summary>
    /// Gets the number of visible columns.
    /// </summary>
    /// <returns>the number of columns</returns>
    public abstract int GetColumnCount();

    /// <summary>
    /// Gets the number of rows without running the query twice.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="filters">the filters of the tables</param>
    /// <param name="limit">the limit</param>
    /// <returns>the row count</returns>
    public abstract long GetRowCount(SessionLocal session, List<TableFilter> filters, long limit);

    /// <summary>
    /// Runs the query.
    /// </summary>
    /// <remarks>
    /// Named Run rather than the reference's query, because C# forbids a member with the same
    /// name as its enclosing type and this class is called Query.
    /// </remarks>
    /// <param name="limit">the limit</param>
    /// <returns>the rows</returns>
    public abstract override ResultInterface Run(long limit);

    /// <summary>
    /// Initializes this query.
    /// </summary>
    /// <remarks>
    /// The call sequence is init(), mapColumns() if it's a subquery, prepare().
    /// </remarks>
    public abstract void Init();

    /// <summary>
    /// Map the columns to the given column resolver.
    /// </summary>
    /// <param name="resolver">the resolver</param>
    /// <param name="level">the subquery level (0 is the top level query, 1 is the first subquery level)</param>
    /// <param name="outer">whether this method was called from the outer query</param>
    public abstract void MapColumns(Tables.ColumnResolver resolver, int level, bool outer);

    /// <summary>
    /// Prepares the expressions of this query.
    /// </summary>
    public abstract void PrepareExpressions();

    /// <summary>
    /// Whether the query is initialized.
    /// </summary>
    /// <remarks>
    /// The reference keeps the flag in the query itself, and the check is what tells the
    /// table a query makes whether it has to initialize it first.
    /// </remarks>
    protected bool checkInit;

    /// <summary>
    /// Makes a table out of this query, as the reference's toTable does.
    /// </summary>
    /// <remarks>
    /// The table is a derived table: its rows are the rows of the query, and the names the
    /// statement gave the columns are the names the table's columns are known by.
    /// </remarks>
    /// <param name="alias">the name of the table</param>
    /// <param name="columnTemplates">the names the statement gave the columns, or null</param>
    /// <param name="forCreateView">whether the table is made for a view</param>
    /// <param name="topQuery">the query the table is a part of, or null</param>
    /// <returns>the table</returns>
    public Tables.Table ToTable(
        string alias,
        Tables.Column[]? columnTemplates,
        bool forCreateView,
        Query? topQuery)
    {
        if (!checkInit)
        {
            Init();
        }

        SessionLocal session = GetSession();
        return new Tables.DerivedTable(
            session,
            alias,
            columnTemplates,
            this,
            topQuery);
    }

    /// <summary>
    /// The order by list, or null when the query has none. The parser fills it; preparing
    /// the expressions turns it into <see cref="sort"/> and drops it.
    /// </summary>
    internal List<QueryOrderBy>? orderList;

    /// <summary>
    /// Set the order by list.
    /// </summary>
    /// <param name="order">the order by list</param>
    public void SetOrder(List<QueryOrderBy> order) => orderList = order;

    /// <summary>
    /// Create a <see cref="SortOrder"/> object given the list of <see cref="QueryOrderBy"/>
    /// objects.
    /// </summary>
    /// <param name="orderList">a list of order by elements</param>
    /// <param name="expressionCount">the number of columns in the query</param>
    internal void PrepareOrder(List<QueryOrderBy> orderList, int expressionCount)
    {
        int size = orderList.Count;
        int[] index = new int[size];
        int[] sortType = new int[size];
        for (int i = 0; i < size; i++)
        {
            QueryOrderBy o = orderList[i];
            int idx;
            bool reverse = false;
            Value v = o.columnIndexExpr!.GetValue(null!);
            if (v == ValueNull.Instance)
            {
                // parameter not yet set - order by first column
                idx = 0;
            }
            else
            {
                idx = v.GetInt();
                if (idx < 0)
                {
                    reverse = true;
                    idx = -idx;
                }

                idx -= 1;
                if (idx < 0 || idx >= expressionCount)
                {
                    throw DbException.Get(
                        ErrorCode.OrderByNotInResult,
                        (idx + 1).ToString(CultureInfo.InvariantCulture));
                }
            }

            index[i] = idx;
            int type = o.sortType;
            if (reverse)
            {
                // TODO NULLS FIRST / LAST should be inverted too? (carried over from the reference).
                type ^= SortOrder.Descending;
            }

            sortType[i] = type;
        }

        sort = new SortOrder(GetSession(), index, sortType, orderList);
        this.orderList = null;
    }

    /// <summary>
    /// Removes constant expressions from the sort order.
    /// Some constants are detected only after optimization of expressions, this
    /// method removes them from the sort order only. They are currently
    /// preserved in the list of expressions.
    /// </summary>
    /// <remarks>
    /// The expressions arrive as a parameter rather than a field: this class does not own
    /// the select list, the select does.
    /// </remarks>
    /// <param name="expressions">the columns of the query</param>
    internal void CleanupOrder(List<Expression> expressions)
    {
        int[] sourceIndexes = sort!.GetQueryColumnIndexes();
        int count = sourceIndexes.Length;
        // The reference keeps these two in a BitSet; two flag arrays say the same thing.
        bool[] deduplication = new bool[expressions.Count];
        bool[] retain = new bool[count];
        for (int i = 0; i < count; i++)
        {
            int sourceIndex = sourceIndexes[i];
            if (!expressions[sourceIndex].IsConstant() && !deduplication[sourceIndex])
            {
                // duplicate expression, remove it from the sort order
                deduplication[sourceIndex] = true;
                retain[i] = true;
            }
        }

        int size = 0;
        foreach (bool kept in retain)
        {
            if (kept)
            {
                size++;
            }
        }

        if (size == count)
        {
            return;
        }

        if (size == 0)
        {
            sort = null;
            return;
        }

        int[] indexes = new int[size];
        int[] sortTypes = new int[size];
        int[] sourceSortTypes = sort.GetSortTypes();
        List<QueryOrderBy> reorderList = sort.GetOrderList()!;
        for (int i = 0, j = 0; i < count; i++)
        {
            if (retain[i])
            {
                indexes[j] = sourceIndexes[i];
                sortTypes[j] = sourceSortTypes[i];
                j++;
            }
            else
            {
                reorderList.RemoveAt(j);
            }
        }

        sort = new SortOrder(GetSession(), indexes, sortTypes, reorderList);
    }

    /// <summary>
    /// Initialize the order by list. This call may extend the expressions list.
    /// </summary>
    /// <remarks>
    /// The expressions arrive as a parameter rather than a field: the reference keeps the
    /// select list on this class, here the select owns it and this class only resolves into
    /// it. The visible count is read off <see cref="GetColumnCount"/>, which answers the
    /// visible columns exactly as there.
    /// </remarks>
    /// <param name="expressionSQL">the select list SQL snippets</param>
    /// <param name="mustBeInResult">all order by expressions must be in the select list</param>
    /// <param name="filters">the table filters</param>
    /// <param name="expressions">the select list, extended in place</param>
    /// <returns>true if ORDER BY clause is preserved, false otherwise</returns>
    internal bool InitOrder(
        List<string>? expressionSQL, bool mustBeInResult, List<TableFilter>? filters, List<Expression> expressions)
    {
        for (int i = 0; i < orderList!.Count; i++)
        {
            QueryOrderBy o = orderList[i];
            Expression? e = o.expression;
            if (e is null)
            {
                continue;
            }

            if (e.IsConstant())
            {
                orderList.RemoveAt(i--);
                continue;
            }

            int idx = InitExpression(expressionSQL, e, mustBeInResult, filters, expressions);
            o.columnIndexExpr = ValueExpression.Get(ValueInteger.Get(idx + 1));
            o.expression = expressions[idx].GetNonAliasExpression();
        }

        if (orderList.Count == 0)
        {
            orderList = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Initialize the 'ORDER BY' or 'DISTINCT' expressions.
    /// </summary>
    /// <remarks>
    /// The mode comes from the session rather than from the database: the reference reads
    /// <c>getDatabase().getMode()</c>, which is the same object, and this database does not
    /// store one. Reading it refuses until Mode is ported, so a DISTINCT query ordering by
    /// an unrelated expression refuses with that rather than with the mismatch error.
    /// </remarks>
    /// <param name="expressionSQL">the select list SQL snippets</param>
    /// <param name="e">the expression</param>
    /// <param name="mustBeInResult">all order by expressions must be in the select list</param>
    /// <param name="filters">the table filters</param>
    /// <param name="expressions">the select list, extended in place</param>
    /// <returns>index on the expression in the expressions list</returns>
    internal int InitExpression(
        List<string>? expressionSQL, Expression e, bool mustBeInResult, List<TableFilter>? filters,
        List<Expression> expressions)
    {
        SessionLocal session = GetSession();
        Database db = session.GetDatabase();
        // special case: SELECT 1 AS A FROM DUAL ORDER BY A
        // (oracle supports it, but only in order by, not in group by and
        // not in having):
        // SELECT 1 AS A FROM DUAL ORDER BY -A
        if (e is ExpressionColumn exprCol)
        {
            // order by expression
            string? tableAlias = exprCol.GetOriginalTableAliasName();
            string? col = exprCol.GetOriginalColumnName();
            for (int j = 0, visible = GetColumnCount(); j < visible; j++)
            {
                Expression ec = expressions[j];
                if (ec is ExpressionColumn c)
                {
                    // select expression
                    if (!db.EqualsIdentifiers(col!, c.GetColumnName(session, j)))
                    {
                        continue;
                    }

                    if (tableAlias is null)
                    {
                        return j;
                    }

                    string? ca = c.GetOriginalTableAliasName();
                    if (ca is not null)
                    {
                        if (db.EqualsIdentifiers(ca, tableAlias))
                        {
                            return j;
                        }
                    }
                    else if (filters is not null)
                    {
                        // select id from test order by test.id
                        foreach (TableFilter f in filters)
                        {
                            if (db.EqualsIdentifiers(f.GetTableAlias(), tableAlias))
                            {
                                return j;
                            }
                        }
                    }
                }
                else if (ec is Alias alias)
                {
                    if (tableAlias is null && db.EqualsIdentifiers(col!, alias.GetAlias(session, j)))
                    {
                        return j;
                    }

                    Expression ec2 = alias.GetNonAliasExpression();
                    if (ec2 is ExpressionColumn c2)
                    {
                        string ta = exprCol.GetSql(HasSql.DefaultSqlFlags, Expression.WithoutParentheses);
                        string tb = c2.GetSql(HasSql.DefaultSqlFlags, Expression.WithoutParentheses);
                        string s2 = c2.GetColumnName(session, j);
                        if (db.EqualsIdentifiers(col!, s2) && db.EqualsIdentifiers(ta, tb))
                        {
                            return j;
                        }
                    }
                }
            }
        }
        else if (expressionSQL is not null)
        {
            string s = e.GetSql(HasSql.DefaultSqlFlags, Expression.WithoutParentheses);
            for (int j = 0, size = expressionSQL.Count; j < size; j++)
            {
                if (db.EqualsIdentifiers(expressionSQL[j], s))
                {
                    return j;
                }
            }
        }

        if (expressionSQL is null
                || mustBeInResult && !session.GetMode().AllowUnrelatedOrderByExpressionsInDistinctQueries
                        && !CheckOrderOther(session, e, expressionSQL))
        {
            throw DbException.Get(Api.ErrorCode.OrderByNotInResult, e.GetTraceSql());
        }

        int idx = expressions.Count;
        expressions.Add(e);
        expressionSQL.Add(e.GetSql(HasSql.DefaultSqlFlags, Expression.WithoutParentheses));
        return idx;
    }

    /// <summary>
    /// An additional check for expression in ORDER BY list for DISTINCT selects
    /// that was not matched with selected expressions in regular way. This
    /// method allows expressions based only on selected expressions in different
    /// complicated ways with functions, comparisons, or operators.
    /// </summary>
    /// <param name="session">session</param>
    /// <param name="expr">expression to check</param>
    /// <param name="expressionSQL">SQL of allowed expressions</param>
    /// <returns>whether the specified expression should be allowed in ORDER BY list of DISTINCT select</returns>
    private static bool CheckOrderOther(SessionLocal session, Expression? expr, List<string> expressionSQL)
    {
        if (expr is null || expr.IsConstant())
        {
            // ValueExpression, null expression in CASE, or other
            return true;
        }

        string exprSQL = expr.GetSql(HasSql.DefaultSqlFlags, Expression.WithoutParentheses);
        foreach (string sql in expressionSQL)
        {
            if (session.GetDatabase().EqualsIdentifiers(exprSQL, sql))
            {
                return true;
            }
        }

        int count = expr.GetSubexpressionCount();
        if (!expr.IsEverything(ExpressionVisitor.DeterministicVisitor))
        {
            return false;
        }
        else if (count <= 0)
        {
            // Expression is an ExpressionColumn, Parameter, SequenceValue or
            // has other unsupported type without subexpressions
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            Expression? sub = expr.GetSubexpression(i);
            if (sub is null || !CheckOrderOther(session, sub, expressionSQL))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The sort order of ORDER BY, or null when the query has none.
    /// </summary>
    /// <remarks>
    /// It lives here rather than on Select because every query has an order, including the set
    /// operations that are not selects: a UNION with an ORDER BY sorts the union, and the sort
    /// belongs to the query that runs rather than to one of its arms.
    /// </remarks>
    protected Result.SortOrder? sort;

    // Ported Query.appendEndOfQueryToSQL. withTies is on Select in this existing port;
    // pass it from Select rather than introducing a second conflicting flag.
    protected void AppendEndOfQueryToSql(StringBuilder builder, int sqlFlags, Expression[] expressions, bool withTies = false)
    {
        if (sort is not null) sort.GetSql(builder.Append("\nORDER BY "), expressions, GetColumnCount(), sqlFlags);
        else if (orderList is not null)
        {
            builder.Append("\nORDER BY ");
            for (int i = 0; i < orderList.Count; i++)
            {
                if (i > 0) builder.Append(", ");
                orderList[i].GetSql(builder, sqlFlags);
            }
        }
        if (offsetExpr is not null)
        {
            string count = offsetExpr.GetSql(sqlFlags, Expression.WithoutParentheses);
            builder.Append("\nOFFSET ").Append(count).Append(count == "1" ? " ROW" : " ROWS");
        }
        if (fetchExpr is not null)
        {
            builder.Append("\nFETCH ").Append(offsetExpr is not null ? "NEXT" : "FIRST");
            string count = fetchExpr.GetSql(sqlFlags, Expression.WithoutParentheses);
            bool withCount = fetchPercent || count != "1";
            if (withCount)
            {
                builder.Append(' ').Append(count);
                if (fetchPercent) builder.Append(" PERCENT");
            }
            builder.Append(withCount ? " ROWS" : " ROW").Append(withTies ? " WITH TIES" : " ONLY");
        }
    }

    /// <summary>
    /// Gets the sort order of this query.
    /// </summary>
    /// <returns>the sort order, or null</returns>
    public Result.SortOrder? GetSortOrder() => sort;
    /// <returns>the index</returns>
    public int GetOrderByIndex() => orderByIndex;

    /// <summary>
    /// Gets the index of the expression the LIMIT is applied to, or -1.
    /// </summary>
    /// <returns>the index</returns>
    public int GetLimitOffset() => limitOffset;

    /// <summary>
    /// Sets the expression that says how many rows to skip.
    /// </summary>
    /// <param name="offset">the expression</param>
    public void SetOffset(Expression offset)
    {
        ArgumentNullException.ThrowIfNull(offset);
        offsetExpr = offset;
    }

    /// <summary>
    /// Gets the expression that says how many rows to skip.
    /// </summary>
    /// <returns>the expression, or null</returns>
    public Expression? GetOffset() => offsetExpr;

    /// <summary>
    /// Sets the expression that says how many rows to read.
    /// </summary>
    /// <param name="fetch">the expression</param>
    public void SetFetch(Expression fetch)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        fetchExpr = fetch;
    }

    /// <summary>
    /// Gets the expression that says how many rows to read.
    /// </summary>
    /// <returns>the expression, or null</returns>
    public Expression? GetFetch() => fetchExpr;

    /// <summary>
    /// Sets whether the fetch is a percentage of the rows rather than a number of them.
    /// </summary>
    /// <param name="fetchPercent">true for a percentage</param>
    public void SetFetchPercent(bool fetchPercent) => this.fetchPercent = fetchPercent;

    /// <summary>
    /// Whether the fetch is a percentage of the rows rather than a number of them.
    /// </summary>
    /// <returns>true for a percentage</returns>
    public bool IsFetchPercent() => fetchPercent;

    /// <summary>
    /// The number of rows to skip and the number of rows to read.
    /// </summary>
    /// <remarks>
    /// The reference's OffsetFetch: the two numbers are read out of the expressions when the
    /// query runs, because a limit may name a parameter and its value is not known until then.
    /// </remarks>
    /// <param name="offset">the number of rows to skip</param>
    /// <param name="fetch">the number of rows to read, -1 for all of them</param>
    /// <param name="fetchPercent">whether the fetch is a percentage</param>
    public readonly record struct OffsetFetch(long Offset, long Fetch, bool FetchPercent);

    /// <summary>
    /// Evaluates the OFFSET and FETCH expressions.
    /// </summary>
    /// <param name="maxRows">the additional limit</param>
    /// <returns>the evaluated values</returns>
    public OffsetFetch GetOffsetFetch(long maxRows)
    {
        long offset;
        if (offsetExpr is not null)
        {
            Value v = offsetExpr.GetValue(GetSession());
            if (v == ValueNull.Instance || (offset = v.GetLong()) < 0)
            {
                throw DbException.GetInvalidValueException("result OFFSET", v);
            }
        }
        else
        {
            offset = 0;
        }

        long fetch = maxRows == 0 ? -1 : maxRows;
        if (fetchExpr is not null)
        {
            Value v = fetchExpr.GetValue(GetSession());
            long l;
            if (v == ValueNull.Instance || (l = v.GetLong()) < 0)
            {
                throw DbException.GetInvalidValueException("result FETCH", v);
            }

            fetch = fetch < 0 ? l : Math.Min(l, fetch);
        }

        bool percent = fetchPercent;
        if (percent)
        {
            if (fetch > 100)
            {
                throw DbException.GetInvalidValueException("result FETCH PERCENT", fetch);
            }

            // 0 PERCENT means 0
            if (fetch == 0)
            {
                percent = false;
            }
        }

        return new OffsetFetch(offset, fetch, percent);
    }

    /// <summary>
    /// Applies limits before converting the result into an IN value list, as Query.finishResult
    /// does. Deduplicating before a limit would change IN (SELECT ... LIMIT ...).
    /// </summary>
    protected LocalResult? FinishResult(
        LocalResult result, long offset, long fetch, bool fetchPercent, ResultTarget? target)
    {
        if (offset != 0)
        {
            result.SetOffset(offset);
        }

        if (fetch >= 0)
        {
            result.SetLimit(fetch);
            result.SetFetchPercent(fetchPercent);
        }

        // FETCH WITH TIES is still refused by the parser; its finishResult branch waits for it.
        result.Done();
        if (inPredicateSortTypes is not null)
        {
            result = ConvertToInPredicateValueListIfNecessary(result);
        }

        if (target is not null)
        {
            while (result.Next())
            {
                target.AddRow(result.CurrentRow());
            }

            result.Close();
            return null;
        }

        return result;
    }

    private LocalResult ConvertToInPredicateValueListIfNecessary(LocalResult result)
    {
        if (distinct)
        {
            if (inPredicateSortTypes is null)
            {
                return result;
            }

            if (sort is not null)
            {
                int[] sortTypes = sort.GetSortTypes();
                int count = inPredicateSortTypes.Length;
                if (sortTypes.Length >= count)
                {
                    int i = 0;
                    for (; i < count; i++)
                    {
                        if ((sortTypes[i] & SortOrder.Descending)
                            != (inPredicateSortTypes[i] & SortOrder.Descending))
                        {
                            break;
                        }
                    }

                    if (i == count)
                    {
                        return result;
                    }
                }
            }
        }

        return ConvertToInPredicateValueList(result);
    }

    /// <summary>Converts the completed result into a distinct, optionally ordered IN value list.</summary>
    public LocalResult ConvertToInPredicateValueList(ResultInterface result)
    {
        int count = GetColumnCount();
        LocalResult distinctResult = new(GetSession(), GetExpressions(), count, count);
        distinctResult.SetInPredicateValueListResult(inPredicateSortTypes!);
        result.Reset();
        while (result.Next())
        {
            distinctResult.AddRow(result.CurrentRow());
        }

        result.Close();
        distinctResult.Done();
        return distinctResult;
    }
}
