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
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Conditions;

/// <summary>
/// Null predicate (IS [NOT] NULL).
/// </summary>
public sealed class ConditionIsNull : Condition
{
    private Expression left;

    private readonly bool not;

    private readonly bool whenOperand;

    /// <summary>
    /// Creates an IS [NOT] NULL predicate.
    /// </summary>
    /// <param name="left">the expression to test</param>
    /// <param name="not">whether this is IS NOT NULL</param>
    /// <param name="whenOperand">whether this is the when operand of a simple case</param>
    public ConditionIsNull(Expression left, bool not, bool whenOperand)
    {
        this.left = left;
        this.not = not;
        this.whenOperand = whenOperand;
    }

    /// <inheritdoc/>
    public override bool NeedParentheses()
    {
        return true;
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

    /// <summary>
    /// The answer for one value. Never NULL: that is what makes this a predicate rather than a
    /// comparison.
    /// </summary>
    private bool Answer(Value value)
    {
        if (value is ValueRow row)
        {
            foreach (Value v in row.GetList())
            {
                if ((v is not ValueNull) == !not)
                {
                    return false;
                }
            }

            return true;
        }

        return (value is ValueNull) != not;
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        return this;
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool b)
    {
        left.SetEvaluatable(tableFilter, b);
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
        return index == 0 ? left : throw new System.IndexOutOfRangeException();
    }

    /// <inheritdoc/>
    public override bool IsWhenConditionOperand()
    {
        return whenOperand;
    }
}
