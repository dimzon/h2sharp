/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the three-valued logic of conditions.
 *
 * Every cell here is one of the answers SQL calls "unknown", and every cell is compared as the
 * word the reference prints (TRUE / FALSE / NULL) rather than as a boolean. That is the whole
 * point of the corpus: a port that answered "unknown" as false would pass every table where
 * the answer is known, and the tables that matter are the ones where it is not.
 *
 * The truth table for AND and OR is not the one people expect. AND stops at the first FALSE,
 * OR stops at the first TRUE, and *either* way a NULL seen on the way is remembered — so
 * FALSE OR NULL is FALSE and not unknown, while TRUE AND NULL is TRUE. Both are in the corpus,
 * because both are the cases a naive three-valued implementation gets wrong.
 *
 * NOT is three-valued too, and in the same way: NOT unknown is unknown. A NOT that turned NULL
 * into TRUE would make "WHERE NOT (x = NULL)" match rows.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Conditions;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ConditionOracleTests
{
    [JavaOracleFact]
    public void AndOrTruthTable_MatchesReference()
    {
        string body = """
                      String[] values = { "TRUE", "FALSE", "NULL" };
                      for (String a : values) {
                        for (String b : values) {
                          System.out.println(word.apply(andOf.apply(a, b)) + "|" + word.apply(orOf.apply(a, b)));
                        }
                      }
            """;

        List<string> java = OracleRunner.Run([], Setup + body);
        List<string> csharp = Describe();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    private static List<string> Describe()
    {
        List<string> lines = [];
        Expression[] sample = [ValueExpression.True, ValueExpression.False, TypedValueExpression.Unknown];
        foreach (Expression a in sample)
        {
            foreach (Expression b in sample)
            {
                // The reference is asked with a null session for the same reason: every operand
                // here is a constant, and a session that only had to exist would be an engine
                // dependency this test does not need to have.
                Expression and = new ConditionAndOr(ConditionAndOr.And, a, b);
                Expression or = new ConditionAndOr(ConditionAndOr.Or, a, b);
                lines.Add(Word(and.GetValue(null!)) + "|" + Word(or.GetValue(null!)));
            }
        }

        return lines;
    }

    private static string Word(Value value) => value switch
    {
        ValueNull => "NULL",
        ValueBoolean b => b.GetBoolean() ? "TRUE" : "FALSE",
        _ => value.GetSql(new System.Text.StringBuilder(), 0).ToString(),
    };

    private const string Setup = """
                      java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:condition_fold");
                      org.h2.jdbc.JdbcConnection c = (org.h2.jdbc.JdbcConnection) conn;
                      org.h2.engine.SessionLocal session = (org.h2.engine.SessionLocal) c.getSession();
                      java.util.function.Function<String, org.h2.expression.Expression> constant =
                          s -> switch (s) {
                              case "TRUE" -> org.h2.expression.ValueExpression.TRUE;
                              case "FALSE" -> org.h2.expression.ValueExpression.FALSE;
                              default -> org.h2.expression.TypedValueExpression.UNKNOWN;
                          };
                      java.util.function.BiFunction<String, String, org.h2.expression.Expression> andOf =
                          (a, b) -> new org.h2.expression.condition.ConditionAndOr(
                              org.h2.expression.condition.ConditionAndOr.AND, constant.apply(a), constant.apply(b));
                      java.util.function.BiFunction<String, String, org.h2.expression.Expression> orOf =
                          (a, b) -> new org.h2.expression.condition.ConditionAndOr(
                              org.h2.expression.condition.ConditionAndOr.OR, constant.apply(a), constant.apply(b));
                      java.util.function.Function<org.h2.expression.Expression, String> word =
                          e -> {
                            org.h2.value.Value v = e.getValue(null);
                            if (v == org.h2.value.ValueNull.INSTANCE) {
                              return "NULL";
                            }
                            return v.getBoolean() ? "TRUE" : "FALSE";
                          };
                      java.util.function.Function<org.h2.expression.Expression, String> folded =
                          e -> {
                            org.h2.expression.Expression o = e.optimize(session);
                            org.h2.value.Value v = o.getValue(null);
                            return (v == org.h2.value.ValueNull.INSTANCE ? "NULL"
                                : v.getBoolean() ? "TRUE" : "FALSE")
                                + "|" + o.getSQL(new StringBuilder(), 0) + "|" + o.getType().getValueType();
                          };
            """;

    /// <summary>
    /// What folding an AND or OR of constants answers: the value, the SQL spelling of the
    /// folded expression, and its type. UNKNOWN is a BOOLEAN NULL, not FALSE, and it prints
    /// as UNKNOWN; a TRUE next to an expression casts it rather than passing it through.
    /// </summary>
    [JavaOracleFact]
    public void AndOrFold_MatchesReference()
    {
        string body = """
                      String[] values = { "TRUE", "FALSE", "NULL" };
                      for (String a : values) {
                        for (String b : values) {
                          System.out.println(folded.apply(andOf.apply(a, b)) + "||"
                              + folded.apply(orOf.apply(a, b)));
                        }
                      }
                      conn.close();
            """;

        List<string> java = OracleRunner.Run([], Setup + body);
        List<string> csharp = DescribeFold();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    private static List<string> DescribeFold()
    {
        List<string> lines = [];
        Expression[] sample = [ValueExpression.True, ValueExpression.False, TypedValueExpression.Unknown];
        foreach (Expression a in sample)
        {
            foreach (Expression b in sample)
            {
                Expression and = new ConditionAndOr(ConditionAndOr.And, a, b);
                Expression or = new ConditionAndOr(ConditionAndOr.Or, a, b);
                lines.Add(Folded(and) + "||" + Folded(or));
            }
        }

        return lines;
    }

    private static string Folded(Expression expression)
    {
        Expression folded = expression.Optimize(null!);
        Value value = folded.GetValue(null!);
        return Word(value) + "|" + folded.GetSql(new System.Text.StringBuilder(), 0, Expression.WithoutParentheses)
            + "|" + folded.GetTypeInfo().GetValueType();
    }

    /// <summary>
    /// NOT over a folded AND and OR: unknown stays unknown through the negation, and the
    /// folded spelling survives it.
    /// </summary>
    [JavaOracleFact]
    public void NotOfFold_MatchesReference()
    {
        string body = """
                      String[] values = { "TRUE", "FALSE", "NULL" };
                      for (String a : values) {
                        for (String b : values) {
                          System.out.println(folded.apply(new org.h2.expression.condition.ConditionNot(
                              andOf.apply(a, b))) + "||" + folded.apply(
                              new org.h2.expression.condition.ConditionNot(orOf.apply(a, b))));
                        }
                      }
                      conn.close();
            """;

        List<string> java = OracleRunner.Run([], Setup + body);
        List<string> csharp = DescribeNotFold();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    private static List<string> DescribeNotFold()
    {
        List<string> lines = [];
        Expression[] sample = [ValueExpression.True, ValueExpression.False, TypedValueExpression.Unknown];
        foreach (Expression a in sample)
        {
            foreach (Expression b in sample)
            {
                lines.Add(Folded(new ConditionNot(new ConditionAndOr(ConditionAndOr.And, a, b))) + "||"
                    + Folded(new ConditionNot(new ConditionAndOr(ConditionAndOr.Or, a, b))));
            }
        }

        return lines;
    }
}
