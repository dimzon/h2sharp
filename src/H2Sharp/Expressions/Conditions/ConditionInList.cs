/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.condition.ConditionInList.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The scalar IN list and getAdditional follow H2 2.5. The specialized row-IN
 * createUniqueIndexConditions path is not ported yet.
 */
using System.Collections.Generic;

using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Conditions;

/// <summary>
/// An 'in' condition with a list of values, as in WHERE NAME IN(...).
/// </summary>
public sealed class ConditionInList : ConditionIn
{
    /// <summary>
    /// Creates a new IN(..) condition.
    /// </summary>
    /// <param name="left">the expression before IN</param>
    /// <param name="not">whether the result should be negated</param>
    /// <param name="whenOperand">whether this is a when operand</param>
    /// <param name="valueList">the value list (at least one element)</param>
    public ConditionInList(Expression left, bool not, bool whenOperand, List<Expression> valueList)
        : base(left, not, whenOperand, valueList)
    {
    }

    /// <inheritdoc/>
    protected override Value GetValue(SessionLocal session, Value left)
    {
        if (left.ContainsNull())
        {
            return ValueNull.Instance;
        }

        bool hasNull = false;
        foreach (Expression e in valueList)
        {
            Value r = e.GetValue(session);
            Value cmp = Comparison.Compare(session, left, r, Comparison.Equal);
            if (cmp is ValueNull)
            {
                hasNull = true;
            }
            else if (cmp is ValueBoolean b && b.GetBoolean())
            {
                return ValueBoolean.Get(!not);
            }
        }

        if (hasNull)
        {
            return ValueNull.Instance;
        }

        return ValueBoolean.Get(not);
    }

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
        left.MapColumns(resolver, level, state);
        foreach (Expression e in valueList)
        {
            e.MapColumns(resolver, level, state);
        }
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        bool constant = !whenOperand && left.IsConstant();
        if (constant && left.IsNullConstant())
        {
            return TypedValueExpression.Unknown;
        }

        bool allValuesConstant = true;
        bool allValuesNull = true;
        TypeInfo leftType = left.GetTypeInfo();
        for (int i = 0, l = valueList.Count; i < l; i++)
        {
            Expression e = valueList[i].Optimize(session);
            TypeInfo.CheckComparable(leftType, e.GetTypeInfo());
            if (e.IsConstant() && !e.GetValue(session).ContainsNull())
            {
                allValuesNull = false;
            }

            if (allValuesConstant && !e.IsConstant())
            {
                allValuesConstant = false;
            }

            if (left is ExpressionColumn column && e is Parameter parameter)
            {
                parameter.SetColumn(column.GetColumn());
            }

            valueList[i] = e;
        }

        return Optimize2(session, constant, allValuesConstant, allValuesNull);
    }

    private Expression Optimize2(SessionLocal session, bool constant, bool allValuesConstant, bool allValuesNull)
    {
        if (constant && allValuesConstant)
        {
            return ValueExpression.Get(ValueBoolean.Get(GetValue(session).GetBoolean()));
        }

        if (valueList.Count == 1)
        {
            return new Comparison(
                not ? Comparison.NotEqual : Comparison.Equal, left, valueList[0], whenOperand).Optimize(session);
        }

        if (allValuesConstant && !allValuesNull)
        {
            int leftType = left.GetTypeInfo().GetValueType();
            if (leftType == Value.Unknown)
            {
                return this;
            }

            if (leftType == Value.Enum && left is not ExpressionColumn)
            {
                return this;
            }

            return new ConditionInConstantSet(session, left, not, whenOperand, valueList).Optimize(session);
        }

        return this;
    }

    /// <inheritdoc/>
    public override Expression? GetNotIfPossible(SessionLocal session)
    {
        if (whenOperand)
        {
            return null;
        }

        return new ConditionInList(left, !not, false, valueList);
    }

    /// <inheritdoc/>
    protected override void CreateIndexConditions(TableFilter filter, ExpressionColumn l, List<Expression> valueList)
    {
        ExpressionVisitor visitor = ExpressionVisitor.GetNotFromResolverVisitor(filter);
        TypeInfo colType = l.GetTypeInfo();
        foreach (Expression e in valueList)
        {
            TypeInfo higherType = TypeInfo.GetHigherType(colType, e.GetTypeInfo());
            if (!e.IsEverything(visitor) || !Comparison.IsSafeIndexCondition(colType, higherType))
            {
                return;
            }
        }

        filter.AddIndexCondition(IndexCondition.GetInList(l, valueList));
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool b)
    {
        left.SetEvaluatable(tableFilter, b);
        foreach (Expression e in valueList)
        {
            e.SetEvaluatable(tableFilter, b);
        }
    }

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
        left.UpdateAggregate(session, stage);
        foreach (Expression e in valueList)
        {
            e.UpdateAggregate(session, stage);
        }
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
        => left.IsEverything(visitor) && AreAllValues(visitor);

    private bool AreAllValues(ExpressionVisitor visitor)
    {
        foreach (Expression e in valueList)
        {
            if (!e.IsEverything(visitor))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        int cost = left.GetCost();
        foreach (Expression e in valueList)
        {
            cost += e.GetCost();
        }

        return cost;
    }

    /// <summary>Extends an IN list with the matching operand of an OR equality.</summary>
    internal Expression? GetAdditional(Comparison other)
    {
        if (!not && !whenOperand && left.IsEverything(ExpressionVisitor.DeterministicVisitor))
        {
            Expression? add = other.GetIfEquals(left);
            if (add is not null)
            {
                List<Expression> list = new(valueList.Count + 1);
                list.AddRange(valueList);
                list.Add(add);
                return new ConditionInList(left, false, false, list);
            }
        }
        return null;
    }
}
