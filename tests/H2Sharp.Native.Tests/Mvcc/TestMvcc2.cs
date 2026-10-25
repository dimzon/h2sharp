// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.mvcc.TestMvcc2.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; setAutoCommit(false) is an
// explicit transaction that each command carries, commit()/rollback() are that
// transaction's Commit/Rollback, and the next statement opens a fresh transaction as
// the reference's autocommit-off session does. org.h2.util.Task is a .NET task. The
// reference's main() entry is the runner; xUnit runs Test() directly.
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Mvcc;

/// <summary>
/// Additional MVCC (multi version concurrency) test cases.
/// </summary>
public sealed class TestMvcc2 : TestDb
{
    private const string DropTable = "DROP TABLE IF EXISTS EMPLOYEE";

    private const string CreateTable =
        "CREATE TABLE EMPLOYEE (id BIGINT, version BIGINT, NAME VARCHAR(255))";

    private const string Insert =
        "INSERT INTO EMPLOYEE (id, version, NAME) VALUES (1, 1, 'Jones')";

    private const string Update =
        "UPDATE EMPLOYEE SET NAME = 'Miller' WHERE version = 1";

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb("mvcc2");
        TestConcurrentInsert();
        TestConcurrentUpdate();
        TestSelectForUpdate();
        TestInsertUpdateRollback();
        TestInsertRollback();
        DeleteDb("mvcc2");
    }

    private DbConnection GetConnection()
    {
        return GetConnection("mvcc2");
    }

    private void TestConcurrentInsert()
    {
        using DbConnection conn = GetConnection();
        using DbConnection conn2 = GetConnection();
        DbCommand stat = conn.CreateCommand();
        DbCommand stat2 = conn2.CreateCommand();
        Execute(stat2, "set lock_timeout 1000");
        Execute(stat, "create table test(id int primary key, name varchar)");
        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            Task t = Task.Run(() =>
            {
                try
                {
                    stat2.CommandText = "insert into test values(0, 'Hallo')";
                    _ = stat2.ExecuteNonQuery();
                    Fail();
                }
                catch (Exception e)
                {
                    int code = e is H2SharpException h ? h.H2ErrorCode : 0;
                    AssertTrue(e.ToString(),
                        code == ErrorCode.DuplicateKey1
                        || code == ErrorCode.ConcurrentUpdate1);
                }
            });
            Execute(stat, "insert into test values(0, 'Hello')");
            transaction.Commit();
            t.Wait();
        }

        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            stat.CommandText = "select name from test";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals("Hello", rs.GetString(0));
            }

            Execute(stat, "drop table test");
            transaction.Commit();
        }
    }

    private void TestConcurrentUpdate()
    {
        using DbConnection conn = GetConnection();
        using DbConnection conn2 = GetConnection();
        DbCommand stat = conn.CreateCommand();
        DbCommand stat2 = conn2.CreateCommand();
        Execute(stat2, "set lock_timeout 1000");
        Execute(stat, "create table test(id int primary key, name varchar)");
        Execute(stat, "insert into test values(0, 'Hello')");
        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            Task t = Task.Run(() =>
            {
                AssertEquals(1, ExecuteUpdate(stat2, "update test set name = 'Hallo'"));
            });
            AssertEquals(1, ExecuteUpdate(stat, "update test set name = 'Hi'"));
            transaction.Commit();
            t.Wait();
        }

        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            stat.CommandText = "select name from test";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals("Hallo", rs.GetString(0));
            }

            Execute(stat, "drop table test");
            transaction.Commit();
        }
    }

    private void TestSelectForUpdate()
    {
        using DbConnection conn = GetConnection();
        using DbConnection conn2 = GetConnection();
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id int primary key, name varchar)");
        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            // The reference's INSERT ... SELECT; the port's parser does not read that
            // form yet (issue #61), so the same rows come from a VALUES list.
            Execute(stat, "insert into test values"
                + " (1, 'Hello'), (2, 'Hello'), (3, 'Hello'), (4, 'Hello'), (5, 'Hello'),"
                + " (6, 'Hello'), (7, 'Hello'), (8, 'Hello'), (9, 'Hello'), (10, 'Hello')");
            Execute(stat, "select * from test where id = 3 for update");
            transaction.Commit();
        }

        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            Execute(stat, "select * from test where id = 3 for update");
            transaction.Commit();
        }

        using (DbTransaction transaction = conn2.BeginTransaction())
        {
            DbCommand stat2 = conn2.CreateCommand();
            stat2.Transaction = transaction;
            Execute(stat2, "select * from test where id = 4 for update");
            AssertThrows(ErrorCode.LockTimeout1, stat2,
                "select * from test where id = 3 for update");
            transaction.Rollback();
        }
    }

    private void TestInsertUpdateRollback()
    {
        using DbConnection conn = GetConnection();
        using (DbTransaction transaction = conn.BeginTransaction())
        {
            DbCommand stmt = conn.CreateCommand();
            stmt.Transaction = transaction;
            Execute(stmt, DropTable);
            Execute(stmt, CreateTable);
            transaction.Commit();
        }

        using (DbTransaction transaction = conn.BeginTransaction())
        {
            DbCommand stmt = conn.CreateCommand();
            stmt.Transaction = transaction;
            Execute(stmt, Insert);
            Execute(stmt, Update);
            transaction.Rollback();
        }
    }

    private void TestInsertRollback()
    {
        using DbConnection conn = GetConnection();
        using (DbTransaction transaction = conn.BeginTransaction())
        {
            DbCommand stmt = conn.CreateCommand();
            stmt.Transaction = transaction;
            Execute(stmt, DropTable);
            Execute(stmt, CreateTable);
            transaction.Commit();
        }

        using (DbTransaction transaction = conn.BeginTransaction())
        {
            DbCommand stmt = conn.CreateCommand();
            stmt.Transaction = transaction;
            Execute(stmt, Insert);
            transaction.Rollback();
        }
    }

    private static int ExecuteUpdate(DbCommand command, string sql)
    {
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }
}
