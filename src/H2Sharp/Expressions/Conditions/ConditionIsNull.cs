/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.condition.NullPredicate.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Null predicate (IS [NOT] NULL).
 *
 * Two things here are less obvious than they look.
 *
 * IS NULL never answers NULL. It is the one predicate that can be true, false, and always one
 * of them — "x IS NULL" is TRUE or FALSE whatever x is, unlike "x = NULL" which is never
 * either. That is the whole reason it is a separate class instead of a comparison.
 *
 * On a ROW it means something the column-wise reading does not: "the row IS NULL" asks whether
 * *every* field is null, not whether the row is null. So a row with one non-null field is not
 * null, and the loop below returns false on the first field that disagrees. The xor with `not`
 * is what makes the same loop answer both IS NULL and IS NOT NULL without a second pass.
 *
 * A NULL constant inside a row is special: for IS NULL it can be dropped from the row (it can
 * never make the answer false), and for IS NOT NULL it makes the answer false outright.
 */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Conditions;

/// <summary>
/// Null predicate (IS [NOT] NULL).
/// </summary>
public sealed class ConditionIsNull : SimplePredicate
{
    private bool optimized;

    /// <summary>
    /// Creates an IS [NOT] NULL predicate.
    /// </summary>
    /// <param name="left">the expression to test</param>
    /// <param name="not">whether this is IS NOT NULL</param>
    /// <param name="whenOperand">whether this is the when operand of a simple case</param>
    public ConditionIsNull(Expression left, bool not, bool whenOperand)
        : base(left, not, whenOperand)
    {
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        return GetWhenSql(left.GetSql(builder, sqlFlags, AutoParentheses), sqlFlags);
    }

    /// <inheritdoc/>
    public override StringBuilder GetWhenSql(StringBuilder builder, int sqlFlags)
    {
        return builder.Append(not ? " IS NOT NULL" : " IS NULL");
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        if (optimized)
        {
            return this;
        }

        Expression o = base.Optimize(session);
        if (!ReferenceEquals(o, this))
        {
            return o;
        }

        optimized = true;
        if (!whenOperand && left is ExpressionList list && !list.IsArray())
        {
            for (int i = 0, count = list.GetSubexpressionCount(); i < count; i++)
            {
                if (!list.GetSubexpression(i)!.IsNullConstant())
                {
                    continue;
                }

                if (not)
                {
                    return ValueExpression.False;
                }

                List<Expression> newList = new(count - 1);
                for (int j = 0; j < i; j++)
                {
                    newList.Add(list.GetSubexpression(j)!);
                }

                for (int j = i + 1; j < count; j++)
                {
                    Expression e = list.GetSubexpression(j)!;
                    if (!e.IsNullConstant())
                    {
                        newList.Add(e);
                    }
                }

                left = newList.Count == 1
                    ? newList[0]
                    : new ExpressionList(newList.ToArray(), false);
                break;
            }
        }

        return this;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        return ValueBoolean.Get(GetValue(left.GetValue(session)));
    }

    /// <inheritdoc/>
    public override bool GetWhenValue(SessionLocal session, Value leftValue)
    {
        if (!whenOperand)
        {
            return base.GetWhenValue(session, leftValue);
        }

        return GetValue(leftValue);
    }

    /// <summary>
    /// The answer for one value. Never NULL: that is what makes this a predicate rather than a
    /// comparison.
    /// </summary>
    private bool GetValue(Value leftValue)
    {
        if (leftValue.GetTypeInfo().GetValueType() == Value.Row)
        {
            foreach (Value v in ((ValueRow)leftValue).GetList())
            {
                if ((v is not ValueNull) ^ not)
                {
                    return false;
                }
            }

            return true;
        }

        return (leftValue is ValueNull) ^ not;
    }

    /// <inheritdoc/>
    public override Expression? GetNotIfPossible(SessionLocal session)
    {
        if (whenOperand)
        {
            return null;
        }

        Expression o = Optimize(session);
        if (!ReferenceEquals(o, this))
        {
            return o.GetNotIfPossible(session);
        }

        switch (left.GetTypeInfo().GetValueType())
        {
            case Value.Unknown:
            case Value.Row:
                return null;
        }

        return new ConditionIsNull(left, !not, false);
    }

    /// <inheritdoc/>
    public override void CreateIndexConditions(SessionLocal session, TableFilter filter)
    {
        if (not || whenOperand || !filter.GetTable()!.IsQueryComparable())
        {
            return;
        }

        if (left is ExpressionColumn column)
        {
            CreateNullIndexCondition(filter, column);
        }
        else if (left is ExpressionList list && !list.IsArray())
        {
            for (int i = 0, count = list.GetSubexpressionCount(); i < count; i++)
            {
                if (list.GetSubexpression(i) is ExpressionColumn item)
                {
                    CreateNullIndexCondition(filter, item);
                }
            }
        }
    }

    private static void CreateNullIndexCondition(TableFilter filter, ExpressionColumn column)
    {
        // Columns with a ROW type are not valid here, but the reference checks anyway.
        if (ReferenceEquals(filter, column.GetTableFilter())
            && column.GetTypeInfo().GetValueType() != Value.Row)
        {
            filter.AddIndexCondition(
                IndexCondition.Get(Comparison.EqualNullSafe, column, ValueExpression.Null));
        }
    }
}
