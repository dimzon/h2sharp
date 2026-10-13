/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.table.ColumnResolver.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The interface between an expression and the row it reads.
 *
 * A column expression does not know which row it is looking at — it knows a resolver does, and
 * asks it. That is what makes the same expression usable inside a table scan, a join and a
 * subquery: the resolver is what the query planner replaces when it knows where the columns
 * come from. The default answers here are the ones the reference gives them, which is the point
 * of defaults: a resolver that has no table alias says so by returning null, and every caller
 * already has to cope with that.
 */
using H2Sharp.Expressions;
using H2Sharp.Values;

namespace H2Sharp.Tables;

/// <summary>
/// Resolves columns to values for an expression.
/// </summary>
public interface ColumnResolver
{
    /// <summary>
    /// Get the table alias.
    /// </summary>
    /// <returns>the table alias</returns>
    string? GetTableAlias() => null;

    /// <summary>
    /// Get the column list.
    /// </summary>
    /// <returns>the column list</returns>
    Column[] GetColumns();

    /// <summary>
    /// Get the column with the specified name.
    /// </summary>
    /// <param name="name">the column name, must be a derived name if this column resolver has a derived column list</param>
    /// <returns>the column with the specified name, or null</returns>
    Column? FindColumn(string name);

    /// <summary>
    /// Get the name of the specified column.
    /// </summary>
    /// <param name="column">column</param>
    /// <returns>column name</returns>
    string GetColumnName(Column column) => column.GetName();

    /// <summary>
    /// Returns whether this column resolver has a derived column list.
    /// </summary>
    /// <returns>true if this column resolver has a derived column list, false otherwise</returns>
    bool HasDerivedColumnList() => false;

    /// <summary>
    /// Get the list of system columns, if any.
    /// </summary>
    /// <returns>the system columns or null</returns>
    Column[]? GetSystemColumns() => null;

    /// <summary>
    /// Get the row id pseudo column, if there is one.
    /// </summary>
    /// <returns>the row id column or null</returns>
    Column? GetRowIdColumn() => null;

    /// <summary>
    /// Get the schema name or null.
    /// </summary>
    /// <returns>the schema name or null</returns>
    string? GetSchemaName() => null;

    /// <summary>
    /// Get the value for the given column.
    /// </summary>
    /// <remarks>
    /// Null when there is no row to read from: that is not SQL NULL but the absence of an
    /// answer, which is why this is nullable while values themselves are not.
    /// </remarks>
    /// <param name="column">the column</param>
    /// <returns>the value, or null</returns>
    Value? GetValue(Column column);

    /// <summary>
    /// Get the table filter.
    /// </summary>
    /// <returns>the table filter</returns>
    TableFilter? GetTableFilter() => null;

    /// <summary>
    /// Descends one query level, for a column of a correlated subquery.
    /// </summary>
    /// <returns>this resolver</returns>
    ColumnResolver IncrementQueryLevel(int increment)
        => throw new TODOException("ColumnResolver.incrementQueryLevel: the query is not ported");

    /// <summary>
    /// Get the expression that represents this column.
    /// </summary>
    /// <param name="expressionColumn">the expression column</param>
    /// <param name="column">the column</param>
    /// <returns>the optimized expression</returns>
    Expression Optimize(ExpressionColumn expressionColumn, Column column) => expressionColumn;
}
