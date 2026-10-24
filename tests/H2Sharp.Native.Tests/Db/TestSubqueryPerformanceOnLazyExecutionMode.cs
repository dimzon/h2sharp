// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestSubqueryPerformanceOnLazyExecutionMode.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands. The reference's main() entry is
// the runner; xUnit runs Test() directly.
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test subquery performance with lazy query execution mode (LAZY_QUERY_EXECUTION).
/// </summary>
public sealed class TestSubqueryPerformanceOnLazyExecutionMode : TestDb
{
    /// <summary>
    /// Rows count.
    /// </summary>
    private const int Rows = 5000;

    /// <summary>
    /// Test repeats when unexpected failure.
    /// </summary>
    private const int FailRepeats = 5;

    /// <summary>
    /// Whether this test is enabled in the current configuration.
    /// </summary>
    /// <returns><c>true</c> when the test runs</returns>
    public override bool IsEnabled()
    {
        return !Config.Ci;
    }

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb("lazySubq");
        try
        {
            using DbConnection conn = GetConnection("lazySubq");
            DbCommand stmt = conn.CreateCommand();
            Execute(stmt, "CREATE TABLE one (x INTEGER, y INTEGER )");
            using (DbCommand prep = conn.CreateCommand())
            {
                prep.CommandText = "insert into one values (?,?)";
                prep.Parameters.Add(new H2Parameter());
                prep.Parameters.Add(new H2Parameter());
                for (int row = 0; row < Rows; row++)
                {
                    prep.Parameters[0].Value = row / 100;
                    prep.Parameters[1].Value = row;
                    _ = prep.ExecuteNonQuery();
                }
            }

            TestSubqueryInCondition(stmt);
            TestSubqueryInJoin(stmt);
            TestSubqueryInJoinFirst(stmt);
            TestJoinTwoSubqueries(stmt);
            TestSubqueryInNestedJoin(stmt);
        }
        finally
        {
            DeleteDb("lazySubq");
        }
    }

    private void TestSubqueryInCondition(DbCommand stmt)
    {
        string sql = "SELECT COUNT (*) FROM one WHERE x IN (SELECT y FROM one WHERE y < 50)";
        CheckExecutionTime(stmt, sql);
    }

    private void TestSubqueryInJoin(DbCommand stmt)
    {
        string sql =
            "SELECT COUNT (one.x) FROM one "
            + "JOIN (SELECT y AS val FROM one WHERE y < 50) AS subq ON subq.val=one.x";
        CheckExecutionTime(stmt, sql);
    }

    private void TestSubqueryInJoinFirst(DbCommand stmt)
    {
        string sql =
            "SELECT COUNT (one.x) FROM "
            + "(SELECT y AS val FROM one WHERE y < 50) AS subq "
            + "JOIN one ON subq.val=one.x";
        CheckExecutionTime(stmt, sql);
    }

    private void TestJoinTwoSubqueries(DbCommand stmt)
    {
        string sql =
            "SELECT COUNT (one_sub.x) FROM "
            + "(SELECT y AS val FROM one WHERE y < 50) AS subq "
            + "JOIN (SELECT x FROM one) AS one_sub ON subq.val=one_sub.x";
        CheckExecutionTime(stmt, sql);
    }

    private void TestSubqueryInNestedJoin(DbCommand stmt)
    {
        string sql =
            "SELECT COUNT (one.x) FROM one "
            + "LEFT JOIN (SELECT 1 AS val_1) AS subq0 "
            + "JOIN (SELECT y AS val FROM one WHERE y < 30) AS subq1 ON subq0.val_1 < subq1.val "
            + "ON one.x = subq1.val "
            + "WHERE one.x < 30";
        CheckExecutionTime(stmt, sql, 3000);
    }

    private void CheckExecutionTime(DbCommand stmt, string sql)
    {
        CheckExecutionTime(stmt, sql, Rows);
    }

    /// <summary>
    /// Compare execution time when lazy execution mode is disabled and enabled.
    /// The execution time must be almost the same.
    /// </summary>
    /// <param name="stmt">the statement</param>
    /// <param name="sql">the query</param>
    /// <param name="expected">the expected row count</param>
    private void CheckExecutionTime(DbCommand stmt, string sql, int expected)
    {
        long totalNotLazy = 0;
        long totalLazy = 0;
        int successCnt = 0;
        int failCnt = 0;
        for (int i = 0; i < FailRepeats; ++i)
        {
            long tLazy = ExecuteAndCheckResult(stmt, sql, true, expected);
            long tNotLazy = ExecuteAndCheckResult(stmt, sql, false, expected);
            totalNotLazy += tNotLazy;
            totalLazy += tLazy;
            if (tNotLazy * 2 > tLazy)
            {
                successCnt++;
                if (i == 0)
                {
                    break;
                }
            }
            else
            {
                failCnt++;
            }
        }

        if (failCnt > successCnt)
        {
            Fail("Lazy execution too slow. Avg lazy time: "
                + (totalLazy / FailRepeats) + ", avg not lazy time: " + (totalNotLazy / FailRepeats));
        }
    }

    /// <summary>
    /// Runs the query at the requested lazy mode and answers the execution time.
    /// </summary>
    /// <param name="stmt">the statement</param>
    /// <param name="sql">the query</param>
    /// <param name="lazy">whether lazy execution is on</param>
    /// <param name="expected">the expected row count</param>
    /// <returns>the time of the query execution</returns>
    private long ExecuteAndCheckResult(DbCommand stmt, string sql, bool lazy, int expected)
    {
        Execute(stmt, "SET LAZY_QUERY_EXECUTION " + (lazy ? 1 : 0));
        long t0 = Environment.TickCount64;
        stmt.CommandText = sql;
        using (DbDataReader rs = stmt.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(expected, rs.GetInt32(0));
        }

        return Environment.TickCount64 - t0;
    }
}
