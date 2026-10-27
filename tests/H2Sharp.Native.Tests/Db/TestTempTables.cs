// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestTempTables.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; setAutoCommit(false)/commit/
// rollback are explicit transactions the statements after them carry. The session
// introspection of testTempSequence (getDatabase().getMainSchema().getAllSequences())
// is the INFORMATION_SCHEMA.SEQUENCES count through the ADO layer — the table the
// engine feeds. The reference's main() entry is the runner; xUnit runs Test() directly.
//
// Modified (per docs/NATIVE_TESTS_DISCUSSION.md): testTempFileResultSet and
// testTempTableResultSet drive TYPE_SCROLL_INSENSITIVE result sets (beforeFirst and
// re-reading); the ADO.NET reader is forward-only, so the two scrollable halves become
// TestTempResultSetRetention — the same shared temp result table, with the retention
// check expressed as "closing one reader does not disturb the survivor", which is what
// the scroll re-read verified in the reference.
using H2Sharp.Api;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Temporary table tests.
/// </summary>
public sealed class TestTempTables : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    [Trait("Category", "BlockedByPort")]
    public override void Test()
    {
        DeleteDb("tempTables");
        TestAnalyzeReuseObjectId();
        TestTempSequence();
        TestTempResultSetRetention();
        TestTransactionalTemp();
        using (DbConnection c1 = GetConnection("tempTables"))
        {
            TestAlter(c1);
            using DbConnection c2 = GetConnection("tempTables");
            TestConstraints(c1, c2);
            TestTables(c1, c2);
            TestIndexes(c1, c2);
        }

        TestLotsOfTables();
        TestCreateAsSelectDistinct();
        DeleteDb("tempTables");
    }

    private void TestAnalyzeReuseObjectId()
    {
        DeleteDb("tempTables");
        using DbConnection conn = GetConnection("tempTables");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create local temporary table test(id identity)");
        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "insert into test default values";
        for (int i = 0; i < 10000; i++)
        {
            _ = prep.ExecuteNonQuery();
        }

        Execute(stat, "create local temporary table "
            + "test2(id identity) as select x from system_range(1, 10)");
    }

    private void TestTempSequence()
    {
        DeleteDb("tempTables");
        using DbConnection conn = GetConnection("tempTables");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create local temporary table test(id identity)");
        AssertEquals(1L, CountSequences(conn));
        Execute(stat, "insert into test default values");
        Execute(stat, "shutdown");
        conn.Close();
        using (DbConnection conn2 = GetConnection("tempTables"))
        {
            AssertEquals(0L, CountSequences(conn2));
        }
    }

    private long CountSequences(DbConnection conn)
        => ((H2Connection)conn).Session!.GetDatabase().GetMainSchema()!.GetAllSequences().Count;

    /// <summary>
    /// The reference's testTempFileResultSet and testTempTableResultSet. Two statements
    /// each spill a result larger than MAX_MEMORY_ROWS into a temp table, the statements
    /// read them interleaved, one result set is closed, and the reference then re-reads
    /// the other via beforeFirst to verify its temp table was not deleted. The ADO.NET
    /// reader is forward-only; the re-read becomes: close the sibling, then keep reading
    /// the survivor to the end.
    /// </summary>
    private void TestTempResultSetRetention()
    {
        if (Config.Lazy)
        {
            return;
        }

        DeleteDb("tempTables");
        using DbConnection conn = GetConnection("tempTables;MAX_MEMORY_ROWS=10");
        DbCommand stat1 = conn.CreateCommand();
        DbCommand stat2 = conn.CreateCommand();

        // The reference's first pass: two readers over the same query, the first reader
        // is closed while the second still has rows to serve.
        RetentionPass(stat1, stat2, "select * from system_range(1, 20)", true);

        // The reference's second pass: ordered result, the other reader is closed first.
        RetentionPass(stat1, stat2,
            "select * from system_range(1, 20) order by x desc", false);

        // The reference's testTempTableResultSet: SELECT DISTINCT re-uses the same temp
        // table for both readers, so the survivor's table is the one that must not be
        // deleted.
        RetentionPass(stat1, stat2, "select distinct * from system_range(1, 20)", true);
    }

    /// <summary>
    /// Read two result sets over the same query interleaved; close the sibling after the
    /// first ten rows; verify the survivor keeps serving the remaining ten.
    /// </summary>
    /// <param name="stat1">the first command</param>
    /// <param name="stat2">the second command</param>
    /// <param name="sql">the query</param>
    /// <param name="closeFirst">whether the first reader is the one to close</param>
    private void RetentionPass(DbCommand stat1, DbCommand stat2, string sql,
        bool closeFirst)
    {
        stat1.CommandText = sql;
        stat2.CommandText = sql;
        using (DbDataReader r1 = stat1.ExecuteReader())
        using (DbDataReader r2 = stat2.ExecuteReader())
        {
            for (int i = 0; i < 10; i++)
            {
                AssertTrue(r1.Read());
                AssertTrue(r2.Read());
                _ = r1.GetInt32(0);
                _ = r2.GetInt32(0);
            }

            DbDataReader survivor = closeFirst ? r2 : r1;
            survivor.Close();
            // verify the temp table is not deleted yet: the other reader still serves rows
            DbDataReader remaining = closeFirst ? r1 : r2;
            for (int i = 10; i < 20; i++)
            {
                AssertTrue(remaining.Read());
                _ = remaining.GetInt32(0);
            }

            AssertFalse(remaining.Read());
        }
    }

    private void TestTransactionalTemp()
    {
        DeleteDb("tempTables");
        using DbConnection conn = GetConnection("tempTables");
        DbCommand stat = conn.CreateCommand();
        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            Execute(stat, "create table test(id int primary key)");
            Execute(stat, "insert into test values(1)");
            transaction.Commit();
        }

        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            Execute(stat, "insert into test values(2)");
            Execute(stat, "create local temporary table temp("
                + "id int primary key, name varchar, constraint x unique(name)) transactional");
            Execute(stat, "insert into temp values(3, 'test')");
            transaction.Rollback();
        }

        stat.Transaction = null;
        stat.CommandText = "select * from test";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertFalse(rs.Read());
        }

        Execute(stat, "drop table test");
        Execute(stat, "drop table temp");
    }

    private void TestAlter(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create temporary table test(id varchar)");
        Execute(stat, "create index idx1 on test(id)");
        Execute(stat, "drop index idx1");
        Execute(stat, "create index idx1 on test(id)");
        Execute(stat, "insert into test select x from system_range(1, 10)");
        AssertThrows(ErrorCode.FeatureNotSupported1,
            () => Execute(stat, "alter table test add column x int"));
        Execute(stat, "drop table test");
    }

    private void TestConstraints(DbConnection conn1, DbConnection conn2)
    {
        DbCommand s1 = conn1.CreateCommand(), s2 = conn2.CreateCommand();
        Execute(s1, "create local temporary table test(id int unique)");
        Execute(s2, "create local temporary table test(id int unique)");
        Execute(s1, "alter table test add constraint a unique(id)");
        Execute(s2, "alter table test add constraint a unique(id)");
        Execute(s1, "drop table test");
        Execute(s2, "drop table test");
    }

    private void TestIndexes(DbConnection conn1, DbConnection conn2)
    {
        DbCommand s1 = conn1.CreateCommand(), s2 = conn2.CreateCommand();
        Execute(s1, "create local temporary table test(id int)");
        Execute(s1, "create index idx_id on test(id)");
        Execute(s2, "create local temporary table test(id int)");
        Execute(s2, "create index idx_id on test(id)");
        Execute(s2, "drop index idx_id");
        Execute(s2, "drop table test");
        Execute(s2, "create table test(id int)");
        Execute(s2, "create index idx_id on test(id)");
        Execute(s1, "drop table test");
        Execute(s1, "drop table test");
    }

    private void TestTables(DbConnection c1, DbConnection c2)
    {
        DbCommand s1 = c1.CreateCommand();
        DbCommand s2 = c2.CreateCommand();
        Execute(s1, "CREATE LOCAL TEMPORARY TABLE LT(A INT)");
        Execute(s1, "CREATE GLOBAL TEMPORARY TABLE GT1(ID INT)");
        Execute(s2, "CREATE GLOBAL TEMPORARY TABLE GT2(ID INT)");
        Execute(s2, "CREATE LOCAL TEMPORARY TABLE LT(B INT)");
        Execute(s2, "SELECT B FROM LT");
        Execute(s1, "SELECT A FROM LT");
        Execute(s1, "SELECT * FROM GT1");
        Execute(s2, "SELECT * FROM GT1");
        Execute(s1, "SELECT * FROM GT2");
        Execute(s2, "SELECT * FROM GT2");
        Execute(s2, "DROP TABLE GT1");
        Execute(s2, "DROP TABLE GT2");
        Execute(s2, "DROP TABLE LT");
        Execute(s1, "DROP TABLE LT");

        // temp tables: 'on commit' syntax is currently not documented, because
        // not tested well
        // and hopefully nobody is using it, as it looks like functional sugar
        // (this features are here for compatibility only)
        DbTransaction transaction = c1.BeginTransaction();
        s1.Transaction = transaction;
        Execute(s1, "create local temporary table test_temp(id int) "
            + "on commit delete rows");
        Execute(s1, "insert into test_temp values(1)");
        s1.CommandText = "select * from test_temp";
        using (DbDataReader rs = s1.ExecuteReader())
        {
            AssertResultRowCount(1, rs);
        }

        transaction.Commit();
        s1.Transaction = null;
        s1.CommandText = "select * from test_temp";
        using (DbDataReader rs = s1.ExecuteReader())
        {
            AssertResultRowCount(0, rs);
        }

        Execute(s1, "drop table test_temp");

        transaction = c1.BeginTransaction();
        s1.Transaction = transaction;
        Execute(s1, "create local temporary table test_temp(id int) on commit drop");
        Execute(s1, "insert into test_temp values(1)");
        s1.CommandText = "select * from test_temp";
        using (DbDataReader rs = s1.ExecuteReader())
        {
            AssertResultRowCount(1, rs);
        }

        transaction.Commit();
        s1.Transaction = null;
        // test_temp should have been dropped automatically
        AssertThrows(ErrorCode.TableOrViewNotFoundDatabaseEmpty1,
            () => Query(s1, "select * from test_temp"));
    }

    /// <summary>
    /// There was a bug where creating lots of tables would overflow the
    /// transaction table in the MVStore.
    /// </summary>
    private void TestLotsOfTables()
    {
        if (Config.Networked || Config.Throttle > 0)
        {
            return; // just to save some testing time
        }

        DeleteDb("tempTables");
        using DbConnection conn = GetConnection("tempTables");
        DbCommand stat = conn.CreateCommand();
        for (int i = 0; i < 100000; i++)
        {
            stat.CommandText = "create local temporary table t(id int)";
            _ = stat.ExecuteNonQuery();
            stat.CommandText = "drop table t";
            _ = stat.ExecuteNonQuery();
        }

        conn.Close();
    }

    /// <summary>
    /// Issue #401: NPE in "SELECT DISTINCT * ORDER BY".
    /// </summary>
    private void TestCreateAsSelectDistinct()
    {
        DeleteDb("tempTables");
        using DbConnection conn = GetConnection("tempTables;MAX_MEMORY_ROWS=1000");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE ONE(S1 VARCHAR(255), S2 VARCHAR(255))");
        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "insert into one values(?,?)";
        prep.Parameters.Add(new H2Parameter());
        prep.Parameters.Add(new H2Parameter());
        for (int row = 0; row < 10000; row++)
        {
            prep.Parameters[0].Value = "abc";
            prep.Parameters[1].Value = "def" + row;
            _ = prep.ExecuteNonQuery();
        }

        Execute(stat, "CREATE TABLE TWO AS SELECT DISTINCT * FROM ONE ORDER BY S1");
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
