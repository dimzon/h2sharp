// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestMaterializedView.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands, and the proxy form of
// assertThrows becomes the lambda form, because a sealed class cannot be proxied. The
// reference's main() entry is the runner; xUnit runs Test() directly.
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Tests for MATERIALIZED VIEW.
/// </summary>
public sealed class TestMaterializedView : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb("materializedview");
        Test1();
        DeleteDb("materializedview");
    }

    private void Test1()
    {
        using DbConnection conn = GetConnection("materializedview");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(a int, b int)");
        Execute(stat, "insert into test values(1, 1)");
        Execute(stat, "create materialized view test_view as select a, b from test");
        stat.CommandText = "select * from test_view";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(rs.GetInt32(0), 1);
            AssertEquals(rs.GetInt32(1), 1);
            AssertFalse(rs.Read());
        }

        Execute(stat, "insert into test values(2, 2)");
        Execute(stat, "refresh materialized view test_view");
        stat.CommandText = "select * from test_view";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(rs.GetInt32(0), 1);
            AssertEquals(rs.GetInt32(1), 1);
            AssertTrue(rs.Read());
            AssertEquals(rs.GetInt32(0), 2);
            AssertEquals(rs.GetInt32(1), 2);
            AssertFalse(rs.Read());
        }

        // cannot drop table until the materialized view is dropped
        AssertThrows(ErrorCode.CannotDrop2, () => Execute(stat, "drop table test"));
        Execute(stat, "drop materialized view test_view");
        Execute(stat, "drop table test");
    }
}
