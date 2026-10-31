// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestCompoundIndexSearch.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements/result sets are ADO.NET commands/readers; the
// reference's main() entry is the runner, xUnit runs Test() directly.
//
// Not run by default (H2Fact): the test asserts the exact EXPLAIN ANALYZE plan text,
// which is only meaningful once the port's optimizer produces the reference's plans.
using System.Text.RegularExpressions;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test various queries against compound indexes.
/// </summary>
public sealed class TestCompoundIndexSearch : TestDb
{
    private const string DbName = "compoundIndexSearch";

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

    /// <summary>
    /// Executes a query with a simple IN condition against an indexed column.
    /// </summary>
    /// <param name="conn">the connection</param>
    private void SimpleInAgainstSimpleIndexCheck(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();
        stat.CommandText = "EXPLAIN ANALYZE SELECT b, c FROM test WHERE (a) IN (1, 4)";
        using DbDataReader rs = stat.ExecuteReader();
        AssertTrue(rs.Read());
        AssertEquals(
            "SELECT \"B\", \"C\" FROM \"PUBLIC\".\"TEST\" /* PUBLIC.IDX_A: A IN(1, 4) */ "
            + "/* scanCount: 5 */ WHERE \"A\" IN(1, 4)",
            Plan(rs.GetString(0)));
    }

    /// <summary>
    /// Executes a query with a simple IN condition against a compound index. The lookup
    /// column is the first component of the index, so the lookup works as it was a
    /// simple index.
    /// </summary>
    /// <param name="conn">the connection</param>
    private void SimpleInAgainstFirstCompoundIndex(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();
        stat.CommandText = "EXPLAIN ANALYZE SELECT b, c FROM test WHERE b IN (1, 2)";
        using DbDataReader rs = stat.ExecuteReader();
        AssertTrue(rs.Read());
        AssertEquals(
            "SELECT \"B\", \"C\" FROM \"PUBLIC\".\"TEST\" /* PUBLIC.IDX_B_C: B IN(1, 2) */ "
            + "/* scanCount: 7 */ WHERE \"B\" IN(1, 2)",
            Plan(rs.GetString(0)));
    }

    /// <summary>
    /// Executes a query with a simple IN condition against a compound index. The lookup
    /// column is the second component of the index, so a full table scan happens.
    /// </summary>
    /// <param name="conn">the connection</param>
    private void SimpleInAgainstSecondCompoundIndex(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();
        stat.CommandText = "EXPLAIN ANALYZE SELECT b, c FROM test WHERE c IN ('1', '2')";
        using DbDataReader rs = stat.ExecuteReader();
        AssertTrue(rs.Read());
        AssertEquals(
            "SELECT \"B\", \"C\" FROM \"PUBLIC\".\"TEST\" /* PUBLIC.IDX_B_C */ "
            + "/* scanCount: 11 */ WHERE \"C\" IN('1', '2')",
            Plan(rs.GetString(0)));
    }

    /// <summary>
    /// Executes a query with a compound IN condition against a compound index.
    /// </summary>
    /// <param name="conn">the connection</param>
    private void CompoundInAgainstCompoundIndex(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();
        stat.CommandText = "EXPLAIN ANALYZE SELECT b, c FROM test "
            + "WHERE (b, c) IN ((2, '1'), (3, '2'))";
        using DbDataReader rs = stat.ExecuteReader();
        AssertTrue(rs.Read());
        AssertEquals(
            "SELECT \"B\", \"C\" FROM \"PUBLIC\".\"TEST\" "
            + "/* PUBLIC.IDX_B_C: IN(ROW (2, '1'), ROW (3, '2')) */ "
            + "/* scanCount: 4 */ WHERE ROW (\"B\", \"C\") IN(ROW (2, '1'), ROW (3, '2'))",
            Plan(rs.GetString(0)));
    }

    /// <summary>
    /// Executes a query with a compound IN condition against a compound index, but the
    /// condition columns are in different order than in the index: condition (c, b)
    /// against index (b, c).
    /// </summary>
    /// <param name="conn">the connection</param>
    private void CompoundInAgainstCompoundIndexUnordered(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();
        stat.CommandText = "EXPLAIN ANALYZE SELECT b, c FROM test "
            + "WHERE (c, b) IN (('1', 2), ('2', 3))";
        using DbDataReader rs = stat.ExecuteReader();
        AssertTrue(rs.Read());
        AssertEquals(
            "SELECT \"B\", \"C\" FROM \"PUBLIC\".\"TEST\" "
            + "/* PUBLIC.IDX_B_C: IN(ROW (2, '1'), ROW (3, '2')) */ "
            + "/* scanCount: 4 */ WHERE ROW (\"C\", \"B\") IN(ROW ('1', 2), ROW ('2', 3))",
            Plan(rs.GetString(0)));
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
        stat.CommandText = "EXPLAIN ANALYZE SELECT * FROM TEST_NULL "
            + "WHERE (A, B) IN ((1, 1), (2, 1), (2, 2), (2, NULL))";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(
                "SELECT \"PUBLIC\".\"TEST_NULL\".\"A\", \"PUBLIC\".\"TEST_NULL\".\"B\" "
                + "FROM \"PUBLIC\".\"TEST_NULL\" /* PUBLIC.TEST_NULL.tableScan */ "
                + "/* scanCount: 5 */ WHERE ROW (\"A\", \"B\") "
                + "IN(ROW (1, 1), ROW (2, 1), ROW (2, 2), ROW (2, NULL))",
                Plan(rs.GetString(0)));
        }

        Execute(stat, "DROP TABLE TEST_NULL;");
    }

    /// <summary>
    /// Executes a query with a compound IN condition against a simple index.
    /// </summary>
    /// <param name="conn">the connection</param>
    private void CompoundInAgainstSimpleIndex(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();
        stat.CommandText = "EXPLAIN ANALYZE SELECT a, d FROM test "
            + "WHERE (a, d) IN ((1, 3), (2, 4))";
        using DbDataReader rs = stat.ExecuteReader();
        AssertTrue(rs.Read());
        AssertEquals(
            "SELECT \"A\", \"D\" FROM \"PUBLIC\".\"TEST\" "
            + "/* PUBLIC.IDX_A: A IN(1, 2) */ "
            + "/* scanCount: 7 */ WHERE ROW (\"A\", \"D\") IN(ROW (1, 3), ROW (2, 4))",
            Plan(rs.GetString(0)));
    }

    /// <summary>
    /// Executes a query with a compound EQ condition against a compound index.
    /// </summary>
    /// <param name="conn">the connection</param>
    private void CompoundEqAgainstCompoundIndex(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();
        stat.CommandText = "EXPLAIN ANALYZE SELECT b, c FROM test WHERE (b, c) = (1, '1')";
        using DbDataReader rs = stat.ExecuteReader();
        AssertTrue(rs.Read());
        AssertEquals(
            "SELECT \"B\", \"C\" FROM \"PUBLIC\".\"TEST\" /* PUBLIC.IDX_B_C: B = 1 AND C = '1' */ "
            + "/* scanCount: 3 */ WHERE ROW (\"B\", \"C\") = ROW (1, '1')",
            Plan(rs.GetString(0)));
    }

    /// <summary>
    /// Executes a query with multiple EQ conditions against a compound index.
    /// </summary>
    /// <param name="conn">the connection</param>
    private void MultipleEqAgainstCompoundIndex(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();
        stat.CommandText = "EXPLAIN ANALYZE SELECT b, c FROM test WHERE b=1 AND c='1'";
        using DbDataReader rs = stat.ExecuteReader();
        AssertTrue(rs.Read());
        AssertEquals(
            "SELECT \"B\", \"C\" FROM \"PUBLIC\".\"TEST\" /* PUBLIC.IDX_B_C: B = 1 AND C = '1' */ "
            + "/* scanCount: 3 */ WHERE (\"B\" = 1) AND (\"C\" = '1')",
            Plan(rs.GetString(0)));
    }

    /// <summary>
    /// The reference's <c>replaceAll("[\\r\\n\\s]+", " ")</c>.
    /// </summary>
    /// <param name="plan">the plan text</param>
    /// <returns>the single-line plan</returns>
    private static string Plan(string plan)
    {
        return Regex.Replace(plan, "[\\r\\n\\s]+", " ");
    }
}
