/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 286: the arithmetic operation expression's own GetValue paths, without a SELECT.
 */
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class BinaryOperationSurfaceTests
{
    [Fact]
    public void ArithmeticOperationsEvaluateTheirOperands()
    {
        Database database = new(ConnectionInfo.Memory("binary_operation_surface"));
        SessionLocal session = new(database, "sa");
        try
        {
            Assert.Equal(5, Evaluate(BinaryOperation.OpType.PLUS, 2, 3, session));
            Assert.Equal(-1, Evaluate(BinaryOperation.OpType.MINUS, 2, 3, session));
            Assert.Equal(6, Evaluate(BinaryOperation.OpType.MULTIPLY, 2, 3, session));
            Assert.Equal(2, Evaluate(BinaryOperation.OpType.DIVIDE, 6, 3, session));
        }
        finally
        {
            database.Close();
        }
    }

    private static int Evaluate(
        BinaryOperation.OpType type, int left, int right, SessionLocal session)
    {
        BinaryOperation operation = new(type,
            ValueExpression.Get(ValueInteger.Get(left)),
            ValueExpression.Get(ValueInteger.Get(right)));
        _ = operation.Optimize(session);
        return operation.GetValue(session).GetInt();
    }
}
