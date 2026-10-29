// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestAlterSchemaRename.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands, and the proxy form of
// assertThrows becomes the lambda form, because a sealed class cannot be proxied. The
// reference's main() entry is the runner; xUnit runs Test() directly.
//
// The alias part of the reference test lives in TestAlterSchemaRenameAlias, marked
// BlockedByPort: CREATE ALIAS is not ported and aliases are out of scope by design.
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test ALTER SCHEMA RENAME statements.
/// </summary>
public sealed class TestAlterSchemaRename : TestDb
{
    private DbConnection? conn;

    private DbCommand? stat;

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb(GetTestName());
        conn = GetConnection(GetTestName());
        stat = conn.CreateCommand();
        TestTryToRenameSystemSchemas();
        TestSimpleRename();
        TestRenameToExistingSchema();
        TestCrossSchemaViews();
        conn.Close();
        DeleteDb(GetTestName());
    }

    private void TestTryToRenameSystemSchemas()
    {
        AssertThrows(ErrorCode.SchemaCanNotBeDropped1,
            () => Execute(stat!, "alter schema information_schema rename to test_info"));
        Execute(stat!, "create sequence test_sequence");
        AssertThrows(ErrorCode.SchemaCanNotBeDropped1,
            () => Execute(stat!, "alter schema public rename to test_schema"));
    }

    private void TestSimpleRename()
    {
        Execute(stat!, "create schema s1");
        Execute(stat!, "create table s1.tab(val int)");
        Execute(stat!, "insert into s1.tab(val) values (3)");
        stat!.CommandText = "select * from s1.tab";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(3, rs.GetInt32(0));
        }

        Execute(stat, "alter schema s1 rename to s2");
        stat.CommandText = "select * from s2.tab";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(3, rs.GetInt32(0));
        }

        Execute(stat, "drop schema s2 cascade");
    }

    private void TestRenameToExistingSchema()
    {
        Execute(stat!, "create schema s1");
        Execute(stat!, "create schema s2");
        AssertThrows(ErrorCode.SchemaAlreadyExists1,
            () => Execute(stat!, "alter schema s1 rename to s2"));
        Execute(stat!, "drop schema s1");
        Execute(stat!, "drop schema s2");
    }

    private void TestCrossSchemaViews()
    {
        Execute(stat!, "create schema s1");
        Execute(stat!, "create schema s2");
        Execute(stat!, "create table s1.tab(val int)");
        Execute(stat!, "insert into s1.tab(val) values (3)");
        Execute(stat!, "create view s1.v1 as select * from s1.tab");
        Execute(stat!, "create view s2.v1 as select val * 2 from s1.tab");
        Execute(stat!, "alter schema s2 rename to s2_new");
        stat!.CommandText = "select * from s1.v1";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(3, rs.GetInt32(0));
        }

        stat.CommandText = "select * from s2_new.v1";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(6, rs.GetInt32(0));
        }

        if (!Config.Memory)
        {
            conn!.Close();
            conn = GetConnection(GetTestName());
            stat = conn.CreateCommand();
            stat.CommandText = "select * from s2_new.v1";
            using DbDataReader rs = stat.ExecuteReader();
            while (rs.Read())
            {
                // just loop
            }
        }

        Execute(stat!, "drop schema s1 cascade");
        Execute(stat!, "drop schema s2_new cascade");
    }
}
