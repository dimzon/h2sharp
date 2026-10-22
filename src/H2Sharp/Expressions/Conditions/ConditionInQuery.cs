/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.condition.ConditionInQuery.
 * Copyright 2026 H2Sharp contributors.
 */
using System.Text;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Conditions;

/// <summary>An IN predicate with a subquery, as in WHERE ID IN (SELECT ...).</summary>
public sealed class ConditionInQuery : PredicateWithSubquery
{
    private Expression left;
    private readonly bool not;
    private readonly bool whenOperand;
    private readonly bool all;
    private readonly int compareType;

    /// <summary>Creates a quantified comparison with a subquery.</summary>
    public ConditionInQuery(
        Expression left, bool not, bool whenOperand, Query query, bool all, int compareType)
        : base(query)
    {
        this.left = left;
        this.not = not;
        this.whenOperand = whenOperand;
        // Must precede preparation: other expression methods may be called in different orders.
        query.SetInPredicateResult();
        query.SetNeverLazy(true);
        query.SetDistinctIfPossible();
        this.all = all;
        this.compareType = compareType;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session) => GetValue(session, left.GetValue(session));

    /// <inheritdoc/>
    public override bool GetWhenValue(SessionLocal session, Value left)
        => !whenOperand ? base.GetWhenValue(session, left) : GetValue(session, left).IsTrue();

    private Value GetValue(SessionLocal session, Value left)
    {
        query.SetSession(session);
        LocalResult rows = (LocalResult)query.Run(0);
        if (!rows.HasNext())
        {
            return ValueBoolean.Get(not ^ all);
        }

        if ((compareType & ~1) == Comparison.EqualNullSafe)
        {
            return GetNullSafeValueSlow(session, rows, left);
        }

        if (left.ContainsNull())
        {
            return ValueNull.Instance;
        }

        // DbSettings is not ported; optimizeInSelect's reference default is true.
        if (all || compareType != Comparison.Equal)
        {
            return GetValueSlow(session, rows, left);
        }

        int columnCount = query.GetColumnCount();
        if (columnCount != 1)
        {
            Value[] leftValue = left.ConvertToAnyRow().GetList();
            if (columnCount == leftValue.Length && rows.ContainsDistinct(leftValue))
            {
                return ValueBoolean.Get(!not);
            }
        }
        else
        {
            TypeInfo colType = rows.GetColumnType(0);
            if (colType.GetValueType() == Value.Null)
            {
                return ValueNull.Instance;
            }

            if (left.GetValueType() == Value.Row)
            {
                left = ((ValueRow)left).GetList()[0];
            }

            if (rows.ContainsDistinct([left]))
            {
                return ValueBoolean.Get(!not);
            }
        }

        return rows.ContainsNull() ? ValueNull.Instance : ValueBoolean.Get(not);
    }

    private Value GetValueSlow(SessionLocal session, ResultInterface rows, Value value)
    {
        bool simple = value.GetValueType() != Value.Row && query.GetColumnCount() == 1;
        bool hasNull = false;
        ValueBoolean searched = ValueBoolean.Get(!all);
        while (rows.Next())
        {
            Value?[] currentRow = rows.CurrentRow();
            Value comparison = Comparison.Compare(
                session, value, simple ? currentRow[0]! : ValueRow.Get(currentRow!), compareType);
            if (ReferenceEquals(comparison, ValueNull.Instance))
            {
                hasNull = true;
            }
            else if (ReferenceEquals(comparison, searched))
            {
                return ValueBoolean.Get(not == all);
            }
        }

        return hasNull ? ValueNull.Instance : ValueBoolean.Get(not ^ all);
    }

    private Value GetNullSafeValueSlow(SessionLocal session, ResultInterface rows, Value value)
    {
        bool simple = value.GetValueType() != Value.Row && query.GetColumnCount() == 1;
        bool searched = all == (compareType == Comparison.NotEqualNullSafe);
        while (rows.Next())
        {
            Value?[] currentRow = rows.CurrentRow();
            if (session.AreEqual(value, simple ? currentRow[0]! : ValueRow.Get(currentRow!)) == searched)
            {
                return ValueBoolean.Get(not == all);
            }
        }

        return ValueBoolean.Get(not ^ all);
    }

    /// <inheritdoc/>
    public override bool IsWhenConditionOperand() => whenOperand;

    /// <inheritdoc/>
    public override Expression? GetNotIfPossible(SessionLocal session)
        => whenOperand ? null : new ConditionInQuery(left, !not, false, query, all, compareType);

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
        left.MapColumns(resolver, level, state);
        base.MapColumns(resolver, level, state);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        base.Optimize(session);
        left = left.Optimize(session);
        TypeInfo.CheckComparable(left.GetTypeInfo(), query.GetRowDataType());
        return this;
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool value)
    {
        left.SetEvaluatable(tableFilter, value);
        base.SetEvaluatable(tableFilter, value);
    }

    /// <inheritdoc/>
    public override bool NeedParentheses() => true;

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        bool outerNot = not && (all || compareType != Comparison.Equal);
        if (outerNot)
        {
            builder.Append("NOT (");
        }

        left.GetSql(builder, sqlFlags, AutoParentheses);
        GetWhenSql(builder, sqlFlags);
        if (outerNot)
        {
            builder.Append(')');
        }

        return builder;
    }

    /// <inheritdoc/>
    public override StringBuilder GetWhenSql(StringBuilder builder, int sqlFlags)
    {
        if (all)
        {
            builder.Append(Comparison.CompareTypes[compareType]).Append(" ALL");
        }
        else if (compareType == Comparison.Equal)
        {
            if (not)
            {
                builder.Append(" NOT");
            }

            builder.Append(" IN");
        }
        else
        {
            builder.Append(' ').Append(Comparison.CompareTypes[compareType]).Append(" ANY");
        }

        return base.GetUnenclosedSql(builder, sqlFlags);
    }

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
        left.UpdateAggregate(session, stage);
        base.UpdateAggregate(session, stage);
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
        => left.IsEverything(visitor) && base.IsEverything(visitor);

    /// <inheritdoc/>
    public override int GetCost() => left.GetCost() + base.GetCost();

    /// <inheritdoc/>
    public override void CreateIndexConditions(SessionLocal session, TableFilter filter)
    {
        // DbSettings.optimizeInList is not yet ported; its reference default is true.
        if (not || whenOperand || compareType != Comparison.Equal || query.GetColumnCount() != 1
            || left is not ExpressionColumn column)
        {
            return;
        }

        TypeInfo colType = left.GetTypeInfo();
        TypeInfo queryType = query.GetExpressions()[0].GetTypeInfo();
        TypeInfo higherType = TypeInfo.GetHigherType(colType, queryType);
        if (!Comparison.IsSafeIndexCondition(colType, higherType))
        {
            return;
        }

        int leftType = colType.GetValueType();
        if (!DataType.HasTotalOrdering(leftType) && leftType != queryType.GetValueType())
        {
            return;
        }

        if (filter != column.GetTableFilter()
            || !query.IsEverything(ExpressionVisitor.GetNotFromResolverVisitor(filter)))
        {
            return;
        }

        filter.AddIndexCondition(IndexCondition.GetInQuery(column, query));
    }
}
