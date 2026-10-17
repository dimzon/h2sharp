/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.condition.BetweenPredicate.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;

using H2Sharp.Engine;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Conditions;

/// <summary>
/// BETWEEN predicate.
/// </summary>
public sealed class BetweenPredicate : Condition
{
    private Expression left;

    private readonly bool not;

    private readonly bool whenOperand;

    private bool symmetric;

    private Expression a;

    private Expression b;

    /// <summary>
    /// Creates a BETWEEN predicate.
    /// </summary>
    /// <param name="left">the expression to test</param>
    /// <param name="not">whether this is NOT BETWEEN</param>
    /// <param name="whenOperand">whether this is the when operand of a simple case</param>
    /// <param name="symmetric">whether the bounds may be given in either order</param>
    /// <param name="a">the lower bound</param>
    /// <param name="b">the upper bound</param>
    public BetweenPredicate(Expression left, bool not, bool whenOperand, bool symmetric, Expression a, Expression b)
    {
        this.left = left;
        this.not = not;
        this.whenOperand = whenOperand;
        this.symmetric = symmetric;
        this.a = a;
        this.b = b;
    }

    /// <inheritdoc/>
    public override bool NeedParentheses() => true;

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
        => GetWhenSql(left.GetSql(builder, sqlFlags, AutoParentheses), sqlFlags);

    /// <inheritdoc/>
    public override StringBuilder GetWhenSql(StringBuilder builder, int sqlFlags)
    {
        if (not)
        {
            _ = builder.Append(" NOT");
        }

        _ = builder.Append(" BETWEEN ");
        if (symmetric)
        {
            _ = builder.Append("SYMMETRIC ");
        }

        a.GetSql(builder, sqlFlags, AutoParentheses).Append(" AND ");
        return b.GetSql(builder, sqlFlags, AutoParentheses);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        a = a.Optimize(session);
        b = b.Optimize(session);
        TypeInfo leftType = left.GetTypeInfo();
        TypeInfo.CheckComparable(leftType, a.GetTypeInfo());
        TypeInfo.CheckComparable(leftType, b.GetTypeInfo());
        if (whenOperand)
        {
            return this;
        }

        Value? value = left.IsConstant() ? left.GetValue(session) : null;
        Value? aValue = a.IsConstant() ? a.GetValue(session) : null;
        Value? bValue = b.IsConstant() ? b.GetValue(session) : null;
        if (value is not null)
        {
            if (value is ValueNull)
            {
                return TypedValueExpression.Unknown;
            }

            if (aValue is not null && bValue is not null)
            {
                return ValueExpression.Get(ValueBoolean.Get(GetValue(session, value, aValue, bValue).GetBoolean()));
            }
        }

        if (symmetric)
        {
            if (aValue is ValueNull || bValue is ValueNull)
            {
                return TypedValueExpression.Unknown;
            }
        }
        else if (aValue is ValueNull && bValue is ValueNull)
        {
            return TypedValueExpression.Unknown;
        }

        if (aValue is not null && bValue is not null
            && session.CompareWithNull(aValue, bValue, false) == 0)
        {
            return new Comparison(
                not ? Comparison.NotEqual : Comparison.Equal, left, a, whenOperand: false).Optimize(session);
        }

        return this;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value value = left.GetValue(session);
        if (value is ValueNull)
        {
            return ValueNull.Instance;
        }

        return GetValue(session, value, a.GetValue(session), b.GetValue(session));
    }

    /// <inheritdoc/>
    public override bool GetWhenValue(SessionLocal session, Value leftValue)
    {
        if (!whenOperand)
        {
            return base.GetWhenValue(session, leftValue);
        }

        if (leftValue is ValueNull)
        {
            return false;
        }

        return GetValue(session, leftValue, a.GetValue(session), b.GetValue(session)).GetBoolean();
    }

    private Value GetValue(SessionLocal session, Value value, Value aValue, Value bValue)
    {
        int cmp1 = session.CompareWithNull(aValue, value, false);
        int cmp2 = session.CompareWithNull(value, bValue, false);
        if (cmp1 == int.MinValue)
        {
            return symmetric || cmp2 <= 0 ? ValueNull.Instance : ValueBoolean.Get(not);
        }

        if (cmp2 == int.MinValue)
        {
            return symmetric || cmp1 <= 0 ? ValueNull.Instance : ValueBoolean.Get(not);
        }

        return ValueBoolean.Get(not ^ (symmetric
            ? cmp1 <= 0 && cmp2 <= 0 || cmp1 >= 0 && cmp2 >= 0
            : cmp1 <= 0 && cmp2 <= 0));
    }

    /// <inheritdoc/>
    public override bool IsWhenConditionOperand() => whenOperand;

    /// <inheritdoc/>
    public override Expression? GetNotIfPossible(SessionLocal session)
    {
        if (whenOperand)
        {
            return null;
        }

        return new BetweenPredicate(left, !not, false, symmetric, a, b);
    }

    /// <inheritdoc/>
    public override void CreateIndexConditions(SessionLocal session, TableFilter filter)
    {
        if (!not && !whenOperand && !symmetric)
        {
            Comparison.CreateIndexConditions(filter, a, left, Comparison.SmallerEqual);
            Comparison.CreateIndexConditions(filter, left, b, Comparison.SmallerEqual);
        }
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool value)
    {
        left.SetEvaluatable(tableFilter, value);
        a.SetEvaluatable(tableFilter, value);
        b.SetEvaluatable(tableFilter, value);
    }

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
        left.UpdateAggregate(session, stage);
        a.UpdateAggregate(session, stage);
        b.UpdateAggregate(session, stage);
    }

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
        left.MapColumns(resolver, level, state);
        a.MapColumns(resolver, level, state);
        b.MapColumns(resolver, level, state);
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
        => left.IsEverything(visitor) && a.IsEverything(visitor) && b.IsEverything(visitor);

    /// <inheritdoc/>
    public override int GetCost() => left.GetCost() + a.GetCost() + b.GetCost() + 1;

    /// <inheritdoc/>
    public override int GetSubexpressionCount() => 3;

    /// <inheritdoc/>
    public override Expression GetSubexpression(int index) => index switch
    {
        0 => left,
        1 => a,
        2 => b,
        _ => throw new System.IndexOutOfRangeException(),
    };
}
