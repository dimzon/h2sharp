/* Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.expression.condition.ConditionInArray. */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Conditions;

/// <summary>Quantified comparison predicate with array.</summary>
public class ConditionInArray : Condition
{
    private Expression left;
    private readonly bool whenOperand;
    private Expression right;
    private readonly bool all;
    private readonly int compareType;
    /// <summary>Creates a quantified array comparison.</summary>
    public ConditionInArray(Expression left, bool whenOperand, Expression right, bool all, int compareType)
    { this.left = left; this.whenOperand = whenOperand; this.right = right; this.all = all; this.compareType = compareType; }
    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session) => GetValue(session, left.GetValue(session));
    /// <inheritdoc/>
    public override bool GetWhenValue(SessionLocal session, Value left) => whenOperand ? GetValue(session, left).IsTrue() : base.GetWhenValue(session, left);
    private Value GetValue(SessionLocal session, Value l)
    {
        Value r = right.GetValue(session);
        if (r is ValueNull) return r;
        Value[] array = r.ConvertToAnyArray(session).GetList();
        if (array.Length == 0) return ValueBoolean.Get(all);
        if ((compareType & ~1) == Comparison.EqualNullSafe) return GetNullSafeValueSlow(session, array, l);
        if (l.ContainsNull()) return ValueNull.Instance;
        return GetValueSlow(session, array, l);
    }
    private Value GetValueSlow(SessionLocal session, Value[] array, Value l)
    {
        bool hasNull = false;
        ValueBoolean searched = ValueBoolean.Get(!all);
        foreach (Value v in array)
        {
            Value cmp = Comparison.Compare(session, l, v, compareType);
            if (cmp is ValueNull) hasNull = true;
            else if (ReferenceEquals(cmp, searched)) return searched;
        }
        return hasNull ? ValueNull.Instance : ValueBoolean.Get(all);
    }
    private Value GetNullSafeValueSlow(SessionLocal session, Value[] array, Value l)
    {
        bool searched = all == (compareType == Comparison.NotEqualNullSafe);
        foreach (Value v in array) if (session.AreEqual(l, v) == searched) return ValueBoolean.Get(!all);
        return ValueBoolean.Get(all);
    }
    /// <inheritdoc/>
    public override bool IsWhenConditionOperand() => whenOperand;
    /// <inheritdoc/>
    public override Expression? GetNotIfPossible(SessionLocal session) => whenOperand ? null
        : new ConditionInArray(left, false, right, !all, Comparison.GetNotCompareType(compareType));
    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    { left.MapColumns(resolver, level, state); right.MapColumns(resolver, level, state); }
    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        right = right.Optimize(session); left = left.Optimize(session);
        return !whenOperand && left.IsConstant() && right.IsConstant()
            ? TypedValueExpression.GetTypedIfNull(GetValue(session), TypeInfo.TypeBoolean) : this;
    }
    /// <inheritdoc/>
    public override void CreateIndexConditions(SessionLocal session, TableFilter filter)
    {
        if (whenOperand || all || compareType != Comparison.Equal || left is not ExpressionColumn column
            || filter != column.GetTableFilter()) return;
        if (right is Parameter parameter)
            filter.AddIndexCondition(IndexCondition.GetInList(column, new ParameterList(parameter)));
        else if (right.IsConstant())
        {
            Value r = right.GetValue(session);
            if (r is ValueArray array)
            {
                Value[] values = array.GetList();
                if (values.Length == 0) filter.AddIndexCondition(IndexCondition.Get(Comparison.False, column, ValueExpression.False));
                else
                {
                    TypeInfo colType = column.GetTypeInfo(), type = colType;
                    foreach (Value value in values) type = TypeInfo.GetHigherType(type, value.GetTypeInfo());
                    if (Comparison.IsSafeIndexCondition(colType, type))
                    {
                        List<Expression> list = [];
                        foreach (Value value in values) list.Add(ValueExpression.Get(value));
                        filter.AddIndexCondition(IndexCondition.GetInList(column, list));
                    }
                }
            }
        }
        else if (right.IsEverything(ExpressionVisitor.GetNotFromResolverVisitor(filter)))
        {
            TypeInfo arrayType = right.GetTypeInfo();
            if (arrayType.GetValueType() == Value.Array)
            {
                TypeInfo colType = column.GetTypeInfo();
                if (Comparison.IsSafeIndexCondition(colType, TypeInfo.GetHigherType(colType, (TypeInfo)arrayType.GetExtTypeInfo()!)))
                    filter.AddIndexCondition(IndexCondition.GetInArray(column, right));
            }
        }
    }
    // Java AbstractList is mapped to IReadOnlyList, preserving live parameter values on reuse.
    private sealed class ParameterList(Parameter parameter) : IReadOnlyList<Expression>
    {
        public int Count => !parameter.IsValueSet() ? 0 : parameter.GetParamValue() is ValueArray array ? array.GetList().Length : 1;
        public Expression this[int index] => parameter.GetParamValue() is ValueArray array
            ? ValueExpression.Get(array.GetList()[index]) : index == 0 ? ValueExpression.Get(parameter.GetParamValue()) : throw new IndexOutOfRangeException();
        public IEnumerator<Expression> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool value) { left.SetEvaluatable(tableFilter, value); right.SetEvaluatable(tableFilter, value); }
    /// <inheritdoc/>
    public override bool NeedParentheses() => true;
    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags) => GetWhenSql(left.GetSql(builder, sqlFlags, AutoParentheses), sqlFlags);
    /// <inheritdoc/>
    public override StringBuilder GetWhenSql(StringBuilder builder, int sqlFlags)
        => right.GetSql(builder.Append(' ').Append(Comparison.CompareTypes[compareType]).Append(all ? " ALL(" : " ANY("), sqlFlags).Append(')');
    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage) { left.UpdateAggregate(session, stage); right.UpdateAggregate(session, stage); }
    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor) => left.IsEverything(visitor) && right.IsEverything(visitor);
    /// <inheritdoc/>
    public override int GetCost() => left.GetCost() + right.GetCost() + 10;
}
