// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestMultiConn.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; setAutoCommit(false) is an
// explicit transaction the statements carry, and commit()/rollback() are the transaction
// wrappers (the next statement of the still-manual-commit connection begins the next
// one). The reference's main() entry is the runner; xUnit runs Test() directly.
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Multi-connection tests.
/// </summary>
public sealed class TestMultiConn : TestDb
{
    /// <summary>
    /// How long to wait in milliseconds.
    /// </summary>
    /// <remarks>
    /// The reference never assigns it either — it is set from outside; the property
    /// carries that shape.
    /// </remarks>
    private static int Wait { get; set; }

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestConcurrentShutdownQuery();
        TestCommitRollback();
        TestConcurrentOpen();
        TestThreeThreads();
        DeleteDb("multiConn");
    }

    private void TestConcurrentShutdownQuery()
    {
        DbConnection conn1 = GetConnection("multiConn");
        DbConnection conn2 = GetConnection("multiConn");
        DbCommand stat1 = conn1.CreateCommand();
        Execute(stat1, "CREATE ALIAS SLEEP FOR 'java.lang.Thread.sleep(long)'");
        DbCommand stat2 = conn2.CreateCommand();
        Execute(stat1, "SET THROTTLE 100");
        using CancellationTokenSource stopped = new();
        Task task = Task.Run(() =>
        {
            stat2.CommandText = "CALL SLEEP(100)";
            using (DbDataReader reader = stat2.ExecuteReader())
            {
                while (reader.Read())
                {
                    // just loop
                }
            }

            Thread.Sleep(10);
            using (DbDataReader reader = stat2.ExecuteReader())
            {
                while (reader.Read())
                {
                    // just loop
                }
            }
        }, stopped.Token);
        Thread.Sleep(50);
        Execute(stat1, "SHUTDOWN");
        conn1.Close();
        try
        {
            conn2.Close();
        }
        catch (DbException)
        {
            // ignore
        }

        try
        {
            task.Wait();
        }
        catch (Exception)
        {
            // ignore
        }
    }

    private void TestThreeThreads()
    {
        DeleteDb("multiConn");
        DbConnection conn1 = GetConnection("multiConn");
        DbConnection conn2 = GetConnection("multiConn");
        DbConnection conn3 = GetConnection("multiConn");
        // setAutoCommit(false): each connection's statements carry its transaction
        DbTransaction tr1 = conn1.BeginTransaction();
        DbTransaction tr2 = conn2.BeginTransaction();
        DbTransaction tr3 = conn3.BeginTransaction();
        DbCommand s1 = conn1.CreateCommand();
        s1.Transaction = tr1;
        DbCommand s2 = conn2.CreateCommand();
        s2.Transaction = tr2;
        DbCommand s3 = conn3.CreateCommand();
        s3.Transaction = tr3;
        Execute(s1, "CREATE TABLE TEST1(ID INT)");
        Execute(s2, "CREATE TABLE TEST2(ID INT)");
        Execute(s3, "CREATE TABLE TEST3(ID INT)");
        Execute(s1, "INSERT INTO TEST1 VALUES(1)");
        Execute(s2, "INSERT INTO TEST2 VALUES(2)");
        Execute(s3, "INSERT INTO TEST3 VALUES(3)");
        Execute(s1, "SET LOCK_TIMEOUT 1000");
        Execute(s2, "SET LOCK_TIMEOUT 1000");
        Execute(s3, "SET LOCK_TIMEOUT 1000");
        Task task1 = Task.Run(() =>
        {
            try
            {
                Execute(s3, "INSERT INTO TEST2 VALUES(4)");
                tr3.Commit();
            }
            catch (DbException e)
            {
                LogError("insert", e);
            }
        });
        task1.Wait(20);
        Task task2 = Task.Run(() =>
        {
            try
            {
                Execute(s2, "INSERT INTO TEST1 VALUES(5)");
                tr2.Commit();
            }
            catch (DbException e)
            {
                LogError("insert", e);
            }
        });
        task2.Wait(20);
        tr1.Commit();
        task2.Wait(1000);
        task1.Wait(1000);
        s1.CommandText = "SELECT * FROM TEST1 ORDER BY ID";
        using (DbDataReader rs = s1.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
            AssertTrue(rs.Read());
            AssertEquals(5, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        conn1.Close();
        conn2.Close();
        conn3.Close();
    }

    private void TestConcurrentOpen()
    {
        if (Config.Memory || Config.GoogleAppEngine)
        {
            return;
        }

        DeleteDb("multiConn");
        DbConnection conn = GetConnection("multiConn");
        Execute(conn.CreateCommand(), "CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR)");
        Execute(conn.CreateCommand(), "INSERT INTO TEST VALUES(0, 'Hello'), (1, 'World')");
        Execute(conn.CreateCommand(), "SHUTDOWN");
        conn.Close();
        string listener = typeof(TestListener).FullName!;
        Thread thread = new(() =>
        {
            try
            {
                DbConnection c1 = GetConnection("multiConn;DATABASE_EVENT_LISTENER='" + listener
                    + "';file_lock=socket");
                c1.Close();
            }
            catch (Exception e)
            {
                LogError("connect", e);
            }
        });
        thread.Start();
        Thread.Sleep(10);
        DbConnection c2 = GetConnection("multiConn;file_lock=socket");
        c2.Close();
        thread.Join();
    }

    private void TestCommitRollback()
    {
        DeleteDb("multiConn");
        DbConnection c1 = GetConnection("multiConn");
        DbConnection c2 = GetConnection("multiConn");
        // setAutoCommit(false)
        DbTransaction tr1 = c1.BeginTransaction();
        DbTransaction tr2 = c2.BeginTransaction();
        DbCommand s1 = c1.CreateCommand();
        s1.Transaction = tr1;
        DbCommand s2 = c2.CreateCommand();
        s2.Transaction = tr2;
        Execute(s1, "DROP TABLE IF EXISTS MULTI_A");
        Execute(s1, "CREATE TABLE MULTI_A(ID INT, NAME VARCHAR(255))");
        Execute(s1, "INSERT INTO MULTI_A VALUES(0, '0-insert-A')");
        Execute(s1, "DROP TABLE IF EXISTS MULTI_B");
        Execute(s1, "CREATE TABLE MULTI_B(ID INT, NAME VARCHAR(255))");
        Execute(s2, "INSERT INTO MULTI_B VALUES(0, '1-insert-B')");
        tr1.Commit();
        tr2.Rollback();
        tr1 = c1.BeginTransaction();
        s1.Transaction = tr1;
        tr2 = c2.BeginTransaction();
        s2.Transaction = tr2;
        Execute(s1, "INSERT INTO MULTI_A VALUES(1, '0-insert-C')");
        Execute(s2, "INSERT INTO MULTI_B VALUES(1, '1-insert-D')");
        tr1.Rollback();
        tr2.Commit();
        c1.Close();
        c2.Close();

        if (!Config.Memory)
        {
            using DbConnection conn = GetConnection("multiConn");
            DbCommand query = conn.CreateCommand();
            query.CommandText = "SELECT * FROM MULTI_A ORDER BY ID";
            using (DbDataReader rs = query.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals("0-insert-A", rs.GetString(rs.GetOrdinal("NAME")));
                AssertFalse(rs.Read());
            }

            query.CommandText = "SELECT * FROM MULTI_B ORDER BY ID";
            using (DbDataReader rs = query.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals("1-insert-D", rs.GetString(rs.GetOrdinal("NAME")));
                AssertFalse(rs.Read());
            }
        }
    }

    /// <summary>
    /// A database event listener used in this test.
    /// </summary>
    public sealed class MyDatabaseEventListener : H2Sharp.Api.DatabaseEventListener
    {
        /// <inheritdoc/>
        void H2Sharp.Api.DatabaseEventListener.SetProgress(int state, string name, long x, long max)
        {
            if (Wait > 0)
            {
                try
                {
                    Thread.Sleep(Wait);
                }
                catch (Exception e)
                {
                    LogError("sleep", e);
                }
            }
        }
    }
}
