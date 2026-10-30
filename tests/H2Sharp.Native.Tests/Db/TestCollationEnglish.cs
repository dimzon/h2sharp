// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from the collation part of org.h2.test.db.TestCases.testCollation.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// The English-collation scenario, split out of TestCases so the rest of the
// suite stays green on its own.
//
// Blocked by the port (BlockedByPort): SET COLLATION ENGLISH needs a real English
// collator, which is not ported (D-002): the platform's collation weights disagree
// with the reference's, so a named collation refuses at use instead of comparing
// wrongly. The trait keeps this in the suite as the defect's checklist entry.
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test the English collation.
/// </summary>
public sealed class TestCollationEnglish : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    [Trait("Category", "BlockedByPort")]
    public override void Test()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "SET COLLATION ENGLISH STRENGTH PRIMARY");
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY, "
            + "NAME VARCHAR(255))");
        Execute(stat, "INSERT INTO TEST VALUES(1, 'Hello'), "
            + "(2, 'World'), (3, 'WORLD'), (4, 'HELLO')");
        Execute(stat, "create index idxname on test(name)");
        stat.CommandText = "select name from test order by name";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("Hello", rs.GetString(0));
            AssertTrue(rs.Read());
            AssertEquals("HELLO", rs.GetString(0));
            AssertTrue(rs.Read());
            AssertEquals("World", rs.GetString(0));
            AssertTrue(rs.Read());
            AssertEquals("WORLD", rs.GetString(0));
        }

        stat.CommandText = "select name from test where name like 'He%'";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("Hello", rs.GetString(0));
            AssertTrue(rs.Read());
            AssertEquals("HELLO", rs.GetString(0));
        }
    }
}
