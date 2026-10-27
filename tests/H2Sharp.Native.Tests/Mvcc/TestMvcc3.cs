// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.mvcc.TestMvcc3.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements/result sets are ADO.NET commands/readers;
// setAutoCommit(false) is an explicit transaction that each command carries,
// commit()/rollback() are that transaction's Commit/Rollback, setTransactionIsolation
// is the level the transaction begins with, and a JDBC Savepoint is
// DbTransaction.Save/Rollback(name). The reference's main() entry is the runner; xUnit
// runs Test() directly.
using System.Data;
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Mvcc;

/// <summary>
/// Additional MVCC (multi version concurrency) test cases.
/// </summary>
public sealed class TestMvcc3 : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestFailedUpdate();
        TestConcurrentUpdate();
        TestInsertUpdateRollback();
        TestCreateTableAsSelect();
        TestDisableAutoCommit();
        TestRollback();
        DeleteDb("mvcc3");
    }

    private void TestFailedUpdate()
    {
        DeleteDb("mvcc3");
        using DbConnection conn = GetConnection("mvcc3");
        using DbTransaction transaction = conn.BeginTransaction();
        DbCommand stat = conn.CreateCommand();
        stat.Transaction = transaction;
        Execute(stat, "create table test(id int primary key, a int unique, b int)");
        Execute(stat, "insert into test values(1, 1, 1)");
        Execute(stat, "insert into test values(2, 2, 2)");
        AssertThrows(ErrorCode.DuplicateKey1, stat,
            "update test set a = 1 where id = 2");
        stat.CommandText = "select * from test where id = 2";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
        }

        stat.CommandText = "select * from test where a = 2";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
        }

        stat.CommandText = "select * from test where b = 2";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
        }
    }

    private void TestConcurrentUpdate()
    {
        DeleteDb("mvcc3");
        using DbConnection c1 = GetConnection("mvcc3");
        using DbTransaction t1 = c1.BeginTransaction();
        DbCommand s1 = c1.CreateCommand();
        s1.Transaction = t1;
        using DbConnection c2 = GetConnection("mvcc3");
        using DbTransaction t2 = c2.BeginTransaction();
        DbCommand s2 = c2.CreateCommand();
        s2.Transaction = t2;

        // The reference's CREATE TABLE ... AS SELECT; the port's parser does not read
        // that form yet (issue #61), so the same rows come from a VALUES list.
        Execute(s1, "create table test(id int primary key, name varchar)");
        Execute(s1, "insert into test values (1, '1'), (2, '2')");
        Execute(s1, "create unique index on test(name)");
        _ = ExecuteUpdate(s1, "update test set name = 100 where id = 1");

        AssertThrows(typeof(H2SharpException),
            () => Execute(s2, "update test set name = 100 where id = 2"));

        s1.CommandText = "select * from test order by id";
        using (DbDataReader rs = s1.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
            AssertEquals("100", rs.GetString(1));
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertEquals("2", rs.GetString(1));
            AssertFalse(rs.Read());
        }

        s2.CommandText = "select * from test order by id";
        using (DbDataReader rs = s2.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
            AssertEquals("1", rs.GetString(1));
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertEquals("2", rs.GetString(1));
            AssertFalse(rs.Read());
        }
    }

    private void TestInsertUpdateRollback()
    {
        DeleteDb("mvcc3");
        using DbConnection c1 = GetConnection("mvcc3");
        DbCommand s1 = c1.CreateCommand();
        using DbConnection c2 = GetConnection("mvcc3");
        DbCommand s2 = c2.CreateCommand();

        // The reference's CREATE TABLE ... AS SELECT; see the header and issue #61.
        Execute(s1, "create table test(id int primary key, name varchar)");
        Execute(s1, "insert into test values (0, 'Hello')");
        using DbTransaction transaction = c1.BeginTransaction();
        s1.Transaction = transaction;
        _ = ExecuteUpdate(s1, "update test set name = 'World'");
        PrintRows("after update", s1, s2);
        transaction.Save("sp1");
        _ = ExecuteUpdate(s1, "delete from test");
        PrintRows("after delete", s1, s2);
        transaction.Rollback("sp1");
        PrintRows("after rollback delete", s1, s2);
        transaction.Rollback();
        // Modified (transaction shape): an ADO command keeps the transaction object it was
        // given; JDBC statements do not. Clear it before reading outside the transaction.
        s1.Transaction = null;
        PrintRows("after rollback all", s1, s2);

        s2.CommandText = "select * from test";
        using (DbDataReader rs = s2.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertFalse(rs.Read());
        }
    }

    private void PrintRows(string s, DbCommand s1, DbCommand s2)
    {
        Trace(s);
        s1.CommandText = "select * from test";
        using (DbDataReader rs = s1.ExecuteReader())
        {
            while (rs.Read())
            {
                Trace("s1: " + rs.GetString(1));
            }
        }

        s2.CommandText = "select * from test";
        using (DbDataReader rs = s2.ExecuteReader())
        {
            while (rs.Read())
            {
                Trace("s2: " + rs.GetString(1));
            }
        }
    }

    private void TestCreateTableAsSelect()
    {
        DeleteDb("mvcc3");
        using DbConnection c1 = GetConnection("mvcc3");
        DbCommand s1 = c1.CreateCommand();
        Execute(s1, "CREATE TABLE TEST AS SELECT X ID, 'Hello' NAME "
            + "FROM SYSTEM_RANGE(1, 3)");
        using DbConnection c2 = GetConnection("mvcc3");
        DbCommand s2 = c2.CreateCommand();
        s2.CommandText = "SELECT NAME FROM TEST WHERE ID=1";
        using (DbDataReader rs = s2.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("Hello", rs.GetString(0));
        }
    }

    private void TestRollback()
    {
        DeleteDb("mvcc3");
        DbConnection conn = GetConnection("mvcc3");
        DbCommand stat = conn.CreateCommand();
        _ = ExecuteUpdate(stat, "DROP TABLE IF EXISTS TEST");
        _ = ExecuteUpdate(stat, "CREATE TABLE TEST (ID NUMBER(2) PRIMARY KEY, "
            + "VAL VARCHAR(10))");
        _ = ExecuteUpdate(stat, "INSERT INTO TEST (ID, VAL) VALUES (1, 'Value')");
        _ = ExecuteUpdate(stat, "INSERT INTO TEST (ID, VAL) VALUES (2, 'Value')");
        if (!Config.Memory)
        {
            conn.Close();
            conn = GetConnection("mvcc3");
            stat = conn.CreateCommand();
        }

        using (conn)
        using (DbTransaction transaction = conn.BeginTransaction(IsolationLevel.ReadCommitted))
        {
            stat.Transaction = transaction;
            using DbConnection conn2 = GetConnection("mvcc3");
            using DbTransaction transaction2 = conn2.BeginTransaction(IsolationLevel.ReadCommitted);
            DbCommand stat2 = conn2.CreateCommand();
            stat2.Transaction = transaction2;

            using (DbCommand update = conn.CreateCommand())
            {
                update.Transaction = transaction;
                _ = ExecuteUpdate(update, "UPDATE TEST SET VAL='Updated' WHERE ID = 1");
            }

            transaction.Rollback();
            transaction.Dispose();

            using (DbDataReader rs = ExecuteQuery(conn2, transaction2,
                "SELECT * FROM TEST"))
            {
                AssertTrue(rs.Read());
                AssertEquals("Value", rs.GetString(1));
                AssertTrue(rs.Read());
                AssertEquals("Value", rs.GetString(1));
                AssertFalse(rs.Read());
            }

            using DbTransaction transaction3 = conn.BeginTransaction(IsolationLevel.ReadCommitted);
            using (DbCommand update = conn.CreateCommand())
            {
                update.Transaction = transaction3;
                update.CommandText = "UPDATE TEST SET VAL='Updated' WHERE ID = 1";
                _ = update.ExecuteNonQuery();
            }

            transaction3.Commit();
            using (DbDataReader rs = ExecuteQuery(conn2, transaction2,
                "SELECT * FROM TEST ORDER BY ID"))
            {
                AssertTrue(rs.Read());
                AssertEquals(1, rs.GetInt32(0));
                AssertEquals("Updated", rs.GetString(1));
                AssertTrue(rs.Read());
                AssertEquals(2, rs.GetInt32(0));
                AssertEquals("Value", rs.GetString(1));
                AssertFalse(rs.Read());
            }

            transaction2.Commit();
        }
    }

    private void TestDisableAutoCommit()
    {
        DeleteDb("mvcc3");
        using DbConnection conn = GetConnection("mvcc3");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY)");
        Execute(stat, "INSERT INTO TEST VALUES(0)");
        using DbTransaction transaction = conn.BeginTransaction();
        stat.Transaction = transaction;
        Execute(stat, "INSERT INTO TEST VALUES(1)");
        stat.CommandText = "SELECT * FROM TEST ORDER BY ID";
        using DbDataReader rs = stat.ExecuteReader();
        AssertTrue(rs.Read());
        AssertEquals(0, rs.GetInt32(0));
        AssertTrue(rs.Read());
        AssertEquals(1, rs.GetInt32(0));
    }

    private static int ExecuteUpdate(DbCommand command, string sql)
    {
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    private static DbDataReader ExecuteQuery(DbConnection conn, DbTransaction transaction,
        string sql)
    {
        DbCommand command = conn.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command.ExecuteReader();
    }
}
