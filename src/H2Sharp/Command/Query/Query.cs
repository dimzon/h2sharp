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
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Tables;
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
    /// Prepares the expressions of this query.
    /// </summary>
    public abstract void PrepareExpressions();

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
    internal void CleanupOrder(Expression[] expressions)
    {
        int[] sourceIndexes = sort!.GetQueryColumnIndexes();
        int count = sourceIndexes.Length;
        // The reference keeps these two in a BitSet; two flag arrays say the same thing.
        bool[] deduplication = new bool[expressions.Length];
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
    /// The sort order of ORDER BY, or null when the query has none.
    /// </summary>
    /// <remarks>
    /// It lives here rather than on Select because every query has an order, including the set
    /// operations that are not selects: a UNION with an ORDER BY sorts the union, and the sort
    /// belongs to the query that runs rather than to one of its arms.
    /// </remarks>
    protected Result.SortOrder? sort;

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
}
