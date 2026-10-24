// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.unit.TestBinaryOperation.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified: the reference's TestExpression extends Operation0, which the port merged
// into the Expression base, so the test twin extends Expression directly. The
// reference's main() entry is the runner; xUnit runs Test() directly.
using ErrorFactory = H2Sharp.Message.DbException;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Native.Tests.Unit;

/// <summary>
/// Test the binary operation.
/// </summary>
public sealed class TestBinaryOperation
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public void Test()
    {
        TestPlusMinus(BinaryOperation.OpType.PLUS);
        TestPlusMinus(BinaryOperation.OpType.MINUS);
        TestMultiply();
        TestDivide();
    }

    private static void TestPlusMinus(BinaryOperation.OpType type)
    {
        AssertPrecisionScale(2, 0, 2, type, 1, 0, 1, 0);
        AssertPrecisionScale(3, 1, 2, type, 1, 1, 1, 0);
        AssertPrecisionScale(3, 1, 2, type, 1, 0, 1, 1);
    }

    private static void TestMultiply()
    {
        AssertPrecisionScale(2, 0, 2, BinaryOperation.OpType.MULTIPLY, 1, 0, 1, 0);
        AssertPrecisionScale(2, 1, 2, BinaryOperation.OpType.MULTIPLY, 1, 1, 1, 0);
        AssertPrecisionScale(2, 1, 2, BinaryOperation.OpType.MULTIPLY, 1, 0, 1, 1);
    }

    private static void TestDivide()
    {
        AssertPrecisionScale(3, 2, 2, BinaryOperation.OpType.DIVIDE, 1, 0, 1, 0);
        AssertPrecisionScale(3, 3, 2, BinaryOperation.OpType.DIVIDE, 1, 1, 1, 0);
        AssertPrecisionScale(3, 1, 2, BinaryOperation.OpType.DIVIDE, 1, 0, 1, 1);
        AssertPrecisionScale(25, 0, 10, BinaryOperation.OpType.DIVIDE, 1, 3, 9, 27);
    }

    private static void AssertPrecisionScale(int expectedPrecision, int expectedScale,
        int expectedDecfloatPrecision, BinaryOperation.OpType type, int precision1,
        int scale1, int precision2, int scale2)
    {
        TestExpression left = new(TypeInfo.GetTypeInfo(Values.Value.Numeric, precision1, scale1, null));
        TestExpression right = new(TypeInfo.GetTypeInfo(Values.Value.Numeric, precision2, scale2, null));
        // The reference passes a null session: the NUMERIC and DECFLOAT paths of
        // BinaryOperation.Optimize never read it. If they ever do, this fails loudly.
        TypeInfo typeInfo = new BinaryOperation(type, left, right).Optimize(null!).GetTypeInfo();
        Assert.Equal(Values.Value.Numeric, typeInfo.GetValueType());
        Assert.Equal(expectedPrecision, typeInfo.GetPrecision());
        Assert.Equal(expectedScale, typeInfo.GetScale());
        left = new TestExpression(TypeInfo.GetTypeInfo(Values.Value.Decfloat, precision1, 0, null));
        right = new TestExpression(TypeInfo.GetTypeInfo(Values.Value.Decfloat, precision2, 0, null));
        typeInfo = new BinaryOperation(type, left, right).Optimize(null!).GetTypeInfo();
        Assert.Equal(Values.Value.Decfloat, typeInfo.GetValueType());
        Assert.Equal(expectedDecfloatPrecision, typeInfo.GetPrecision());
    }

    /// <summary>
    /// The reference's TestExpression: an expression that is only its type.
    /// </summary>
    private sealed class TestExpression : Expression
    {
        private readonly TypeInfo type;

        /// <summary>
        /// Builds the expression of the given type.
        /// </summary>
        /// <param name="type">the type the expression answers</param>
        public TestExpression(TypeInfo type)
        {
            this.type = type;
        }

        /// <inheritdoc/>
        public override Value GetValue(SessionLocal session)
            => throw ErrorFactory.GetUnsupportedException("");

        /// <inheritdoc/>
        public override TypeInfo GetTypeInfo() => type;

        /// <inheritdoc/>
        public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
            => throw ErrorFactory.GetUnsupportedException("");

        /// <inheritdoc/>
        public override bool IsEverything(ExpressionVisitor visitor) => false;

        /// <inheritdoc/>
        public override int GetCost() => 0;
    }
}
