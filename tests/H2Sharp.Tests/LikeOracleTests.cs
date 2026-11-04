/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * LIKE in every shape the matcher knows: the exact pattern, the prefix and suffix and
 * contains shortcuts, the single-character wildcard, several wildcards that force the
 * backtracking path, escapes and invalid escapes, ILIKE and VARCHAR_IGNORECASE, REGEXP and
 * its invalid pattern, and the index conditions the planner derives from a fixed prefix.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Data;
using H2Sharp.Expressions.Conditions;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class LikeOracleTests
{
    [JavaOracleFact]
    public void PatternShapesMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "SELECT '' LIKE ''",
            "SELECT 'abc' LIKE 'abc'",
            "SELECT 'abc' LIKE 'abd'",
            "SELECT 'abc' LIKE 'ab'",
            "SELECT 'abc' LIKE 'abcd'",
            "SELECT 'abc' LIKE 'a_c'",
            "SELECT 'ab' LIKE 'a_c'",
            "SELECT 'abc' LIKE 'a__'",
            "SELECT 'abc' LIKE '_b_'",
            "SELECT 'abc' LIKE 'ab%'",
            "SELECT 'abc' LIKE 'a%'",
            "SELECT 'abc' LIKE '%bc'",
            "SELECT 'abc' LIKE '%c'",
            "SELECT 'abc' LIKE '%b%'",
            "SELECT 'abc' LIKE '%bc%'",
            "SELECT 'abc' LIKE '%'",
            "SELECT 'abc' LIKE '%%'",
            "SELECT 'abc' LIKE '%%%'",
            "SELECT 'abc' LIKE 'a%c'",
            "SELECT 'abbc' LIKE 'a%bc'",
            "SELECT 'abab' LIKE '%ab%ab%'",
            "SELECT 'a%b%c' LIKE 'a%c'",
            "SELECT 'abc' LIKE '_b%'",
            "SELECT 'abc' LIKE '%b_'",
            "SELECT 'a_c' LIKE 'a_c'",
            "SELECT 'x' LIKE 'x'",
            "SELECT 'x' LIKE 'X'",
            "SELECT 'X' LIKE 'x'",
            // NULL on either side.
            "SELECT NULL LIKE 'a'",
            "SELECT 'a' LIKE NULL",
            "SELECT NULL LIKE NULL",
            "SELECT NULL NOT LIKE 'a'",
            // NOT LIKE follows the same matcher.
            "SELECT 'abc' NOT LIKE 'a%'",
            "SELECT 'abc' NOT LIKE 'x%'",
            "SELECT 'abc' NOT LIKE 'a_c'",
        ]);
    }

    [JavaOracleFact]
    public void EscapesMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "SELECT 'a%c' LIKE 'a\\%c' ESCAPE '\\'",
            "SELECT 'abc' LIKE 'a\\%c' ESCAPE '\\'",
            "SELECT 'a_c' LIKE 'a\\_c' ESCAPE '\\'",
            "SELECT 'abc' LIKE 'a\\_c' ESCAPE '\\'",
            "SELECT 'a\\c' LIKE 'a\\\\c' ESCAPE '\\'",
            "SELECT 'a%c' LIKE 'a\\%c' ESCAPE ''",
            "SELECT 'a%c' LIKE 'a\\%c'",
            // An escape string longer than one character is refused.
            "SELECT 'abc' LIKE 'abc' ESCAPE 'xx'",
            // A pattern that ends in the escape character cannot be matched.
            "SELECT 'abc' LIKE 'ab\\' ESCAPE '\\'",
            "SELECT 'ab\\' LIKE 'ab\\\\' ESCAPE '\\'",
            "SELECT 'abc' LIKE 'ab' ESCAPE '\\'",
        ]);
    }

    [JavaOracleFact]
    public void RegexpMatchesJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "SELECT 'abc' REGEXP 'a.c'",
            "SELECT 'abc' REGEXP 'a.*c'",
            "SELECT 'abc' REGEXP '^b'",
            "SELECT 'abc' NOT REGEXP '^b'",
            "SELECT 'abc' RLIKE '^a'",
            "SELECT 'ABC' REGEXP 'abc'",
            "SELECT NULL REGEXP 'a'",
            "SELECT 'abc' REGEXP NULL",
            "SELECT 'abc' REGEXP '['",
            "SELECT 'abc' LIKE 'a.c'",
        ]);
    }

    /// <summary>
    /// The matcher itself over every pattern and value built from the reference unit test's
    /// alphabet: the patterns exercise the exact, prefix, suffix, contains and backtracking
    /// paths as one differential against <c>CompareLike.test(pattern, value, escape)</c>.
    /// </summary>
    [JavaOracleFact]
    public void TheMatcherMatchesTheReference()
    {
        List<string> inputs = [];
        foreach (string value in Generated("AB_%", 3))
        {
            foreach (string pattern in Generated("AB_%\\", 3))
            {
                inputs.Add(pattern + "\t" + value);
            }
        }

        List<string> java = OracleRunner.Run(inputs, """
            org.h2.value.CompareMode mode = org.h2.value.CompareMode.getInstance(null, 0);
            org.h2.expression.condition.CompareLike comp = new org.h2.expression.condition.CompareLike(
                mode, "\\", null, false, false, null, null,
                org.h2.expression.condition.CompareLike.LikeType.LIKE);
            for (String input : inputs) {
                int tab = input.indexOf('\t');
                String pattern = input.substring(0, tab);
                String value = input.substring(tab + 1);
                System.out.println(comp.test(pattern, value, '\\') ? "1" : "0");
            }
            """);

        CompareLike comp = new(CompareMode.GetInstance(null, 0), "\\", null, false, false, null, null,
            CompareLike.LikeType.Like);
        List<string> port = [];
        foreach (string input in inputs)
        {
            int tab = input.IndexOf('\t');
            port.Add(comp.Test(input[..tab], input[(tab + 1)..], '\\') ? "1" : "0");
        }

        Assert.Equal(java, port);
    }

    private static IEnumerable<string> Generated(string alphabet, int maxLength)
    {
        List<string> all = [string.Empty];
        List<string> frontier = [string.Empty];
        for (int length = 0; length < maxLength; length++)
        {
            List<string> next = [];
            foreach (string prefix in frontier)
            {
                foreach (char c in alphabet)
                {
                    next.Add(prefix + c);
                }
            }

            all.AddRange(next);
            frontier = next;
        }

        return all;
    }

    /// <summary>
    /// The reference's own CompareLike unit-test cases, ported as fixed expectations.
    /// </summary>
    [Fact]
    public void TheReferencePatternCasesAnswer()
    {
        CompareLike comp = new(CompareMode.GetInstance(null, 0), "\\", null, false, false, null, null,
            CompareLike.LikeType.Like);
        Assert.True(comp.Test("%_", "B", '\\'));
        Assert.True(comp.Test("A%", "A", '\\'));
        Assert.True(comp.Test("A%%", "A", '\\'));
        Assert.True(comp.Test("%\\_%", "A_A", '\\'));
        Assert.False(comp.Test("A_", "A", '\\'));
        Assert.False(comp.Test("A%", "B", '\\'));
        Assert.False(comp.Test("ab\\", "ab", '\\'));
    }

    [JavaOracleFact]
    public void CaseJoinAndParametersMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE T(ID INT, V VARCHAR, I VARCHAR_IGNORECASE(10), N INT)",
            "INSERT INTO T VALUES (1, 'abc', 'Abc', 123), (2, 'abcd', 'abc', 12), (3, NULL, NULL, NULL)",
            "CREATE INDEX T_V ON T(V)",
            // A CASE WHEN keeps the when-operand form of the comparison.
            "SELECT CASE WHEN 'a' LIKE 'a' THEN 1 ELSE 0 END",
            "SELECT CASE WHEN 'abc' LIKE 'a%' THEN 1 ELSE 0 END",
            "SELECT CASE WHEN 'abc' LIKE 'b%' THEN 1 ELSE 0 END",
            "SELECT CASE WHEN 'ABC' ILIKE 'a%' THEN 1 ELSE 0 END",
            "SELECT CASE WHEN V LIKE 'a%' THEN ID ELSE -ID END FROM T ORDER BY ID",
            // A pattern or an escape coming from another table cannot serve as a constant.
            "CREATE TABLE P(ID INT, PAT VARCHAR, ESC VARCHAR)",
            "INSERT INTO P VALUES (1, 'ab%', '\\'), (2, '%bc', '\\'), (3, 'a_c', NULL)",
            "SELECT T.ID, P.ID FROM T JOIN P ON T.V LIKE P.PAT ORDER BY T.ID, P.ID",
            "SELECT T.ID, P.ID FROM T JOIN P ON T.V LIKE P.PAT ESCAPE P.ESC ORDER BY T.ID, P.ID",
            "SELECT T.ID FROM T JOIN P ON T.V LIKE 'a%' ESCAPE P.ESC ORDER BY T.ID, P.ID",
            // ILIKE and a VARCHAR_IGNORECASE column take the case-insensitive matcher.
            "SELECT ID FROM T WHERE I ILIKE '%BC%' ORDER BY ID",
            "SELECT ID FROM T WHERE I LIKE '%BC%' ORDER BY ID",
            "SELECT 'xabcx' ILIKE '%ABC%'",
            "SELECT 'xx' ILIKE '%'",
            "SELECT 'xx' ILIKE '%%'",
            // A non-string column cannot use the string index; the answer is the conversion.
            "SELECT ID FROM T WHERE N LIKE '12%' ORDER BY ID",
            // A NULL pattern, an invalid pattern and the bare percent optimize to NULL or
            // to a null test; the columns show the same answers as literals.
            "SELECT ID FROM T WHERE V LIKE NULL ORDER BY ID",
            "SELECT ID FROM T WHERE V LIKE 'ab\' ORDER BY ID",
            "SELECT ID FROM T WHERE V LIKE '%' ORDER BY ID",
            "SELECT ID FROM T WHERE V NOT LIKE '%' ORDER BY ID",
            "SELECT ID FROM T WHERE V LIKE '%%' ORDER BY ID",
            // A NULL escape makes the whole comparison NULL.
            "SELECT 'abc' LIKE 'abc' ESCAPE NULL",
            "SELECT ID FROM T WHERE V LIKE 'a%' ESCAPE NULL ORDER BY ID",
        ]);
    }

    [Fact]
    public void ABoundPatternIsInitializedAtExecution()
    {
        using System.Data.Common.DbCommand command = PrepareLike(out H2Connection connection);
        using (connection)
        {
            System.Data.Common.DbParameter pattern = command.Parameters[0];
            foreach ((string value, string expected) in
                new (string Value, string Expected)[]
                {
                    ("ab%", "1"), ("%bc", "1,2"), ("a_c", "1"), ("x%", "2"),
                })
            {
                pattern.Value = value;
                using System.Data.Common.DbDataReader reader = command.ExecuteReader();
                List<string> ids = [];
                while (reader.Read())
                {
                    ids.Add(Convert.ToString(reader.GetValue(0),
                        System.Globalization.CultureInfo.InvariantCulture)!);
                }

                Assert.Equal(expected, string.Join(',', ids));
            }
        }
    }

    private static System.Data.Common.DbCommand PrepareLike(out H2Connection connection)
    {
        connection = new("Data Source=like_params;Storage=Memory");
        connection.Open();
        Execute(connection, "CREATE TABLE T(ID INT, V VARCHAR)");
        Execute(connection, "CREATE INDEX T_V ON T(V)");
        Execute(connection, "INSERT INTO T VALUES (1, 'abc'), (2, 'xbc'), (3, NULL)");
        System.Data.Common.DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ID FROM T WHERE V LIKE ? ORDER BY ID";
        System.Data.Common.DbParameter parameter = command.CreateParameter();
        command.Parameters.Add(parameter);
        // Prepared with the parameter unset: the planner sees no pattern and the pattern is
        // initialized when a value is bound, which is the reference's deferred road.
        command.Prepare();
        parameter.Value = "a%";
        return command;
    }

    private static void Execute(H2Connection connection, string sql)
    {
        using System.Data.Common.DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [JavaOracleFact]
    public void TheJoinPlansForLikeMatchJdbc()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE T(ID INT, V VARCHAR, N INT)",
            "INSERT INTO T VALUES (1, 'abc', 123), (2, 'abcd', 12)",
            "CREATE INDEX T_V ON T(V)",
            "CREATE TABLE P(ID INT, PAT VARCHAR, ESC VARCHAR)",
            "INSERT INTO P VALUES (1, 'ab%', '\'), (2, '%bc', '\'), (3, 'a_c', NULL)",
            "EXPLAIN SELECT T.ID FROM T JOIN P ON T.V LIKE P.PAT",
            "EXPLAIN SELECT T.ID FROM T JOIN P ON T.V LIKE 'a%' ESCAPE P.ESC",
            "EXPLAIN SELECT ID FROM T WHERE N LIKE '12%'",
        ]);
    }

    [JavaOracleFact]
    public void ColumnLikeAndItsIndexPlanMatchJdbc()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE T(ID INT, V VARCHAR, C CHAR(5), I VARCHAR_IGNORECASE(10))",
            "INSERT INTO T VALUES (1, 'abc', 'abc', 'Abc'), (2, 'abd', 'abd', 'abc'), "
                + "(3, 'xabc', 'xabc', 'ABC'), (4, NULL, NULL, NULL), (5, 'a%c', 'a%c', 'a%c'), "
                + "(6, 'a_c', 'a_c', 'a_c')",
            "CREATE INDEX T_V ON T(V)",
            "CREATE INDEX T_I ON T(I)",
            "SELECT ID FROM T WHERE V LIKE 'ab%' ORDER BY ID",
            "SELECT ID FROM T WHERE V LIKE 'abc' ORDER BY ID",
            "SELECT ID FROM T WHERE V LIKE '%bc' ORDER BY ID",
            "SELECT ID FROM T WHERE V LIKE '%b%' ORDER BY ID",
            "SELECT ID FROM T WHERE V LIKE 'a%c' ORDER BY ID",
            "SELECT ID FROM T WHERE V LIKE '_bc' ORDER BY ID",
            "SELECT ID FROM T WHERE V NOT LIKE 'ab%' ORDER BY ID",
            "SELECT ID FROM T WHERE V LIKE 'a\\%c' ESCAPE '\\' ORDER BY ID",
            "SELECT ID FROM T WHERE I LIKE 'abc' ORDER BY ID",
            "SELECT ID FROM T WHERE I LIKE 'AB%' ORDER BY ID",
            "SELECT ID FROM T WHERE I ILIKE 'ab%' ORDER BY ID",
            "SELECT ID FROM T WHERE C LIKE 'abc%' ORDER BY ID",
            "SELECT ID FROM T WHERE C = 'abc' ORDER BY ID",
            "EXPLAIN SELECT ID FROM T WHERE V LIKE 'ab%'",
            "EXPLAIN SELECT ID FROM T WHERE V LIKE 'abc'",
            "EXPLAIN SELECT ID FROM T WHERE V LIKE 'a%c'",
            "EXPLAIN SELECT ID FROM T WHERE V LIKE '%bc'",
            "EXPLAIN SELECT ID FROM T WHERE V LIKE '_bc'",
            "EXPLAIN SELECT ID FROM T WHERE I LIKE 'ab%'",
            "EXPLAIN SELECT ID FROM T WHERE V NOT LIKE 'ab%'",
            "EXPLAIN SELECT ID FROM T WHERE V LIKE 'a\\%c' ESCAPE '\\'",
        ]);
    }
}
