/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 127: the defects the reference-script runner found. Each test is the smallest
 * statement that reproduces one of them; the reference script that covers it end to end is
 * named in the comment. The expected answers are the reference's own.
 */
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ScriptConformanceRegressionTests
{
    private static (Database Database, SessionLocal Session) Open()
    {
        Database database = new(ConnectionInfo.Memory("script_regression_" + Guid.NewGuid().ToString("N")));
        SessionLocal session = new(database, "sa");
        return (database, session);
    }

    private static Value? Single(SessionLocal session, string sql)
    {
        using ResultInterface result = session.PrepareCommand(sql).ExecuteQuery(-1, 0, false);
        Assert.True(result.Next());
        return result.CurrentRow()[0];
    }

    /// <summary>
    /// <c>U&amp;'\0'</c> has fewer digits than an escape needs. The reference refuses it with
    /// STRING_FORMAT_ERROR_1 (datatypes/varchar.sql); the port indexed past the end instead.
    /// </summary>
    [Theory]
    [InlineData("\\")]
    [InlineData("\\0")]
    [InlineData("\\00")]
    [InlineData("\\003")]
    [InlineData("\\zzzz")]
    [InlineData("\\+0")]
    [InlineData("\\+00003")]
    [InlineData("\\+zzzzzz")]
    public void ShortUnicodeEscapeIsAFormatError(string text)
    {
        H2SharpException e = Assert.Throws<H2SharpException>(
            () => StringUtils.DecodeUnicodeStringSql(text, '\\'));
        Assert.Equal(Api.ErrorCode.StringFormatError1, e.H2ErrorCode);
    }

    /// <summary>
    /// A bracketed comment that starts at the end of the statement has no room for its two
    /// characters. The reference reports a syntax error (parser/comments.sql); the port threw
    /// an index error out of the span.
    /// </summary>
    [Fact]
    public void UnterminatedBracketCommentIsASyntaxError()
    {
        (Database database, SessionLocal session) = Open();
        try
        {
            H2SharpException e = Assert.Throws<H2SharpException>(
                () => session.PrepareCommand("DROP TABLE TEST /*"));
            Assert.Equal(Api.ErrorCode.SyntaxError1, e.H2ErrorCode);
        }
        finally
        {
            database.Close();
        }
    }

    /// <summary>
    /// <c>VALUES ROW (1, 2)</c> is the row form of a table value constructor, and the
    /// reference reads it exactly like <c>VALUES (1, 2)</c> (queries/values.sql).
    /// </summary>
    [Fact]
    public void ValuesRowKeywordReadsAMultiColumnRow()
    {
        (Database database, SessionLocal session) = Open();
        try
        {
            using (ResultInterface result = session.PrepareCommand("VALUES ROW (1, 2)").ExecuteQuery(-1, 0, false))
            {
                Assert.Equal(2, result.GetVisibleColumnCount());
                Assert.True(result.Next());
                Assert.Equal("1", result.CurrentRow()[0]!.GetString());
                Assert.Equal("2", result.CurrentRow()[1]!.GetString());
                Assert.False(result.Next());
            }

            using (ResultInterface result = session
                       .PrepareCommand("VALUES ROW (1), ROW (2)").ExecuteQuery(-1, 0, false))
            {
                Assert.Equal(1, result.GetVisibleColumnCount());
                Assert.True(result.Next());
                Assert.Equal("1", result.CurrentRow()[0]!.GetString());
                Assert.True(result.Next());
                Assert.Equal("2", result.CurrentRow()[0]!.GetString());
                Assert.False(result.Next());
            }
        }
        finally
        {
            database.Close();
        }
    }

    /// <summary>
    /// A timestamp a billion years before the epoch has no <see cref="DateTimeOffset"/>, but
    /// the reference names it and converts it. The fold that keeps the zone lookup inside the
    /// calendar used a minimum a year too low, and the conversion of the last year the
    /// reference allows threw (datatypes/timestamp-with-time-zone.sql).
    /// </summary>
    [Fact]
    public void ExtremeYearsSurviveTheZoneConversion()
    {
        (Database database, SessionLocal session) = Open();
        try
        {
            Value? negative = Single(session,
                "SELECT CAST (CAST (TIMESTAMP '-1000000000-12-31 00:00:00' AS TIMESTAMP(9) WITH TIME ZONE) AS TIMESTAMP(9))");
            Assert.Equal("-1000000000-12-31 00:00:00", negative!.GetString());

            Value? positive = Single(session,
                "SELECT CAST (CAST (TIMESTAMP '1000000000-12-31 23:59:59.999999999' AS TIMESTAMP(9) WITH TIME ZONE) AS TIMESTAMP(9))");
            Assert.Equal("1000000000-12-31 23:59:59.999999999", positive!.GetString());
        }
        finally
        {
            database.Close();
        }
    }

    /// <summary>
    /// Within one statement <c>CURRENT_TIMESTAMP</c> and <c>LOCALTIMESTAMP</c> are the same
    /// instant (datatypes/timestamp-with-time-zone.sql); before the session cached it, the two
    /// functions were called at different moments and the equality was FALSE.
    /// </summary>
    [Fact]
    public void CurrentTimestampIsFixedWithinAStatement()
    {
        (Database database, SessionLocal session) = Open();
        try
        {
            Assert.Equal("TRUE", Single(session, "SELECT LOCALTIMESTAMP = CAST(CURRENT_TIMESTAMP AS TIMESTAMP)")!.GetString());
            Assert.Equal("TRUE", Single(session, "SELECT (LOCALTIMESTAMP + 1) = (CURRENT_TIMESTAMP + 1)")!.GetString());
            Assert.Equal("TRUE", Single(session, "SELECT (LOCALTIMESTAMP - 1) = (CURRENT_TIMESTAMP - 1)")!.GetString());
        }
        finally
        {
            database.Close();
        }
    }

    /// <summary>
    /// A percentage fetch is a share of the rows the query produces, rounded up. Reading it as
    /// a row count stopped the walk at ten rows instead of all twelve, so ten percent answered
    /// one row where the reference answers two (queries/select.sql).
    /// </summary>
    [Fact]
    public void FetchPercentIsAShareOfAllRows()
    {
        (Database database, SessionLocal session) = Open();
        try
        {
            session.PrepareCommand("CREATE TABLE TEST(A INT, B INT, C INT)").ExecuteUpdate(false);
            session.PrepareCommand(
                "INSERT INTO TEST VALUES (1,1,1),(1,1,2),(1,1,3),(1,2,1),(1,2,2),(1,2,3),"
                + "(2,1,1),(2,1,2),(2,1,3),(2,2,1),(2,2,2),(2,2,3)").ExecuteUpdate(false);

            Assert.Equal(2, CountRows(session, "SELECT * FROM TEST FETCH FIRST 10 PERCENT ROWS ONLY"));
            Assert.Equal(1, CountRows(session, "SELECT * FROM TEST FETCH FIRST 1 PERCENT ROWS ONLY"));
            Assert.Equal(2, CountRows(session,
                "SELECT * FROM TEST OFFSET 2 ROWS FETCH NEXT 10 PERCENT ROWS ONLY"));
            Assert.Equal(2, CountRows(session,
                "SELECT * FROM TEST ORDER BY A, B, C FETCH FIRST 10 PERCENT ROWS ONLY"));
        }
        finally
        {
            database.Close();
        }
    }

    /// <summary>
    /// WITH TIES needs an order to know which rows are tied. A union has none of its own, so
    /// the reference refuses it with WITH_TIES_WITHOUT_ORDER_BY (queries/select.sql); the port
    /// returned rows instead of an error.
    /// </summary>
    [Fact]
    public void UnionWithTiesWithoutOrderIsRefused()
    {
        (Database database, SessionLocal session) = Open();
        try
        {
            H2SharpException e = Assert.Throws<H2SharpException>(() => Single(session,
                "(SELECT 1) UNION (SELECT 2) FETCH NEXT 1 ROW WITH TIES"));
            Assert.Equal(Api.ErrorCode.WithTiesWithoutOrderBy, e.H2ErrorCode);
        }
        finally
        {
            database.Close();
        }
    }

    /// <summary>
    /// A grouped query with HAVING used to crash the aggregate walk: the copy map was sized
    /// over the visible columns while the walk indexes every expression, and HAVING had been
    /// appended to that list (queries/select.sql).
    /// </summary>
    [Fact]
    public void GroupedHavingWithDuplicateColumnsAnswers()
    {
        (Database database, SessionLocal session) = Open();
        try
        {
            session.PrepareCommand("CREATE TABLE TEST(A INT)").ExecuteUpdate(false);
            session.PrepareCommand("INSERT INTO TEST VALUES (1), (1), (2)").ExecuteUpdate(false);
            using ResultInterface result = session
                .PrepareCommand("SELECT A, A FROM TEST GROUP BY A HAVING SUM(A) > 0 ORDER BY A")
                .ExecuteQuery(-1, 0, false);
            Assert.True(result.Next());
            Assert.Equal("1", result.CurrentRow()[0]!.GetString());
            Assert.True(result.Next());
            Assert.Equal("2", result.CurrentRow()[1]!.GetString());
            Assert.False(result.Next());
        }
        finally
        {
            database.Close();
        }
    }

    /// <summary>
    /// The runner's parameter block: one execution per line, values bound as strings, a
    /// query's own answer block, and the summed update count compared with the line after `}`.
    /// </summary>
    [Fact]
    public void ParameterBlocksExecuteAndCompare()
    {
        List<string> failures = ScriptRunner.RunLines(
        [
            "CREATE TABLE T(A INT);",
            "> ok",
            "INSERT INTO T VALUES (?), (?);",
            "{",
            "1, 2",
            "};",
            "> update count: 2",
            "SELECT A FROM T WHERE A > ?;",
            "{",
            "1",
            ">> 2",
            "};",
            "> update count: 0",
        ]);
        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }

    private static int CountRows(SessionLocal session, string sql)
    {
        using ResultInterface result = session.PrepareCommand(sql).ExecuteQuery(-1, 0, false);
        int count = 0;
        while (result.Next())
        {
            count++;
        }

        return count;
    }
}
