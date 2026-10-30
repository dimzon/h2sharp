// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestView.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands, and the proxy form of
// assertThrows becomes the lambda form. The alias of testCacheFunction stays the
// reference's Java class name, as written. The reference's main() entry is the runner;
// xUnit runs Test() directly.
//
// Modified (deferred, see MIGRATION_STATUS.md): testSubQueryViewIndexCache introspects
// the session's view index cache (getSession().getViewIndexCache/clearViewIndexCache),
// which has no ADO.NET shape — it is engine-internal state no provider surface carries.
// The part is skipped; the rest of the file is ported.
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test for views.
/// </summary>
public sealed class TestView : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb("view");
        TestSubSubQuery();
        // testSubQueryViewIndexCache: deferred, see the header and MIGRATION_STATUS.md.
        TestInnerSelectWithRownum();
        TestInnerSelectWithRange();
        TestEmptyColumn();
        TestChangeSchemaSearchPath();
        TestParameterizedView();
        TestCache();
        // TestCacheFunction lives in TestViewCacheFunctionAlias, marked BlockedByPort:
        // it is built on CREATE ALIAS.
        TestInSelect();
        TestUnionReconnect();
        TestManyViews();
        TestReferenceView();
        TestViewAlterAndCommandCache();
        DeleteDb("view");
    }

    private void TestSubSubQuery()
    {
        using DbConnection conn = GetConnection("view");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "drop table test if exists");
        Execute(stat, "create table test(a int, b int, c int)");
        Execute(stat, "insert into test values(1, 1, 1)");
        stat.CommandText = "select 1 x from (select a, b, c from "
            + "(select * from test) bbb where bbb.a >=1 and bbb.a <= 1) sp "
            + "where sp.a = 1 and sp.b = 1 and sp.c = 1";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
        }
    }

    private void TestInnerSelectWithRownum()
    {
        using DbConnection conn = GetConnection("view");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "drop table test if exists");
        Execute(stat, "create table test(id int primary key, name varchar(1))");
        Execute(stat, "insert into test(id, name) values(1, 'b'), (3, 'a')");
        stat.CommandText = "select nr from (select rownum() as nr, "
            + "a.id as id from (select id from test order by name) as a) as b "
            + "where b.id = 1;";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        stat.CommandText = "select nr from (select row_number() over() as nr, "
            + "a.id as id from (select id from test order by name) as a) as b "
            + "where b.id = 1;";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        Execute(stat, "drop table test");
    }

    private void TestInnerSelectWithRange()
    {
        using DbConnection conn = GetConnection("view");
        DbCommand stat = conn.CreateCommand();
        stat.CommandText = "select x from (select x from ("
            + "select x from system_range(1, 5)) "
            + "where x > 2 and x < 4) where x = 3";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertFalse(rs.Read());
        }

        stat.CommandText = "select x from (select x from ("
            + "select x from system_range(1, 5)) "
            + "where x = 3) where x > 2 and x < 4";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertFalse(rs.Read());
        }
    }

    private void TestEmptyColumn()
    {
        DeleteDb("view");
        using DbConnection conn = GetConnection("view");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(a int, b int)");
        Execute(stat, "create view test_view as select a, b from test");
        Execute(stat, "select * from test_view where a between 1 and 2 and b = 2");
    }

    private void TestChangeSchemaSearchPath()
    {
        DeleteDb("view");
        using DbConnection conn = GetConnection("view");
        DbCommand stat = conn.CreateCommand();
        // The alias half of this scenario lives in TestViewChangeSchemaSearchPathAlias,
        // marked BlockedByPort.
        Execute(stat, "CREATE SCHEMA S");
        Execute(stat, "CREATE VIEW S.TEST AS SELECT 1 FROM DUAL");
        Execute(stat, "SET SCHEMA=S");
        Execute(stat, "SET SCHEMA_SEARCH_PATH=S");
        Execute(stat, "SELECT * FROM TEST");
    }

    private void TestParameterizedView()
    {
        DeleteDb("view");
        using DbConnection conn = GetConnection("view");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE Test(id INT AUTO_INCREMENT NOT NULL, "
            + "f1 VARCHAR NOT NULL, f2 VARCHAR NOT NULL)");
        Execute(stat, "INSERT INTO Test(f1, f2) VALUES ('value1','value2')");
        Execute(stat, "INSERT INTO Test(f1, f2) VALUES ('value1','value3')");
        using DbCommand ps = conn.CreateCommand();
        ps.CommandText = "CREATE VIEW Test_View AS SELECT f2 FROM Test WHERE f1=?";
        ps.Parameters.Add(new H2Parameter { Value = "value1" });
        AssertThrows(ErrorCode.FeatureNotSupported1,
            () => _ = ps.ExecuteNonQuery());
    }

    private void TestCache()
    {
        DeleteDb("view");
        using DbConnection conn = GetConnection("view");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "SET @X 8");
        Execute(stat, "CREATE VIEW V AS SELECT * FROM (SELECT @X)");
        stat.CommandText = "SELECT * FROM V";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(8, rs.GetInt32(0));
        }

        Execute(stat, "SET @X 5");
        stat.CommandText = "SELECT * FROM V";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(5, rs.GetInt32(0));
        }
    }

    private void TestInSelect()
    {
        DeleteDb("view");
        using DbConnection conn = GetConnection("view");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id int primary key) as select 1");
        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "select * from test t where t.id in "
            + "(select t2.id from test t2 where t2.id in (?, ?))";
        prep.Parameters.Add(new H2Parameter { Value = 1 });
        prep.Parameters.Add(new H2Parameter { Value = 2 });
        _ = prep.ExecuteNonQuery();
    }

    private void TestUnionReconnect()
    {
        if (Config.Memory)
        {
            return;
        }

        DeleteDb("view");
        DbConnection conn = GetConnection("view");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table t1(k smallint, ts timestamp(6))");
        Execute(stat, "create table t2(k smallint, ts timestamp(6))");
        Execute(stat, "create table t3(k smallint, ts timestamp(6))");
        Execute(stat, "create view v_max_ts as select "
            + "max(ts) from (select max(ts) as ts from t1 "
            + "union select max(ts) as ts from t2 "
            + "union select max(ts) as ts from t3)");
        Execute(stat, "create view v_test as select max(ts) as ts from t1 "
            + "union select max(ts) as ts from t2 "
            + "union select max(ts) as ts from t3");
        conn.Close();
        conn = GetConnection("view");
        stat = conn.CreateCommand();
        Execute(stat, "select * from v_max_ts");
        conn.Close();
        DeleteDb("view");
    }

    private void TestManyViews()
    {
        DeleteDb("view");
        using DbConnection conn = GetConnection("view");
        DbCommand s = conn.CreateCommand();
        Execute(s, "create table t0(id int primary key)");
        Execute(s, "insert into t0 values(1), (2), (3)");
        for (int i = 0; i < 30; i++)
        {
            Execute(s, "create view t" + (i + 1) + " as select * from t" + i);
            Execute(s, "select * from t" + (i + 1));
            s.CommandText = "select count(*) from t" + (i + 1) + " where id=2";
            using (DbDataReader rs = s.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals(1, rs.GetInt32(0));
            }
        }

        conn.Close();
        using (DbConnection conn2 = GetConnection("view"))
        {
            conn2.Close();
        }

        DeleteDb("view");
    }

    private void TestReferenceView()
    {
        DeleteDb("view");
        using DbConnection conn = GetConnection("view");
        DbCommand s = conn.CreateCommand();
        Execute(s, "create table t0(id int primary key)");
        Execute(s, "create view t1 as select * from t0");
        AssertThrows(ErrorCode.FeatureNotSupported1, s,
            "create table t2(id int primary key, "
            + "col1 int not null, foreign key (col1) references t1(id))");
    }

    /// <summary>
    /// Make sure that when we change a view, that change in reflected in other
    /// sessions command cache.
    /// </summary>
    private void TestViewAlterAndCommandCache()
    {
        DeleteDb("view");
        using DbConnection conn = GetConnection("view");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table t0(id int primary key)");
        Execute(stat, "create table t1(id int primary key)");
        Execute(stat, "insert into t0 values(0)");
        Execute(stat, "insert into t1 values(1)");
        Execute(stat, "create view v1 as select * from t0");
        stat.CommandText = "select * from v1";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(0, rs.GetInt32(0));
        }

        Execute(stat, "create or replace view v1 as select * from t1");
        stat.CommandText = "select * from v1";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
        }
    }
}
