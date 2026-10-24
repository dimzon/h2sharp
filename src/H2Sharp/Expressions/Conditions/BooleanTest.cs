/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.condition.BooleanTest.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Conditions;

/// <summary>
/// Boolean test (IS [NOT] { TRUE | FALSE | UNKNOWN }).
/// </summary>
public sealed class BooleanTest : SimplePredicate
{
    private readonly bool? right;

    /// <summary>
    /// Creates a boolean test.
    /// </summary>
    /// <param name="left">the expression to test</param>
    /// <param name="not">whether this is IS NOT</param>
    /// <param name="whenOperand">whether this is the when operand of the simple case</param>
    /// <param name="right">the tested value, or null for UNKNOWN</param>
    public BooleanTest(Expression left, bool not, bool whenOperand, bool? right)
        : base(left, not, whenOperand)
    {
        this.right = right;
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        return GetWhenSql(left.GetSql(builder, sqlFlags, AutoParentheses), sqlFlags);
    }

    /// <inheritdoc/>
    public override StringBuilder GetWhenSql(StringBuilder builder, int sqlFlags)
    {
        return builder.Append(not ? " IS NOT " : " IS ")
            .Append(right is null ? "UNKNOWN" : right.Value ? "TRUE" : "FALSE");
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        return ValueBoolean.Get(Answer(left.GetValue(session)));
    }

    /// <inheritdoc/>
    public override bool GetWhenValue(SessionLocal session, Value leftValue)
    {
        if (!whenOperand)
        {
            return base.GetWhenValue(session, leftValue);
        }

        return Answer(leftValue);
    }

    private bool Answer(Value value)
    {
        return (value is ValueNull ? right is null : right.HasValue && right.Value == value.GetBoolean()) ^ not;
    }

    /// <inheritdoc/>
    public override Expression? GetNotIfPossible(SessionLocal session)
    {
        if (whenOperand)
        {
            return null;
        }

        return new BooleanTest(left, !not, false, right);
    }

    /// <inheritdoc/>
    public override void CreateIndexConditions(SessionLocal session, TableFilter filter)
    {
        if (whenOperand || !filter.GetTable().IsQueryComparable())
        {
            return;
        }

        if (left is ExpressionColumn column
            && column.GetTypeInfo().GetValueType() == Value.Boolean
            && ReferenceEquals(filter, column.GetTableFilter()))
        {
            if (not)
            {
                if (right is null && column.GetColumn()!.IsNullable() == Column.Nullable)
                {
                    filter.AddIndexCondition(
                        Indexes.IndexCondition.GetInList(column, [ValueExpression.False, ValueExpression.True]));
                }
            }
            else
            {
                filter.AddIndexCondition(Indexes.IndexCondition.Get(Comparison.EqualNullSafe, column,
                    right is null ? TypedValueExpression.Unknown : ValueExpression.Get(ValueBoolean.Get(right.Value))));
            }
        }
    }
}
