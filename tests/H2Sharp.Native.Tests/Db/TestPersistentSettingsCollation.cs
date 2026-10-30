// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestCases.testPersistentSettings.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// The persistent-collation scenario, split out of TestCases so the rest of the
// suite stays green on its own.
//
// Blocked by the port (BlockedByPort): SET COLLATION de_DE needs a real German
// collator, which is not ported (D-002): the platform's collation weights disagree
// with the reference's, so a named collation refuses at use instead of comparing
// wrongly. The trait keeps this in the suite as the defect's checklist entry.
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test that a collation survives closing and reopening the database.
/// </summary>
public sealed class TestPersistentSettingsCollation : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    [Trait("Category", "BlockedByPort")]
    public override void Test()
    {
        DeleteDb("cases");
        DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "SET COLLATION de_DE");
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY, "
            + "NAME VARCHAR)");
        Execute(stat, "CREATE INDEX IDXNAME ON TEST(NAME)");
        // \u00f6 = oe
        Execute(stat, "INSERT INTO TEST VALUES(1, 'B\u00f6hlen'), "
            + "(2, 'Bach'), (3, 'Bucher')");
        conn.Close();
        conn = GetConnection("cases");
        DbCommand query = conn.CreateCommand();
        query.CommandText = "SELECT NAME FROM TEST ORDER BY NAME";
        using (DbDataReader rs = query.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("Bach", rs.GetString(0));
            AssertTrue(rs.Read());
            AssertEquals("B\u00f6hlen", rs.GetString(0));
            AssertTrue(rs.Read());
            AssertEquals("Bucher", rs.GetString(0));
        }
    }
}
