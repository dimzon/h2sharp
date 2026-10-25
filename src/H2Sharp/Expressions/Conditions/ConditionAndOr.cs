/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.condition.ConditionAndOr, H2 2.5.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Conditions;

/// <summary>An AND / OR condition with two operands.</summary>
public sealed class ConditionAndOr : Condition
{
    public const int And = 0;
    public const int Or = 1;

    private readonly int andOrType;
    private Expression left, right;
    private Expression? added;

    public ConditionAndOr(int andOrType, Expression left, Expression right)
    {
        if (left is null || right is null) throw DbException.GetInternalError(left + " " + right);
        this.andOrType = andOrType;
        this.left = left;
        this.right = right;
    }

    public int GetAndOrType() => andOrType;
    public override bool NeedParentheses() => true;

    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        left.GetSql(builder, sqlFlags, AutoParentheses);
        builder.Append(andOrType switch
        {
            And => "\n    AND ", Or => "\n    OR ",
            _ => throw DbException.GetInternalError("andOrType=" + andOrType),
        });
        return right.GetSql(builder, sqlFlags, AutoParentheses);
    }

    public override void CreateIndexConditions(SessionLocal session, TableFilter filter)
    {
        if (andOrType != And) return;
        left.CreateIndexConditions(session, filter);
        right.CreateIndexConditions(session, filter);
        added?.CreateIndexConditions(session, filter);
    }

    public override Expression? GetNotIfPossible(SessionLocal session)
    {
        Expression l = left.GetNotIfPossible(session) ?? new ConditionNot(left);
        Expression r = right.GetNotIfPossible(session) ?? new ConditionNot(right);
        return new ConditionAndOr(andOrType == And ? Or : And, l, r);
    }

    public override Value GetValue(SessionLocal session)
    {
        Value l = left.GetValue(session), r;
        switch (andOrType)
        {
            case And:
                if (l.IsFalse() || (r = right.GetValue(session)).IsFalse()) return ValueBoolean.False;
                if (l == ValueNull.Instance || r == ValueNull.Instance) return ValueNull.Instance;
                return ValueBoolean.True;
            case Or:
                if (l.IsTrue() || (r = right.GetValue(session)).IsTrue()) return ValueBoolean.True;
                if (l == ValueNull.Instance || r == ValueNull.Instance) return ValueNull.Instance;
                return ValueBoolean.False;
            default: throw DbException.GetInternalError("type=" + andOrType);
        }
    }

    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        right = right.Optimize(session);
        if (right.GetCost() < left.GetCost()) (left, right) = (right, left);
        // DbSettings is not ported: optimizeTwoEquals / optimizeOr use H2's true defaults,
        // as ConditionIn.CreateIndexConditions does for optimizeInList.
        switch (andOrType)
        {
            case And:
                if (left is Comparison l && right is Comparison r)
                {
                    Expression? additional = l.GetAdditionalAnd(session, r);
                    if (additional is not null) added = additional.Optimize(session);
                }
                break;
            case Or:
                Expression? reduced;
                if (left is Comparison lc && right is Comparison rc) reduced = lc.OptimizeOr(session, rc);
                else if (left is ConditionInList li && right is Comparison ric) reduced = li.GetAdditional(ric);
                else if (right is ConditionInList ri && left is Comparison lic) reduced = ri.GetAdditional(lic);
                else if (left is ConditionInConstantSet ls && right is Comparison rsc) reduced = ls.GetAdditional(session, rsc);
                else if (right is ConditionInConstantSet rs && left is Comparison lsc) reduced = rs.GetAdditional(session, lsc);
                else if (left is ConditionAndOr la && right is ConditionAndOr ra) reduced = OptimizeConditionAndOr(la, ra);
                else break;
                if (reduced is not null) return reduced.Optimize(session);
                break;
        }
        Expression? e = OptimizeIfConstant(session, andOrType, left, right);
        if (e is null) return OptimizeN(this);
        return e is ConditionAndOr condition ? OptimizeN(condition) : e;
    }

    private static Expression OptimizeN(ConditionAndOr condition)
    {
        if (condition.right is ConditionAndOr binary && binary.andOrType == condition.andOrType)
            return new ConditionAndOrN(condition.andOrType, condition.left, binary.left, binary.right);
        if (condition.right is ConditionAndOrN list && list.GetAndOrType() == condition.andOrType)
        {
            list.AddFirst(condition.left);
            return list;
        }
        return condition;
    }

    public static Expression? OptimizeIfConstant(SessionLocal session, int andOrType, Expression left, Expression right)
    {
        if (!left.IsConstant())
            return right.IsConstant() ? OptimizeConstant(session, andOrType, right.GetValue(session), left) : null;
        Value l = left.GetValue(session);
        if (!right.IsConstant()) return OptimizeConstant(session, andOrType, l, right);
        Value r = right.GetValue(session);
        switch (andOrType)
        {
            case And:
                if (l.IsFalse() || r.IsFalse()) return ValueExpression.False;
                if (l == ValueNull.Instance || r == ValueNull.Instance) return TypedValueExpression.Unknown;
                return ValueExpression.True;
            case Or:
                if (l.IsTrue() || r.IsTrue()) return ValueExpression.True;
                if (l == ValueNull.Instance || r == ValueNull.Instance) return TypedValueExpression.Unknown;
                return ValueExpression.False;
            default: throw DbException.GetInternalError("type=" + andOrType);
        }
    }

    private static Expression? OptimizeConstant(SessionLocal session, int andOrType, Value l, Expression right)
    {
        if (l == ValueNull.Instance) return null;
        return andOrType switch
        {
            And => l.GetBoolean() ? CastToBoolean(session, right) : ValueExpression.False,
            Or => l.GetBoolean() ? ValueExpression.True : CastToBoolean(session, right),
            _ => throw DbException.GetInternalError("type=" + andOrType),
        };
    }

    public override void AddFilterConditions(TableFilter filter)
    {
        if (andOrType == And) { left.AddFilterConditions(filter); right.AddFilterConditions(filter); }
        else base.AddFilterConditions(filter);
    }

    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
        left.MapColumns(resolver, level, state);
        right.MapColumns(resolver, level, state);
    }
    public override void SetEvaluatable(TableFilter filter, bool b)
    {
        left.SetEvaluatable(filter, b);
        right.SetEvaluatable(filter, b);
    }
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
        left.UpdateAggregate(session, stage);
        right.UpdateAggregate(session, stage);
    }
    public override bool IsEverything(ExpressionVisitor visitor) => left.IsEverything(visitor) && right.IsEverything(visitor);
    public override int GetCost() => left.GetCost() + right.GetCost();
    public override int GetSubexpressionCount() => 2;
    public override Expression? GetSubexpression(int index) => index switch
    {
        0 => left, 1 => right, _ => throw new IndexOutOfRangeException(),
    };

    internal static Expression? OptimizeConditionAndOr(ConditionAndOr left, ConditionAndOr right)
    {
        if (left.andOrType != And || right.andOrType != And) return null;
        Expression ll = left.left, lr = left.right, rl = right.left, rr = right.right;
        string rls = rl.GetSql(Util.HasSql.DefaultSqlFlags), rrs = rr.GetSql(Util.HasSql.DefaultSqlFlags);
        if (ll.IsEverything(ExpressionVisitor.DeterministicVisitor))
        {
            string sql = ll.GetSql(Util.HasSql.DefaultSqlFlags);
            if (sql == rls) return new ConditionAndOr(And, ll, new ConditionAndOr(Or, lr, rr));
            if (sql == rrs) return new ConditionAndOr(And, ll, new ConditionAndOr(Or, lr, rl));
        }
        if (lr.IsEverything(ExpressionVisitor.DeterministicVisitor))
        {
            string sql = lr.GetSql(Util.HasSql.DefaultSqlFlags);
            if (sql == rls) return new ConditionAndOr(And, lr, new ConditionAndOr(Or, ll, rr));
            if (sql == rrs) return new ConditionAndOr(And, lr, new ConditionAndOr(Or, ll, rl));
        }
        return null;
    }
}
