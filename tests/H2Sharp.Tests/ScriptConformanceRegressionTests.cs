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

    /// <summary>
    /// A view stores its query text, not the values that text produced: a function that reads
    /// the session's time zone must answer with the reader's zone, and the row-choosing
    /// clauses of a view may not depend on a session variable at all — otherwise the number
    /// of rows a view answers would change with the reader (issues #4406/#4409,
    /// ddl/createView.sql). A view may also have no columns at all.
    /// </summary>
    [Fact]
    public void ViewsFollowTheReaderAndRefuseNonConstantRows()
    {
        List<string> failures = ScriptRunner.RunLines(
        [
            "SET TIME ZONE '+05:00';",
            "> ok",
            "CREATE VIEW V_EXTRACT_TZ AS SELECT EXTRACT(TIMEZONE_HOUR FROM TIMESTAMP '2010-01-01 10:00:00') AS TZ_HOUR;",
            "> ok",
            "CREATE VIEW V() AS SELECT;",
            "> ok",
            "TABLE V;",
            ">",
            ">",
            ">",
            "> rows: 1",
            "SET @VN = 1;",
            "> ok",
            "CREATE VIEW VF AS SELECT X FROM SYSTEM_RANGE(1, 3) ORDER BY X OFFSET @VN;",
            "> exception FEATURE_NOT_SUPPORTED_1",
            "CREATE VIEW VF AS SELECT X FROM SYSTEM_RANGE(1, 3) ORDER BY X FETCH FIRST @VN ROWS ONLY;",
            "> exception FEATURE_NOT_SUPPORTED_1",
            "SET TIME ZONE '+08:00';",
            "> ok",
            "SELECT * FROM V_EXTRACT_TZ;",
            ">> 8",
            "DROP VIEW V;",
            "> ok",
            "DROP VIEW V_EXTRACT_TZ;",
            "> ok",
        ]);
        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }

    /// <summary>
    /// INFORMATION_SCHEMA.VIEWS answers the view's own query text and its status, as the
    /// reference does; the instead-of trigger columns stay "NO" while triggers are unported
    /// (ddl/createView.sql).
    /// </summary>
    [Fact]
    public void ViewsExposeTheirDefinition()
    {
        (Database database, SessionLocal session) = Open();
        try
        {
            session.PrepareCommand("CREATE VIEW V_TEST AS SELECT 1 AS A").ExecuteUpdate(false);
            using ResultInterface result = session.PrepareCommand(
                    "SELECT VIEW_DEFINITION, STATUS, IS_TRIGGER_UPDATABLE FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_NAME = 'V_TEST'")
                .ExecuteQuery(-1, 0, false);
            Assert.True(result.Next());
            Assert.Contains("SELECT", result.CurrentRow()[0]!.GetString(), StringComparison.Ordinal);
            Assert.Equal("VALID", result.CurrentRow()[1]!.GetString());
            Assert.Equal("NO", result.CurrentRow()[2]!.GetString());
        }
        finally
        {
            database.Close();
        }
    }

    /// <summary>
    /// Constants and domains take comments and show them in their system views, and DROP ALL
    /// OBJECTS drops them with the rest — otherwise `CREATE CONSTANT ONE` after the drop would
    /// find the old one and refuse (ddl/createConstant.sql).
    /// </summary>
    [Fact]
    public void ConstantsAndDomainsTakeCommentsAndDrop()
    {
        List<string> failures = ScriptRunner.RunLines(
        [
            "CREATE CONSTANT ONE VALUE 1;",
            "> ok",
            "COMMENT ON CONSTANT ONE IS 'Eins';",
            "> ok",
            "SELECT REMARKS FROM INFORMATION_SCHEMA.CONSTANTS WHERE CONSTANT_NAME = 'ONE';",
            ">> Eins",
            "CREATE DOMAIN D1 AS INT;",
            "> ok",
            "COMMENT ON DOMAIN D1 IS 'dom';",
            "> ok",
            "SELECT REMARKS FROM INFORMATION_SCHEMA.DOMAINS WHERE DOMAIN_NAME = 'D1';",
            ">> dom",
            "DROP ALL OBJECTS;",
            "> ok",
            "CREATE CONSTANT ONE VALUE 1;",
            "> ok",
            "CREATE DOMAIN D1 AS INT;",
            "> ok",
        ]);
        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }

    /// <summary>
    /// A VALUES constructor folds into a scalar subquery only as a whole when every row is
    /// constant — the base check alone would evaluate RAND() and put its value into the plan
    /// text — and a single non-constant row is replaced by its expressions, a row of several
    /// columns rendered as ROW (...); a multi-row VALUES is refused only when its value is
    /// read, not when its plan is printed (queries/select.sql, lines 1082-1107).
    /// </summary>
    [Fact]
    public void ScalarValueSubqueriesUseTheReferencePlans()
    {
        List<string> failures = ScriptRunner.RunLines(
        [
            "EXPLAIN SELECT (VALUES 1, 2 OFFSET 1 ROW);",
            ">> SELECT 2",
            "EXPLAIN SELECT (VALUES 1, 2 OFFSET RAND() ROWS);",
            ">> SELECT (VALUES (1), (2) OFFSET RAND() ROWS)",
            "EXPLAIN SELECT (VALUES 1 FETCH FIRST 2 ROWS ONLY);",
            ">> SELECT 1",
            "EXPLAIN SELECT (VALUES 1, 2 FETCH FIRST RAND() ROWS ONLY);",
            ">> SELECT (VALUES (1), (2) FETCH FIRST RAND() ROWS ONLY)",
            "EXPLAIN SELECT (SELECT 1, RAND());",
            ">> SELECT ROW (1, RAND())",
            "EXPLAIN SELECT (VALUES (1, RAND()));",
            ">> SELECT ROW (1, RAND())",
            "EXPLAIN SELECT (VALUES 1, RAND());",
            ">> SELECT (VALUES (1), (RAND()))",
            "SELECT (VALUES 1, 2);",
            "> exception SCALAR_SUBQUERY_CONTAINS_MORE_THAN_ONE_ROW",
        ]);
        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }

    /// <summary>
    /// The reference measures the select list against MAX_COLUMNS in init: 16384 columns
    /// still pass, 16385 are refused — and while the refusal was missing, the wrongly built
    /// table hid behind its name and broke every following statement
    /// (queries/select.sql, lines 1141-1169).
    /// </summary>
    [Theory]
    [InlineData(16384, true)]
    [InlineData(16385, false)]
    public void SelectListWidthEnforcesMaxColumns(int columns, bool allowed)
    {
        (Database database, SessionLocal session) = Open();
        try
        {
            string sql = "SELECT " + string.Join(", ",
                Enumerable.Range(1, columns).Select(static i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            if (allowed)
            {
                using ResultInterface result = session.PrepareCommand(sql).ExecuteQuery(-1, 0, false);
                Assert.True(result.Next());
            }
            else
            {
                H2SharpException e = Assert.Throws<H2SharpException>(
                    () => session.PrepareCommand(sql).ExecuteQuery(-1, 0, false));
                Assert.Equal(Api.ErrorCode.TooManyColumns1, e.H2ErrorCode);
            }
        }
        finally
        {
            database.Close();
        }
    }

    /// <summary>
    /// The <c>_ROWID_</c> pseudo-column reads through every qualifier — the table, the schema,
    /// and the database name the reference checks and drops — while the quoted spelling of the
    /// same name is a real column of the table. The expectation names the catalog SCRIPT, so
    /// the database carries the harness name (queries/select.sql, lines 678-690).
    /// </summary>
    [Fact]
    public void RowidPseudoColumnResolvesThroughEveryQualifier()
    {
        List<string> failures = ScriptRunner.RunLines(
        [
            "CREATE TABLE TEST(\"_ROWID_\" INT) AS VALUES 2;",
            "> ok",
            "SELECT _ROWID_ S1, TEST._ROWID_ S2, PUBLIC.TEST._ROWID_ S3, SCRIPT.PUBLIC.TEST._ROWID_ S4,",
            "    \"_ROWID_\" U1, TEST.\"_ROWID_\" U2, PUBLIC.TEST.\"_ROWID_\" U3, SCRIPT.PUBLIC.TEST.\"_ROWID_\" U4",
            "    FROM TEST;",
            "> S1 S2 S3 S4 U1 U2 U3 U4",
            "> -- -- -- -- -- -- -- --",
            "> 1  1  1  1  2  2  2  2",
            "> rows: 1",
            "DROP TABLE TEST;",
            "> ok",
        ], "script");
        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }

    /// <summary>
    /// A hex digit beyond the decode table is a wrong hex string wherever it stands —
    /// the reference guards its whole spaced loop with one catch, while the port guarded only
    /// the digit pairs and a character like '~' fell out of the odd-last-digit branch
    /// uncaught (datatypes/varbinary.sql).
    /// </summary>
    [Fact]
    public void HexLiteralsRefuseDigitsPastTheDecodeTable()
    {
        List<string> failures = ScriptRunner.RunLines(
        [
            "SELECT X'1';",
            "> exception HEX_STRING_ODD_1",
            "SELECT X'~';",
            "> exception HEX_STRING_WRONG_1",
            "SELECT X'G';",
            "> exception HEX_STRING_WRONG_1",
            "SELECT X'TT';",
            "> exception HEX_STRING_WRONG_1",
            "SELECT X' TT';",
            "> exception HEX_STRING_WRONG_1",
            "SELECT X' 1 2  3 4 ';",
            ">> X'1234'",
            "SELECT X'1 2 3';",
            "> exception HEX_STRING_ODD_1",
            "SELECT X'AB' 'CD';",
            ">> X'abcd'",
            "SELECT X'AB' 'CX';",
            "> exception HEX_STRING_WRONG_1",
        ]);
        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }

    /// <summary>
    /// LISTAGG names its ordering even when the caller wrote none, and the separator may be a
    /// parameter: both defects kept functions/aggregate/listagg.sql from matching the
    /// reference (its own remaining difference there is SET MODE Oracle, D-029).
    /// </summary>
    [Fact]
    public void ListaggNamesItsOrderingAndAcceptsAParameterSeparator()
    {
        List<string> failures = ScriptRunner.RunLines(
        [
            "CREATE TABLE T(ID INT, V INT);",
            "> ok",
            "INSERT INTO T VALUES (1, 7), (2, 2), (3, 7);",
            "> update count: 3",
            "SELECT LISTAGG(V) FROM T;",
            "> LISTAGG(V) WITHIN GROUP (ORDER BY NULL)",
            "> ---------------------------------------",
            "> 7,2,7",
            "> rows: 1",
            "SELECT LISTAGG(DISTINCT V) FROM T;",
            "> LISTAGG(DISTINCT V) WITHIN GROUP (ORDER BY NULL)",
            "> ------------------------------------------------",
            "> 2,7",
            "> rows: 1",
            "SELECT LISTAGG(DISTINCT V, '-') FROM T;",
            "> LISTAGG(DISTINCT V, '-') WITHIN GROUP (ORDER BY NULL)",
            "> -----------------------------------------------------",
            "> 2-7",
            "> rows: 1",
            "SELECT LISTAGG(V, ?) L FROM (VALUES 'a', 'b', 'c') T2(V);",
            "{",
            ":",
            "> L",
            "> -----",
            "> a:b:c",
            "> rows: 1",
            "};",
            "> update count: 0",
            "SELECT LISTAGG(V, V) FROM (VALUES 'a') T2(V);",
            "> exception SYNTAX_ERROR_2",
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
