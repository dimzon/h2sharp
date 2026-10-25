// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestCompoundIndexParamSearch.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements/result sets are ADO.NET commands/readers; a
// PreparedStatement is a command with parameters, and setObject(x, null) is a parameter
// whose value is DBNull.Value (a null Value is refused by the port's command layer).
// Patterns are System.Text.RegularExpressions.
//
// Not run by default (H2Fact): the test compares EXPLAIN ANALYZE scan counts, which are
// only meaningful once the port's optimizer produces the reference's plans.
using System.Text.RegularExpressions;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test various queries with hardcoded, and prepared statement parameters. The test
/// cases are the same as in <see cref="TestCompoundIndexSearch"/> but we are checking
/// whether the hard coded, and the passed parameters work the same.
/// </summary>
public sealed class TestCompoundIndexParamSearch : TestDb
{
    private const string DbName = "paramSearch";

    private static readonly Regex ScanCountPattern =
        new("\\/\\* scanCount: (\\d+) \\*\\/");

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        using DbConnection conn = Prepare();

        SimpleInAgainstSimpleIndexCheck(conn);
        SimpleInAgainstFirstCompoundIndex(conn);
        SimpleInAgainstSecondCompoundIndex(conn);
        CompoundInNoIndexAndNull(conn);
        CompoundInAgainstCompoundIndex(conn);
        CompoundInAgainstCompoundIndexUnordered(conn);
        CompoundInAgainstSimpleIndex(conn);
        CompoundEqAgainstCompoundIndex(conn);
        MultipleEqAgainstCompoundIndex(conn);
        TestInListWithParameters(conn);

        conn.Close();
        DeleteDb(DbName);
    }

    private DbConnection Prepare()
    {
        DeleteDb(DbName);
        DbConnection conn = GetConnection(DbName);
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE test (a INT, b INT, c CHAR, d INT);");
        Execute(stat, "CREATE INDEX idx_a ON test(a);");
        Execute(stat, "CREATE INDEX idx_b_c ON test(b, c);");
        Execute(stat, "INSERT INTO test (a, b, c, d) VALUES "
            + "(1, 1, '1', 1), "
            + "(1, 1, '2', 2), "
            + "(1, 3, '3', 3), "
            + "(2, 2, '1', 4), "
            + "(2, 3, '2', 1), "
            + "(2, 3, '3', 2), "
            + "(3, 2, '1', 3), "
            + "(3, 2, '2', 4), "
            + "(3, 3, '3', 1), "
            + "(4, 1, '1', 2);");
        return conn;
    }

    private static string FindScanCount(string input)
    {
        Match matcher = ScanCountPattern.Match(
            Regex.Replace(input, "[\\r\\n\\s]+", " "));
        return matcher.Success ? matcher.Groups[1].Value : string.Empty;
    }

    /// <summary>
    /// Executes a query with a simple IN condition against an indexed column.
    /// </summary>
    /// <param name="conn">the connection</param>
    private void SimpleInAgainstSimpleIndexCheck(DbConnection conn)
    {
        string expected = LiteralScanCount(conn,
            "EXPLAIN ANALYZE SELECT b, c FROM test WHERE (a) IN (1, 4)");

        string actual = ParamScanCount(conn,
            "EXPLAIN ANALYZE SELECT b, c FROM test WHERE (a) IN (1, ?)", 4);
        AssertEquals(actual, expected);

        actual = ParamScanCount(conn,
            "EXPLAIN ANALYZE SELECT b, c FROM test WHERE (a) IN (?, ?)", 1, 4);
        AssertEquals(actual, expected);
    }

    /// <summary>
    /// Executes a query with a simple IN condition against a compound index. The lookup
    /// column is the first component of the index, so the lookup works as it was a
    /// simple index.
    /// </summary>
    /// <param name="conn">the connection</param>
    private void SimpleInAgainstFirstCompoundIndex(DbConnection conn)
    {
        string expected = LiteralScanCount(conn,
            "EXPLAIN ANALYZE SELECT b, c FROM test WHERE b IN (1, 2)");

        string actual = ParamScanCount(conn,
            "EXPLAIN ANALYZE SELECT b, c FROM test WHERE b IN (1, ?)", 2);
        AssertEquals(actual, expected);

        actual = ParamScanCount(conn,
            "EXPLAIN ANALYZE SELECT b, c FROM test WHERE b IN (?, ?)", 1, 2);
        AssertEquals(actual, expected);
    }

    /// <summary>
    /// Executes a query with a simple IN condition against a compound index. The lookup
    /// column is the second component of the index, so a full table scan happens.
    /// </summary>
    /// <param name="conn">the connection</param>
    private void SimpleInAgainstSecondCompoundIndex(DbConnection conn)
    {
        string expected = LiteralScanCount(conn,
            "EXPLAIN ANALYZE SELECT b, c FROM test WHERE c IN ('1', '2')");

        string actual = ParamScanCount(conn,
            "EXPLAIN ANALYZE SELECT b, c FROM test WHERE c IN ('1', ?)", "2");
        AssertEquals(actual, expected);

        actual = ParamScanCount(conn,
            "EXPLAIN ANALYZE SELECT b, c FROM test WHERE c IN (?, ?)", "1", "2");
        AssertEquals(actual, expected);
    }

    /// <summary>
    /// Executes a query with a compound IN condition against a compound index.
    /// </summary>
    /// <param name="conn">the connection</param>
    private void CompoundInAgainstCompoundIndex(DbConnection conn)
    {
        string expected = LiteralScanCount(conn,
            "EXPLAIN ANALYZE SELECT b, c FROM test WHERE (b, c) IN ((2, '1'), (3, '2'))");

        string actual = ParamScanCount(conn,
            "EXPLAIN ANALYZE SELECT b, c FROM test WHERE (b, c) IN ((2, ?), (3, ?))",
            "1", "2");
        AssertEquals(actual, expected);

        actual = ParamScanCount(conn,
            "EXPLAIN ANALYZE SELECT b, c FROM test WHERE (b, c) IN ((?, ?), (?, ?))",
            2, "1", 3, "2");
        AssertEquals(actual, expected);
    }

    /// <summary>
    /// Executes a query with a compound IN condition against a compound index, but the
    /// condition columns are in different order than in the index: condition (c, b)
    /// against index (b, c).
    /// </summary>
    /// <param name="conn">the connection</param>
    private void CompoundInAgainstCompoundIndexUnordered(DbConnection conn)
    {
        string expected = LiteralScanCount(conn,
            "EXPLAIN ANALYZE SELECT b, c FROM test WHERE (c, b) IN (('1', 2), ('2', 3))");

        string actual = ParamScanCount(conn,
            "EXPLAIN ANALYZE SELECT b, c FROM test WHERE (c, b) IN (('1', ?), ('2', ?))",
            2, 3);
        AssertEquals(actual, expected);

        actual = ParamScanCount(conn,
            "EXPLAIN ANALYZE SELECT b, c FROM test WHERE (c, b) IN ((?, ?), (?, ?))",
            "1", 2, "2", 3);
        AssertEquals(actual, expected);
    }

    /// <summary>
    /// Executes a query with a compound IN condition. Creates a table on the fly without
    /// any indexes. The table and the query both contain NULL values.
    /// </summary>
    /// <param name="conn">the connection</param>
    private void CompoundInNoIndexAndNull(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();
        Execute(stat,
            "CREATE TABLE TEST_NULL(A INT, B INT) AS (VALUES (1, 1), (1, 2), (2, 1), (2, NULL));");

        string expected = LiteralScanCount(conn,
            "EXPLAIN ANALYZE SELECT * FROM TEST_NULL "
            + "WHERE (A, B) IN ((1, 1), (2, 1), (2, 2), (2, NULL))");

        string actual = ParamScanCount(conn,
            "EXPLAIN ANALYZE SELECT * FROM TEST_NULL "
            + "WHERE (A, B) IN ((1, ?), (2, ?), (2, ?), (2, ?))",
            1, 1, 2, DBNull.Value);
        AssertEquals(actual, expected);

        actual = ParamScanCount(conn,
            "EXPLAIN ANALYZE SELECT * FROM TEST_NULL "
            + "WHERE (A, B) IN ((?, ?), (?, ?), (?, ?), (?, ?))",
            1, 1, 1, 2, 2, 1, 2, DBNull.Value);
        AssertEquals(actual, expected);
    }

    /// <summary>
    /// Executes a query with a compound IN condition against a simple index.
    /// </summary>
    /// <param name="conn">the connection</param>
    private void CompoundInAgainstSimpleIndex(DbConnection conn)
    {
        string expected = LiteralScanCount(conn,
            "EXPLAIN ANALYZE SELECT a, d FROM test WHERE (a, d) IN ((1, 3), (2, 4))");

        string actual = ParamScanCount(conn,
            "EXPLAIN ANALYZE SELECT a, d FROM test WHERE (a, d) IN ((1, ?), (2, ?))",
            3, 2);
        AssertEquals(actual, expected);

        actual = ParamScanCount(conn,
            "EXPLAIN ANALYZE SELECT a, d FROM test WHERE (a, d) IN ((?, ?), (?, ?))",
            1, 3, 2, 4);
        AssertEquals(actual, expected);
    }

    /// <summary>
    /// Executes a query with a compound EQ condition against a compound index.
    /// </summary>
    /// <param name="conn">the connection</param>
    private void CompoundEqAgainstCompoundIndex(DbConnection conn)
    {
        string expected = LiteralScanCount(conn,
            "EXPLAIN ANALYZE SELECT b, c FROM test WHERE (b, c) = (1, '1')");

        string actual = ParamScanCount(conn,
            "EXPLAIN ANALYZE SELECT b, c FROM test WHERE (b, c) = (1, ?)", "1");
        AssertEquals(actual, expected);

        actual = ParamScanCount(conn,
            "EXPLAIN ANALYZE SELECT b, c FROM test WHERE (b, c) = (?, ?)", 1, "1");
        AssertEquals(actual, expected);
    }

    /// <summary>
    /// Executes a query with multiple EQ conditions against a compound index.
    /// </summary>
    /// <param name="conn">the connection</param>
    private void MultipleEqAgainstCompoundIndex(DbConnection conn)
    {
        string expected = LiteralScanCount(conn,
            "EXPLAIN ANALYZE SELECT b, c FROM test WHERE b=1 AND c='1'");

        string actual = ParamScanCount(conn,
            "EXPLAIN ANALYZE SELECT b, c FROM test WHERE b=1 AND c=?", "1");
        AssertEquals(actual, expected);

        actual = ParamScanCount(conn,
            "EXPLAIN ANALYZE SELECT b, c FROM test WHERE b=? AND c=?", 1, "1");
        AssertEquals(actual, expected);
    }

    private void TestInListWithParameters(DbConnection conn)
    {
        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "SELECT TRUE WHERE (CAST(? AS INT), CAST(? AS INT)) "
            + "IN((CAST(? AS INT), CAST(? AS INT)), (CAST(? AS INT), CAST(? AS INT)))";
        Add(prep, 1, 2, 1, 2, 3, 4);
        using (DbDataReader rs = prep.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertTrue(rs.GetBoolean(0));
            AssertFalse(rs.Read());
        }

        prep.Parameters[2].Value = 5;
        using (DbDataReader rs = prep.ExecuteReader())
        {
            AssertFalse(rs.Read());
        }
    }

    /// <summary>
    /// The reference's <c>stat.executeQuery(sql); rs.getString(1)</c> scan count.
    /// </summary>
    /// <param name="conn">the connection</param>
    /// <param name="sql">the query</param>
    /// <returns>the scan count text</returns>
    private string LiteralScanCount(DbConnection conn, string sql)
    {
        using DbCommand stat = conn.CreateCommand();
        stat.CommandText = sql;
        using DbDataReader rs = stat.ExecuteReader();
        AssertTrue(rs.Read());
        return FindScanCount(rs.GetString(0));
    }

    /// <summary>
    /// Runs the query with parameters and returns its scan count.
    /// </summary>
    /// <param name="conn">the connection</param>
    /// <param name="sql">the query</param>
    /// <param name="values">the parameter values</param>
    /// <returns>the scan count text</returns>
    private string ParamScanCount(DbConnection conn, string sql,
        params object?[] values)
    {
        using DbCommand pStat = conn.CreateCommand();
        pStat.CommandText = sql;
        Add(pStat, values);
        using DbDataReader rs = pStat.ExecuteReader();
        AssertTrue(rs.Read());
        return FindScanCount(rs.GetString(0));
    }

    /// <summary>
    /// The reference's setInt/setString/setObject calls: a parameter per value, with
    /// null mapped to DBNull.Value.
    /// </summary>
    /// <param name="prep">the command</param>
    /// <param name="values">the values</param>
    private static void Add(DbCommand prep, params object?[] values)
    {
        foreach (object? value in values)
        {
            prep.Parameters.Add(new H2Parameter { Value = value ?? DBNull.Value });
        }
    }
}
