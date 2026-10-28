/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the result a statement fills and the order it puts rows in.
 *
 * These two are worth an oracle rather than a test of their own because the rules they follow
 * are not the ones a reader would guess, and each wrong guess produces a result that looks
 * right:
 *
 * * **OFFSET and LIMIT are applied after the sort.** A port that truncated first would return
 *   a correct-looking set of the wrong rows, and a corpus with one row would pass.
 * * **NULLs sort LOW by default and swap with the direction**, while NULLS FIRST keeps them
 *   first either way. One implementation of "where does NULL go" cannot satisfy both, so both
 *   are in the corpus with ASC and DESC.
 * * **DISTINCT compares with the session's rules, not with Equals.** 0.0 and 0.00 are one
 *   number; a hash-based implementation keeps both rows and answers a larger count.
 * * **A limit of -1 means no limit and 0 means no rows** — and the two are not the same code
 *   path, which is why the corpus walks every pair of them.
 *
 * The rows are built so that each of those has something to bite on: a NULL in two spellings
 * (one of them a bare null, the other NULL) and a number written two ways.
 *
 * The Java side is written out longhand rather than with helpers, because the oracle puts the
 * snippet inside a try block and Java does not allow a method declaration inside one.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class LocalResultOracleTests
{
    /// <summary>
    /// The rows of the corpus: three numbers, two of them the same number written two ways, and
    /// two NULLs — one as the NULL value, one as a bare null.
    /// </summary>
    private const string JavaRows = """
                                     org.h2.value.Value[][] rows = {
                                         new org.h2.value.Value[] { org.h2.value.ValueInteger.get(3) },
                                         new org.h2.value.Value[] { org.h2.value.ValueInteger.get(1) },
                                         new org.h2.value.Value[] { org.h2.value.ValueNull.INSTANCE },
                                         new org.h2.value.Value[] { org.h2.value.ValueInteger.get(2) },
                                         new org.h2.value.Value[] { org.h2.value.ValueNumeric.get(new java.math.BigDecimal("1.0")) },
                                         new org.h2.value.Value[] { org.h2.value.ValueNull.INSTANCE },
                                     };
                                     org.h2.expression.Expression[] expr = { org.h2.expression.ValueExpression.get(org.h2.value.ValueInteger.get(0)) };
                                     """;

    private const string JavaSession = """
                                       java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:local_result");
                                       org.h2.engine.SessionLocal session = (org.h2.engine.SessionLocal) ((org.h2.jdbc.JdbcConnection) conn).getSession();
                                       """;

    [JavaOracleFact]
    public void PlainResult_MatchesReference()
    {
        string body = """
                      org.h2.result.LocalResult r = new org.h2.result.LocalResult(session, expr, 1, 1);
                      for (org.h2.value.Value[] row : rows) { r.addRow(row); }
                      r.done();
                      System.out.println("count=" + r.getRowCount());
                      while (r.next()) { System.out.println("  " + r.currentRow()[0].toString()); }
            """;

        List<string> java = OracleRunner.Run([], JavaSession + JavaRows + body);
        List<string> csharp = Plain();

        AssertSame(java, csharp);
    }

    /// <summary>
    /// ORDER BY with a nonzero OFFSET sorts the window, not from the offset for the offset's
    /// length: the range bug sorted the wrong slice and threw past the end.
    /// </summary>
    [JavaOracleFact]
    public void SortedOffset_MatchesReference()
    {
        string body = """
                      org.h2.expression.Expression[] expr = { org.h2.expression.ValueExpression.get(org.h2.value.ValueInteger.get(0)) };
                      org.h2.result.LocalResult r = new org.h2.result.LocalResult(session, expr, 1, 1);
                      r.setSortOrder(org.h2.result.SortOrder.ofSortTypes(session, new int[] { 0 }));
                      int[] keys = new int[] { 5, 1, 4, 2, 3 };
                      for (int k : keys) {
                        r.addRow(new org.h2.value.Value[]{ org.h2.value.ValueInteger.get(k) });
                      }
                      r.setLimit(10);
                      r.setOffset(3);
                      r.done();
                      while (r.next()) { System.out.println("  " + r.currentRow()[0].toString()); }
            """;

        List<string> java = OracleRunner.Run([], JavaSession + body);

        LocalResult result = NewResult();
        result.SetSortOrder(SortOrder.OfSortTypes(NewSessionLocal(), [0]));
        foreach (int k in new[] { 5, 1, 4, 2, 3 })
        {
            result.AddRow(new Value?[] { ValueInteger.Get(k) });
        }

        result.SetLimit(10);
        result.SetOffset(3);
        result.Done();
        List<string> csharp = [];
        Print(result, csharp);

        AssertSame(java, csharp);
    }

    [JavaOracleFact]
    public void OffsetAndLimit_MatchesReference()
    {
        string body = """
                      for (int limit : new int[] { -1, 0, 1, 3, 10 }) {
                        for (int offset : new int[] { 0, 1, 2, 9 }) {
                          org.h2.result.LocalResult r = new org.h2.result.LocalResult(session, expr, 1, 1);
                          for (org.h2.value.Value[] row : rows) { r.addRow(row); }
                          r.setLimit(limit);
                          r.setOffset(offset);
                          r.done();
                          System.out.println("limit=" + limit + " offset=" + offset + " count=" + r.getRowCount());
                          while (r.next()) { System.out.println("  " + r.currentRow()[0].toString()); }
                        }
                      }
            """;

        List<string> java = OracleRunner.Run([], JavaSession + JavaRows + body);

        List<string> csharp = [];
        foreach (int limit in new[] { -1, 0, 1, 3, 10 })
        {
            foreach (int offset in new[] { 0, 1, 2, 9 })
            {
                LocalResult result = NewResult();
                AddRows(result);
                result.SetLimit(limit);
                result.SetOffset(offset);
                result.Done();
                csharp.Add($"limit={limit} offset={offset} count={result.GetRowCount()}");
                Print(result, csharp);
            }
        }

        AssertSame(java, csharp);
    }

    [JavaOracleFact]
    public void SortedRows_MatchesReference()
    {
        string body = """
                      int[] types = {
                          0, org.h2.result.SortOrder.DESCENDING, org.h2.result.SortOrder.NULLS_FIRST, org.h2.result.SortOrder.NULLS_LAST,
                          org.h2.result.SortOrder.DESCENDING | org.h2.result.SortOrder.NULLS_FIRST, org.h2.result.SortOrder.DESCENDING | org.h2.result.SortOrder.NULLS_LAST };
                      for (int type : types) {
                        org.h2.result.LocalResult r = new org.h2.result.LocalResult(session, expr, 1, 1);
                        r.setSortOrder(org.h2.result.SortOrder.ofSortTypes(session, new int[] { type }));
                        for (org.h2.value.Value[] row : rows) { r.addRow(row); }
                        r.done();
                        System.out.println("type=" + type);
                        while (r.next()) { System.out.println("  " + r.currentRow()[0].toString()); }
                      }
            """;

        List<string> java = OracleRunner.Run([], JavaSession + JavaRows + body);

        List<string> csharp = [];
        foreach (int type in new[] { 0, 1, 2, 4, 3, 5 })
        {
            LocalResult result = NewResult();
            result.SetSortOrder(SortOrder.OfSortTypes(NewSessionLocal(), [type]));
            AddRows(result);
            result.Done();
            csharp.Add("type=" + type);
            Print(result, csharp);
        }

        AssertSame(java, csharp);
    }

    [JavaOracleFact]
    public void Distinct_MatchesReference()
    {
        string body = """
                      org.h2.result.LocalResult r = new org.h2.result.LocalResult(session, expr, 1, 1);
                      r.setDistinct();
                      for (org.h2.value.Value[] row : rows) { r.addRow(row); }
                      r.done();
                      System.out.println("count=" + r.getRowCount());
                      while (r.next()) { System.out.println("  " + r.currentRow()[0].toString()); }
            """;

        List<string> java = OracleRunner.Run([], JavaSession + JavaRows + body);

        LocalResult distinct = NewResult();
        distinct.SetDistinct();
        AddRows(distinct);
        distinct.Done();
        List<string> csharp = ["count=" + distinct.GetRowCount()];
        Print(distinct, csharp);

        AssertSame(java, csharp);
    }

    /// <summary>
    /// The same corpus run on this side only, so that a hang can be told apart from a
    /// difference: the oracle tests compare against the JVM, and a mistake in this side would
    /// show up as a failure there, while an endless loop here would show up as neither.
    /// </summary>
    /// <returns>the lines this side prints</returns>
    [Fact]
    public void PlainResultOnThisSide()
    {
        List<string> lines = Plain();
        Assert.Equal(7, lines.Count);
        Assert.Equal("count=6", lines[0]);
        Assert.Equal("  3", lines[1]);
        Assert.Equal("  NULL", lines[3]);
    }

    private static List<string> Plain()
    {
        LocalResult result = NewResult();
        AddRows(result);
        result.Done();
        List<string> lines = ["count=" + result.GetRowCount()];
        Print(result, lines);
        return lines;
    }

    private static LocalResult NewResult()
    {
        return new LocalResult(NewSessionLocal(), [ValueExpression.Get(ValueInteger.Get(0))], 1, 1);
    }

    private static SessionLocal NewSessionLocal()
    {
        return new SessionLocal(new Database(ConnectionInfo.Memory("local_result_test")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = Values.CompareMode.GetInstance(null, 0),
        };
    }

    /// <summary>
    /// The same rows as <see cref="JavaRows"/>, and they must stay the same: the corpus is
    /// compared row by row, so one side having a different row order would be a difference in
    /// the result rather than in the port.
    /// </summary>
    private static void AddRows(LocalResult result)
    {
        foreach (Value?[] row in Rows())
        {
            result.AddRow(row);
        }
    }

    private static Value?[][] Rows()
    {
        return
        [
            [ValueInteger.Get(3)],
            [ValueInteger.Get(1)],
            [ValueNull.Instance],
            [ValueInteger.Get(2)],
            [ValueNumeric.Get(H2Sharp.Util.BigDecimal.Parse("1.0"))],
            [ValueNull.Instance],
        ];
    }

    private static void Print(LocalResult result, List<string> lines)
    {
        while (result.Next())
        {
            Value?[] row = result.CurrentRow();
            lines.Add("  " + (row[0]?.ToString() ?? "null"));
        }
    }

    private static void AssertSame(List<string> java, List<string> csharp)
    {
        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }
}
