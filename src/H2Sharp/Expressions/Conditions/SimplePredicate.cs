/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.condition.SimplePredicate.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Conditions;

/// <summary>
/// Base class for simple predicates.
/// </summary>
public abstract class SimplePredicate : Condition
{
    /// <summary>
    /// The left hand side of the expression.
    /// </summary>
    protected Expression left;

    /// <summary>
    /// Whether it is a "not" condition (e.g. "is not null").
    /// </summary>
    protected readonly bool not;

    /// <summary>
    /// Where this is the when operand of the simple case.
    /// </summary>
    protected readonly bool whenOperand;

    /// <summary>
    /// Creates a simple predicate.
    /// </summary>
    /// <param name="left">the left hand side</param>
    /// <param name="not">whether it is a "not" condition</param>
    /// <param name="whenOperand">whether this is the when operand of the simple case</param>
    protected SimplePredicate(Expression left, bool not, bool whenOperand)
    {
        this.left = left;
        this.not = not;
        this.whenOperand = whenOperand;
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        if (!whenOperand && left.IsConstant())
        {
            // Reference ValueExpression.getBoolean(Value): NULL folds to UNKNOWN, not to NULL.
            Value value = GetValue(session);
            return value is ValueNull ? TypedValueExpression.Unknown : ValueExpression.Get(value);
        }

        return this;
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool b)
    {
        left.SetEvaluatable(tableFilter, b);
    }

    /// <inheritdoc/>
    public sealed override bool NeedParentheses()
    {
        return true;
    }

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
        left.UpdateAggregate(session, stage);
    }

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
        left.MapColumns(resolver, level, state);
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        return left.IsEverything(visitor);
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        return left.GetCost() + 1;
    }

    /// <inheritdoc/>
    public override int GetSubexpressionCount()
    {
        return 1;
    }

    /// <inheritdoc/>
    public override Expression? GetSubexpression(int index)
    {
        if (index == 0)
        {
            return left;
        }

        throw new System.IndexOutOfRangeException();
    }

    /// <inheritdoc/>
    public sealed override bool IsWhenConditionOperand()
    {
        return whenOperand;
    }
}
