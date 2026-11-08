/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of TrimFunction.GetUnenclosedSql, which the plan renderer only reaches
 * for a trim sitting in a WHERE/HAVING/QUALIFY clause.
 */
using System.Text;
using H2Sharp;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Functions;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class TrimFunctionCoverageTests
{
    [Fact]
    public void GetUnenclosedSqlAnswersThePort()
    {
        Expression from = ValueExpression.Get(ValueVarchar.Get("  x  "));
        Expression space = ValueExpression.Get(ValueVarchar.Get("x"));

        int[] flags = new[]
        {
            0,
            TrimFunction.Leading,
            TrimFunction.Trailing,
            TrimFunction.Leading | TrimFunction.Trailing,
            TrimFunction.MultiCharacter,
            TrimFunction.Leading | TrimFunction.MultiCharacter,
            TrimFunction.Trailing | TrimFunction.MultiCharacter,
            TrimFunction.Leading | TrimFunction.Trailing | TrimFunction.MultiCharacter,
        };

        foreach (int flag in flags)
        {
            Expression? right = (flag & TrimFunction.MultiCharacter) != 0 ? space : null;
            TrimFunction trim = new(from, right, flag);
            StringBuilder builder = new();
            trim.GetUnenclosedSql(builder, HasSql.DefaultSqlFlags);
            Assert.False(string.IsNullOrEmpty(builder.ToString()));
        }

        // The form without a trim set (space == null) renders too.
        TrimFunction plain = new(from, null, 0);
        StringBuilder b2 = new();
        plain.GetUnenclosedSql(b2, HasSql.DefaultSqlFlags);
        Assert.False(string.IsNullOrEmpty(b2.ToString()));
    }
}