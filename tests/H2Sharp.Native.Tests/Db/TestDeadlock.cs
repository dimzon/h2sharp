// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestDeadlock.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; org.h2.util.Task is a .NET task
// with a cancellation source; the DoIt thread subclasses are lambdas over the same
// catchDeadlock sink; setAutoCommit(false) is an explicit transaction the statements
// carry, and commit() is the COMMIT statement. The fulltext alias stays the reference's
// Java class name, as written. The reference's main() entry is the runner; xUnit runs
// Test() directly.
using System.Diagnostics;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test for deadlocks in the code, and test the deadlock detection mechanism.
/// </summary>
public sealed class TestDeadlock : TestDb
{
    /// <summary>
    /// The first connection.
    /// </summary>
    private DbConnection? c1;

    /// <summary>
    /// The second connection.
    /// </summary>
    private DbConnection? c2;

    /// <summary>
    /// The third connection.
    /// </summary>
    private DbConnection? c3;

    /// <summary>
    /// The transactions of the three connections; the manual-commit mode of the JDBC
    /// connections.
    /// </summary>
    private DbTransaction? t1;

    private DbTransaction? t2;

    private DbTransaction? t3;

    private DbException? lastException;

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb("deadlock");
        TestTemporaryTablesAndMetaDataLocking();
        TestDeadlockInFulltextSearch();
        TestConcurrentLobReadAndTempResultTableDelete();
        TestNoDeadlock();
        DeleteDb("deadlock");
    }

    private void TestDeadlockInFulltextSearch()
    {
        DeleteDb("deadlock");
        string url = "deadlock";
        DbConnection conn, conn2;
        conn = GetConnection(url);
        conn2 = GetConnection(url);
        DbCommand stat = conn.CreateCommand();
        DbCommand stat2 = conn2.CreateCommand();
        Execute(stat, "create alias if not exists ft_init for "
            + "\"org.h2.fulltext.FullText.init\"");
        Execute(stat, "call ft_init()");
        Execute(stat, "create table test(id int primary key, name varchar)");
        Execute(stat, "call ft_create_index('PUBLIC', 'TEST', null)");
        using CancellationTokenSource stopped = new();
        Task task = Task.Run(() =>
        {
            while (!stopped.IsCancellationRequested)
            {
                stat.CommandText = "select * from test";
                using DbDataReader reader = stat.ExecuteReader();
                while (reader.Read())
                {
                    // just loop
                }
            }
        }, stopped.Token);
        long start = Stopwatch.GetTimestamp();
        while ((Stopwatch.GetTimestamp() - start) * 1000 / Stopwatch.Frequency < 1_000)
        {
            Execute(stat2, "insert into test values(1, 'Hello')");
            Execute(stat2, "delete from test");
        }

        stopped.Cancel();
        task.Wait();
        conn2.Close();
        conn.Close();
        conn = GetConnection(url);
        Execute(conn.CreateCommand(), "drop all objects");
        conn.Close();
    }

    private void TestConcurrentLobReadAndTempResultTableDelete()
    {
        DeleteDb("deadlock");
        string url = "deadlock;MAX_MEMORY_ROWS=10";
        DbConnection conn, conn2;
        DbCommand stat2;
        conn = GetConnection(url);
        conn2 = GetConnection(url);
        DbCommand stat = conn.CreateCommand();
        stat2 = conn2.CreateCommand();
        Execute(stat, "create table test(id int primary key, name varchar) as "
            + "select x, 'Hello' from system_range(1,20)");
        Execute(stat2, "create table test_clob(id int primary key, data clob) as "
            + "select 1, space(10000)");
        stat2.CommandText = "select * from test_clob";
        using DbDataReader rs2 = stat2.ExecuteReader();
        AssertTrue(rs2.Read());
        using CancellationTokenSource stopped = new();
        Task task = Task.Run(() =>
        {
            while (!stopped.IsCancellationRequested)
            {
                stat.CommandText = "select * from (select distinct id from test)";
                using DbDataReader reader = stat.ExecuteReader();
                while (reader.Read())
                {
                    // just loop
                }
            }
        }, stopped.Token);
        long start = Stopwatch.GetTimestamp();
        while ((Stopwatch.GetTimestamp() - start) * 1000 / Stopwatch.Frequency < 1_000)
        {
            using TextReader r = rs2.GetTextReader(1);
            char[] buff = new char[1024];
            while (true)
            {
                int x = r.Read(buff, 0, buff.Length);
                if (x <= 0)
                {
                    break;
                }
            }
        }

        stopped.Cancel();
        task.Wait();
        Execute(stat, "drop all objects");
        conn.Close();
        conn2.Close();
    }

    private void InitTest()
    {
        c1 = GetConnection("deadlock");
        c2 = GetConnection("deadlock");
        c3 = GetConnection("deadlock");
        Execute(c1.CreateCommand(), "SET LOCK_TIMEOUT 1000");
        Execute(c2.CreateCommand(), "SET LOCK_TIMEOUT 1000");
        Execute(c3.CreateCommand(), "SET LOCK_TIMEOUT 1000");
        // setAutoCommit(false): the statements of each connection carry its transaction
        t1 = c1.BeginTransaction();
        t2 = c2.BeginTransaction();
        t3 = c3.BeginTransaction();
        lastException = null;
    }

    private void End()
    {
        c1!.Close();
        c2!.Close();
        c3!.Close();
    }

    /// <summary>
    /// Add the exception to the list of exceptions.
    /// </summary>
    /// <param name="e">the exception</param>
    private void CatchDeadlock(DbException e)
    {
        if (lastException is not null)
        {
            // SQLException chains through setNextException; the inner exception is the
            // same chain.
            lastException = new DbExceptionChain(lastException, e);
        }
        else
        {
            lastException = e;
        }
    }

    /// <summary>
    /// The setNextException chain of the reference, carried as the inner exception.
    /// </summary>
    private sealed class DbExceptionChain : DbException
    {
        /// <summary>
        /// Builds the chain head.
        /// </summary>
        /// <param name="previous">the exception chained so far</param>
        /// <param name="next">the exception to append</param>
        public DbExceptionChain(DbException previous, DbException next)
            : base(previous.Message, next)
        {
        }
    }

    private void TestNoDeadlock()
    {
        InitTest();
        Execute(c1!.CreateCommand(), "CREATE TABLE TEST_A(ID INT PRIMARY KEY)");
        Execute(c1.CreateCommand(), "CREATE TABLE TEST_B(ID INT PRIMARY KEY)");
        Execute(c1.CreateCommand(), "CREATE TABLE TEST_C(ID INT PRIMARY KEY)");
        Execute(c1.CreateCommand(), "COMMIT");
        DbCommand insertA = c1.CreateCommand();
        insertA.Transaction = t1;
        insertA.CommandText = "INSERT INTO TEST_A VALUES(1)";
        _ = insertA.ExecuteNonQuery();
        DbCommand insertB = c2!.CreateCommand();
        insertB.Transaction = t2;
        insertB.CommandText = "INSERT INTO TEST_B VALUES(1)";
        _ = insertB.ExecuteNonQuery();
        DbCommand insertC = c3!.CreateCommand();
        insertC.Transaction = t3;
        insertC.CommandText = "INSERT INTO TEST_C VALUES(1)";
        _ = insertC.ExecuteNonQuery();

        // c1.commit(), c2.commit(), c3.commit(): the transaction wrapper commits and the
        // next statement of the still-manual-commit connection begins the next one.
        Task task2 = Task.Run(() =>
        {
            try
            {
                DbCommand command = c1!.CreateCommand();
                command.Transaction = t1;
                command.CommandText = "DELETE FROM TEST_B";
                _ = command.ExecuteNonQuery();
                t1!.Commit();
                t1 = c1.BeginTransaction();
            }
            catch (DbException e)
            {
                CatchDeadlock(e);
            }
        });
        Task task3 = Task.Run(() =>
        {
            try
            {
                DbCommand command = c2!.CreateCommand();
                command.Transaction = t2;
                command.CommandText = "DELETE FROM TEST_C";
                _ = command.ExecuteNonQuery();
                t2!.Commit();
                t2 = c2.BeginTransaction();
            }
            catch (DbException e)
            {
                CatchDeadlock(e);
            }
        });
        Thread.Sleep(500);
        try
        {
            DbCommand command = c3!.CreateCommand();
            command.Transaction = t3;
            command.CommandText = "DELETE FROM TEST_C";
            _ = command.ExecuteNonQuery();
            t3!.Commit();
            t3 = c3.BeginTransaction();
        }
        catch (DbException e)
        {
            CatchDeadlock(e);
        }

        Task.WaitAll(task2, task3);
        if (lastException is not null)
        {
            throw lastException;
        }

        t1!.Commit();
        t1 = c1!.BeginTransaction();
        t2!.Commit();
        t2 = c2!.BeginTransaction();
        t3!.Commit();
        t3 = c3!.BeginTransaction();
        DbCommand drop = c1.CreateCommand();
        drop.Transaction = t1;
        drop.CommandText = "DROP TABLE TEST_A, TEST_B, TEST_C";
        _ = drop.ExecuteNonQuery();
        End();
    }

    // there was a bug in the meta data locking here
    private void TestTemporaryTablesAndMetaDataLocking()
    {
        DeleteDb("deadlock");
        using DbConnection conn = GetConnection("deadlock");
        DbCommand stmt = conn.CreateCommand();
        using DbTransaction transaction = conn.BeginTransaction();
        stmt.Transaction = transaction;
        Execute(stmt, "CREATE SEQUENCE IF NOT EXISTS SEQ1 START WITH 1000000");
        Execute(stmt, "CREATE FORCE VIEW V1 AS WITH RECURSIVE TEMP(X) AS "
            + "(SELECT x FROM DUAL) SELECT * FROM TEMP");
        stmt.CommandText = "SELECT NEXT VALUE FOR SEQ1";
        using (DbDataReader reader = stmt.ExecuteReader())
        {
            while (reader.Read())
            {
                // just loop
            }
        }
    }
}
