// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestCases.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; setAutoCommit(false) /
// commit() / rollback() are the explicit transaction wrappers; ResultSetMetaData
// precision/scale are the reader's column schema.
//
// Modified (deferred, see MIGRATION_STATUS.md):
//  - testAutoCommitInDatabaseURL: the JDBC autocommit flag has no ADO.NET surface.
//  - testExecuteTrace: the {1: 'Hello'} literal-parameter syntax is driver-specific.
//  - testMutableObjects: java.sql.Date/Time/Timestamp identity checks have no ADO.NET
//    mapping (the reader answers DateTime values).
//  - testDoubleRecovery: needs the power-off seam (crash); deferred with TestPowerOff.
//  - testUpperCaseLowerCaseDatabase: requires the platform separator to be '\'
//    (the reference gates on File.separatorChar), so it stays behind that gate.
// The reference's main() entry is the runner; xUnit runs Test() directly.
using H2Sharp.Api;
using H2Sharp.Store.FileSystem;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Various test cases.
/// </summary>
public sealed class TestCases : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestMinimalCoveringIndexPlan();
        TestMinMaxDirectLookupIndex();
        TestReferenceLaterTable();
        // testAutoCommitInDatabaseURL: deferred, see the header.
        TestReferenceableIndexUsage();
        TestClearSyntaxException();
        TestEmptyStatements();
        TestViewParameters();
        TestLargeKeys();
        TestExtraSemicolonInDatabaseURL();
        TestGroupSubquery();
        TestCountDistinctNotNull();
        TestDependencies();
        TestConvertType();
        TestSortedSelect();
        TestMaxMemoryRows();
        TestLikeExpressions();
        TestUnicode();
        TestOuterJoin();
        TestCommentOnColumnWithSchemaEqualDatabase();
        TestColumnWithConstraintAndComment();
        TestTruncateConstraintsDisabled();
        TestPreparedSubquery2();
        TestPreparedSubquery();
        TestCompareDoubleWithIntColumn();
        TestDeleteIndexOutOfBounds();
        TestInsertDeleteRollback();
        TestLargeRollback();
        TestConstraintAlterTable();
        TestJoinWithView();
        TestLobDecrypt();
        TestInvalidDatabaseName();
        TestReuseSpace();
        TestDeleteGroup();
        TestDisconnect();
        // testExecuteTrace: deferred, see the header.
        TestExplain();
        TestExplainAnalyze();
        TestDataChangeDeltaTable();
        TestGroupSortedReset();
        TestShowColumns();
        if (Config.Memory)
        {
            return;
        }

        TestCheckConstraintWithFunction();
        TestDeleteAndDropTableWithLobs(true);
        TestDeleteAndDropTableWithLobs(false);
        TestEmptyBtreeIndex();
        TestReservedKeywordReconnect();
        TestSpecialSQL();
        TestUpperCaseLowerCaseDatabase();
        TestManualCommitSet();
        TestSchemaIdentityReconnect();
        TestAlterTableReconnect();
        TestPersistentSettings();
        TestInsertSelectUnion();
        TestViewReconnect();
        TestDefaultQueryReconnect();
        TestBigString();
        TestRenameReconnect();
        TestCreateDrop();
        TestPolePos();
        TestQuick();
        // testMutableObjects: deferred, see the header.
        TestSelectForUpdate();
        // testDoubleRecovery: deferred, see the header (power-off seam).
        TestConstraintReconnect();
        TestCollation();
        DeleteDb("cases");
    }

    private void TestReferenceLaterTable()
    {
        DeleteDb("cases");
        DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table a(id int)");
        Execute(stat, "create table b(id int)");
        Execute(stat, "drop table a");
        AssertThrows(ErrorCode.FeatureNotSupported1, stat,
            "create table a(id int check id < select max(id) from b)");
        Execute(stat, "drop table b");
        Execute(stat, "create table b(id int)");
        Execute(stat, "create table a(id int check id < select max(id) from b)");
        conn.Close();
        conn = GetConnection("cases");
        conn.Close();
    }

    private void TestReferenceableIndexUsage()
    {
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "drop table if exists a, b");
        Execute(stat, "create table a(id int, x int) as select 1, 100");
        Execute(stat, "create index idx1 on a(id, x)");
        Execute(stat, "alter table a add unique(id)");
        Execute(stat, "create table b(id int primary key, a_id int) as select 1, 1");
        Execute(stat, "alter table b add constraint x "
            + "foreign key(a_id) references a(id)");
        Execute(stat, "update a set x=200");
        Execute(stat, "drop table if exists a, b cascade");
    }

    private void TestClearSyntaxException()
    {
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        AssertThrows(42000, stat, "select t.x, t.x t.y from dual t");
    }

    private void TestEmptyStatements()
    {
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;");
        Execute(stat, "");
        Execute(stat, ";");
        Execute(stat, " ;");
    }

    private void TestViewParameters()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id int, name varchar)");
        Execute(stat, "create view test_v as select * from test where id = ?");
        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "select * from test_v";
        prep.Parameters.Add(new H2Parameter { Value = 1 });
        using (DbDataReader reader = prep.ExecuteReader())
        {
        }
    }

    private void TestLargeKeys()
    {
        if (Config.Memory)
        {
            return;
        }

        DeleteDb("cases");
        DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id int primary key, name varchar)");
        Execute(stat, "create index on test(name)");
        Execute(stat, "insert into test values(1, '1' || space(1500))");
        conn.Close();
        conn = GetConnection("cases");
        stat = conn.CreateCommand();
        Execute(stat, "insert into test values(2, '2' || space(1500))");
        conn.Close();
        conn = GetConnection("cases");
        stat = conn.CreateCommand();
        Execute(stat, "select name from test order by name");
        conn.Close();
    }

    private void TestExtraSemicolonInDatabaseURL()
    {
        DeleteDb("cases");
        DbConnection conn = GetConnection("cases;");
        conn.Close();
        conn = GetConnection("cases;;mode=mysql;");
        conn.Close();
    }

    private void TestGroupSubquery()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(a int, b int)");
        Execute(stat, "create index idx on test(a)");
        Execute(stat, "insert into test values (1, 9), (2, 9), (3, 9)");
        stat.CommandText = "select (select count(*)"
            + " from test where a = t.a and b = 0) from test t group by a";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(0, rs.GetInt32(0));
        }
    }

    private void TestCountDistinctNotNull()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id int not null) as "
            + "select 1 from system_range(1, 10)");
        stat.CommandText = "select count(distinct id) from test";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
        }
    }

    private void TestDependencies()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();

        // avoid endless recursion when adding dependencies
        Execute(stat, "create table test(id int primary key, parent int)");
        Execute(stat, "alter table test add constraint test check "
            + "(select count(*) from test) < 10");
        Execute(stat, "create table b()");
        Execute(stat, "drop table b");
        Execute(stat, "drop table test");

        // ensure the dependency is detected
        Execute(stat, "create alias is_positive as "
            + "'boolean isPositive(int x) { return x > 0; }'");
        Execute(stat, "create table a(a integer, constraint test check is_positive(a))");
        AssertThrows(ErrorCode.CannotDrop2, stat, "drop alias is_positive");
        Execute(stat, "drop table a");
        Execute(stat, "drop alias is_positive");

        // ensure trying to reference the table fails
        // (otherwise re-opening the database is not possible)
        Execute(stat, "create table test(id int primary key)");
        AssertThrows(ErrorCode.ColumnIsReferenced1, stat,
            "alter table test alter column id "
            + "set default ifnull((select max(id) from test)+1, 0)");
        Execute(stat, "drop table test");
    }

    private void TestConvertType()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test as select cast(0 as dec(10, 2)) x");
        stat.CommandText = "select * from test";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            DbColumn column = rs.GetColumnSchema()[0];
            AssertEquals(10, column.NumericPrecision.GetValueOrDefault());
            AssertEquals(2, column.NumericScale.GetValueOrDefault());
        }

        Execute(stat, "alter table test add column y int");
        Execute(stat, "drop table test");
    }

    private void TestSortedSelect()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create memory temporary table test(id int) not persistent");
        Execute(stat, "insert into test(id) direct sorted select 1");
        Execute(stat, "drop table test");
    }

    private void TestMaxMemoryRows()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases;MAX_MEMORY_ROWS=1");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id int primary key)");
        Execute(stat, "insert into test values(1), (2)");
        Execute(stat, "select * from system_range(1, 1) where x not in "
            + "(select id from test order by id)");
        Execute(stat, "select * from system_range(1, 1) where x not in "
            + "(select id from test union select id from test)");
        Execute(stat, "(select id from test order by id) "
            + "intersect (select id from test order by id)");
    }

    private void TestLikeExpressions()
    {
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        stat.CommandText = "select * from (select 'fo%' a union all select '%oo') where 'foo' like a";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("fo%", rs.GetString(0));
            AssertTrue(rs.Read());
            AssertEquals("%oo", rs.GetString(0));
        }
    }

    private void TestUnicode()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id identity, name text)");
        string[] data = ["\uff1e", "\ud848\udf1e"];
        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "insert into test(name) values(?)";
            prep.Parameters.Add(new H2Parameter());
            foreach (string s in data)
            {
                prep.Parameters[0].Value = s;
                _ = prep.ExecuteNonQuery();
            }
        }

        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "select * from test order by id";
            using (DbDataReader rs = prep.ExecuteReader())
            {
                foreach (string s in data)
                {
                    AssertTrue(rs.Read());
                    AssertEquals(s, rs.GetString(1));
                }
            }
        }

        Execute(stat, "drop table test");
        stat.CommandText = "select floor(\u3000 1.2) \ud835\udca9";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetDecimal(0));
            AssertEquals("\ud835\udca9", rs.GetName(0));
        }
    }

    private void TestOuterJoin()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table parent(p int primary key) as select 1");
        Execute(stat, "create table child(c int primary key, pc int) as select 2, 1");
        stat.CommandText = "select * from parent "
            + "left outer join child on p = pc where c is null";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertFalse(rs.Read());
        }

        Execute(stat, "drop all objects");
    }

    private void TestCommentOnColumnWithSchemaEqualDatabase()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create schema cases");
        Execute(stat, "create table cases.cases(cases int)");
        Execute(stat, "comment on column "
            + "cases.cases.cases is 'schema.table.column'");
        Execute(stat, "comment on column "
            + "cases.cases.cases.cases is 'db.schema.table.column'");
    }

    private void TestColumnWithConstraintAndComment()
    {
        if (Config.Memory)
        {
            return;
        }

        DeleteDb("cases");
        DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id int check id < 500)");
        Execute(stat, "comment on column test.id is 'comment'");
        conn.Close();
        conn = GetConnection("cases");
        conn.Close();
    }

    private void TestTruncateConstraintsDisabled()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table parent(id identity) as select 0");
        Execute(stat, "create table child(id identity, "
            + "parent int references parent(id)) as select 0, 0");
        AssertThrows(ErrorCode.CannotTruncate1, stat, "truncate table parent");
        AssertThrows(ErrorCode.ReferentialIntegrityViolatedChildExists1, stat,
            "delete from parent");
        Execute(stat, "alter table parent set referential_integrity false");
        Execute(stat, "delete from parent");
        Execute(stat, "truncate table parent");
        Execute(stat, "alter table parent set referential_integrity true");
        AssertThrows(ErrorCode.CannotTruncate1, stat, "truncate table parent");
        Execute(stat, "set referential_integrity false");
        Execute(stat, "truncate table parent");
    }

    private void TestPreparedSubquery2()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id int primary key, name varchar(255))");
        Execute(stat, "insert into test values(1, 'Hello')");
        Execute(stat, "insert into test values(2, 'World')");

        using (DbCommand ps = conn.CreateCommand())
        {
            ps.CommandText = "select name from test where id in "
                + "(select id from test where name = ?)";
            ps.Parameters.Add(new H2Parameter { Value = "Hello" });
            using (DbDataReader rs = ps.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals("Hello", rs.GetString(rs.GetOrdinal("name")));
            }

            ps.Parameters[0].Value = "World";
            using (DbDataReader rs = ps.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals("World", rs.GetString(rs.GetOrdinal("name")));
            }
        }
    }

    private void TestPreparedSubquery()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id int)");
        Execute(stat, "insert into test values(1)");
        string sql = "select ?, ?, (select count(*) from test inner join "
            + "(select id from test where 0=?) as t2 on t2.id=test.id) from test";
        stat.CommandText = sql.Replace('?', '0');
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(2));
        }

        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = sql;
            prep.Parameters.Add(new H2Parameter { Value = 0 });
            prep.Parameters.Add(new H2Parameter { Value = 0 });
            prep.Parameters.Add(new H2Parameter { Value = 0 });
            using (DbDataReader rs = prep.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals(1, rs.GetInt32(2));
            }
        }
    }

    private void TestCompareDoubleWithIntColumn()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        TestCompareDoubleWithIntColumn(stat, false, 0.1, false);
        TestCompareDoubleWithIntColumn(stat, false, 0.1, true);
        TestCompareDoubleWithIntColumn(stat, false, 0.9, false);
        TestCompareDoubleWithIntColumn(stat, false, 0.9, true);
        TestCompareDoubleWithIntColumn(stat, true, 0.1, false);
        TestCompareDoubleWithIntColumn(stat, true, 0.1, true);
        TestCompareDoubleWithIntColumn(stat, true, 0.9, false);
        TestCompareDoubleWithIntColumn(stat, true, 0.9, true);
    }

    private void TestCompareDoubleWithIntColumn(DbCommand stat, bool pk,
        double x, bool prepared)
    {
        Execute(stat, "create table test(id int " + (pk ? "primary key" : "") + ")");
        Execute(stat, "insert into test values(1)");
        if (prepared)
        {
            using DbCommand prep = stat.Connection!.CreateCommand();
            prep.CommandText = "select * from test where id > ?";
            prep.Parameters.Add(new H2Parameter { Value = x });
            using (DbDataReader rs = prep.ExecuteReader())
            {
                AssertTrue(rs.Read());
            }
        }
        else
        {
            stat.CommandText = "select * from test where id > " + x;
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertTrue(rs.Read());
            }
        }

        Execute(stat, "drop table test");
    }

    private void TestDeleteIndexOutOfBounds()
    {
        if (Config.Memory || !Config.Big)
        {
            return;
        }

        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE IF NOT EXISTS test "
            + "(rowid INTEGER PRIMARY KEY AUTO_INCREMENT, txt VARCHAR(64000));");
        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "insert into test (txt) values(space(?))";
            prep.Parameters.Add(new H2Parameter());
            for (int i = 0; i < 3000; i++)
            {
                prep.Parameters[0].Value = i * 3;
                _ = prep.ExecuteNonQuery();
            }
        }

        Execute(stat, "DELETE FROM test;");
    }

    private void TestInsertDeleteRollback()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "set cache_size 1");
        Execute(stat, "SET MAX_MEMORY_ROWS " + int.MaxValue);
        Execute(stat, "SET MAX_MEMORY_UNDO " + int.MaxValue);
        Execute(stat, "SET MAX_OPERATION_MEMORY " + int.MaxValue);
        Execute(stat, "create table test(id identity)");
        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            Execute(stat, "insert into test select x from system_range(1, 11)");
            Execute(stat, "delete from test");
            transaction.Rollback();
        }
    }

    private void TestLargeRollback()
    {
        DeleteDb("cases");
        DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "set max_operation_memory 1");
        Execute(stat, "create table test(id int)");
        Execute(stat, "insert into test values(1), (2)");
        Execute(stat, "create index idx on test(id)");
        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            Execute(stat, "update test set id = id where id=2");
            Execute(stat, "update test set id = id");
            transaction.Rollback();
        }

        conn.Close();

        DeleteDb("cases");
        conn = GetConnection("cases");
        DbCommand create = conn.CreateCommand();
        Execute(create, "set MAX_MEMORY_UNDO 1");
        Execute(create, "create table test(id number primary key)");
        Execute(create, "insert into test(id) select x from system_range(1, 2)");
        using (DbConnection conn2 = GetConnection("cases"))
        {
            using DbTransaction transaction = conn2.BeginTransaction();
            DbCommand command = conn2.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "delete from test";
            AssertEquals(2, command.ExecuteNonQuery());
        }

        conn.Close();

        DeleteDb("cases");
        conn = GetConnection("cases");
        create = conn.CreateCommand();
        Execute(create, "set MAX_MEMORY_UNDO 8");
        Execute(create, "create table test(id number primary key)");
        using (DbTransaction transaction = conn.BeginTransaction())
        {
            create.Transaction = transaction;
            Execute(create, "insert into test select x from system_range(1, 10)");
            Execute(create, "delete from test");
            transaction.Rollback();
        }

        conn.Close();
    }

    private void TestConstraintAlterTable()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table parent (pid int primary key)");
        Execute(stat, "create table child (cid int primary key, pid int)");
        Execute(stat, "alter table child add foreign key (pid) references parent(pid)");
        Execute(stat, "alter table child add column c2 int");
        Execute(stat, "alter table parent add column p2 varchar");
    }

    private void TestEmptyBtreeIndex()
    {
        DeleteDb("cases");
        DbConnection conn;
        conn = GetConnection("cases");
        Execute(conn.CreateCommand(), "CREATE TABLE test(id int PRIMARY KEY);");
        Execute(conn.CreateCommand(), "INSERT INTO test SELECT X FROM SYSTEM_RANGE(1, 77)");
        Execute(conn.CreateCommand(), "DELETE from test");
        conn.Close();
        conn = GetConnection("cases");
        Execute(conn.CreateCommand(), "INSERT INTO test (id) VALUES (1)");
        conn.Close();
        conn = GetConnection("cases");
        Execute(conn.CreateCommand(), "DELETE from test");
        Execute(conn.CreateCommand(), "drop table test");
        conn.Close();
    }

    private void TestJoinWithView()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        Execute(conn.CreateCommand(),
            "create table t(i identity, n varchar) as select 1, 'x'");
        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "select 1 from dual "
            + "inner join(select n from t where i=?) a on a.n='x' "
            + "inner join(select n from t where i=?) b on b.n='x'";
        prep.Parameters.Add(new H2Parameter { Value = 1 });
        prep.Parameters.Add(new H2Parameter { Value = 1 });
        _ = prep.ExecuteNonQuery();
    }

    private void TestLobDecrypt()
    {
        using DbConnection conn = GetConnection("cases");
        string key = "key";
        string value = "Hello World";
        byte[] encrypted;
        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "CALL ENCRYPT('AES', RAWTOHEX(?), STRINGTOUTF8(?))";
            prep.Parameters.Add(new H2Parameter { Value = key });
            prep.Parameters.Add(new H2Parameter { Value = value });
            using (DbDataReader rs = prep.ExecuteReader())
            {
                AssertTrue(rs.Read());
                encrypted = (byte[])rs.GetValue(0);
            }
        }

        using (DbCommand prep2 = conn.CreateCommand())
        {
            prep2.CommandText = "CALL TRIM(CHAR(0) FROM "
                + "UTF8TOSTRING(DECRYPT('AES', RAWTOHEX(?), ?)))";
            prep2.Parameters.Add(new H2Parameter { Value = key });
            prep2.Parameters.Add(new H2Parameter { Value = encrypted });
            using (DbDataReader rs2 = prep2.ExecuteReader())
            {
                AssertTrue(rs2.Read());
                string decrypted = rs2.GetString(0);
                AssertEquals(value, decrypted);
            }
        }
    }

    private void TestInvalidDatabaseName()
    {
        if (Config.Memory)
        {
            return;
        }

        AssertThrows(ErrorCode.InvalidDatabaseName1,
            () => _ = GetConnection("cases/"));
    }

    private void TestReuseSpace()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        int tableCount = GetSize(2, 5);
        for (int i = 0; i < tableCount; i++)
        {
            Execute(stat, "create table t" + i + "(data varchar)");
        }

        Random random = new(1);
        int len = GetSize(50, 500);
        for (int i = 0; i < len; i++)
        {
            string table = "t" + random.Next(tableCount);
            string sql;
            if (random.Next(2) == 0)
            {
                sql = "insert into " + table + " values(space(100000))";
            }
            else
            {
                sql = "delete from " + table;
            }

            Execute(stat, sql);
            Execute(stat, "script to '" + GetBaseDir() + "/test.sql'");
        }

        conn.Close();
        File.Delete(GetBaseDir() + "/test.sql");
    }

    private void TestDeleteGroup()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "set max_memory_rows 2");
        Execute(stat, "create table test(id int primary key, x int)");
        Execute(stat, "insert into test values(0, 0), (1, 1), (2, 2)");
        Execute(stat, "delete from test where id not in "
            + "(select min(x) from test group by id)");
    }

    private void TestDisconnect()
    {
        if (Config.Networked || Config.CodeCoverage)
        {
            return;
        }

        DeleteDb("cases");
        DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID IDENTITY)");
        for (int i = 0; i < 1000; i++)
        {
            Execute(stat, "INSERT INTO TEST() VALUES()");
        }

        DbException? stopped = null;
        Thread t = new(() =>
        {
            try
            {
                long time = Environment.TickCount64;
                stat.CommandText = "SELECT MAX(T.ID) "
                    + "FROM TEST T, TEST, TEST, TEST, TEST, "
                    + "TEST, TEST, TEST, TEST, TEST, TEST";
                using (DbDataReader rs = stat.ExecuteReader())
                {
                    AssertTrue(rs.Read());
                    time = Environment.TickCount64 - time;
                    LogError("query was too quick; result: "
                        + rs.GetInt32(0) + " time:" + time, null);
                }
            }
            catch (DbException e)
            {
                stopped = e;
                // ok
            }
        });
        t.Start();
        Thread.Sleep(300);
        long closeStart = Environment.TickCount64;
        conn.Close();
        t.Join(5000);
        if (stopped is null)
        {
            Fail("query still running");
        }
        else
        {
            AssertKnownException(stopped);
        }

        if (Environment.TickCount64 - closeStart > 5000)
        {
            if (!Config.Reopen)
            {
                Fail("closing took too long");
            }
        }

        DeleteDb("cases");
    }

    private void CheckExplain(DbCommand stat, string sql, string expected)
    {
        stat.CommandText = sql;
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(expected, rs.GetString(0));
        }
    }

    private void TestExplain()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "CREATE TABLE ORGANIZATION"
            + "(id int primary key, name varchar(100))");
        Execute(stat, "CREATE TABLE PERSON"
            + "(id int primary key, orgId int, name varchar(100), salary int)");

        CheckExplain(stat, "/* bla-bla */ EXPLAIN SELECT ID FROM ORGANIZATION WHERE id = ?",
            "SELECT\n"
            + "    \"ID\"\n"
            + "FROM \"PUBLIC\".\"ORGANIZATION\"\n"
            + "    /* PUBLIC.PRIMARY_KEY_D: ID = ?1 */\n"
            + "WHERE \"ID\" = ?1");

        CheckExplain(stat, "EXPLAIN SELECT ID FROM ORGANIZATION WHERE id = 1",
            "SELECT\n"
            + "    \"ID\"\n"
            + "FROM \"PUBLIC\".\"ORGANIZATION\"\n"
            + "    /* PUBLIC.PRIMARY_KEY_D: ID = 1 */\n"
            + "WHERE \"ID\" = 1");

        CheckExplain(stat, "EXPLAIN SELECT * FROM PERSON WHERE id = ?",
            "SELECT\n"
            + "    \"PUBLIC\".\"PERSON\".\"ID\",\n"
            + "    \"PUBLIC\".\"PERSON\".\"ORGID\",\n"
            + "    \"PUBLIC\".\"PERSON\".\"NAME\",\n"
            + "    \"PUBLIC\".\"PERSON\".\"SALARY\"\n"
            + "FROM \"PUBLIC\".\"PERSON\"\n"
            + "    /* PUBLIC.PRIMARY_KEY_8: ID = ?1 */\n"
            + "WHERE \"ID\" = ?1");

        CheckExplain(stat, "EXPLAIN SELECT * FROM PERSON WHERE id = 50",
            "SELECT\n"
            + "    \"PUBLIC\".\"PERSON\".\"ID\",\n"
            + "    \"PUBLIC\".\"PERSON\".\"ORGID\",\n"
            + "    \"PUBLIC\".\"PERSON\".\"NAME\",\n"
            + "    \"PUBLIC\".\"PERSON\".\"SALARY\"\n"
            + "FROM \"PUBLIC\".\"PERSON\"\n"
            + "    /* PUBLIC.PRIMARY_KEY_8: ID = 50 */\n"
            + "WHERE \"ID\" = 50");

        CheckExplain(stat, "EXPLAIN SELECT * FROM PERSON WHERE salary > ? and salary < ?",
            "SELECT\n"
            + "    \"PUBLIC\".\"PERSON\".\"ID\",\n"
            + "    \"PUBLIC\".\"PERSON\".\"ORGID\",\n"
            + "    \"PUBLIC\".\"PERSON\".\"NAME\",\n"
            + "    \"PUBLIC\".\"PERSON\".\"SALARY\"\n"
            + "FROM \"PUBLIC\".\"PERSON\"\n"
            + "    /* PUBLIC.PERSON.tableScan */\n"
            + "WHERE (\"SALARY\" > ?1)\n"
            + "    AND (\"SALARY\" < ?2)");

        CheckExplain(stat, "EXPLAIN SELECT * FROM PERSON WHERE salary > 1000 and salary < 2000",
            "SELECT\n"
            + "    \"PUBLIC\".\"PERSON\".\"ID\",\n"
            + "    \"PUBLIC\".\"PERSON\".\"ORGID\",\n"
            + "    \"PUBLIC\".\"PERSON\".\"NAME\",\n"
            + "    \"PUBLIC\".\"PERSON\".\"SALARY\"\n"
            + "FROM \"PUBLIC\".\"PERSON\"\n"
            + "    /* PUBLIC.PERSON.tableScan */\n"
            + "WHERE (\"SALARY\" > 1000)\n"
            + "    AND (\"SALARY\" < 2000)");

        CheckExplain(stat, "EXPLAIN SELECT * FROM PERSON WHERE name = lower(?)",
            "SELECT\n"
            + "    \"PUBLIC\".\"PERSON\".\"ID\",\n"
            + "    \"PUBLIC\".\"PERSON\".\"ORGID\",\n"
            + "    \"PUBLIC\".\"PERSON\".\"NAME\",\n"
            + "    \"PUBLIC\".\"PERSON\".\"SALARY\"\n"
            + "FROM \"PUBLIC\".\"PERSON\"\n"
            + "    /* PUBLIC.PERSON.tableScan */\n"
            + "WHERE \"NAME\" = LOWER(?1)");

        CheckExplain(stat, "EXPLAIN SELECT * FROM PERSON WHERE name = lower('Smith')",
            "SELECT\n"
            + "    \"PUBLIC\".\"PERSON\".\"ID\",\n"
            + "    \"PUBLIC\".\"PERSON\".\"ORGID\",\n"
            + "    \"PUBLIC\".\"PERSON\".\"NAME\",\n"
            + "    \"PUBLIC\".\"PERSON\".\"SALARY\"\n"
            + "FROM \"PUBLIC\".\"PERSON\"\n"
            + "    /* PUBLIC.PERSON.tableScan */\n"
            + "WHERE \"NAME\" = 'smith'");

        CheckExplain(stat, "EXPLAIN SELECT * FROM PERSON p "
            + "INNER JOIN ORGANIZATION o ON p.id = o.id WHERE o.id = ? AND p.salary > ?",
            "SELECT\n"
            + "    \"P\".\"ID\",\n"
            + "    \"P\".\"ORGID\",\n"
            + "    \"P\".\"NAME\",\n"
            + "    \"P\".\"SALARY\",\n"
            + "    \"O\".\"ID\",\n"
            + "    \"O\".\"NAME\"\n"
            + "FROM \"PUBLIC\".\"ORGANIZATION\" \"O\"\n"
            + "    /* PUBLIC.PRIMARY_KEY_D: ID = ?1 */\n"
            + "    /* WHERE O.ID = ?1\n"
            + "    */\n"
            + "INNER JOIN \"PUBLIC\".\"PERSON\" \"P\"\n"
            + "    /* PUBLIC.PRIMARY_KEY_8: ID = O.ID */\n"
            + "    ON 1=1\n"
            + "WHERE (\"P\".\"ID\" = \"O\".\"ID\")\n"
            + "    AND (\"O\".\"ID\" = ?1)\n"
            + "    AND (\"P\".\"SALARY\" > ?2)");

        CheckExplain(stat, "EXPLAIN SELECT * FROM PERSON p "
            + "INNER JOIN ORGANIZATION o ON p.id = o.id WHERE o.id = 10 AND p.salary > 1000",
            "SELECT\n"
            + "    \"P\".\"ID\",\n"
            + "    \"P\".\"ORGID\",\n"
            + "    \"P\".\"NAME\",\n"
            + "    \"P\".\"SALARY\",\n"
            + "    \"O\".\"ID\",\n"
            + "    \"O\".\"NAME\"\n"
            + "FROM \"PUBLIC\".\"ORGANIZATION\" \"O\"\n"
            + "    /* PUBLIC.PRIMARY_KEY_D: ID = 10 */\n"
            + "    /* WHERE O.ID = 10\n"
            + "    */\n"
            + "INNER JOIN \"PUBLIC\".\"PERSON\" \"P\"\n"
            + "    /* PUBLIC.PRIMARY_KEY_8: ID = O.ID */\n"
            + "    ON 1=1\n"
            + "WHERE (\"P\".\"ID\" = \"O\".\"ID\")\n"
            + "    AND (\"O\".\"ID\" = 10)\n"
            + "    AND (\"P\".\"SALARY\" > 1000)");

        using (DbCommand pStat = conn.CreateCommand())
        {
            pStat.CommandText = "/* bla-bla */ EXPLAIN SELECT ID FROM ORGANIZATION WHERE id = ?";
            pStat.Parameters.Add(new H2Parameter { Value = 0 });
            using (DbDataReader rs = pStat.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals("SELECT\n"
                    + "    \"ID\"\n"
                    + "FROM \"PUBLIC\".\"ORGANIZATION\"\n"
                    + "    /* PUBLIC.PRIMARY_KEY_D: ID = ?1 */\n"
                    + "WHERE \"ID\" = ?1",
                    rs.GetString(0));
            }
        }
    }

    private void TestExplainAnalyze()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "CREATE TABLE ORGANIZATION"
            + "(id int primary key, name varchar(100))");
        Execute(stat, "CREATE TABLE PERSON"
            + "(id int primary key, orgId int, name varchar(100), salary int)");

        Execute(stat, "INSERT INTO ORGANIZATION VALUES(1, 'org1')");
        Execute(stat, "INSERT INTO ORGANIZATION VALUES(2, 'org2')");

        Execute(stat, "INSERT INTO PERSON VALUES(1, 1, 'person1', 1000)");
        Execute(stat, "INSERT INTO PERSON VALUES(2, 1, 'person2', 2000)");
        Execute(stat, "INSERT INTO PERSON VALUES(3, 2, 'person3', 3000)");
        Execute(stat, "INSERT INTO PERSON VALUES(4, 2, 'person4', 4000)");

        using (DbCommand pStat = conn.CreateCommand())
        {
            pStat.CommandText = "/* bla-bla */ EXPLAIN ANALYZE SELECT ID FROM ORGANIZATION WHERE id = ?";
            AssertThrows(ErrorCode.ParameterNotSet1,
                () => _ = pStat.ExecuteScalar());
            pStat.Parameters.Add(new H2Parameter { Value = 1 });
            using (DbDataReader rs = pStat.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals("SELECT\n"
                    + "    \"ID\"\n"
                    + "FROM \"PUBLIC\".\"ORGANIZATION\"\n"
                    + "    /* PUBLIC.PRIMARY_KEY_D: ID = ?1 */\n"
                    + "    /* scanCount: 2 */\n"
                    + "WHERE \"ID\" = ?1",
                    rs.GetString(0));
            }
        }

        using (DbCommand pStat = conn.CreateCommand())
        {
            pStat.CommandText = "EXPLAIN ANALYZE SELECT * FROM PERSON p "
                + "INNER JOIN ORGANIZATION o ON o.id = p.id WHERE o.id = ?";
            AssertThrows(ErrorCode.ParameterNotSet1,
                () => _ = pStat.ExecuteScalar());
            pStat.Parameters.Add(new H2Parameter { Value = 1 });
            using (DbDataReader rs = pStat.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals("SELECT\n"
                    + "    \"P\".\"ID\",\n"
                    + "    \"P\".\"ORGID\",\n"
                    + "    \"P\".\"NAME\",\n"
                    + "    \"P\".\"SALARY\",\n"
                    + "    \"O\".\"ID\",\n"
                    + "    \"O\".\"NAME\"\n"
                    + "FROM \"PUBLIC\".\"PERSON\" \"P\"\n"
                    + "    /* PUBLIC.PRIMARY_KEY_8: ID = ?1 */\n"
                    + "    /* scanCount: 2 */\n"
                    + "INNER JOIN \"PUBLIC\".\"ORGANIZATION\" \"O\"\n"
                    + "    /* PUBLIC.PRIMARY_KEY_D: ID = ?1\n"
                    + "        AND ID = P.ID\n"
                    + "     */\n"
                    + "    ON 1=1\n"
                    + "    /* scanCount: 2 */\n"
                    + "WHERE (\"O\".\"ID\" = ?1)\n"
                    + "    AND (\"O\".\"ID\" = \"P\".\"ID\")",
                    rs.GetString(0));
            }
        }
    }

    private void TestAlterTableReconnect()
    {
        DeleteDb("cases");
        DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id identity);");
        Execute(stat, "insert into test values(1);");
        AssertThrows(ErrorCode.NullNotAllowed, stat,
            "alter table test add column name varchar not null;");
        conn.Close();
        conn = GetConnection("cases");
        stat = conn.CreateCommand();
        stat.CommandText = "SELECT * FROM TEST";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("1", rs.GetString(0));
            AssertFalse(rs.Read());
        }

        Execute(stat, "drop table test");
        Execute(stat, "create table test(id identity)");
        Execute(stat, "insert into test values(1)");
        Execute(stat, "alter table test alter column id set default 'x'");
        conn.Close();
        conn = GetConnection("cases");
        stat = conn.CreateCommand();
        stat.CommandText = "SELECT * FROM TEST";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("1", rs.GetString(0));
            AssertFalse(rs.Read());
        }

        Execute(stat, "drop table test");
        Execute(stat, "create table test(id identity)");
        Execute(stat, "insert into test values(1)");
        AssertThrows(ErrorCode.DataConversionError1, stat,
            "alter table test alter column id date");
        conn.Close();
        conn = GetConnection("cases");
        stat = conn.CreateCommand();
        stat.CommandText = "SELECT * FROM TEST";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("1", rs.GetString(0));
            AssertFalse(rs.Read());
        }

        conn.Close();
    }

    private void TestCollation()
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

    private void TestPersistentSettings()
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

        conn.Close();
    }

    private void TestInsertSelectUnion()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        using DbCommand ps = conn.CreateCommand();
        ps.CommandText = "insert into TEST (ORDER_ID,ORDER_DATE,"
            + "USER_ID,DESCRIPTION,STATE,TRACKING_ID) "
            + "select cast(? as int),cast(? as date),cast(? as int),cast(? as varchar),"
            + "cast(? as varchar),cast(? as varchar) union all select ?,?,?,?,?,?";
        ps.Parameters.Add(new H2Parameter { Value = 5555 });
        ps.Parameters.Add(new H2Parameter { Value = DateTime.Parse("2005-05-21 17:46:00") });
        ps.Parameters.Add(new H2Parameter { Value = 2222 });
        ps.Parameters.Add(new H2Parameter { Value = "test desc" });
        ps.Parameters.Add(new H2Parameter { Value = "test_state" });
        ps.Parameters.Add(new H2Parameter { Value = "testid" });
        ps.Parameters.Add(new H2Parameter { Value = 5556 });
        ps.Parameters.Add(new H2Parameter { Value = DateTime.Parse("2005-05-21 17:46:00") });
        ps.Parameters.Add(new H2Parameter { Value = 2222 });
        ps.Parameters.Add(new H2Parameter { Value = "test desc" });
        ps.Parameters.Add(new H2Parameter { Value = "test_state" });
        ps.Parameters.Add(new H2Parameter { Value = "testid" });
        AssertEquals(2, ps.ExecuteNonQuery());
    }

    private void TestViewReconnect()
    {
        Trace("testViewReconnect");
        DeleteDb("cases");
        DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id int)");
        Execute(stat, "create view abc as select * from test");
        Execute(stat, "drop table test cascade");
        conn.Close();
        conn = GetConnection("cases");
        stat = conn.CreateCommand();
        AssertThrows(ErrorCode.TableOrViewNotFoundDatabaseEmpty1, stat,
            "select * from abc");
        conn.Close();
    }

    private void TestDefaultQueryReconnect()
    {
        Trace("testDefaultQueryReconnect");
        DeleteDb("cases");
        DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table parent(id int)");
        Execute(stat, "insert into parent values(1)");
        Execute(stat, "create table test(id int default "
            + "(select max(id) from parent), name varchar)");

        conn.Close();
        conn = GetConnection("cases");
        stat = conn.CreateCommand();
        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            Execute(stat, "insert into parent values(2)");
            Execute(stat, "insert into test(name) values('test')");
            stat.CommandText = "select * from test";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals(2, rs.GetInt32(0));
                AssertFalse(rs.Read());
            }

            transaction.Commit();
        }

        conn.Close();
    }

    private void TestBigString()
    {
        Trace("testBigString");
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "DROP TABLE IF EXISTS TEST");
        Execute(stat, "CREATE TABLE TEST(ID INT, TEXT VARCHAR, TEXT_C CLOB)");
        int len = GetSize(1000, 66000);
        char[] buff = new char[len];

        // Unicode problem:
        // The UCS code values 0xd800-0xdfff (UTF-16 surrogates)
        // as well as 0xfffe and 0xffff (UCS non-characters)
        // should not appear in conforming UTF-8 streams.
        // (String.getBytes("UTF-8") only returns 1 byte for 0xd800-0xdfff)
        Random random = new();
        random = new Random(1);
        for (int i = 0; i < len; i++)
        {
            char c;
            do
            {
                c = (char)random.Next();
            } while (c >= 0xd800 && c <= 0xdfff);
            buff[i] = c;
        }

        string big = new(buff);
        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "INSERT INTO TEST VALUES(?, ?, ?)";
            prep.Parameters.Add(new H2Parameter { Value = 1 });
            prep.Parameters.Add(new H2Parameter { Value = big });
            prep.Parameters.Add(new H2Parameter { Value = big });
            _ = prep.ExecuteNonQuery();
            prep.Parameters[0].Value = 2;
            prep.Parameters[1].Value = new StringReader(big);
            prep.Parameters[2].Value = new StringReader(big);
            _ = prep.ExecuteNonQuery();
        }

        stat.CommandText = "SELECT * FROM TEST ORDER BY ID";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
            AssertEquals(big, rs.GetString(1));
            AssertEquals(big, ReadString(rs.GetTextReader(1)));
            AssertEquals(big, rs.GetString(2));
            AssertEquals(big, ReadString(rs.GetTextReader(2)));
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertEquals(big, rs.GetString(1));
            AssertEquals(big, ReadString(rs.GetTextReader(1)));
            AssertEquals(big, rs.GetString(2));
            AssertEquals(big, ReadString(rs.GetTextReader(2)));
            AssertFalse(rs.Read());
            AssertFalse(rs.Read());
        }
    }

    private void TestConstraintReconnect()
    {
        Trace("testConstraintReconnect");
        DeleteDb("cases");
        DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "drop table if exists parent");
        Execute(stat, "drop table if exists child");
        Execute(stat, "create table parent(id int primary key)");
        Execute(stat, "create table child(c_id int, p_id int, "
            + "foreign key(p_id) references parent(id))");
        Execute(stat, "insert into parent values(1), (2)");
        Execute(stat, "insert into child values(1, 1)");
        Execute(stat, "insert into child values(2, 2)");
        Execute(stat, "insert into child values(3, 2)");
        Execute(stat, "delete from child");
        conn.Close();
        conn = GetConnection("cases");
        conn.Close();
    }

    private void TestRenameReconnect()
    {
        Trace("testRenameReconnect");
        DeleteDb("cases");
        DbConnection conn = GetConnection("cases");
        Execute(conn.CreateCommand(), "CREATE TABLE TEST_SEQ"
            + "(ID INT GENERATED BY DEFAULT AS IDENTITY, NAME VARCHAR(255))");
        Execute(conn.CreateCommand(), "CREATE TABLE TEST"
            + "(ID INT PRIMARY KEY)");
        Execute(conn.CreateCommand(), "ALTER TABLE TEST RENAME TO TEST2");
        Execute(conn.CreateCommand(), "CREATE TABLE TEST_B"
            + "(ID INT PRIMARY KEY, NAME VARCHAR, UNIQUE(NAME))");
        conn.Close();
        conn = GetConnection("cases");
        DbCommand command = conn.CreateCommand();
        command.CommandText = "SELECT ID FROM FINAL TABLE(INSERT INTO TEST_SEQ(NAME) VALUES('Hi'))";
        using (DbDataReader rs = command.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
        }

        Execute(conn.CreateCommand(), "SELECT * FROM TEST2");
        Execute(conn.CreateCommand(), "SELECT * FROM TEST_B");
        Execute(conn.CreateCommand(), "ALTER TABLE TEST_B RENAME TO TEST_B2");
        conn.Close();
        conn = GetConnection("cases");
        Execute(conn.CreateCommand(), "SELECT * FROM TEST_B2");
        command = conn.CreateCommand();
        command.CommandText = "SELECT ID FROM FINAL TABLE(INSERT INTO TEST_SEQ(NAME) VALUES('World'))";
        using (DbDataReader rs = command.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
        }

        conn.Close();
    }

    private void TestSelectForUpdate()
    {
        Trace("testSelectForUpdate");
        DeleteDb("cases");
        using DbConnection conn1 = GetConnection("cases");
        DbCommand stat1 = conn1.CreateCommand();
        Execute(stat1, "CREATE TABLE TEST(ID INT)");
        Execute(stat1, "INSERT INTO TEST VALUES(1)");
        using (DbTransaction transaction = conn1.BeginTransaction())
        {
            stat1.Transaction = transaction;
            Execute(stat1, "SELECT * FROM TEST FOR UPDATE");
            using DbConnection conn2 = GetConnection("cases");
            DbCommand stat2 = conn2.CreateCommand();
            AssertThrows(ErrorCode.LockTimeout1, stat2, "UPDATE TEST SET ID=2");
            transaction.Commit();
            Execute(stat2, "UPDATE TEST SET ID=2");
        }
    }

    private void TestCreateDrop()
    {
        Trace("testCreateDrop");
        DeleteDb("cases");
        DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table employee(id int, firstName VARCHAR(50), "
            + "salary decimal(10, 2), "
            + "superior_id int, CONSTRAINT PK_employee PRIMARY KEY (id), "
            + "CONSTRAINT FK_superior FOREIGN KEY (superior_id) "
            + "REFERENCES employee(ID))");
        Execute(stat, "DROP TABLE employee");
        conn.Close();
        conn = GetConnection("cases");
        conn.Close();
    }

    private void TestPolePos()
    {
        Trace("testPolePos");
        // poleposition-0.20

        DbConnection c0 = GetConnection("cases");
        using (DbTransaction transaction = c0.BeginTransaction())
        {
            DbCommand create = c0.CreateCommand();
            create.Transaction = transaction;
            Execute(create, "create table australia (ID  INTEGER NOT NULL, "
                + "Name VARCHAR(100), firstName VARCHAR(100), "
                + "Points INTEGER, LicenseID INTEGER, PRIMARY KEY(ID))");
            transaction.Commit();
        }

        c0.Close();

        c0 = GetConnection("cases");
        using (DbTransaction transaction = c0.BeginTransaction())
        {
            using DbCommand p15 = c0.CreateCommand();
            p15.Transaction = transaction;
            p15.CommandText = "insert into australia"
                + "(id, Name, firstName, Points, LicenseID) values (?, ?, ?, ?, ?)";
            p15.Parameters.Add(new H2Parameter());
            p15.Parameters.Add(new H2Parameter());
            p15.Parameters.Add(new H2Parameter());
            p15.Parameters.Add(new H2Parameter());
            p15.Parameters.Add(new H2Parameter());
            int len = GetSize(1, 1000);
            for (int i = 0; i < len; i++)
            {
                p15.Parameters[0].Value = i;
                p15.Parameters[1].Value = "Pilot_" + i;
                p15.Parameters[2].Value = "Herkules";
                p15.Parameters[3].Value = i;
                p15.Parameters[4].Value = i;
                _ = p15.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        c0.Close();

        c0 = GetConnection("cases");
        using (DbTransaction transaction = c0.BeginTransaction())
        {
            DbCommand create = c0.CreateCommand();
            create.Transaction = transaction;
            Execute(create, "drop table australia");
            Execute(create, "create table australia "
                + "(ID  INTEGER NOT NULL, Name VARCHAR(100), "
                + "firstName VARCHAR(100), Points INTEGER, "
                + "LicenseID INTEGER, PRIMARY KEY(ID))");
            transaction.Commit();
        }

        c0.Close();

        c0 = GetConnection("cases");
        using (DbTransaction transaction = c0.BeginTransaction())
        {
            using DbCommand p65 = c0.CreateCommand();
            p65.Transaction = transaction;
            p65.CommandText = "insert into australia"
                + "(id, Name, FirstName, Points, LicenseID) values (?, ?, ?, ?, ?)";
            p65.Parameters.Add(new H2Parameter());
            p65.Parameters.Add(new H2Parameter());
            p65.Parameters.Add(new H2Parameter());
            p65.Parameters.Add(new H2Parameter());
            p65.Parameters.Add(new H2Parameter());
            int len = GetSize(1, 1000);
            for (int i = 0; i < len; i++)
            {
                p65.Parameters[0].Value = i;
                p65.Parameters[1].Value = "Pilot_" + i;
                p65.Parameters[2].Value = "Herkules";
                p65.Parameters[3].Value = i;
                p65.Parameters[4].Value = i;
                _ = p65.ExecuteNonQuery();
            }

            transaction.Commit();
            transaction.Commit();
            transaction.Commit();
        }

        c0.Close();

        c0 = GetConnection("cases");
        c0.Close();
    }

    private void TestQuick()
    {
        Trace("testQuick");
        DeleteDb("cases");

        DbConnection c0 = GetConnection("cases");
        DbCommand create = c0.CreateCommand();
        Execute(create, "create table test (ID  int PRIMARY KEY)");
        Execute(create, "insert into test values(1)");
        Execute(create, "drop table test");
        Execute(create, "create table test (ID  int PRIMARY KEY)");
        c0.Close();

        c0 = GetConnection("cases");
        DbCommand insert = c0.CreateCommand();
        Execute(insert, "insert into test values(1)");
        c0.Close();

        c0 = GetConnection("cases");
        c0.Close();
    }

    private void TestOrderByWithSubselect()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table master"
            + "(id number primary key, name varchar2(30));");
        Execute(stat, "create table detail"
            + "(id number references master(id), location varchar2(30));");

        Execute(stat, "Insert into master values(1,'a'), (2,'b'), (3,'c');");
        Execute(stat, "Insert into detail values(1,'a'), (2,'b'), (3,'c');");

        stat.CommandText = "select master.id, master.name "
            + "from master "
            + "where master.id in (select detail.id from detail) "
            + "order by master.id";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertTrue(rs.Read());
            AssertEquals(3, rs.GetInt32(0));
        }
    }

    private void TestDeleteAndDropTableWithLobs(bool useDrop)
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(id int, content BLOB)");
        Execute(stat, "set MAX_LENGTH_INPLACE_LOB 1");

        using (DbCommand prepared = conn.CreateCommand())
        {
            prepared.CommandText = "INSERT INTO TEST VALUES(?, ?)";
            byte[] blobContent = "BLOB_CONTENT"u8.ToArray();
            prepared.Parameters.Add(new H2Parameter { Value = 1 });
            prepared.Parameters.Add(new H2Parameter { Value = blobContent });
            _ = prepared.ExecuteNonQuery();
        }

        if (useDrop)
        {
            Execute(stat, "DROP TABLE TEST");
        }
        else
        {
            Execute(stat, "DELETE FROM TEST");
        }

        conn.Close();

        int lobFiles = 0;
        foreach (FilePath path in FilePath.Get(GetBaseDir()).NewDirectoryStream())
        {
            if (path.ToString().Contains("cases.lobs", StringComparison.Ordinal)
                && path.IsRegularFile())
            {
                lobFiles++;
            }
        }

        AssertEquals("Lob file was not deleted", 0, lobFiles);
    }

    private void TestMinimalCoveringIndexPlan()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "create table t(a int, b int, c int)");
        Execute(stat, "create index a_idx on t(a)");
        Execute(stat, "create index b_idx on t(b)");
        Execute(stat, "create index ab_idx on t(a, b)");
        Execute(stat, "create index abc_idx on t(a, b, c)");

        stat.CommandText = "explain select a from t";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertContains(rs.GetString(0), "/* PUBLIC.A_IDX */");
        }

        stat.CommandText = "explain select b from t";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertContains(rs.GetString(0), "/* PUBLIC.B_IDX */");
        }

        stat.CommandText = "explain select b, a from t";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertContains(rs.GetString(0), "/* PUBLIC.AB_IDX */");
        }

        stat.CommandText = "explain select b, a, c from t";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertContains(rs.GetString(0), "/* PUBLIC.ABC_IDX */");
        }
    }

    private void TestMinMaxDirectLookupIndex()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "create table t(a int, b int)");
        Execute(stat, "create index b_idx on t(b desc)");
        Execute(stat, "create index ab_idx on t(a, b)");

        const int count = 100;

        using (DbCommand p = conn.CreateCommand())
        {
            p.CommandText = "insert into t values (?,?)";
            p.Parameters.Add(new H2Parameter());
            p.Parameters.Add(new H2Parameter());
            for (int i = 0; i <= count; i++)
            {
                p.Parameters[0].Value = i;
                p.Parameters[1].Value = count - i;
                AssertEquals(1, p.ExecuteNonQuery());
            }
        }

        stat.CommandText = "select max(b) from t";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(count, rs.GetInt32(0));
        }

        stat.CommandText = "explain select max(b) from t";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            string plan = rs.GetString(0);
            AssertContains(plan, "/* PUBLIC.B_IDX */");
            AssertContains(plan, "/* direct lookup */");
        }

        stat.CommandText = "select min(b) from t";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(0, rs.GetInt32(0));
        }

        stat.CommandText = "explain select min(b) from t";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            string plan = rs.GetString(0);
            AssertContains(plan, "/* PUBLIC.B_IDX */");
            AssertContains(plan, "/* direct lookup */");
        }
    }

    private void TestCheckConstraintWithFunction()
    {
        DeleteDb("cases");
        DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create alias is_email as "
            + "'boolean isEmail(String x) { return x != null && x.indexOf(''@'') > 0; }'");
        Execute(stat, "create domain email as varchar check is_email(value)");
        Execute(stat, "create table test(e email)");
        conn.Close();
        conn = GetConnection("cases");
        conn.Close();
    }

    private void TestSpecialSQL()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        AssertThrows(ErrorCode.SyntaxError2, stat,
            "create table address"
            + "(id identity, name varchar check? instr(value, '@') > 1)");
        Execute(stat, "SET AUTOCOMMIT OFF; \n//"
            + "create sequence if not exists object_id;\n");
        Execute(stat, "SET AUTOCOMMIT OFF;\n//"
            + "create sequence if not exists object_id;\n");
        Execute(stat, "SET AUTOCOMMIT OFF; //"
            + "create sequence if not exists object_id;");
        Execute(stat, "SET AUTOCOMMIT OFF;//"
            + "create sequence if not exists object_id;");
        Execute(stat, "SET AUTOCOMMIT OFF \n//"
            + "create sequence if not exists object_id;");
        Execute(stat, "SET AUTOCOMMIT OFF\n//"
            + "create sequence if not exists object_id;");
        Execute(stat, "SET AUTOCOMMIT OFF //"
            + "create sequence if not exists object_id;");
        Execute(stat, "SET AUTOCOMMIT OFF//"
            + "create sequence if not exists object_id;");
        Execute(stat, "SET AUTOCOMMIT OFF; \n///"
            + "create sequence if not exists object_id;");
        Execute(stat, "SET AUTOCOMMIT OFF;\n///"
            + "create sequence if not exists object_id;");
        Execute(stat, "SET AUTOCOMMIT OFF; ///"
            + "create sequence if not exists object_id;");
        Execute(stat, "SET AUTOCOMMIT OFF;///"
            + "create sequence if not exists object_id;");
        Execute(stat, "SET AUTOCOMMIT OFF \n///"
            + "create sequence if not exists object_id;");
        Execute(stat, "SET AUTOCOMMIT OFF\n///"
            + "create sequence if not exists object_id;");
        Execute(stat, "SET AUTOCOMMIT OFF ///"
            + "create sequence if not exists object_id;");
        Execute(stat, "SET AUTOCOMMIT OFF///"
            + "create sequence if not exists object_id;");
    }

    private void TestUpperCaseLowerCaseDatabase()
    {
        if (Path.DirectorySeparatorChar != '\\' || Config.GoogleAppEngine)
        {
            return;
        }

        DeleteDb("cases");
        DeleteDb("CaSeS");
        DbConnection conn, conn2;
        conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CHECKPOINT");
        Execute(stat, "CREATE TABLE TEST(ID INT)");
        Execute(stat, "INSERT INTO TEST VALUES(1)");
        Execute(stat, "CHECKPOINT");

        conn2 = GetConnection("CaSeS");
        DbCommand command = conn.CreateCommand();
        command.CommandText = "SELECT * FROM TEST";
        using (DbDataReader rs = command.ExecuteReader())
        {
            AssertTrue(rs.Read());
        }

        conn2.Close();

        conn.Close();

        conn = GetConnection("cases");
        command = conn.CreateCommand();
        command.CommandText = "SELECT * FROM TEST";
        using (DbDataReader rs = command.ExecuteReader())
        {
            AssertTrue(rs.Read());
        }

        conn.Close();

        conn = GetConnection("CaSeS");
        command = conn.CreateCommand();
        command.CommandText = "SELECT * FROM TEST";
        using (DbDataReader rs = command.ExecuteReader())
        {
            AssertTrue(rs.Read());
        }

        conn.Close();

        DeleteDb("cases");
        DeleteDb("CaSeS");
    }

    private void TestManualCommitSet()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        using DbConnection conn2 = GetConnection("cases");
        using DbTransaction t1 = conn.BeginTransaction();
        using DbTransaction t2 = conn2.BeginTransaction();
        DbCommand c1 = conn.CreateCommand();
        c1.Transaction = t1;
        DbCommand c2 = conn2.CreateCommand();
        c2.Transaction = t2;
        Execute(c1, "SET MODE REGULAR");
        Execute(c2, "SET MODE REGULAR");
    }

    private void TestSchemaIdentityReconnect()
    {
        DeleteDb("cases");
        DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create schema s authorization sa");
        Execute(stat, "create table s.test(id identity)");
        conn.Close();
        conn = GetConnection("cases");
        DbCommand query = conn.CreateCommand();
        query.CommandText = "SELECT * FROM S.TEST";
        using (DbDataReader rs = query.ExecuteReader())
        {
            while (rs.Read())
            {
                // ignore
            }
        }

        conn.Close();
    }

    private void TestReservedKeywordReconnect()
    {
        if (Config.Memory)
        {
            return;
        }

        DeleteDb("cases");
        DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table \"UNIQUE\"(\"UNIQUE\" int)");
        conn.Close();
        conn = GetConnection("cases");
        stat = conn.CreateCommand();
        Execute(stat, "select \"UNIQUE\" from \"UNIQUE\"");
        Execute(stat, "drop table \"UNIQUE\"");
        conn.Close();
    }

    private void TestDataChangeDeltaTable()
    {
        /*
         * This test case didn't reproduce the issue in the TestScript.
         *
         * The same UPDATE is necessary before and after usage of a data change
         * delta table.
         */
        string updateCommand = "UPDATE TEST SET V = 3 WHERE ID = 1";
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID INT, V INT)");
        stat.CommandText = updateCommand;
        AssertEquals(0, stat.ExecuteNonQuery());
        stat.CommandText = "SELECT V FROM FINAL TABLE (INSERT INTO TEST VALUES (1, 1))";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
        }

        stat.CommandText = updateCommand;
        AssertEquals(1, stat.ExecuteNonQuery());
        stat.CommandText = "SELECT V FROM TEST";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(3, rs.GetInt32(0));
        }
    }

    private void TestGroupSortedReset()
    {
        // This test case didn't reproduce the issue in the TestScript.
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE T1(A INT PRIMARY KEY, B INT) AS VALUES (1, 4), (2, 5), (3, 6)");
        string sql = "SELECT B FROM T1 LEFT JOIN (VALUES 2) T2(A) USING(A) WHERE T2.A = 2 GROUP BY T1.A";
        Execute(stat, sql);
        Execute(stat, "UPDATE T1 SET B = 7 WHERE A = 3");
        Execute(stat, sql);
    }

    private void TestShowColumns()
    {
        // This test requires a PreparedStatement
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(A INTEGER)");
        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "SHOW COLUMNS FROM TEST";
            using (DbDataReader rs = prep.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertFalse(rs.Read());
            }

            Execute(stat, "ALTER TABLE TEST ADD COLUMN B INTEGER");
            using (DbDataReader rs = prep.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertTrue(rs.Read());
                AssertFalse(rs.Read());
            }
        }
    }
}
