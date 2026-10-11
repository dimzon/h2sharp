/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.condition.ConditionNot.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A NOT condition.
 *
 * NOT is three-valued too, and that is the only interesting thing about it: NOT unknown is
 * unknown, so the NULL check comes first and returns the same NULL rather than negating it. A
 * negation that turned NULL into TRUE would make "WHERE NOT (x = NULL)" match rows.
 *
 * The optimisation is the reason this class is more than a negation. The wrapped condition is
 * asked for a negated form of itself, and `NOT ID > 10` becomes `ID <= 10` — which is not the
 * same work, and in some cases is much cheaper: it can use an index. So the NOT disappears when
 * the thing under it can negate itself, and only stays when it cannot.
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Conditions;

/// <summary>
/// A NOT condition.
/// </summary>
public sealed class ConditionNot : Condition
{
    private Expression condition;

    /// <summary>
    /// Creates a NOT condition.
    /// </summary>
    /// <param name="condition">the condition to negate</param>
    public ConditionNot(Expression condition)
    {
        this.condition = condition;
    }

    /// <inheritdoc/>
    public override Expression? GetNotIfPossible(SessionLocal session)
    {
        return Condition.CastToBoolean(session, condition.Optimize(session));
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value v = condition.GetValue(session);
        if (v is ValueNull)
        {
            return v;
        }

        // The reference calls convertToBoolean().negate(): converting first is not decoration,
        // it is what makes NOT work on a non-boolean ("NOT 1" is FALSE, not a type error).
        return ((ValueBoolean)v.ConvertToType(TypeInfo.TypeBoolean)).Negate();
    }

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
        condition.MapColumns(resolver, level, state);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        Expression? negated = condition.GetNotIfPossible(session);
        if (negated is not null)
        {
            return negated.Optimize(session);
        }

        Expression expr = condition.Optimize(session);
        if (expr.IsConstant())
        {
            Value v = expr.GetValue(session);
            if (v is ValueNull)
            {
                return TypedValueExpression.Unknown;
            }

            return ValueExpression.Get(((ValueBoolean)v.ConvertToType(TypeInfo.TypeBoolean)).Negate());
        }

        condition = expr;
        return this;
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool b)
    {
        condition.SetEvaluatable(tableFilter, b);
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        return condition.GetSql(builder.Append("NOT "), sqlFlags, WithoutParentheses);
    }

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
        condition.UpdateAggregate(session, stage);
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        return condition.IsEverything(visitor);
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        return condition.GetCost();
    }

    /// <inheritdoc/>
    public override int GetSubexpressionCount()
    {
        return 1;
    }

    /// <inheritdoc/>
    public override Expression? GetSubexpression(int index)
    {
        return index == 0 ? condition : throw new System.IndexOutOfRangeException();
    }
}
