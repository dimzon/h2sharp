// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from the function-alias part of org.h2.test.db.TestView.testCacheFunction.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// The cache-function scenario, split out of TestView so the rest of the suite stays
// green on its own.
//
// Blocked by the port (BlockedByPort): CREATE ALIAS is not ported, and aliases are
// out of scope by design. The trait keeps this in the suite as the defect's
// checklist entry.
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test whether a view caches the value of a deterministic function alias.
/// </summary>
public sealed class TestViewCacheFunctionAlias : TestDb
{
    private static int x;

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    [Trait("Category", "BlockedByPort")]
    public override void Test()
    {
        TestCacheFunction(true);
        TestCacheFunction(false);
    }

    private void TestCacheFunction(bool deterministic)
    {
        DeleteDb("view");
        using DbConnection conn = GetConnection("view");
        DbCommand stat = conn.CreateCommand();
        x = 8;
        Execute(stat, "CREATE ALIAS GET_X "
            + (deterministic ? "DETERMINISTIC" : "")
            + " FOR '" + GetType().FullName + ".getX'");
        Execute(stat, "CREATE VIEW V AS SELECT * FROM (SELECT GET_X())");
        stat.CommandText = "SELECT * FROM V";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(8, rs.GetInt32(0));
        }

        x = 5;
        stat.CommandText = "SELECT * FROM V";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(deterministic ? 8 : 5, rs.GetInt32(0));
        }
    }

    /// <summary>
    /// This method is called via reflection from the database.
    /// </summary>
    /// <returns>the static value x</returns>
    public static int GetX()
    {
        return x;
    }
}
