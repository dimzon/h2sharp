// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestOptimizations.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; org.h2.util.Task is a .NET task.
// wasNull is IsDBNull. The reference's main() entry is the runner; xUnit runs Test()
// directly.
//
// Modified (port gaps, named): testOptimizeInJoinSelect creates an alias returning
// java.sql.ResultSet (org.h2.tools.SimpleResultSet), which the port has no type for —
// the alias SQL stays as written and the C# twin of the method is not defined; the test
// stops at the alias resolution until the alias machinery answers.
using System.Diagnostics;
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Tests various optimizations and that the optimizer uses the expected indexes.
/// </summary>
public sealed class TestOptimizations : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb("optimizations");
        TestConditionsStackOverflow();
        TestIdentityIndexUsage();
        TestFastRowIdCondition();
        TestExplainRoundTrip();
        TestOrderByExpression();
        TestGroupSubquery();
        TestAnalyzeLob();
        TestLike();
        TestExistsSubquery();
        TestQueryCacheConcurrentUse();
        TestQueryCacheResetParams();
        TestRowId();
        TestSortIndex();
        TestAutoAnalyze();
        TestInAndBetween();
        TestNestedIn();
        TestConstantIn1();
        TestConstantIn2();
        TestConstantTypeConversionToColumnType();
        TestNestedInSelectAndLike();
        TestNestedInSelect();
        TestInSelectJoin();
        TestMinMaxNullOptimization();
        TestUseCoveringIndex();
        TestInPredicate();
        // testUseIndexWhenAllColumnsNotInOrderBy();
        if (Config.Networked)
        {
            return;
        }

        // testOptimizeInJoinSelect: waits for the alias machinery, see the header.
        TestOptimizeInJoin();
        TestMultiColumnRangeQuery();
        TestDistinctOptimization();
        TestQueryCacheTimestamp();
        if (!Config.Lazy)
        {
            TestQueryCacheSpeed();
        }

        TestQueryCache(true);
        TestQueryCache(false);
        TestIn();
        TestMinMaxCountOptimization(true);
        TestMinMaxCountOptimization(false);
        TestOrderedIndexes();
        TestIndexUseDespiteNullsFirst();
        TestConvertOrToIn();
        TestConditionAndOrDistributiveLaw();
        DeleteDb("optimizations");
    }

    private void TestIdentityIndexUsage()
    {
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(a identity)");
        Execute(stat, "insert into test values()");
        stat.CommandText = "explain select * from test where a = 1";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertContains(rs.GetString(0), "PRIMARY_KEY");
        }

        Execute(stat, "drop table test");
    }

    private void TestFastRowIdCondition()
    {
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table many(id int) "
            + "as select x from system_range(1, 10000)");
        stat.CommandText = "explain analyze select * from many "
            + "where _rowid_ = 400";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertContains(rs.GetString(0), "/* scanCount: 2 */");
        }
    }

    private void TestExplainRoundTrip()
    {
        using DbConnection conn = GetConnection("optimizations");
        AssertExplainRoundTrip(conn, "SELECT \"X\" FROM SYSTEM_RANGE(1, 1)"
            + " WHERE \"X\" > ANY(SELECT DISTINCT \"X\" FROM SYSTEM_RANGE(1, 1))");
    }

    private void AssertExplainRoundTrip(DbConnection conn, string sql)
    {
        DbCommand stat = conn.CreateCommand();
        stat.CommandText = "explain " + sql;
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            string plan = rs.GetString(0);
            plan = System.Text.RegularExpressions.Regex.Replace(plan, "\\s+", " ");
            plan = System.Text.RegularExpressions.Regex.Replace(plan, "/\\*[^\\*]*\\*/", "");
            plan = System.Text.RegularExpressions.Regex.Replace(plan, "\\s+", " ");
            plan = plan.Replace("( ", "(");
            plan = plan.Replace(" )", ")");
            AssertEquals(sql, plan);
        }
    }

    private void TestOrderByExpression()
    {
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id int primary key, name varchar)");
        Execute(stat, "insert into test values(1, 'Hello'), (2, 'Hello'), (3, 'Hello')");
        stat.CommandText = "explain select name from test where name='Hello' order by name";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            string plan = rs.GetString(0);
            AssertContains(plan, "tableScan");
        }

        Execute(stat, "drop table test");
    }

    private void TestGroupSubquery()
    {
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table t1(id int)");
        Execute(stat, "create table t2(id int)");
        Execute(stat, "insert into t1 values(2), (2), (3)");
        Execute(stat, "insert into t2 values(2), (3)");
        Execute(stat, "create index t1id_index on t1(id)");
        stat.CommandText = "select id, (select count(*) from t2 "
            + "where t2.id = t1.id) cc from t1 group by id order by id";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertEquals(1, rs.GetInt32(1));
            AssertTrue(rs.Read());
            AssertEquals(3, rs.GetInt32(0));
            AssertEquals(1, rs.GetInt32(1));
            AssertFalse(rs.Read());
        }

        Execute(stat, "drop table t1, t2");
    }

    private void TestAnalyzeLob()
    {
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(v varchar, b varbinary, cl clob, bl blob) as "
            + "select ' ', '00', ' ', '00' from system_range(1, 100)");
        Execute(stat, "analyze");
        stat.CommandText = "select column_name, selectivity "
            + "from information_schema.columns where table_name='TEST'";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("V", rs.GetString(0));
            AssertEquals(1, rs.GetInt32(1));
            AssertTrue(rs.Read());
            AssertEquals("B", rs.GetString(0));
            AssertEquals(1, rs.GetInt32(1));
            AssertTrue(rs.Read());
            AssertEquals("CL", rs.GetString(0));
            AssertEquals(50, rs.GetInt32(1));
            AssertTrue(rs.Read());
            AssertEquals("BL", rs.GetString(0));
            AssertEquals(50, rs.GetInt32(1));
        }

        Execute(stat, "drop table test");
    }

    private void TestLike()
    {
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(name varchar primary key) as "
            + "select x from system_range(1, 10)");
        stat.CommandText = "explain select * from test "
            + "where name like ? || '%' {1: 'Hello'}";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            // ensure the ID = 10 part is evaluated first
            AssertContains(rs.GetString(0), "PRIMARY_KEY_");
        }

        Execute(stat, "drop table test");
    }

    private void TestExistsSubquery()
    {
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id int) as select x from system_range(1, 10)");
        stat.CommandText = "explain select * from test "
            + "where exists(select 1 from test, test, test) and id = 10";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            // ensure the ID = 10 part is evaluated first
            AssertContains(rs.GetString(0), "WHERE (\"ID\" = 10)");
        }

        Execute(stat, "drop table test");
    }

    private void TestQueryCacheConcurrentUse()
    {
        if (Config.Lazy)
        {
            return;
        }

        DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id int primary key, data clob)");
        Execute(stat, "insert into test values(0, space(10000))");
        Execute(stat, "insert into test values(1, space(10001))");
        using CancellationTokenSource stopped = new();
        Task[] tasks = new Task[2];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                using DbConnection taskConnection = GetConnection("optimizations");
                using DbCommand prep = taskConnection.CreateCommand();
                prep.CommandText = "select * from test where id = ?";
                prep.Parameters.Add(new H2Parameter());
                while (!stopped.IsCancellationRequested)
                {
                    int x = Random.Shared.Next(2);
                    prep.Parameters[0].Value = x;
                    using (DbDataReader rs = prep.ExecuteReader())
                    {
                        AssertTrue(rs.Read());
                        string data = rs.GetString(1);
                        if (data.Length != 10000 + x)
                        {
                            throw new Exception(data.Length + " != " + x);
                        }
                    }
                }
            }, stopped.Token);
        }

        Thread.Sleep(1000);
        stopped.Cancel();
        Task.WaitAll(tasks);
        Execute(stat, "drop table test");
        conn.Close();
    }

    private void TestQueryCacheResetParams()
    {
        using DbConnection conn = GetConnection("optimizations");
        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "select ?";
            prep.Parameters.Add(new H2Parameter { Value = "Hello" });
            _ = prep.ExecuteNonQuery();
        }

        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "select ?";
            AssertThrows(ErrorCode.ParameterNotSet1,
                () => _ = prep.ExecuteScalar());
        }
    }

    private void TestRowId()
    {
        if (Config.Memory)
        {
            return;
        }

        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "create table test(data varchar)");
        Execute(stat, "select min(_rowid_ + 1) from test");
        Execute(stat, "insert into test(_rowid_, data) values(10, 'Hello')");
        Execute(stat, "insert into test(data) values('World')");
        Execute(stat, "insert into test(_rowid_, data) values(20, 'Hello')");
        Execute(stat, "merge into test using (values(20, 'Hallo')) s(id, data) on test._rowid_ = s.id"
            + " when matched then update set data = s.data");
        stat.CommandText = "select _rowid_, data from test order by _rowid_";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(10, rs.GetInt32(0));
            AssertEquals("Hello", rs.GetString(1));
            AssertTrue(rs.Read());
            AssertEquals(11, rs.GetInt32(0));
            AssertEquals("World", rs.GetString(1));
            AssertTrue(rs.Read());
            AssertEquals(20, rs.GetInt32(0));
            AssertEquals("Hallo", rs.GetString(1));
            AssertFalse(rs.Read());
        }

        Execute(stat, "drop table test");

        Execute(stat, "create table test(id int primary key, name varchar)");
        Execute(stat, "insert into test values(0, 'Hello')");
        Execute(stat, "insert into test values(3, 'Hello')");
        Execute(stat, "insert into test values(2, 'Hello')");

        stat.CommandText = "explain select * from test where _rowid_ = 2";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertContains(rs.GetString(0), ".tableScan: _ROWID_ =");
        }

        stat.CommandText = "explain select * from test where _rowid_ > 2";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertContains(rs.GetString(0), ".tableScan: _ROWID_ >");
        }

        stat.CommandText = "explain select * from test order by _rowid_";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertContains(rs.GetString(0), "/* index sorted */");
        }

        stat.CommandText = "select _rowid_, * from test order by _rowid_";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(0, rs.GetInt32(0));
            AssertEquals(0, rs.GetInt32(1));
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertEquals(2, rs.GetInt32(1));
            AssertTrue(rs.Read());
            AssertEquals(3, rs.GetInt32(0));
            AssertEquals(3, rs.GetInt32(1));
        }

        Execute(stat, "drop table test");
    }

    private void TestSortIndex()
    {
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "drop table test if exists");
        Execute(stat, "create table test(id int)");
        Execute(stat, "create index idx_id_asc on test(id)");

        stat.CommandText = "explain select * from test "
            + "where id > 10 order by id";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertContains(rs.GetString(0), "IDX_ID_ASC");
        }

        stat.CommandText = "explain select * from test "
            + "where id < 10 order by id desc";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertContains(rs.GetString(0), "IDX_ID_ASC");
        }

        Execute(stat, "drop table test");
    }

    private void TestAutoAnalyze()
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        stat.CommandText = "SELECT SETTING_VALUE FROM INFORMATION_SCHEMA.SETTINGS WHERE SETTING_NAME = 'analyzeAuto'";
        int auto;
        using (DbDataReader rs = stat.ExecuteReader())
        {
            auto = rs.Read() ? rs.GetInt32(0) : 0;
        }

        if (auto != 0)
        {
            Execute(stat, "create table test(id int)");
            Execute(stat, "create user onlyInsert password ''");
            Execute(stat, "grant insert on test to onlyInsert");
            using DbConnection conn2 = GetConnection(
                "optimizations", "onlyInsert", GetPassword(""));
            DbCommand stat2 = conn2.CreateCommand();
            Execute(stat2, "insert into test select x "
                + "from system_range(1, " + (auto + 10) + ")");
        }
    }

    private void TestInAndBetween()
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id int, name varchar)");
        Execute(stat, "create index idx_name on test(id, name)");
        Execute(stat, "insert into test values(1, 'Hello'), (2, 'World')");
        stat.CommandText = "select * from test "
            + "where id between 1 and 3 and name in ('World')";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
        }

        stat.CommandText = "select * from test "
            + "where id between 1 and 3 and name in (select 'World')";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
        }

        Execute(stat, "drop table test");
    }

    private void TestNestedIn()
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "create table accounts(id integer primary key, "
            + "status varchar(255), tag varchar(255))");
        Execute(stat, "insert into accounts values (31, 'X', 'A')");
        Execute(stat, "create table parent(id int)");
        Execute(stat, "insert into parent values(31)");
        Execute(stat, "create view test_view as select a.status, a.tag "
            + "from accounts a, parent t where a.id = t.id");
        stat.CommandText = "select * from test_view "
            + "where status='X' and tag in ('A','B')";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
        }

        stat.CommandText = "select * from (select a.status, a.tag "
            + "from accounts a, parent t where a.id = t.id) x "
            + "where status='X' and tag in ('A','B')";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
        }

        Execute(stat, "create table test(id int primary key, name varchar(255))");
        Execute(stat, "create unique index idx_name on test(name, id)");
        Execute(stat, "insert into test values(1, 'Hello'), (2, 'World')");
        stat.CommandText = "select * from (select * from test) "
            + "where id=1 and name in('Hello', 'World')";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
        }

        Execute(stat, "drop table test");
    }

    private void TestConstantIn1()
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "create table test(id int primary key, name varchar(255))");
        Execute(stat, "insert into test values(1, 'Hello'), (2, 'World')");
        AssertSingleValue(stat,
            "select count(*) from test where name in ('Hello', 'World', '1')", 2);
        AssertSingleValue(stat,
            "select count(*) from test where name in ('Hello', 'World')", 2);
        AssertSingleValue(stat,
            "select count(*) from test where name in ('Hello', 'Not')", 1);
        Execute(stat, "drop table test");
    }

    private void TestConstantIn2()
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        // The port applies IGNORECASE through SET; the URL setting is not wired into open yet.
        Execute(stat, "SET IGNORECASE TRUE");

        Execute(stat, "CREATE TABLE testValues (x VARCHAR(50))");
        Execute(stat, "INSERT INTO testValues (x) SELECT 'foo' x");
        stat.CommandText = "SELECT x FROM testValues WHERE x IN ('foo')";
        using (DbDataReader resultSet = stat.ExecuteReader())
        {
            AssertTrue(resultSet.Read());
        }

        stat.CommandText = "SELECT x FROM testValues WHERE x IN ('FOO')";
        using (DbDataReader resultSet = stat.ExecuteReader())
        {
            AssertTrue(resultSet.Read());
        }

        stat.CommandText = "SELECT x FROM testValues WHERE x IN ('foo','bar')";
        using (DbDataReader resultSet = stat.ExecuteReader())
        {
            AssertTrue(resultSet.Read());
        }

        stat.CommandText = "SELECT x FROM testValues WHERE x IN ('FOO','bar')";
        using (DbDataReader resultSet = stat.ExecuteReader())
        {
            AssertTrue(resultSet.Read());
        }
    }

    private void TestConstantTypeConversionToColumnType()
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations;IGNORECASE=TRUE");
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "CREATE TABLE test (x int)");
        stat.CommandText = "EXPLAIN SELECT x FROM test WHERE x = '5'";
        using (DbDataReader resultSet = stat.ExecuteReader())
        {
            AssertTrue(resultSet.Read());
            // String constant '5' has been converted to int constant 5 on
            // optimization
            AssertTrue(resultSet.GetString(0).EndsWith("\"X\" = 5", StringComparison.Ordinal));
        }

        Execute(stat, "drop table test");
    }

    private void TestNestedInSelect()
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "create table test(id int primary key, name varchar) "
            + "as select 1, 'Hello'");
        Execute(stat, "select * from (select * from test) "
            + "where id=1 and name in('Hello', 'World')");

        Execute(stat, "drop table test");

        Execute(stat, "create table test(id int, name varchar) as select 1, 'Hello'");
        Execute(stat, "create index idx2 on test(id, name)");
        stat.CommandText = "select count(*) from test "
            + "where id=1 and name in('Hello', 'x')";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
        }
    }

    private void TestNestedInSelectAndLike()
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "create table test(id int primary key)");
        Execute(stat, "insert into test values(2)");
        stat.CommandText = "select * from test where id in(1, 2)";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        Execute(stat, "create table test2(id int primary key hash)");
        Execute(stat, "insert into test2 values(2)");
        stat.CommandText = "select * from test where id in(1, 2)";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "SELECT * FROM SYSTEM_RANGE(1, 1) A "
                + "WHERE A.X IN (SELECT B.X FROM SYSTEM_RANGE(1, 1) B WHERE B.X LIKE ?)";
            prep.Parameters.Add(new H2Parameter { Value = "1" });
            _ = prep.ExecuteNonQuery();
            prep.Parameters.Clear();
            _ = prep.Parameters.Add(new H2Parameter { Value = 1 });
            _ = prep.Parameters.Add(new H2Parameter { Value = 1 });
            prep.CommandText = "SELECT * FROM SYSTEM_RANGE(1, 1) A "
                + "WHERE A.X IN (SELECT B.X FROM SYSTEM_RANGE(1, 1) B WHERE B.X IN (?, ?))";
            using (DbDataReader rs = prep.ExecuteReader())
            {
                while (rs.Read())
                {
                    // just loop
                }
            }
        }
    }

    private void TestInSelectJoin()
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(a int, b int, c int, d int) "
            + "as select 1, 1, 1, 1 from dual;");
        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "SELECT 2 FROM TEST A "
                + "INNER JOIN (SELECT DISTINCT B.C AS X FROM TEST B "
                + "WHERE B.D = ?2) V ON 1=1 WHERE (A = ?1) AND (B = V.X)";
            prep.Parameters.Add(new H2Parameter { Value = 1 });
            prep.Parameters.Add(new H2Parameter { Value = 1 });
            using (DbDataReader rs = prep.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertFalse(rs.Read());
            }

            prep.Parameters.Clear();
            _ = prep.Parameters.Add(new H2Parameter { Value = 1 });
            _ = prep.Parameters.Add(new H2Parameter { Value = 1 });
            prep.CommandText = "select 2 from test a where a=? and b in("
                + "select b.c from test b where b.d=?)";
            using (DbDataReader rs = prep.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertFalse(rs.Read());
            }
        }
    }

    private void TestOptimizeInJoin()
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "create table test(id int primary key)");
        Execute(stat, "insert into test select x from system_range(1, 1000)");
        stat.CommandText = "explain select * "
            + "from test where id in (400, 300)";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            string plan = rs.GetString(0);
            if (plan.IndexOf("/* PUBLIC.PRIMARY_KEY_", StringComparison.Ordinal) < 0)
            {
                Fail("Expected using the primary key, got: " + plan);
            }
        }
    }

    private void TestMinMaxNullOptimization()
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        Random random = new(1);
        int len = GetSize(50, 500);
        for (int i = 0; i < len; i++)
        {
            Execute(stat, "drop table if exists test");
            Execute(stat, "create table test(x int)");
            if (random.Next(2) == 0)
            {
                int count = random.Next(2) == 0 ? 1 : 1 + random.Next(len);
                if (count > 0)
                {
                    Execute(stat, "insert into test select null "
                        + "from system_range(1, " + count + ")");
                }
            }

            int maxExpected = -1;
            int minExpected = -1;
            if (random.Next(10) != 1)
            {
                minExpected = 1;
                maxExpected = 1 + random.Next(len);
                Execute(stat, "insert into test select x "
                    + "from system_range(1, " + maxExpected + ")");
            }

            string sql = "create index idx on test(x";
            if (random.Next(2) == 0)
            {
                sql += " desc";
            }

            if (random.Next(2) == 0)
            {
                if (random.Next(2) == 0)
                {
                    sql += " nulls first";
                }
                else
                {
                    sql += " nulls last";
                }
            }

            sql += ")";
            Execute(stat, sql);
            stat.CommandText = "explain select min(x), max(x) from test";
            using (_ = stat.ExecuteReader())
            {
            }

            stat.CommandText = "select min(x), max(x) from test";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertTrue(rs.Read());
                int min = rs.IsDBNull(0) ? -1 : rs.GetInt32(0);
                int max = rs.IsDBNull(1) ? -1 : rs.GetInt32(1);
                AssertEquals("i=" + i + " " + sql, minExpected, min);
                AssertEquals("i=" + i + " " + sql, maxExpected, max);
            }
        }
    }

    private void TestMultiColumnRangeQuery()
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE Logs(id INT PRIMARY KEY, type INT)");
        Execute(stat, "CREATE unique INDEX type_index ON Logs(type, id)");
        Execute(stat, "INSERT INTO Logs SELECT X, MOD(X, 3) "
            + "FROM SYSTEM_RANGE(1, 1000)");
        Execute(stat, "ANALYZE SAMPLE_SIZE 0");
        stat.CommandText = "EXPLAIN SELECT id FROM Logs "
            + "WHERE id < 100 and type=2 AND id<100";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            string plan = rs.GetString(0);
            AssertContains(plan, "TYPE_INDEX");
        }
    }

    private void TestDistinctOptimization()
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY, "
            + "NAME VARCHAR, TYPE INT)");
        Execute(stat, "CREATE INDEX IDX_TEST_TYPE ON TEST(TYPE)");
        Random random = new(1);
        int len = GetSize(10000, 100000);
        int[] groupCount = new int[10];
        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "INSERT INTO TEST VALUES(?, ?, ?)";
            prep.Parameters.Add(new H2Parameter());
            prep.Parameters.Add(new H2Parameter());
            prep.Parameters.Add(new H2Parameter());
            for (int i = 0; i < len; i++)
            {
                prep.Parameters[0].Value = i;
                prep.Parameters[1].Value = "Hello World";
                int type = random.Next(10);
                groupCount[type]++;
                prep.Parameters[2].Value = type;
                _ = prep.ExecuteNonQuery();
            }
        }

        stat.CommandText = "SELECT TYPE, COUNT(*) FROM TEST "
            + "GROUP BY TYPE ORDER BY TYPE";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            for (int i = 0; rs.Read(); i++)
            {
                AssertEquals(i, rs.GetInt32(0));
                AssertEquals(groupCount[i], rs.GetInt32(1));
            }

            AssertFalse(rs.Read());
        }

        stat.CommandText = "SELECT DISTINCT TYPE FROM TEST ORDER BY TYPE";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            for (int i = 0; rs.Read(); i++)
            {
                AssertEquals(i, rs.GetInt32(0));
            }

            AssertFalse(rs.Read());
        }

        stat.CommandText = "SELECT DISTINCT TYPE FROM TEST";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            for (int i = 0; rs.Read(); i++)
            {
                AssertEquals(i, rs.GetInt32(0));
            }

            AssertFalse(rs.Read());
        }

        Execute(stat, "ANALYZE");
        stat.CommandText = "SELECT DISTINCT TYPE FROM TEST ORDER BY TYPE";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            for (int i = 0; i < 10; i++)
            {
                AssertTrue(rs.Read());
                AssertEquals(i, rs.GetInt32(0));
            }

            AssertFalse(rs.Read());
        }

        stat.CommandText = "SELECT DISTINCT TYPE FROM TEST ORDER BY TYPE LIMIT 5 OFFSET 2";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            for (int i = 2; i < 7; i++)
            {
                AssertTrue(rs.Read());
                AssertEquals(i, rs.GetInt32(0));
            }

            AssertFalse(rs.Read());
        }
    }

    private void TestQueryCacheTimestamp()
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations");
        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "SELECT CURRENT_TIMESTAMP()";
        string a, b;
        using (DbDataReader rs = prep.ExecuteReader())
        {
            AssertTrue(rs.Read());
            a = rs.GetString(0);
        }

        Thread.Sleep(50);
        using (DbDataReader rs = prep.ExecuteReader())
        {
            AssertTrue(rs.Read());
            b = rs.GetString(0);
        }

        AssertFalse(a.Equals(b, StringComparison.Ordinal));
    }

    private void TestQueryCacheSpeed()
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        TestQuerySpeed(stat,
            "select sum(a.n), sum(b.x) from system_range(1, 100) b, "
            + "(select sum(x) n from system_range(1, 4000)) a");
    }

    private void TestQuerySpeed(DbCommand stat, string sql)
    {
        long totalTime = 0;
        long totalTimeOptimized = 0;
        for (int i = 0; i < 3; i++)
        {
            totalTime += MeasureQuerySpeed(stat, sql, false);
            totalTimeOptimized += MeasureQuerySpeed(stat, sql, true);
        }

        if (totalTimeOptimized > totalTime)
        {
            Fail("not optimized: " + (totalTime * 1000 / Stopwatch.Frequency)
                + " optimized: " + (totalTimeOptimized * 1000 / Stopwatch.Frequency)
                + " sql:" + sql);
        }
    }

    private long MeasureQuerySpeed(DbCommand stat, string sql, bool optimized)
    {
        Execute(stat, "set OPTIMIZE_REUSE_RESULTS " + (optimized ? "1" : "0"));
        Execute(stat, sql);
        long time = Stopwatch.GetTimestamp();
        Execute(stat, sql);
        return Stopwatch.GetTimestamp() - time;
    }

    private void TestQueryCache(bool optimize)
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "set OPTIMIZE_REUSE_RESULTS " + (optimize ? 1 : 0));
        Execute(stat, "create table test(id int)");
        Execute(stat, "create table test2(id int)");
        Execute(stat, "insert into test values(1), (1), (2)");
        Execute(stat, "insert into test2 values(1)");
        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "select * from test where id = (select id from test2)";
        using (DbDataReader rs1 = prep.ExecuteReader())
        {
            AssertTrue(rs1.Read());
            AssertEquals(1, rs1.GetInt32(0));
            AssertTrue(rs1.Read());
            AssertEquals(1, rs1.GetInt32(0));
            AssertFalse(rs1.Read());
        }

        Execute(stat, "update test2 set id = 2");
        using (DbDataReader rs2 = prep.ExecuteReader())
        {
            AssertTrue(rs2.Read());
            AssertEquals(2, rs2.GetInt32(0));
        }
    }

    private void TestMinMaxCountOptimization(bool memory)
    {
        DeleteDb("optimizations");
        DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create " + (memory ? "memory" : "")
            + " table test(id int primary key, v int)");
        Execute(stat, "create index idx_v_id on test(v, id);");
        int len = GetSize(1000, 10000);
        Dictionary<int, int?> map = [];
        SortedSet<int> set = new();
        Random random = new(1);
        for (int i = 0; i < len; i++)
        {
            if (i == len / 2)
            {
                if (!Config.Memory)
                {
                    conn.Close();
                    conn = GetConnection("optimizations");
                    stat = conn.CreateCommand();
                }
            }

            switch (random.Next(10))
            {
                case 0:
                case 1:
                case 2:
                case 3:
                case 4:
                case 5:
                    if (random.Next(1000) == 1)
                    {
                        Execute(stat, "insert into test values(" + i + ", null)");
                        map[i] = null;
                    }
                    else
                    {
                        int value = random.Next();
                        Execute(stat, "insert into test values(" + i + ", " + value + ")");
                        map[i] = value;
                        set.Add(value);
                    }

                    break;
                case 6:
                case 7:
                case 8:
                    if (map.Count > 0)
                    {
                        for (int j = random.Next(i), k = 0; k < 10; k++, j++)
                        {
                            if (map.ContainsKey(j))
                            {
                                int? x = map.Remove(j) ? map.GetValueOrDefault(j) : null;
                                if (x is not null)
                                {
                                    set.Remove(x.Value);
                                }

                                Execute(stat, "delete from test where id=" + j);
                            }
                        }
                    }

                    break;
                case 9:
                {
                    List<int?> list = [.. map.Values];
                    int count = list.Count;
                    int? min = null, max = null;
                    if (count > 0)
                    {
                        min = set.Min;
                        max = set.Max;
                    }

                    stat.CommandText = "select min(v), max(v), count(*) from test";
                    using (DbDataReader rs = stat.ExecuteReader())
                    {
                        AssertTrue(rs.Read());
                        int? minDb = rs.IsDBNull(0) ? null : rs.GetInt32(0);
                        int? maxDb = rs.IsDBNull(1) ? null : rs.GetInt32(1);
                        int countDb = rs.GetInt32(2);
                        AssertEquals(minDb, min);
                        AssertEquals(maxDb, max);
                        AssertEquals(countDb, count);
                    }

                    break;
                }

                default:
                    break;
            }
        }

        conn.Close();
    }

    private void TestIn()
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();

        stat.CommandText = "select * from dual where x in()";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertFalse(rs.Read());
        }

        stat.CommandText = "select * from dual where null in(1)";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertFalse(rs.Read());
        }

        stat.CommandText = "select * from dual where null in(null)";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertFalse(rs.Read());
        }

        stat.CommandText = "select * from dual where null in(null, 1)";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertFalse(rs.Read());
        }

        stat.CommandText = "select * from system_range(1, 1) where 1+x in(3, 4)";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertFalse(rs.Read());
        }

        stat.CommandText = "select * from system_range(1, 1) d1, dual d2 where d1.x in(3, 4)";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertFalse(rs.Read());
        }

        Execute(stat, "create table test(id int primary key, name varchar)");
        Execute(stat, "insert into test values(1, 'Hello')");
        Execute(stat, "insert into test values(2, 'World')");

        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "select * from test t1 where t1.id in(?)";
            prep.Parameters.Add(new H2Parameter { Value = 1 });
            using (DbDataReader rs = prep.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals(1, rs.GetInt32(0));
                AssertEquals("Hello", rs.GetString(1));
                AssertFalse(rs.Read());
            }

            prep.Parameters.Clear();
            _ = prep.Parameters.Add(new H2Parameter { Value = 1 });
            _ = prep.Parameters.Add(new H2Parameter { Value = 2 });
            prep.CommandText = "select * from test t1 where t1.id in(?, ?) order by id";
            using (DbDataReader rs = prep.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals(1, rs.GetInt32(0));
                AssertEquals("Hello", rs.GetString(1));
                AssertTrue(rs.Read());
                AssertEquals(2, rs.GetInt32(0));
                AssertEquals("World", rs.GetString(1));
                AssertFalse(rs.Read());
            }

            prep.Parameters.Clear();
            _ = prep.Parameters.Add(new H2Parameter { Value = 2 });
            prep.CommandText = "select * from test t1 where t1.id "
                + "in(select t2.id from test t2 where t2.id=?)";
            using (DbDataReader rs = prep.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals(2, rs.GetInt32(0));
                AssertEquals("World", rs.GetString(1));
                AssertFalse(rs.Read());
            }

            prep.CommandText = "select * from test t1 where t1.id "
                + "in(select t2.id from test t2 where t2.id=? and t1.id<>t2.id)";
            using (DbDataReader rs = prep.ExecuteReader())
            {
                AssertFalse(rs.Read());
            }

            prep.CommandText = "select * from test t1 where t1.id "
                + "in(select t2.id from test t2 where t2.id in(cast(?+10 as varchar)))";
            using (DbDataReader rs = prep.ExecuteReader())
            {
                AssertFalse(rs.Read());
            }
        }
    }

    /// <summary>
    /// Where there are multiple indices, and we have an ORDER BY, select the
    /// index that already has the required ordering.
    /// </summary>
    private void TestOrderedIndexes()
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE my_table(K1 INT, K2 INT, "
            + "VAL VARCHAR, PRIMARY KEY(K1, K2))");
        Execute(stat, "CREATE INDEX my_index ON my_table(K1, VAL)");
        stat.CommandText = "EXPLAIN PLAN FOR SELECT * FROM my_table WHERE K1=7 "
            + "ORDER BY K1, VAL";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertContains(rs.GetString(0), "/* PUBLIC.MY_INDEX: K1 = 7 */");
        }

        Execute(stat, "DROP TABLE my_table");

        // where we have two covering indexes, make sure
        // we choose the one that covers more
        Execute(stat, "CREATE TABLE my_table(K1 INT, K2 INT, VAL VARCHAR)");
        Execute(stat, "CREATE INDEX my_index1 ON my_table(K1, K2)");
        Execute(stat, "CREATE INDEX my_index2 ON my_table(K1, K2, VAL)");
        stat.CommandText = "EXPLAIN PLAN FOR SELECT * FROM my_table WHERE K1=7 "
            + "ORDER BY K1, K2, VAL";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertContains(rs.GetString(0), "/* PUBLIC.MY_INDEX2: K1 = 7 */");
        }
    }

    private void TestIndexUseDespiteNullsFirst()
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "CREATE TABLE my_table(K1 INT)");
        Execute(stat, "CREATE INDEX my_index ON my_table(K1)");
        Execute(stat, "INSERT INTO my_table VALUES (NULL)");
        Execute(stat, "INSERT INTO my_table VALUES (1)");
        Execute(stat, "INSERT INTO my_table VALUES (2)");

        stat.CommandText = "EXPLAIN PLAN FOR SELECT * FROM my_table "
            + "ORDER BY K1 ASC NULLS FIRST";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertContains(rs.GetString(0), "/* index sorted */");
        }

        stat.CommandText = "SELECT * FROM my_table ORDER BY K1 ASC NULLS FIRST";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertNull(rs.IsDBNull(0) ? null : rs.GetValue(0));
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
        }

        // ===
        stat.CommandText = "EXPLAIN PLAN FOR SELECT * FROM my_table "
            + "ORDER BY K1 DESC NULLS FIRST";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            string result = rs.GetString(0);
            if (result.Contains("/* index sorted */", StringComparison.Ordinal))
            {
                Fail(result + " does not contain: /* index sorted */");
            }
        }

        stat.CommandText = "SELECT * FROM my_table ORDER BY K1 DESC NULLS FIRST";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertNull(rs.IsDBNull(0) ? null : rs.GetValue(0));
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
        }

        // ===
        stat.CommandText = "EXPLAIN PLAN FOR SELECT * FROM my_table "
            + "ORDER BY K1 ASC NULLS LAST";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            string result = rs.GetString(0);
            if (result.Contains("/* index sorted */", StringComparison.Ordinal))
            {
                Fail(result + " does not contain: /* index sorted */");
            }
        }

        stat.CommandText = "SELECT * FROM my_table ORDER BY K1 ASC NULLS LAST";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertTrue(rs.Read());
            AssertNull(rs.IsDBNull(0) ? null : rs.GetValue(0));
        }

        // TODO: Test "EXPLAIN PLAN FOR SELECT * FROM my_table ORDER BY K1 DESC NULLS FIRST"
        // Currently fails, as using the index when sorting DESC is currently not supported.

        Execute(stat, "DROP TABLE my_table");
    }

    private void TestConvertOrToIn()
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "create table test(id int primary key, name varchar(255))");
        Execute(stat, "insert into test values"
            + "(1, '1'), (2, '2'), (3, '3'), (4, '4'), (5, '5')");

        stat.CommandText = "EXPLAIN PLAN FOR SELECT * "
            + "FROM test WHERE ID=1 OR ID=2 OR ID=3 OR ID=4 OR ID=5";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertContains(rs.GetString(0), "\"ID\" IN(1, 2, 3, 4, 5)");
        }

        stat.CommandText = "SELECT COUNT(*) FROM test "
            + "WHERE ID=1 OR ID=2 OR ID=3 OR ID=4 OR ID=5";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(5, rs.GetInt32(0));
        }
    }

    private void TestUseCoveringIndex()
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TABLE_A(id IDENTITY PRIMARY KEY NOT NULL, "
            + "name VARCHAR NOT NULL, active BOOLEAN DEFAULT TRUE, "
            + "CONSTRAINT TABLE_A_UK UNIQUE (name) )");
        Execute(stat, "CREATE TABLE TABLE_B(id IDENTITY PRIMARY KEY NOT NULL,  "
            + "TABLE_a_id BIGINT NOT NULL,  createDate TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, "
            + "CONSTRAINT TABLE_B_UK UNIQUE (table_a_id, createDate))");
        Execute(stat, "CREATE INDEX TABLE_B_IDX ON TABLE_B(TABLE_A_ID)");
        Execute(stat, "ALTER TABLE TABLE_B ADD FOREIGN KEY (table_a_id) REFERENCES TABLE_A(id)");
        Execute(stat, "INSERT INTO TABLE_A (name)  SELECT 'package_' || CAST(X as VARCHAR) "
            + "FROM SYSTEM_RANGE(1, 100)  WHERE X <= 100");
        int count = Config.Memory ? 30_000 : 50_000;
        Execute(stat, "INSERT INTO TABLE_B (table_a_id, createDate)  SELECT "
            + "CASE WHEN table_a_id = 0 THEN 1 ELSE table_a_id END, createDate "
            + "FROM ( SELECT ROUND((RAND() * 100)) AS table_a_id, "
            + "DATEADD('SECOND', X, CURRENT_TIMESTAMP) as createDate FROM SYSTEM_RANGE(1, " + count + ") "
            + "WHERE X < " + count + "  )");
        Execute(stat, "ANALYZE");

        stat.CommandText = "EXPLAIN ANALYZE SELECT MAX(b.id) as id "
            + "FROM table_b b JOIN table_a a ON b.table_a_id = a.id GROUP BY b.table_a_id "
            + "HAVING A.ACTIVE = TRUE";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertContains(rs.GetString(0), "/* PUBLIC.TABLE_B_IDX: TABLE_A_ID = A.ID */");
        }

        stat.CommandText = "EXPLAIN ANALYZE SELECT MAX(id) FROM table_b GROUP BY table_a_id";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertContains(rs.GetString(0), "/* PUBLIC.TABLE_B_IDX");
        }
    }

    private void TestInPredicate()
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE Q_T(A INT) AS VALUES 1, 3, 4, 2");
        Execute(stat, "CREATE TABLE Q_T_A(A INT, PRIMARY KEY(A ASC)) AS VALUES 1, 3, 4, 2");
        Execute(stat, "CREATE TABLE Q_T_D(A INT, PRIMARY KEY(A DESC)) AS VALUES 1, 3, 4, 2");
        Execute(stat, "CREATE TABLE V_T(V INT) AS VALUES 2, 1, 5, 4");
        Execute(stat, "CREATE TABLE V_T_A(V INT, PRIMARY KEY(V ASC)) AS VALUES 2, 1, 5, 4");
        Execute(stat, "CREATE TABLE V_T_D(V INT, PRIMARY KEY(V DESC)) AS VALUES 2, 1, 5, 4");
        for (int q = 1; q <= 3; q++)
        {
            for (int qOrder = 1; qOrder <= 3; qOrder++)
            {
                TestInPredicate(conn, stat, q, qOrder, 0, 1);
                for (int v = 1; v <= 3; v++)
                {
                    for (int vOrder = 1; vOrder <= 3; vOrder++)
                    {
                        TestInPredicate(conn, stat, q, qOrder, v, vOrder);
                    }
                }
            }
        }
    }

    private void TestInPredicate(DbConnection conn, DbCommand stat, int q, int qOrder,
        int v, int vOrder)
    {
        StringBuilder builder = new();
        _ = builder.Append("SELECT * FROM ");
        switch (q)
        {
            case 1:
                _ = builder.Append("Q_T");
                break;
            case 2:
                _ = builder.Append("Q_T_A");
                break;
            case 3:
                _ = builder.Append("Q_T_D");
                break;
            default:
                Fail();
                break;
        }

        _ = builder.Append(" WHERE A IN (");
        switch (v)
        {
            case 0:
                _ = builder.Append("2, 1, 5, 4");
                break;
            case 1:
            case 2:
            case 3:
                _ = builder.Append("SELECT * FROM V_T");
                break;
            default:
                Fail();
                break;
        }

        switch (vOrder)
        {
            case 1:
                break;
            case 2:
                if (v == 0)
                {
                    Fail();
                }

                _ = builder.Append(" ORDER BY V ASC");
                break;
            case 3:
                if (v == 0)
                {
                    Fail();
                }

                _ = builder.Append(" ORDER BY V DESC");
                break;
            default:
                Fail();
                break;
        }

        _ = builder.Append(')');
        switch (qOrder)
        {
            case 1:
                break;
            case 2:
                _ = builder.Append(" ORDER BY A ASC");
                break;
            case 3:
                _ = builder.Append(" ORDER BY A DESC");
                break;
            default:
                Fail();
                break;
        }

        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = builder.ToString();
            using (DbDataReader rs = prep.ExecuteReader())
            {
                switch (qOrder)
                {
                    case 1:
                    {
                        HashSet<int> set = [];
                        for (int i = 0; i < 3; i++)
                        {
                            AssertTrue(rs.Read());
                            set.Add(rs.GetInt32(0));
                        }

                        AssertFalse(rs.Read());
                        AssertTrue(set.Contains(1));
                        AssertTrue(set.Contains(2));
                        AssertTrue(set.Contains(4));
                        break;
                    }

                    case 2:
                        AssertTrue(rs.Read());
                        AssertEquals(1, rs.GetInt32(0));
                        AssertTrue(rs.Read());
                        AssertEquals(2, rs.GetInt32(0));
                        AssertTrue(rs.Read());
                        AssertEquals(4, rs.GetInt32(0));
                        AssertFalse(rs.Read());
                        break;
                    case 3:
                        AssertTrue(rs.Read());
                        AssertEquals(4, rs.GetInt32(0));
                        AssertTrue(rs.Read());
                        AssertEquals(2, rs.GetInt32(0));
                        AssertTrue(rs.Read());
                        AssertEquals(1, rs.GetInt32(0));
                        AssertFalse(rs.Read());
                        break;
                }
            }
        }

        _ = builder.Insert(0, "EXPLAIN ");
        stat.CommandText = builder.ToString();
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            string plan = rs.GetString(0);
            bool expectedQuerySorted = q > 1 && qOrder > 1;
            bool querySorted = plan.EndsWith("/* index sorted */", StringComparison.Ordinal);
            AssertEquals(expectedQuerySorted, querySorted);
        }
    }

    private void TestConditionAndOrDistributiveLaw()
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE IF NOT EXISTS TABLE_A ("
            + "id int NOT NULL AUTO_INCREMENT, "
            + "name VARCHAR(30) NOT NULL,"
            + "occupation VARCHAR(20),"
            + "age int,"
            + "salary int,"
            + "PRIMARY KEY(id))");
        Execute(stat, "INSERT INTO TABLE_A (name,occupation,age,salary) VALUES"
            + "('mark', 'doctor',25,5000),"
            + "('kevin', 'artist',20,4000),"
            + "('isuru', 'engineer',25,5000),"
            + "('josaph', 'businessman',30,7000),"
            + "('sajeewa', 'analyst',24,5000),"
            + "('randil', 'engineer',25,5000),"
            + "('ashan', 'developer',24,5000)");
        stat.CommandText = "SELECT * FROM TABLE_A WHERE (salary = 5000 AND name = 'isuru') OR"
            + "(age = 25 AND name = 'isuru') ";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertTrue("engineer".Equals(rs.GetString(rs.GetOrdinal("occupation")), StringComparison.Ordinal));
        }
    }

    private void TestConditionsStackOverflow()
    {
        DeleteDb("optimizations");
        using DbConnection conn = GetConnection("optimizations");
        DbCommand stat = conn.CreateCommand();
        StringBuilder b = new("SELECT 1");
        for (int i = 0; i < 10000; i++)
        {
            _ = b.Append(" AND 1");
        }

        stat.CommandText = b.ToString();
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertTrue(rs.GetBoolean(0));
        }
    }
}
