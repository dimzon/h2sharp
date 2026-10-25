/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.condition.ConditionAndOrN, H2 2.5.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Conditions;

/// <summary>A flat AND / OR condition, avoiding recursion for long generated predicates.</summary>
public sealed class ConditionAndOrN : Condition
{
    private readonly int andOrType;
    private readonly List<Expression> expressions;
    private List<Expression>? added;

    public ConditionAndOrN(int andOrType, Expression expr1, Expression expr2, Expression expr3)
        : this(andOrType, new List<Expression> { expr1, expr2, expr3 }) { }
    public ConditionAndOrN(int andOrType, List<Expression> expressions)
    {
        this.andOrType = andOrType;
        this.expressions = expressions;
    }
    public int GetAndOrType() => andOrType;
    internal void AddFirst(Expression e) => expressions.Insert(0, e);
    public override bool NeedParentheses() => true;

    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        expressions[0].GetSql(builder, sqlFlags, AutoParentheses);
        for (int i = 1; i < expressions.Count; i++)
        {
            builder.Append(andOrType switch
            {
                ConditionAndOr.And => "\n    AND ", ConditionAndOr.Or => "\n    OR ",
                _ => throw DbException.GetInternalError("andOrType=" + andOrType),
            });
            expressions[i].GetSql(builder, sqlFlags, AutoParentheses);
        }
        return builder;
    }
    public override void CreateIndexConditions(SessionLocal session, TableFilter filter)
    {
        if (andOrType != ConditionAndOr.And) return;
        foreach (Expression e in expressions) e.CreateIndexConditions(session, filter);
        if (added is not null) foreach (Expression e in added) e.CreateIndexConditions(session, filter);
    }
    public override Expression? GetNotIfPossible(SessionLocal session)
    {
        List<Expression> list = new(expressions.Count);
        foreach (Expression e in expressions) list.Add(e.GetNotIfPossible(session) ?? new ConditionNot(e));
        return new ConditionAndOrN(andOrType == ConditionAndOr.And ? ConditionAndOr.Or : ConditionAndOr.And, list);
    }
    public override Value GetValue(SessionLocal session)
    {
        bool hasNull = false;
        switch (andOrType)
        {
            case ConditionAndOr.And:
                foreach (Expression e in expressions)
                {
                    Value value = e.GetValue(session);
                    if (value == ValueNull.Instance) hasNull = true;
                    else if (!value.GetBoolean()) return ValueBoolean.False;
                }
                return hasNull ? ValueNull.Instance : ValueBoolean.True;
            case ConditionAndOr.Or:
                foreach (Expression e in expressions)
                {
                    Value value = e.GetValue(session);
                    if (value == ValueNull.Instance) hasNull = true;
                    else if (value.GetBoolean()) return ValueBoolean.True;
                }
                return hasNull ? ValueNull.Instance : ValueBoolean.False;
            default: throw DbException.GetInternalError("type=" + andOrType);
        }
    }

    private static readonly IComparer<Expression> CompareByCost = Comparer<Expression>.Create((left, right) => left.GetCost() - right.GetCost());

    public override Expression Optimize(SessionLocal session)
    {
        for (int i = 0; i < expressions.Count; i++) expressions[i] = expressions[i].Optimize(session);
        // Collections.sort is stable; List.Sort is not. OrderBy preserves the
        // reference's relative order for operands with equal evaluation costs.
        Expression[] sorted = expressions.OrderBy(e => e, CompareByCost).ToArray();
        expressions.Clear();
        expressions.AddRange(sorted);
        OptimizeMerge(0);
        for (int i = 1; i < expressions.Count;)
        {
            Expression left = expressions[i - 1], right = expressions[i];
            // DbSettings is not ported: use the true defaults of optimizeTwoEquals / optimizeOr.
            switch (andOrType)
            {
                case ConditionAndOr.And:
                    if (left is Comparison l && right is Comparison r)
                    {
                        Expression? additional = l.GetAdditionalAnd(session, r);
                        if (additional is not null) (added ??= []).Add(additional.Optimize(session));
                    }
                    break;
                case ConditionAndOr.Or:
                    Expression? reduced;
                    if (left is Comparison lc && right is Comparison rc) reduced = lc.OptimizeOr(session, rc);
                    else if (left is ConditionInList li && right is Comparison ric) reduced = li.GetAdditional(ric);
                    else if (right is ConditionInList ri && left is Comparison lic) reduced = ri.GetAdditional(lic);
                    else if (left is ConditionInConstantSet ls && right is Comparison rsc) reduced = ls.GetAdditional(session, rsc);
                    else if (right is ConditionInConstantSet rs && left is Comparison lsc) reduced = rs.GetAdditional(session, lsc);
                    else if (left is ConditionAndOr la && right is ConditionAndOr ra) reduced = ConditionAndOr.OptimizeConditionAndOr(la, ra);
                    else break;
                    if (reduced is not null)
                    {
                        expressions.RemoveAt(i);
                        expressions[i - 1] = reduced.Optimize(session);
                        continue;
                    }
                    break;
            }
            Expression? folded = ConditionAndOr.OptimizeIfConstant(session, andOrType, left, right);
            if (folded is not null)
            {
                expressions.RemoveAt(i);
                expressions[i - 1] = folded;
                continue;
            }
            if (OptimizeMerge(i)) continue;
            i++;
        }
        sorted = expressions.OrderBy(e => e, CompareByCost).ToArray();
        expressions.Clear();
        expressions.AddRange(sorted);
        return expressions.Count == 1 ? CastToBoolean(session, expressions[0]) : this;
    }

    private bool OptimizeMerge(int i)
    {
        Expression e = expressions[i];
        if (e is ConditionAndOrN list && andOrType == list.andOrType)
        {
            expressions.RemoveAt(i);
            expressions.InsertRange(i, list.expressions);
            return true;
        }
        if (e is ConditionAndOr binary && andOrType == binary.GetAndOrType())
        {
            expressions[i] = binary.GetSubexpression(0)!;
            expressions.Insert(i + 1, binary.GetSubexpression(1)!);
            return true;
        }
        return false;
    }
    public override void AddFilterConditions(TableFilter filter)
    {
        if (andOrType == ConditionAndOr.And) foreach (Expression e in expressions) e.AddFilterConditions(filter);
        else base.AddFilterConditions(filter);
    }
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
        foreach (Expression e in expressions) e.MapColumns(resolver, level, state);
    }
    public override void SetEvaluatable(TableFilter filter, bool b)
    {
        foreach (Expression e in expressions) e.SetEvaluatable(filter, b);
    }
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
        foreach (Expression e in expressions) e.UpdateAggregate(session, stage);
    }
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        foreach (Expression e in expressions) if (!e.IsEverything(visitor)) return false;
        return true;
    }
    public override int GetCost()
    {
        int cost = 0;
        foreach (Expression e in expressions) cost += e.GetCost();
        return cost;
    }
    public override int GetSubexpressionCount() => expressions.Count;
    public override Expression? GetSubexpression(int index) => (uint)index < (uint)expressions.Count
        ? expressions[index] : throw new IndexOutOfRangeException();
}
