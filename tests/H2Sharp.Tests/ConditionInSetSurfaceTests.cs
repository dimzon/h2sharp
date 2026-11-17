/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 282: the constant IN-set's own negation and mapping surface, driven without a query.
 */
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Conditions;
using H2Sharp.Tables;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ConditionInSetSurfaceTests
{
    [Fact]
    public void NegationIsBuiltUnlessTheConditionIsWhenOperand()
    {
        Database database = new(ConnectionInfo.Memory("in_set_surface"));
        SessionLocal session = new(database, "sa");
        try
        {
            Expression left = new ExpressionColumn(database, new Column("A", TypeInfo.TypeInteger));
            List<Expression> values =
            [
                ValueExpression.Get(ValueInteger.Get(1)),
                ValueExpression.Get(ValueInteger.Get(2)),
            ];

            ConditionInConstantSet condition = new(session, left, false, false, values);
            Expression? negated = condition.GetNotIfPossible(session);
            Assert.NotNull(negated);
            Assert.IsType<ConditionInConstantSet>(negated);

            ConditionInConstantSet whenOperand = new(session, left, false, true, values);
            Assert.Null(whenOperand.GetNotIfPossible(session));
        }
        finally
        {
            database.Close();
        }
    }
}
