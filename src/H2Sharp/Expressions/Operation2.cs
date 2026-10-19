/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.Operation2.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;

using H2Sharp.Engine;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>
/// Operation with two arguments.
/// </summary>
public abstract class Operation2 : Expression
{
    /// <summary>
    /// The left part of the operation (the first argument).
    /// </summary>
    protected Expression left;

    /// <summary>
    /// The right part of the operation (the second argument).
    /// </summary>
    protected Expression right;

    /// <summary>
    /// The type of the result.
    /// </summary>
    protected TypeInfo? type;

    /// <summary>
    /// Creates an operation with the given arguments.
    /// </summary>
    /// <param name="left">the first argument</param>
    /// <param name="right">the second argument</param>
    protected Operation2(Expression left, Expression right)
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
        right.MapColumns(resolver, level, state);
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool value)
    {
        left.SetEvaluatable(tableFilter, value);
        right.SetEvaluatable(tableFilter, value);
    }

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
        left.UpdateAggregate(session, stage);
        right.UpdateAggregate(session, stage);
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        return left.IsEverything(visitor) && right.IsEverything(visitor);
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        return left.GetCost() + right.GetCost() + 1;
    }

    /// <inheritdoc/>
    public override int GetSubexpressionCount()
    {
        return 2;
    }

    /// <inheritdoc/>
    public override Expression GetSubexpression(int index)
    {
        switch (index)
        {
            case 0:
                return left;
            case 1:
                return right;
            default:
                throw new IndexOutOfRangeException();
        }
    }
}
