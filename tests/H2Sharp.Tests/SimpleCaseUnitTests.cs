/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0. */
using System;
using System.Text;
using H2Sharp.Expressions;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class SimpleCaseUnitTests
{
    [Fact]
    public void LinkedWhensAreAddressedInOrder()
    {
        SimpleCase.SimpleWhen first = new(
            ValueExpression.Get(ValueInteger.Get(1)),
            ValueExpression.Get(ValueVarchar.Get("one")));
        SimpleCase.SimpleWhen second = new(
            ValueExpression.Get(ValueInteger.Get(2)),
            ValueExpression.Get(ValueVarchar.Get("two")));
        first.SetWhen(second);
        SimpleCase expression = new(
            ValueExpression.Get(ValueInteger.Get(2)), first,
            ValueExpression.Get(ValueVarchar.Get("other")));

        Assert.Equal(6, expression.GetSubexpressionCount());
        Assert.Same(first.Operands[0], expression.GetSubexpression(1));
        Assert.Same(first.Result, expression.GetSubexpression(2));
        Assert.Same(second.Operands[0], expression.GetSubexpression(3));
        Assert.Same(second.Result, expression.GetSubexpression(4));
    }

    [Fact]
    public void MultiOperandWhenCountsEveryOperand()
    {
        SimpleCase.SimpleWhen when = new(
            [
                ValueExpression.Get(ValueInteger.Get(1)),
                ValueExpression.Get(ValueInteger.Get(2)),
            ],
            ValueExpression.Get(ValueVarchar.Get("odd")));
        SimpleCase expression = new(ValueExpression.Get(ValueInteger.Get(1)), when, null);

        Assert.Equal(4, expression.GetSubexpressionCount());
        Assert.Same(when.Operands[1], expression.GetSubexpression(2));
        Assert.Same(when.Result, expression.GetSubexpression(3));
    }

    [Fact]
    public void SubexpressionRejectsInvalidIndexes()
    {
        SimpleCase expression = new(
            ValueExpression.Get(ValueInteger.Get(1)),
            new SimpleCase.SimpleWhen(
                ValueExpression.Get(ValueInteger.Get(1)),
                ValueExpression.Get(ValueVarchar.Get("one"))),
            null);

        Assert.Throws<IndexOutOfRangeException>(() => expression.GetSubexpression(-1));
        Assert.Throws<IndexOutOfRangeException>(() => expression.GetSubexpression(3));
    }

    [Fact]
    public void CombineTypesSkipsNullConstants()
    {
        Assert.Equal(
            TypeInfo.TypeUnknown,
            SimpleCase.CombineTypes(TypeInfo.TypeUnknown, ValueExpression.Null));
        Assert.Equal(
            TypeInfo.TypeInteger,
            SimpleCase.CombineTypes(TypeInfo.TypeUnknown, ValueExpression.Get(ValueInteger.Get(1))));
        Assert.Equal(
            TypeInfo.TypeInteger,
            SimpleCase.CombineTypes(TypeInfo.TypeInteger, ValueExpression.Null));
    }

    [Fact]
    public void SqlRendersOperandsAndElse()
    {
        SimpleCase.SimpleWhen when = new(
            [
                ValueExpression.Get(ValueInteger.Get(1)),
                ValueExpression.Get(ValueInteger.Get(2)),
            ],
            ValueExpression.Get(ValueVarchar.Get("odd")));
        SimpleCase expression = new(
            ValueExpression.Get(ValueInteger.Get(1)), when,
            ValueExpression.Get(ValueVarchar.Get("even")));

        Assert.Equal(
            "CASE 1 WHEN 1, 2 THEN 'odd' ELSE 'even' END",
            expression.GetUnenclosedSql(new StringBuilder(), HasSql.DefaultSqlFlags).ToString());
    }

    [Fact]
    public void ConstantExpressionIsIndependent()
    {
        SimpleCase expression = new(
            ValueExpression.Get(ValueInteger.Get(1)),
            new SimpleCase.SimpleWhen(
                ValueExpression.Get(ValueInteger.Get(1)),
                ValueExpression.Get(ValueVarchar.Get("one"))),
            ValueExpression.Get(ValueVarchar.Get("other")));

        Assert.True(expression.IsEverything(ExpressionVisitor.IndependentVisitor));
        Assert.Equal(1, expression.GetCost());
    }
}
