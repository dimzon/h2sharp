/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.Operation1_2.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;

using H2Sharp.Engine;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>
/// Operation with one or two arguments.
/// </summary>
public abstract class Operation1_2 : Expression
{
    /// <summary>
    /// The left part of the operation (the first argument).
    /// </summary>
    protected Expression left;

    /// <summary>
    /// The right part of the operation (the second argument), or null.
    /// </summary>
    protected Expression? right;

    /// <summary>
    /// The type of the result.
    /// </summary>
    protected TypeInfo? type;

    /// <summary>
    /// Creates an operation with the given arguments.
    /// </summary>
    /// <param name="left">the first argument</param>
    /// <param name="right">the second argument, or null</param>
    protected Operation1_2(Expression left, Expression? right)
    {
        this.left = left;
        this.right = right;
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => type!;

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
        left.MapColumns(resolver, level, state);
        right?.MapColumns(resolver, level, state);
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool value)
    {
        left.SetEvaluatable(tableFilter, value);
        right?.SetEvaluatable(tableFilter, value);
    }

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
        left.UpdateAggregate(session, stage);
        right?.UpdateAggregate(session, stage);
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        return left.IsEverything(visitor) && (right == null || right.IsEverything(visitor));
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        int cost = left.GetCost() + 1;
        if (right != null)
        {
            cost += right.GetCost();
        }

        return cost;
    }

    /// <inheritdoc/>
    public override int GetSubexpressionCount()
    {
        return right != null ? 2 : 1;
    }

    /// <inheritdoc/>
    public override Expression GetSubexpression(int index)
    {
        if (index == 0)
        {
            return left;
        }

        if (index == 1 && right != null)
        {
            return right;
        }

        throw new IndexOutOfRangeException();
    }
}
