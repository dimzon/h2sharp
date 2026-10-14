/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.query.QueryOrderBy.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;
using H2Sharp.Expressions;
using H2Sharp.Result;

namespace H2Sharp.Command.Query;

/// <summary>
/// Describes one element of the ORDER BY clause of a query.
/// </summary>
public sealed class QueryOrderBy
{
    /// <summary>
    /// The order by expression.
    /// </summary>
    public Expression? expression;

    /// <summary>
    /// The column index expression. This can be a column index number (1 meaning
    /// the first column of the select list) or a parameter (the parameter is a
    /// number representing the column index number).
    /// </summary>
    public Expression? columnIndexExpr;

    /// <summary>
    /// Sort type for this column.
    /// </summary>
    public int sortType;

    /// <summary>
    /// Appends the order by expression to the specified builder.
    /// </summary>
    /// <param name="builder">the string builder</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the builder</returns>
    public StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        _ = (expression ?? columnIndexExpr)!.GetUnenclosedSql(builder, sqlFlags);
        SortOrder.TypeToString(builder, sortType);
        return builder;
    }
}
