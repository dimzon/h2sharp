/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.query.QueryOrderBy.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * One item of an ORDER BY clause: what to order by, and how.
 *
 * It is kept as a list rather than folded into the sort order, because the clause has to be
 * printed back out — a prepared statement's SQL is the text the caller wrote — and because
 * ORDER BY may name a column of the select list by its alias, which only the query knows how to
 * resolve. Both halves are the reference's and neither is answered here yet.
 */
using System.Text;
using H2Sharp.Expressions;

namespace H2Sharp.Command.Query;

/// <summary>
/// One item of an ORDER BY clause.
/// </summary>
public sealed class QueryOrderBy
{
    private readonly Expression expression;

    private readonly bool isDesc;

    /// <summary>
    /// Creates an item.
    /// </summary>
    /// <param name="expression">what to order by</param>
    /// <param name="isDesc">whether descending</param>
    public QueryOrderBy(Expression expression, bool isDesc)
    {
        this.expression = expression;
        this.isDesc = isDesc;
    }

    /// <summary>
    /// Gets what to order by.
    /// </summary>
    /// <returns>the expression</returns>
    public Expression GetExpression()
    {
        return expression;
    }

    /// <summary>
    /// Whether the order is descending.
    /// </summary>
    /// <returns>true if it is</returns>
    public bool IsDesc()
    {
        return isDesc;
    }

    /// <summary>
    /// Appends the SQL of this item.
    /// </summary>
    /// <param name="builder">the builder</param>
    /// <returns>the builder</returns>
    public StringBuilder GetSql(StringBuilder builder)
    {
        _ = expression.GetSql(builder, 0);
        return isDesc ? _ = builder.Append(" DESC") : builder;
    }
}
