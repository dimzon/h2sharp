// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestIndexHints.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands, and the proxy form of
// assertThrows becomes the lambda form, because a sealed class cannot be proxied. The
// reference's main() entry is the runner; xUnit runs Test() directly.
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Tests the index hints feature of this database.
/// </summary>
public sealed class TestIndexHints : TestDb
{
    private DbConnection? conn;

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb("indexhints");
        CreateDb();
        TestQuotedIdentifier();
        TestWithSingleIndexName();
        TestWithEmptyIndexHintsList();
        TestWithInvalidIndexName();
        TestWithMultipleIndexNames();
        TestPlanSqlHasIndexesInCorrectOrder();
        TestWithTableAlias();
        TestWithTableAliasCalledUse();
        conn!.Close();
        DeleteDb("indexhints");
    }

    private void CreateDb()
    {
        conn = GetConnection("indexhints");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test (x int, y int)");
        Execute(stat, "create index idx1 on test (x)");
        Execute(stat, "create index idx2 on test (x, y)");
        Execute(stat, "create index \"Idx3\" on test (y, x)");
    }

    private void TestQuotedIdentifier()
    {
        DbCommand stat = conn!.CreateCommand();
        stat.CommandText = "explain analyze select * "
            + "from test use index(\"Idx3\") where x=1 and y=1";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            string plan = rs.GetString(0);
            AssertTrue(plan.Contains("/* PUBLIC.Idx3:", StringComparison.Ordinal));
            AssertTrue(plan.Contains("USE INDEX (\"Idx3\")", StringComparison.Ordinal));
            stat.CommandText = "EXPLAIN ANALYZE " + plan;
        }

        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            string plan = rs.GetString(0);
            AssertTrue(plan.Contains("/* PUBLIC.Idx3:", StringComparison.Ordinal));
            AssertTrue(plan.Contains("USE INDEX (\"Idx3\")", StringComparison.Ordinal));
        }
    }

    private void TestWithSingleIndexName()
    {
        DbCommand stat = conn!.CreateCommand();
        stat.CommandText = "explain analyze select * "
            + "from test use index(idx1) where x=1 and y=1";
        using DbDataReader rs = stat.ExecuteReader();
        _ = rs.Read();
        string result = rs.GetString(0);
        AssertTrue(result.Contains("/* PUBLIC.IDX1:", StringComparison.Ordinal));
    }

    private void TestWithTableAlias()
    {
        DbCommand stat = conn!.CreateCommand();
        stat.CommandText = "explain analyze select * "
            + "from test t use index(idx2) where x=1 and y=1";
        using DbDataReader rs = stat.ExecuteReader();
        _ = rs.Read();
        string result = rs.GetString(0);
        AssertTrue(result.Contains("/* PUBLIC.IDX2:", StringComparison.Ordinal));
    }

    private void TestWithTableAliasCalledUse()
    {
        // make sure that while adding new syntax for table hints, code
        // that uses "USE" as a table alias still works
        DbCommand stat = conn!.CreateCommand();
        stat.CommandText = "explain analyze select * "
            + "from test use where use.x=1 and use.y=1";
        using DbDataReader rs = stat.ExecuteReader();
        while (rs.Read())
        {
            // just loop
        }
    }

    private void TestWithMultipleIndexNames()
    {
        DbCommand stat = conn!.CreateCommand();
        stat.CommandText = "explain analyze select * "
            + "from test use index(idx1, idx2) where x=1 and y=1";
        using DbDataReader rs = stat.ExecuteReader();
        _ = rs.Read();
        string result = rs.GetString(0);
        AssertTrue(result.Contains("/* PUBLIC.IDX2:", StringComparison.Ordinal));
    }

    private void TestPlanSqlHasIndexesInCorrectOrder()
    {
        DbCommand command = conn!.CreateCommand();
        command.CommandText = "explain analyze select * "
            + "from test use index(idx1, idx2) where x=1 and y=1";
        using (DbDataReader rs = command.ExecuteReader())
        {
            _ = rs.Read();
            AssertTrue(rs.GetString(0).Contains("USE INDEX (\"IDX1\", \"IDX2\")",
                StringComparison.Ordinal));
        }

        command.CommandText = "explain analyze select * "
            + "from test use index(idx2, idx1) where x=1 and y=1";
        using (DbDataReader rs2 = command.ExecuteReader())
        {
            _ = rs2.Read();
            AssertTrue(rs2.GetString(0).Contains("USE INDEX (\"IDX2\", \"IDX1\")",
                StringComparison.Ordinal));
        }
    }

    private void TestWithEmptyIndexHintsList()
    {
        DbCommand stat = conn!.CreateCommand();
        stat.CommandText = "explain analyze select * "
            + "from test use index () where x=1 and y=1";
        using DbDataReader rs = stat.ExecuteReader();
        _ = rs.Read();
        string result = rs.GetString(0);
        AssertTrue(result.Contains("/* PUBLIC.TEST.tableScan", StringComparison.Ordinal));
    }

    private void TestWithInvalidIndexName()
    {
        DbCommand stat = conn!.CreateCommand();
        AssertThrows(ErrorCode.IndexNotFound1,
            () =>
            {
                stat.CommandText = "explain analyze select * "
                    + "from test use index(idx_doesnt_exist) where x=1 and y=1";
                using DbDataReader rs = stat.ExecuteReader();
            });
    }
}
