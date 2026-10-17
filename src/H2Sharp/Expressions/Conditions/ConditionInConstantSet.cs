/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.condition.ConditionInConstantSet.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Collections.Generic;

using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Conditions;

/// <summary>
/// Used for optimised IN(...) queries where the contents of the IN list are all constant and of
/// the same type.
/// </summary>
public sealed class ConditionInConstantSet : ConditionIn
{
    // A hash set cannot be used here, because values of different type or scale must compare
    // properly.
    private readonly SortedSet<Value> valueSet;

    private bool hasNull;

    private readonly TypeInfo type;

    /// <summary>
    /// Creates a new IN(..) condition.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="left">the expression before IN</param>
    /// <param name="not">whether the result should be negated</param>
    /// <param name="whenOperand">whether this is a when operand</param>
    /// <param name="valueList">the value list (at least two elements)</param>
    public ConditionInConstantSet(
        SessionLocal session, Expression left, bool not, bool whenOperand, List<Expression> valueList)
        : base(left, not, whenOperand, valueList)
    {
        // The reference holds the database rather than the session, because an expression can be
        // reused by another session; here the compare mode and the cast provider are captured,
        // which is the same two things the comparison reads.
        CastDataProvider provider = session.CastDataProvider;
        CompareMode compareMode = session.GetCompareMode();
        valueSet = new SortedSet<Value>(Comparer<Value>.Create((o1, o2) => o1.CompareTo(o2, provider, compareMode)));
        TypeInfo type = left.GetTypeInfo();
        foreach (Expression expression in valueList)
        {
            type = TypeInfo.GetHigherType(type, expression.GetTypeInfo());
        }

        this.type = type;
        foreach (Expression expression in valueList)
        {
            Add(expression.GetValue(session), session);
        }
    }

    private void Add(Value v, SessionLocal session)
    {
        if ((v = v.CastToValue(type, session)).ContainsNull())
        {
            hasNull = true;
        }
        else
        {
            _ = valueSet.Add(v);
        }
    }

    /// <inheritdoc/>
    protected override Value GetValue(SessionLocal session, Value left)
    {
        if ((left = left.CastToValue(type, session)).ContainsNull())
        {
            return ValueNull.Instance;
        }

        bool result = valueSet.Contains(left);
        if (!result && hasNull)
        {
            return ValueNull.Instance;
        }

        return ValueBoolean.Get(not ^ result);
    }

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
        => left.MapColumns(resolver, level, state);

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        return this;
    }

    /// <inheritdoc/>
    public override Expression? GetNotIfPossible(SessionLocal session)
    {
        if (whenOperand)
        {
            return null;
        }

        return new ConditionInConstantSet(session, left, !not, false, valueList);
    }

    /// <inheritdoc/>
    protected override void CreateIndexConditions(TableFilter filter, ExpressionColumn l, List<Expression> valueList)
    {
        TypeInfo colType = l.GetTypeInfo();
        TypeInfo higherType = TypeInfo.GetHigherType(colType, type);
        if (Comparison.IsSafeIndexCondition(colType, higherType))
        {
            filter.AddIndexCondition(IndexCondition.GetInList(l, valueList));
        }
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool b) => left.SetEvaluatable(tableFilter, b);

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage) => left.UpdateAggregate(session, stage);

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor) => left.IsEverything(visitor);

    /// <inheritdoc/>
    public override int GetCost() => left.GetCost();
}
