/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.ExpressionVisitor.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The visitor pattern is used to iterate through all expressions of a query
 * to optimize a statement.
 *
 * A visitor is a question asked of every expression in a query tree, and the whole optimisation
 * rests on being able to ask the same question many times without allocating: the two that are
 * asked on every node of every query — can this be evaluated now, and is it independent of
 * anything unset — are cached per query level, because "independent at level 0" is a different
 * answer from "independent at level 1", and recomputing that per node is the sort of cost the
 * reference went out of its way to avoid.
 *
 * The collectors that gather something (dependencies, columns) are not here: they need
 * AllColumnsForPlan and DbObject, which are not ported yet, and a visitor that collected into a
 * set the port invented would be a second kind of planner. They refuse, and the questions they
 * answer are listed so the gap is visible rather than absent.
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using H2Sharp.Tables;

namespace H2Sharp.Expressions;

/// <summary>
/// The visitor pattern is used to iterate through all expressions of a query
/// to optimize a statement.
/// </summary>
public sealed class ExpressionVisitor
{
    /// <summary>
    /// Is the value independent on unset parameters or on columns of a higher
    /// level query, or sequence values (that means can it be evaluated right
    /// now)?
    /// </summary>
    public const int Independent = 0;

    /// <summary>
    /// The visitor singleton for the type INDEPENDENT.
    /// </summary>
    public static readonly ExpressionVisitor IndependentVisitor = new(Independent);

    /// <summary>
    /// Are all aggregates MIN(column), MAX(column), COUNT(*), MEDIAN(column),
    /// ENVELOPE(count) for the given table (GetTable)?
    /// </summary>
    public const int OptimizableAggregate = 1;

    /// <summary>
    /// Does the expression return the same results for the same parameters?
    /// </summary>
    public const int Deterministic = 2;

    /// <summary>
    /// The visitor singleton for the type DETERMINISTIC.
    /// </summary>
    public static readonly ExpressionVisitor DeterministicVisitor = new(Deterministic);

    /// <summary>
    /// Can the expression be evaluated, that means are all columns set to
    /// 'evaluatable'?
    /// </summary>
    public const int Evaluatable = 3;

    /// <summary>
    /// The visitor singleton for the type EVALUATABLE.
    /// </summary>
    public static readonly ExpressionVisitor EvaluableVisitor = new(Evaluatable);

    /// <summary>
    /// Count of cached INDEPENDENT and EVALUATABLE visitors with different query
    /// level.
    /// </summary>
    private const int Cached = 8;

    private static readonly ExpressionVisitor[] IndependentVisitors = BuildIndependent();
    private static readonly ExpressionVisitor[] EvaluableVisitors = BuildEvaluable();

    /// <summary>
    /// Request to set the latest modification id (AddDataModificationId).
    /// </summary>
    public const int SetMaxDataModificationId = 4;

    /// <summary>
    /// Does the expression have no side effects (change the data)?
    /// </summary>
    public const int ReadOnly = 5;

    /// <summary>
    /// The visitor singleton for the type READONLY.
    /// </summary>
    public static readonly ExpressionVisitor ReadOnlyVisitor = new(ReadOnly);

    /// <summary>
    /// Does an expression have no relation to the given table filter
    /// (GetResolver)?
    /// </summary>
    public const int NotFromResolver = 6;

    /// <summary>
    /// Request to get the set of dependencies (AddDependency).
    /// </summary>
    public const int GetDependencies = 7;

    /// <summary>
    /// Can the expression be added to a condition of an outer query. Example:
    /// ROWNUM() can't be added as a condition to the inner query of select id
    /// from (select t.*, rownum as r from test t) where r between 2 and 3; Also
    /// a sequence expression must not be used.
    /// </summary>
    public const int QueryComparable = 8;

    /// <summary>
    /// Get all referenced columns for the optimiser.
    /// </summary>
    public const int GetColumns1 = 9;

    /// <summary>
    /// Get all referenced columns.
    /// </summary>
    public const int GetColumns2 = 10;

    /// <summary>
    /// Decrement query level of all expression columns.
    /// </summary>
    public const int DecrementQueryLevel = 11;

    /// <summary>
    /// The visitor singleton for the type QUERY_COMPARABLE.
    /// </summary>
    public static readonly ExpressionVisitor QueryComparableVisitor = new(QueryComparable);

    private readonly int type;

    private readonly int queryLevel;

    private readonly Table? table;

    private readonly ColumnResolver? resolver;

    private ExpressionVisitor(int type)
    {
        this.type = type;
        queryLevel = 0;
    }

    private ExpressionVisitor(int type, int queryLevel)
    {
        this.type = type;
        this.queryLevel = queryLevel;
    }

    private ExpressionVisitor(int type, Table? table, ColumnResolver? resolver)
    {
        this.type = type;
        queryLevel = 0;
        this.table = table;
        this.resolver = resolver;
    }

    private static ExpressionVisitor[] BuildIndependent()
    {
        ExpressionVisitor[] a = new ExpressionVisitor[Cached];
        a[0] = IndependentVisitor;
        for (int i = 1; i < Cached; i++)
        {
            a[i] = new ExpressionVisitor(Independent, i);
        }

        return a;
    }

    private static ExpressionVisitor[] BuildEvaluable()
    {
        ExpressionVisitor[] a = new ExpressionVisitor[Cached];
        a[0] = EvaluableVisitor;
        for (int i = 1; i < Cached; i++)
        {
            a[i] = new ExpressionVisitor(Evaluatable, i);
        }

        return a;
    }

    /// <summary>
    /// Create a new visitor to check if all aggregates are for the given table.
    /// </summary>
    /// <param name="table">the table</param>
    /// <returns>the new visitor</returns>
    public static ExpressionVisitor GetOptimizableVisitor(Table table)
    {
        return new ExpressionVisitor(OptimizableAggregate, table, null);
    }

    /// <summary>
    /// Create a new visitor to check if no expression depends on the given
    /// resolver.
    /// </summary>
    /// <param name="resolver">the resolver</param>
    /// <returns>the new visitor</returns>
    public static ExpressionVisitor GetNotFromResolverVisitor(ColumnResolver resolver)
    {
        return new ExpressionVisitor(NotFromResolver, null, resolver);
    }

    /// <summary>
    /// Increment or decrement the query level.
    /// </summary>
    /// <remarks>
    /// Only the two cached visitors have levels; every other kind answers with itself, because
    /// "independent" is the only question whose answer depends on where in the query tree the
    /// expression is.
    /// </remarks>
    /// <param name="offset">1 to increment, -1 to decrement</param>
    /// <returns>this visitor or its clone with the changed query level</returns>
    public ExpressionVisitor IncrementQueryLevel(int offset)
    {
        if (type == Independent)
        {
            int level = offset + queryLevel;
            return level < Cached ? IndependentVisitors[level] : new ExpressionVisitor(Independent, level);
        }

        if (type == Evaluatable)
        {
            int level = offset + queryLevel;
            return level < Cached ? EvaluableVisitors[level] : new ExpressionVisitor(Evaluatable, level);
        }

        return this;
    }

    /// <summary>
    /// Get the column resolver. This is used for NOT_FROM_RESOLVER visitors.
    /// </summary>
    /// <returns>the column resolver</returns>
    public ColumnResolver? GetResolver()
    {
        return resolver;
    }

    /// <summary>
    /// Get the table. This is used for OPTIMIZABLE_MIN_MAX_COUNT_ALL visitors.
    /// </summary>
    /// <returns>the table</returns>
    public Table? GetTable()
    {
        return table;
    }

    /// <summary>
    /// Get the visitor type. Named for the same reason as Expression.GetTypeInfo: object.GetType()
    /// already means something else.
    /// </summary>
    /// <returns>the type</returns>
    public int GetVisitorType()
    {
        return type;
    }

    /// <summary>
    /// Get the query level. Only INDEPENDENT, EVALUATABLE and DECREMENT_QUERY_LEVEL have one.
    /// </summary>
    /// <returns>the query level</returns>
    internal int GetQueryLevel()
    {
        Debug.Assert(type is Independent or Evaluatable or DecrementQueryLevel, "only these visitors carry a level");
        return queryLevel;
    }

    /// <summary>
    /// Create a new visitor object to collect dependencies.
    /// </summary>
    public static ExpressionVisitor GetDependenciesVisitor(HashSet<object> dependencies)
        => throw new TODOException("ExpressionVisitor.GetDependenciesVisitor: DbObject is not ported");

    /// <summary>
    /// Get all referenced columns into the given set.
    /// </summary>
    public static ExpressionVisitor GetColumnsVisitor(HashSet<Column> columns, Table? table)
        => throw new TODOException("ExpressionVisitor.GetColumnsVisitor: AllColumnsForPlan is not ported");

    /// <summary>
    /// Create a visitor that tracks the newest data modification id seen.
    /// </summary>
    public static ExpressionVisitor GetMaxModificationIdVisitor()
        => throw new TODOException("ExpressionVisitor.GetMaxModificationIdVisitor");

    /// <summary>
    /// Create a new visitor to decrement query level in columns with the
    /// specified resolvers.
    /// </summary>
    /// <param name="columnResolvers">column resolvers</param>
    /// <param name="queryDecrement">0 to check whether operation is allowed, 1 to actually perform the decrement</param>
    /// <returns>the new visitor</returns>
    public static ExpressionVisitor GetDecrementQueryLevelVisitor(HashSet<ColumnResolver> columnResolvers, int queryDecrement)
        => throw new TODOException("ExpressionVisitor.GetDecrementQueryLevelVisitor");
}
