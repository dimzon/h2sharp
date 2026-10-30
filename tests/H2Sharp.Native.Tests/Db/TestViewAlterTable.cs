// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestViewAlterTable.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands, and the proxy form of
// assertThrows becomes the lambda form. conn.getMetaData().getTables is the
// INFORMATION_SCHEMA.TABLES query through the ADO layer. The reference's main() entry
// is the runner; xUnit runs Test() directly.
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test the impact of ALTER TABLE statements on views.
/// </summary>
public sealed class TestViewAlterTable : TestDb
{
    private DbConnection? conn;

    private DbCommand? stat;

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    [Trait("Category", "BlockedByPort")]
    public override void Test()
    {
        // BlockedByPort: CreateTestData uses CREATE USER, which is not ported (D-004).
        DeleteDb(GetTestName());
        conn = GetConnection(GetTestName());
        stat = conn.CreateCommand();

        TestDropColumnWithoutViews();
        TestViewsAreWorking();
        TestAlterTableDropColumnNotInView();
        TestAlterTableDropColumnInView();
        TestAlterTableAddColumnWithView();
        TestAlterTableAlterColumnDataTypeWithView();
        TestSelectStar();
        TestJoinAndAlias();
        TestSubSelect();
        TestForeignKey();
        TestAlterTableDropColumnInViewWithDoubleQuotes();

        conn.Close();
        DeleteDb(GetTestName());
    }

    private void TestDropColumnWithoutViews()
    {
        Execute(stat!, "create table test(a int, b int, c int)");
        Execute(stat!, "alter table test drop column c");
        Execute(stat!, "drop table test");
    }

    private void TestViewsAreWorking()
    {
        CreateTestData();
        CheckViewRemainsValid();
    }

    private void TestAlterTableDropColumnNotInView()
    {
        CreateTestData();
        Execute(stat!, "alter table test drop column c");
        CheckViewRemainsValid();
    }

    private void TestAlterTableDropColumnInView()
    {
        // simple
        Execute(stat!, "create table test(id identity, name varchar) "
            + "as select 1, 'Hello' from dual");
        Execute(stat!, "create view test_view as select * from test");
        AssertThrows(ErrorCode.ColumnIsReferenced1,
            () => Execute(stat!, "alter table test drop name"));
        stat!.CommandText = "select * from test_view";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
        }

        Execute(stat!, "drop view test_view");
        Execute(stat!, "drop table test");

        // nested
        CreateTestData();
        // should throw exception because V1 uses column A
        AssertThrows(ErrorCode.ColumnIsReferenced1,
            () => Execute(stat!, "alter table test drop column a"));
        Execute(stat!, "drop table test cascade");
    }

    private void TestAlterTableAddColumnWithView()
    {
        CreateTestData();
        Execute(stat!, "alter table test add column d int");
        CheckViewRemainsValid();
    }

    private void TestAlterTableAlterColumnDataTypeWithView()
    {
        CreateTestData();
        Execute(stat!, "alter table test alter b char(1)");
        CheckViewRemainsValid();
    }

    private void TestSelectStar()
    {
        CreateTestData();
        Execute(stat!, "create view v4 as select * from test");
        Execute(stat!, "alter table test add d int default 6");
        // H2 doesn't remember v4 as 'select * from test', it instead remembers
        // each individual column that was in 'test' when the view was
        // originally created. This is consistent with PostgreSQL.
        AssertThrows(ErrorCode.ColumnNotFound1, () => Query(stat!, "select d from v4"));
        CheckViewRemainsValid();
    }

    private void TestJoinAndAlias()
    {
        CreateTestData();
        Execute(stat!, "create view v4 as select v1.a dog, v3.a cat "
            + "from v1 join v3 on v1.b = v3.a");
        // should make no difference
        Execute(stat!, "alter table test add d int default 6");
        stat!.CommandText = "select cat, dog from v4";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
            AssertEquals(2, rs.GetInt32(1));
            AssertFalse(rs.Read());
        }

        CheckViewRemainsValid();
    }

    private void TestSubSelect()
    {
        CreateTestData();
        Execute(stat!, "create view v4 as select * from v3 "
            + "where a in (select b from v2)");
        // should make no difference
        Execute(stat!, "alter table test add d int default 6");
        stat!.CommandText = "select a from v4";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        CheckViewRemainsValid();
    }

    private void TestForeignKey()
    {
        CreateTestData();
        Execute(stat!, "create table test2(z int, a int, primary key(z), "
            + "foreign key (a) references TEST(a))");
        Execute(stat!, "insert into test2(z, a) values (99, 1)");
        // should make no difference
        Execute(stat!, "alter table test add d int default 6");
        stat!.CommandText = "select z from test2";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(99, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        Execute(stat!, "drop table test2");
        CheckViewRemainsValid();
    }

    private void CreateTestData()
    {
        Execute(stat!, "create table test(a int primary key, b int, c int)");
        Execute(stat!, "insert into test(a, b, c) values (1, 2, 3)");
        Execute(stat!, "create view v1 as select a as b, b as a from test");
        // child of v1
        Execute(stat!, "create view v2 as select * from v1");
        Execute(stat!, "create user if not exists test_user password 'x'");
        Execute(stat!, "grant select on v2 to test_user");
        // sibling of v1
        Execute(stat!, "create view v3 as select a from test");
    }

    private void CheckViewRemainsValid()
    {
        stat!.CommandText = "select b from v1";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        stat.CommandText = "select b from v2";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        stat.CommandText = "select * from information_schema.rights";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("TEST_USER", rs.GetString(rs.GetOrdinal("GRANTEE")));
            AssertEquals("V2", rs.GetString(rs.GetOrdinal("TABLE_NAME")));
        }

        stat.CommandText = "select b from test";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        Execute(stat!, "drop table test cascade");

        // getMetaData().getTables: only INFORMATION_SCHEMA rows remain.
        stat.CommandText = "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            while (rs.Read())
            {
                // should have no tables left in the database
                AssertEquals(rs.GetString(0) + "." + rs.GetString(1),
                    "INFORMATION_SCHEMA", rs.GetString(0));
            }
        }
    }

    // original error: table "XX_COPY_xx_xx" not found
    private void TestAlterTableDropColumnInViewWithDoubleQuotes()
    {
        // simple
        Execute(stat!, "create table \"test\"(id identity, name varchar) "
            + "as select 1, 'Hello' from dual");
        Execute(stat!, "create view test_view as select * from \"test\"");
        AssertThrows(ErrorCode.ColumnIsReferenced1,
            () => Execute(stat!, "alter table \"test\" drop name"));
        stat!.CommandText = "select * from test_view";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
        }

        Execute(stat!, "drop view test_view");
        Execute(stat!, "drop table \"test\"");
    }

    /// <summary>
    /// The reference's stat.executeQuery(sql); the rows are read and dropped.
    /// </summary>
    private static void Query(DbCommand stat, string sql)
    {
        stat.CommandText = sql;
        using DbDataReader rs = stat.ExecuteReader();
        while (rs.Read())
        {
            // just loop
        }
    }
}
