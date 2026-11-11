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

    /// <summary>
    /// The clauses that choose rows are one mechanism in the reference: a TOP has already
    /// filled the fetch, so OFFSET/FETCH/LIMIT after it are not read at all, and the leftover
    /// keyword is the syntax error the reference reports (queries/select.sql). A comma with no
    /// select item after it is a keyword where a term does not belong, which the reference
    /// answers with the expected-token error (SYNTAX_ERROR_2), not with a missing function.
    /// </summary>
    [Fact]
    public void MixedRowLimitClausesAreRefused()
    {
        List<string> failures = ScriptRunner.RunLines(
        [
            "CREATE TABLE TEST (ID BIGINT);",
            "> ok",
            "SELECT TOP 1 ID FROM TEST OFFSET 1 ROW;",
            "> exception SYNTAX_ERROR_1",
            "SELECT TOP 1 ID FROM TEST FETCH NEXT ROW ONLY;",
            "> exception SYNTAX_ERROR_1",
            "SELECT TOP 1 ID FROM TEST LIMIT 1;",
            "> exception SYNTAX_ERROR_1",
            "SELECT ID FROM TEST OFFSET 1 ROW LIMIT 1;",
            "> exception SYNTAX_ERROR_1",
            "SELECT ID FROM TEST FETCH NEXT ROW ONLY LIMIT 1;",
            "> exception SYNTAX_ERROR_1",
            "SELECT X, FROM (VALUES 1) T(X);",
            "> exception SYNTAX_ERROR_2",
        ]);
        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }

    /// <summary>
    /// The EXCEPT list of a wildcard is resolved, not guessed: a name that is no column of the
    /// query's tables is COLUMN_NOT_FOUND_1, the same column named twice is
    /// DUPLICATE_COLUMN_NAME_1, a bare name shared by two tables is AMBIGUOUS_COLUMN_NAME_1 —
    /// and each star of the select list carries its own list (queries/select.sql).
    /// </summary>
    [Fact]
    public void WildcardExceptResolvesColumns()
    {
        List<string> failures = ScriptRunner.RunLines(
        [
            "CREATE TABLE TEST1(A INT, B INT, C INT);",
            "> ok",
            "CREATE TABLE TEST2(A INT, D INT);",
            "> ok",
            "INSERT INTO TEST1 VALUES (1, 2, 3);",
            "> update count: 1",
            "INSERT INTO TEST2 VALUES (4, 5);",
            "> update count: 1",
            "SELECT * EXCEPT (Z) FROM TEST1;",
            "> exception COLUMN_NOT_FOUND_1",
            "SELECT * EXCEPT (B, TEST1.B) FROM TEST1;",
            "> exception DUPLICATE_COLUMN_NAME_1",
            "SELECT * EXCEPT (A) FROM TEST1, TEST2;",
            "> exception AMBIGUOUS_COLUMN_NAME_1",
            "SELECT * EXCEPT (TEST1.A, B, TEST2.D) FROM TEST1, TEST2;",
            "> C A",
            "> - -",
            "> 3 4",
            "> rows: 1",
            "SELECT TEST1.* EXCEPT (A), TEST2.* EXCEPT (A) FROM TEST1, TEST2;",
            "> B C D",
            "> - - -",
            "> 2 3 5",
            "> rows: 1",
            "SELECT TEST1.* EXCEPT (A), TEST2.* EXCEPT (D) FROM TEST1, TEST2;",
            "> B C A",
            "> - - -",
            "> 2 3 4",
            "> rows: 1",
        ]);
        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }

    /// <summary>
    /// FOR UPDATE locks rows; a distinct or a grouped answer has no row to lock, and the
    /// reference refuses it with FOR_UPDATE_IS_NOT_ALLOWED_IN_DISTINCT_OR_GROUPED_SELECT at
    /// the moment the lock is set (queries/select.sql).
    /// </summary>
    [Fact]
    public void ForUpdateIsRefusedOnDistinctOrGroupedSelect()
    {
        List<string> failures = ScriptRunner.RunLines(
        [
            "CREATE TABLE TEST(ID INT PRIMARY KEY, \"VALUE\" INT);",
            "> ok",
            "INSERT INTO TEST VALUES (1, 10), (2, 20);",
            "> update count: 2",
            "SELECT DISTINCT \"VALUE\" FROM TEST FOR UPDATE;",
            "> exception FOR_UPDATE_IS_NOT_ALLOWED_IN_DISTINCT_OR_GROUPED_SELECT",
            "SELECT DISTINCT ON(\"VALUE\") ID, \"VALUE\" FROM TEST FOR UPDATE;",
            "> exception FOR_UPDATE_IS_NOT_ALLOWED_IN_DISTINCT_OR_GROUPED_SELECT",
            "SELECT SUM(\"VALUE\") FROM TEST FOR UPDATE;",
            "> exception FOR_UPDATE_IS_NOT_ALLOWED_IN_DISTINCT_OR_GROUPED_SELECT",
            "SELECT ID FROM TEST GROUP BY \"VALUE\" FOR UPDATE;",
            "> exception FOR_UPDATE_IS_NOT_ALLOWED_IN_DISTINCT_OR_GROUPED_SELECT",
            "SELECT 1 FROM TEST HAVING TRUE FOR UPDATE;",
            "> exception FOR_UPDATE_IS_NOT_ALLOWED_IN_DISTINCT_OR_GROUPED_SELECT",
            "SELECT ID FROM TEST ORDER BY ID FOR UPDATE;",
            "> ID",
            "> --",
            "> 1",
            "> 2",
            "> rows (ordered): 2",
        ]);
        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }

    /// <summary>
    /// A nested join is a FROM clause: the reference asks the filter whether it has one, and
    /// the port asked whether its table was DUAL — a join wrapper's table is exactly that, so
    /// the plan lost the FROM and its index comments (queries/joins.sql).
    /// </summary>
    [Fact]
    public void NestedJoinPlanKeepsTheFromClause()
    {
        List<string> failures = ScriptRunner.RunLines(
        [
            "CREATE TABLE T1(C1 INT);",
            "> ok",
            "CREATE TABLE T2(C2 INT);",
            "> ok",
            "CREATE TABLE T3(C3 INT);",
            "> ok",
            "EXPLAIN SELECT * FROM T1 JOIN T2 LEFT JOIN T3 ON T2.C2 = T3.C3 ON T1.C1 = T2.C2;",
            ">> SELECT \"PUBLIC\".\"T1\".\"C1\", \"PUBLIC\".\"T2\".\"C2\", \"PUBLIC\".\"T3\".\"C3\" "
                + "FROM ( \"PUBLIC\".\"T2\" /* PUBLIC.T2.tableScan */ LEFT OUTER JOIN \"PUBLIC\".\"T3\" "
                + "/* PUBLIC.T3.tableScan */ ON \"T2\".\"C2\" = \"T3\".\"C3\" ) INNER JOIN \"PUBLIC\".\"T1\" "
                + "/* PUBLIC.T1.tableScan */ ON 1=1 WHERE \"T1\".\"C1\" = \"T2\".\"C2\"",
        ]);
        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }

    /// <summary>
    /// A parenthesized GROUP BY element is a grouping set, not a row: the reference flattens
    /// <c>GROUP BY (B, C)</c> into the two expressions, reads <c>()</c> as nothing, and keeps
    /// the query grouped so the aggregate answers the single group the whole table makes —
    /// one row even when the condition admits none (queries/select.sql).
    /// </summary>
    [Fact]
    public void GroupingSetsAreFlattenedAndEmptyGroupIsOneGroup()
    {
        List<string> failures = ScriptRunner.RunLines(
        [
            "CREATE TABLE TEST(A INT, B INT, C INT);",
            "> ok",
            "INSERT INTO TEST VALUES (1, 1, 1), (1, 2, 1), (1, NULL, 1);",
            "> update count: 3",
            "SELECT SUM(A), B, C FROM TEST GROUP BY (B), C, ();",
            "> SUM(A) B    C",
            "> ------ ---- -",
            "> 1      1    1",
            "> 1      2    1",
            "> 1      null 1",
            "> rows: 3",
            "EXPLAIN SELECT SUM(A), B, C FROM TEST GROUP BY (B), C, ();",
            ">> SELECT SUM(\"A\"), \"B\", \"C\" FROM \"PUBLIC\".\"TEST\" /* PUBLIC.TEST.tableScan */ GROUP BY \"B\", \"C\"",
            "SELECT COUNT(*) FROM TEST GROUP BY ();",
            ">> 3",
            "EXPLAIN SELECT COUNT(*) FROM TEST GROUP BY ();",
            ">> SELECT COUNT(*) FROM \"PUBLIC\".\"TEST\" /* PUBLIC.TEST.tableScan */ /* direct lookup */",
            "SELECT COUNT(*) FROM TEST WHERE FALSE GROUP BY ();",
            ">> 0",
            "SELECT COUNT(*) FROM TEST WHERE FALSE GROUP BY (), ();",
            ">> 0",
            "SELECT 1 FROM TEST GROUP BY ();",
            ">> 1",
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
