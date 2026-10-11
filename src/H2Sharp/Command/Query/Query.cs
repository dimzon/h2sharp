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
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Result;
using H2Sharp.Tables;

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
    /// Gets the index of the ORDER BY item this query orders by, or -1.
    /// </summary>
    /// <returns>the index</returns>
    public int GetOrderByIndex() => orderByIndex;

    /// <summary>
    /// Gets the index of the expression the LIMIT is applied to, or -1.
    /// </summary>
    /// <returns>the index</returns>
    public int GetLimitOffset() => limitOffset;
}
