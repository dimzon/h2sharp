// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestViewDropView.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands, and the proxy form of
// assertThrows becomes the lambda form. conn.getMetaData().getTables is the
// INFORMATION_SCHEMA.TABLES query through the ADO layer — the same table the metadata
// call walks. The reference's main() entry is the runner; xUnit runs Test() directly.
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test the impact of DROP VIEW statements on dependent views.
/// </summary>
public sealed class TestViewDropView : TestDb
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

        TestDropViewDefaultBehaviour();
        TestDropViewRestrict();
        TestDropViewCascade();
        TestCreateForceView();
        TestCreateOrReplaceView();
        TestCreateOrReplaceViewWithNowInvalidDependentViews();
        TestCreateOrReplaceForceViewWithNowInvalidDependentViews();

        conn.Close();
        DeleteDb(GetTestName());
    }

    private void TestCreateForceView()
    {
        AssertThrows(ErrorCode.TableOrViewNotFoundDatabaseEmpty1,
            () => Execute(stat!, "create view test_view as select * from test"));
        Execute(stat!, "create force view test_view as select * from test");
        Execute(stat!, "create table test(id int)");
        Execute(stat!, "alter view test_view recompile");
        Execute(stat!, "select * from test_view");
        Execute(stat!, "drop table test_view, test cascade");
        Execute(stat!, "create force view test_view as select * from test where 1=0");
        Execute(stat!, "create table test(id int)");
        Execute(stat!, "alter view test_view recompile");
        Execute(stat!, "select * from test_view");
        Execute(stat!, "drop table test_view, test cascade");
    }

    private void TestDropViewDefaultBehaviour()
    {
        CreateTestData();
        stat!.CommandText =
            "SELECT SETTING_VALUE FROM INFORMATION_SCHEMA.SETTINGS WHERE SETTING_NAME = 'DROP_RESTRICT'";
        bool dropRestrict;
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            dropRestrict = rs.GetBoolean(0);
        }

        if (dropRestrict)
        {
            // should fail because have dependencies
            AssertThrows(ErrorCode.CannotDrop2, () => Execute(stat!, "drop view v1"));
        }
        else
        {
            Execute(stat!, "drop view v1");
            CheckViewRemainsValid();
        }
    }

    private void TestDropViewRestrict()
    {
        CreateTestData();
        // should fail because have dependencies
        AssertThrows(ErrorCode.CannotDrop2, () => Execute(stat!, "drop view v1 restrict"));
        CheckViewRemainsValid();
    }

    private void TestDropViewCascade()
    {
        CreateTestData();
        Execute(stat!, "drop view v1 cascade");
        // v1, v2, v3 should be deleted
        AssertThrows(ErrorCode.TableOrViewNotFound1, () => Query(stat!, "select * from v1"));
        AssertThrows(ErrorCode.TableOrViewNotFound1, () => Query(stat!, "select * from v2"));
        AssertThrows(ErrorCode.TableOrViewNotFound1, () => Query(stat!, "select * from v3"));
        Execute(stat!, "drop table test");
    }

    private void TestCreateOrReplaceView()
    {
        CreateTestData();

        Execute(stat!, "create or replace view v1 as select a as b, b as a, c from test");

        CheckViewRemainsValid();
    }

    private void TestCreateOrReplaceViewWithNowInvalidDependentViews()
    {
        CreateTestData();
        // v2 and v3 need more than just "c", so we should get an error
        // dependent views need more columns than just 'c'
        AssertThrows(ErrorCode.ColumnNotFound1,
            () => Execute(stat!, "create or replace view v1 as select c from test"));
        // make sure our old views come back ok
        CheckViewRemainsValid();
    }

    private void TestCreateOrReplaceForceViewWithNowInvalidDependentViews()
    {
        CreateTestData();

        // v2 and v3 need more than just "c",
        // but we want to force the creation of v1 anyway
        Execute(stat!, "create or replace force view v1 as select c from test");
        // now v2 and v3 are broken, but they still exist
        AssertThrows(ErrorCode.ColumnNotFound1, () => Query(stat!, "select b from v2"));
        Execute(stat!, "drop table test cascade");
    }

    private void CreateTestData()
    {
        Execute(stat!, "drop all objects");
        Execute(stat!, "create table test(a int, b int, c int)");
        Execute(stat!, "insert into test(a, b, c) values (1, 2, 3)");
        Execute(stat!, "create view v1 as select a as b, b as a from test");
        // child of v1
        Execute(stat!, "create view v2 as select * from v1");
        // child of v2
        Execute(stat!, "create view v3 as select * from v2");
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
        using (DbDataReader d = stat.ExecuteReader())
        {
            while (d.Read())
            {
                // should have no tables left in the database
                AssertEquals(d.GetString(0) + "." + d.GetString(1),
                    "INFORMATION_SCHEMA", d.GetString(0));
            }
        }
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
