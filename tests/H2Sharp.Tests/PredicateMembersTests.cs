/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 103: BETWEEN and IN as expressions — the WHEN-operand form a simple CASE
 * uses, the plain evaluation form, the subexpression walk and the SQL text.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Conditions;
using H2Sharp.Tables;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class PredicateMembersTests
{
    private static (Database Database, SessionLocal Session) NewSession(string name)
    {
        Database database = new(ConnectionInfo.Memory(name + "_" + Guid.NewGuid().ToString("N")));
        return (database, new SessionLocal(database, "sa"));
    }

    [Fact]
    public void BetweenAsWhenOperandAndAsPredicate()
    {
        var (database, session) = NewSession("between_direct");

        Expression left = ValueExpression.Get(ValueInteger.Get(5));
        BetweenPredicate when = new(
            left, not: false, whenOperand: true, symmetric: false,
            a: ValueExpression.Get(ValueInteger.Get(1)),
            b: ValueExpression.Get(ValueInteger.Get(10)));

        Assert.True(when.NeedParentheses());
        Assert.True(when.IsWhenConditionOperand());
        Assert.True(when.GetWhenValue(session, ValueInteger.Get(5)));
        Assert.False(when.GetWhenValue(session, ValueInteger.Get(0)));
        // A NULL CASE operand is not between anything.
        Assert.False(when.GetWhenValue(session, ValueNull.Instance));
        // A when operand has no negated form of its own.
        Assert.Null(when.GetNotIfPossible(session));
        // A when operand is not folded: the CASE operand arrives later.
        Assert.Same(when, when.Optimize(session));
        Assert.Equal(3, when.GetSubexpressionCount());
        Assert.Same(left, when.GetSubexpression(0));
        Assert.NotNull(when.GetSubexpression(1));
        Assert.NotNull(when.GetSubexpression(2));
        Assert.Throws<IndexOutOfRangeException>(() => when.GetSubexpression(3));
        Assert.True(when.GetCost() > 0);

        // In predicate position the operands are folded and the result evaluated.
        BetweenPredicate plain = new(
            ValueExpression.Get(ValueInteger.Get(5)), not: false, whenOperand: false, symmetric: false,
            a: ValueExpression.Get(ValueInteger.Get(1)),
            b: ValueExpression.Get(ValueInteger.Get(10)));
        Assert.True(plain.GetValue(session).GetBoolean());
        // The base comparison is reachable through the same when-value entry point.
        _ = plain.GetWhenValue(session, ValueInteger.Get(5));

        BetweenPredicate symmetric = new(
            ValueExpression.Get(ValueInteger.Get(5)), not: false, whenOperand: false, symmetric: true,
            a: ValueExpression.Get(ValueInteger.Get(10)),
            b: ValueExpression.Get(ValueInteger.Get(1)));
        Assert.True(symmetric.GetValue(session).GetBoolean());

        database.Close();
    }

    [Fact]
    public void InAsWhenOperandAndAsPredicate()
    {
        var (database, session) = NewSession("in_direct");

        Expression left = ValueExpression.Get(ValueInteger.Get(2));
        List<Expression> values =
        [
            ValueExpression.Get(ValueInteger.Get(1)),
            ValueExpression.Get(ValueInteger.Get(2)),
        ];
        ConditionInConstantSet when = new(session, left, not: false, whenOperand: true, values);

        Assert.True(when.IsWhenConditionOperand());
        Assert.True(when.GetWhenValue(session, ValueInteger.Get(2)));
        Assert.False(when.GetWhenValue(session, ValueInteger.Get(3)));
        Assert.Equal(3, when.GetSubexpressionCount());
        Assert.Same(left, when.GetSubexpression(0));
        Assert.NotNull(when.GetSubexpression(1));
        Assert.NotNull(when.GetSubexpression(2));
        Assert.Throws<IndexOutOfRangeException>(() => when.GetSubexpression(-1));
        Assert.Throws<IndexOutOfRangeException>(() => when.GetSubexpression(3));
        Assert.True(when.GetValue(session).GetBoolean());

        ConditionInConstantSet plain = new(
            session,
            ValueExpression.Get(ValueInteger.Get(2)),
            not: false,
            whenOperand: false,
            [ValueExpression.Get(ValueInteger.Get(1)), ValueExpression.Get(ValueInteger.Get(2))]);
        Assert.True(plain.GetValue(session).GetBoolean());
        // The base when-value comparison runs when the predicate is not a when operand.
        _ = plain.GetWhenValue(session, ValueInteger.Get(2));

        ConditionInConstantSet not = new(
            session,
            ValueExpression.Get(ValueInteger.Get(9)),
            not: true,
            whenOperand: false,
            [ValueExpression.Get(ValueInteger.Get(1))]);
        Assert.True(not.GetValue(session).GetBoolean());

        database.Close();
    }
}
