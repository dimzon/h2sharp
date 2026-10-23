// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestAutoRecompile.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; the prepared statement's column
// count is the reader's FieldCount. The reference's main() entry is the runner; xUnit
// runs Test() directly.
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Tests if prepared statements are re-compiled when required.
/// </summary>
public sealed class TestAutoRecompile : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb("autoRecompile");
        using DbConnection conn = GetConnection("autoRecompile");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY)");
        DbCommand prep = conn.CreateCommand();
        prep.CommandText = "SELECT * FROM TEST";
        using (DbDataReader reader = prep.ExecuteReader())
        {
            AssertEquals(1, reader.FieldCount);
        }

        Execute(stat, "ALTER TABLE TEST ADD COLUMN NAME VARCHAR(255)");
        using (DbDataReader reader = prep.ExecuteReader())
        {
            AssertEquals(2, reader.FieldCount);
        }

        Execute(stat, "DROP TABLE TEST");
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY, X INT, Y INT)");
        using (DbDataReader reader = prep.ExecuteReader())
        {
            AssertEquals(3, reader.FieldCount);
        }

        // TODO test auto-recompile with insert..select, views and so on

        prep = conn.CreateCommand();
        prep.CommandText = "INSERT INTO TEST VALUES(1, 2, 3)";
        Execute(stat, "ALTER TABLE TEST ADD COLUMN Z INT");
        AssertThrows(ErrorCode.ColumnCountDoesNotMatch, () => _ = prep.ExecuteNonQuery());
        AssertThrows(ErrorCode.ColumnCountDoesNotMatch, () => _ = prep.ExecuteNonQuery());
    }
}
