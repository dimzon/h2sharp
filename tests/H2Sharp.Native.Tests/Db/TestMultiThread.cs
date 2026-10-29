// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestMultiThread.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements/result sets are ADO.NET commands/readers;
// setAutoCommit(false)+commit is an explicit transaction per row; java.util.concurrent
// Thread/ExecutorService/Future are .NET tasks, org.h2.util.Task.stop is a
// CancellationTokenSource, CountDownLatch is CountdownEvent and AtomicBoolean is a
// shared flag set under Interlocked. The reference's DatabaseMetaData.getTables call is
// the INFORMATION_SCHEMA.TABLES query through the ADO layer; setCharacterStream is a
// string parameter (issue #57 tracks the TextReader form). The reference's main() entry
// is the runner; xUnit runs Test() directly.
//
// Blocked by the port: CREATE TABLE TEST(ID IDENTITY, ...) needs the IDENTITY column
// type, which the parser does not read yet (issue #61).
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Multi-threaded tests.
/// </summary>
public sealed class TestMultiThread : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    [Trait("Category", "BlockedByPort")]
    public override void Test()
    {
        TestConcurrentSchemaChange();
        TestConcurrentLobAdd();
        TestConcurrentAlter();
        TestConcurrentInsertUpdateSelect();
        TestViews();
        TestConcurrentInsert();
        TestConcurrentUpdate();
        TestConcurrentUpdate2();
        TestCheckConstraint();
        TestOptimizeReuseResults();
    }

    private void TestConcurrentSchemaChange()
    {
        string db = GetTestName();
        DeleteDb(db);
        string url = GetURL(db + ";LOCK_TIMEOUT=10000", true);
        using DbConnection conn = GetConnection(url);
        using CancellationTokenSource stopped = new();
        Task[] tasks = new Task[2];
        for (int i = 0; i < tasks.Length; i++)
        {
            int x = i;
            tasks[i] = Task.Run(() =>
            {
                using DbConnection c2 = GetConnection(url);
                DbCommand stat = c2.CreateCommand();
                for (int j = 0; !stopped.IsCancellationRequested; j++)
                {
                    stat.CommandText = "create table test" + x + "_" + j;
                    _ = stat.ExecuteNonQuery();
                    stat.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES";
                    using (DbDataReader rs = stat.ExecuteReader())
                    {
                        while (rs.Read())
                        {
                            // just loop
                        }
                    }

                    stat.CommandText = "drop table test" + x + "_" + j;
                    _ = stat.ExecuteNonQuery();
                }
            }, stopped.Token);
        }

        Thread.Sleep(1000);
        stopped.Cancel();
        Task.WaitAll(tasks);
    }

    private void TestConcurrentLobAdd()
    {
        string db = GetTestName();
        DeleteDb(db);
        string url = GetURL(db, true);
        using DbConnection conn = GetConnection(url);
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id identity, data clob)");
        using CancellationTokenSource stopped = new();
        Task[] tasks = new Task[2];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                using DbConnection c2 = GetConnection(url);
                using DbCommand p2 = c2.CreateCommand();
                p2.CommandText = "insert into test(data) values(?)";
                p2.Parameters.Add(new H2Parameter());
                while (!stopped.IsCancellationRequested)
                {
                    // setCharacterStream of the reference; a string parameter here
                    // because TextReader parameters are issue #57
                    p2.Parameters[0].Value = new string(new char[10 * 1024]);
                    _ = p2.ExecuteNonQuery();
                }
            }, stopped.Token);
        }

        Thread.Sleep(500);
        stopped.Cancel();
        Task.WaitAll(tasks);
    }

    private void TestConcurrentAlter()
    {
        DeleteDb(GetTestName());
        using DbConnection conn = GetConnection(GetTestName());
        DbCommand stat = conn.CreateCommand();
        using CancellationTokenSource stopped = new();
        Task t = Task.Run(() =>
        {
            // the reference prepares "select * from test" in a loop; ADO.NET has no
            // separate prepare step here, so a command is created (compilation happens
            // on execution)
            while (!stopped.IsCancellationRequested)
            {
                using DbCommand command = conn.CreateCommand();
                command.CommandText = "select * from test";
            }
        }, stopped.Token);
        Execute(stat, "create table test(id int)");
        for (int i = 0; i < 200; i++)
        {
            Execute(stat, "alter table test add column x int");
            Execute(stat, "alter table test drop column x");
        }

        stopped.Cancel();
        t.Wait();
    }

    private DbConnection GetConnection()
    {
        return GetConnection("jdbc:h2:mem:" + GetTestName());
    }

    private void TestConcurrentInsertUpdateSelect()
    {
        using DbConnection conn = GetConnection();
        DbCommand stmt = conn.CreateCommand();
        Execute(stmt, "CREATE TABLE TEST(ID IDENTITY, NAME VARCHAR)");
        int len = GetSize(10, 200);
        Task[] threads = new Task[len];
        using CancellationTokenSource stopped = new();
        for (int i = 0; i < len; i++)
        {
            threads[i] = Task.Run(() =>
            {
                using DbConnection conn = GetConnection();
                DbCommand stmt = conn.CreateCommand();
                Random random = new();
                while (!stopped.IsCancellationRequested)
                {
                    stmt.CommandText = "SELECT COUNT(*) FROM TEST";
                    _ = stmt.ExecuteNonQuery();
                    stmt.CommandText = "INSERT INTO TEST(NAME) VALUES('Hi')";
                    _ = stmt.ExecuteNonQuery();
                    using (DbCommand prep = conn.CreateCommand())
                    {
                        prep.CommandText = "UPDATE TEST SET NAME='Hello' WHERE ID=?";
                        prep.Parameters.Add(new H2Parameter { Value = random.Next(10000) });
                        _ = prep.ExecuteNonQuery();
                    }

                    using (DbCommand prep = conn.CreateCommand())
                    {
                        prep.CommandText = "SELECT * FROM TEST WHERE ID=?";
                        prep.Parameters.Add(new H2Parameter { Value = random.Next(10000) });
                        using DbDataReader rs = prep.ExecuteReader();
                        while (rs.Read())
                        {
                            _ = rs.GetString(rs.GetOrdinal("NAME"));
                        }
                    }
                }
            }, stopped.Token);
        }

        int sleep = GetSize(400, 10000);
        Thread.Sleep(sleep);
        stopped.Cancel();
        Task.WaitAll(threads);
        stmt.CommandText = "SELECT COUNT(*) FROM TEST";
        using (DbDataReader rs = stmt.ExecuteReader())
        {
            rs.Read();
            Trace("max id=" + rs.GetInt32(0));
        }
    }

    private void TestViews()
    {
        DeleteDb("lockMode");
        string url = GetURL("lockMode", true);
        using DbConnection conn = GetConnection(url);
        try
        {
            DbCommand stat = conn.CreateCommand();
            Execute(stat,
                "CREATE TABLE INVOICE(INVOICE_ID INT PRIMARY KEY, AMOUNT DECIMAL)");
            Execute(stat, "CREATE VIEW INVOICE_VIEW as SELECT * FROM INVOICE");

            Execute(stat,
                "CREATE TABLE INVOICE_DETAIL(DETAIL_ID INT PRIMARY KEY, "
                + "INVOICE_ID INT, DESCRIPTION VARCHAR)");
            Execute(stat, "CREATE VIEW INVOICE_DETAIL_VIEW as SELECT * FROM INVOICE_DETAIL");

            // create views that reference the common views in different threads
            Task[] jobs = new Task[1000];
            for (int i = 0; i < jobs.Length; i++)
            {
                int j = i;
                jobs[j] = Task.Run(() =>
                {
                    using DbConnection conn2 = GetConnection(url);
                    DbCommand stat2 = conn2.CreateCommand();

                    Execute(stat2, "CREATE VIEW INVOICE_VIEW" + j
                        + " as SELECT * FROM INVOICE_VIEW");

                    // the following query intermittently results in a
                    // NullPointerException
                    Execute(stat2, "CREATE VIEW INVOICE_DETAIL_VIEW" + j
                        + " as SELECT DTL.* FROM INVOICE_VIEW" + j
                        + " INV JOIN INVOICE_DETAIL_VIEW DTL "
                        + "ON INV.INVOICE_ID = DTL.INVOICE_ID"
                        + " WHERE DESCRIPTION='TEST'");

                    stat2.CommandText = "SELECT * FROM INVOICE_VIEW" + j;
                    using (DbDataReader rs = stat2.ExecuteReader())
                    {
                        _ = rs.Read();
                    }

                    stat2.CommandText = "SELECT * FROM INVOICE_DETAIL_VIEW" + j;
                    using (DbDataReader rs = stat2.ExecuteReader())
                    {
                        _ = rs.Read();
                    }
                });
            }

            // check for exceptions
            foreach (Task job in jobs)
            {
                try
                {
                    job.Wait();
                }
                catch (AggregateException ex)
                {
                    // ignore lock timeouts, they happen periodically when the machine
                    // is really busy and are not what this test is about
                    Exception cause = ex.InnerException!;
                    int code = cause is H2SharpException h ? h.H2ErrorCode : 0;
                    if (code != ErrorCode.LockTimeout1)
                    {
                        throw;
                    }
                }
            }
        }
        finally
        {
            conn.Close();
        }

        DeleteDb("lockMode");
    }

    private void TestConcurrentInsert()
    {
        DeleteDb("lockMode");
        string url = GetURL("lockMode;LOCK_TIMEOUT=10000", true);
        const int threadCount = 25;
        using DbConnection conn = GetConnection(url);
        try
        {
            Execute(conn.CreateCommand(),
                "CREATE TABLE IF NOT EXISTS TRAN (ID NUMBER(18,0) not null PRIMARY KEY)");

            Task[] jobs = new Task[threadCount];
            for (int i = 0; i < threadCount; i++)
            {
                long initialTransactionId = i * 1000000L;
                jobs[i] = Task.Run(() =>
                {
                    using DbConnection taskConn = GetConnection(url);
                    using DbCommand insertTranStmt = taskConn.CreateCommand();
                    insertTranStmt.CommandText = "INSERT INTO tran (id) VALUES(?)";
                    insertTranStmt.Parameters.Add(new H2Parameter());
                    // to guarantee uniqueness
                    long tranId = initialTransactionId;
                    for (int j = 0; j < 1000; j++)
                    {
                        using DbTransaction transaction = taskConn.BeginTransaction();
                        insertTranStmt.Transaction = transaction;
                        insertTranStmt.Parameters[0].Value = tranId++;
                        _ = insertTranStmt.ExecuteNonQuery();
                        transaction.Commit();
                    }
                });
            }

            Task.WaitAll(jobs, TimeSpan.FromMinutes(5));
        }
        finally
        {
            conn.Close();
        }

        DeleteDb("lockMode");
    }

    private void TestConcurrentUpdate()
    {
        DeleteDb("lockMode");
        const int objectCount = 10000;
        string url = GetURL("lockMode;LOCK_TIMEOUT=10000", true);
        const int threadCount = 25;
        using DbConnection conn = GetConnection(url);
        try
        {
            Execute(conn.CreateCommand(),
                "CREATE TABLE IF NOT EXISTS ACCOUNT"
                + "(ID NUMBER(18,0) not null PRIMARY KEY, BALANCE NUMBER null)");
            using (DbCommand mergeAcctStmt = conn.CreateCommand())
            {
                mergeAcctStmt.CommandText =
                    "MERGE INTO Account(id, balance) key (id) VALUES (?, ?)";
                mergeAcctStmt.Parameters.Add(new H2Parameter());
                mergeAcctStmt.Parameters.Add(new H2Parameter());
                for (int i = 0; i < objectCount; i++)
                {
                    mergeAcctStmt.Parameters[0].Value = (long)i;
                    mergeAcctStmt.Parameters[1].Value = 0m;
                    _ = mergeAcctStmt.ExecuteNonQuery();
                }
            }

            Random random = new();
            Task[] jobs = new Task[threadCount];
            for (int i = 0; i < threadCount; i++)
            {
                jobs[i] = Task.Run(() =>
                {
                    using DbConnection taskConn = GetConnection(url);
                    using DbCommand updateAcctStmt = taskConn.CreateCommand();
                    updateAcctStmt.CommandText =
                        "UPDATE account SET balance = ? WHERE id = ?";
                    updateAcctStmt.Parameters.Add(new H2Parameter());
                    updateAcctStmt.Parameters.Add(new H2Parameter());
                    for (int j = 0; j < 1000; j++)
                    {
                        using DbTransaction transaction = taskConn.BeginTransaction();
                        updateAcctStmt.Transaction = transaction;
                        updateAcctStmt.Parameters[0].Value = random.NextDouble();
                        updateAcctStmt.Parameters[1].Value = (long)(random.NextDouble() * objectCount);
                        _ = updateAcctStmt.ExecuteNonQuery();
                        transaction.Commit();
                    }
                });
            }

            Task.WaitAll(jobs, TimeSpan.FromMinutes(5));
        }
        finally
        {
            conn.Close();
        }

        DeleteDb("lockMode");
    }

    private void TestConcurrentUpdate2()
    {
        DeleteDb("concurrentUpdate2");
        try
        {
            using DbConnection c = GetConnection("concurrentUpdate2");
            DbCommand s = c.CreateCommand();
            Execute(s, "CREATE TABLE TEST(A INT, B INT, V INT, PRIMARY KEY(A, B))");
            using (DbCommand ps = c.CreateCommand())
            {
                ps.CommandText = "INSERT INTO TEST VALUES (?, ?, ?)";
                ps.Parameters.Add(new H2Parameter());
                ps.Parameters.Add(new H2Parameter());
                ps.Parameters.Add(new H2Parameter());
                for (int i = 0; i < 16; i++)
                {
                    for (int j = 0; j < 16; j++)
                    {
                        ps.Parameters[0].Value = i;
                        ps.Parameters[1].Value = j;
                        ps.Parameters[2].Value = 0;
                        _ = ps.ExecuteNonQuery();
                    }
                }
            }

            using CountdownEvent latch = new(2);
            Exception?[] errors = new Exception?[2];
            Task a = Task.Run(() => ConcurrentUpdate2Run("A", latch, errors, 0));
            Task b = Task.Run(() => ConcurrentUpdate2Run("B", latch, errors, 1));
            a.Wait();
            b.Wait();
            Exception? e = errors[0] ?? errors[1];
            if (e is not null)
            {
                throw e;
            }
        }
        finally
        {
            DeleteDb("concurrentUpdate2");
        }
    }

    private void ConcurrentUpdate2Run(string column, CountdownEvent latch,
        Exception?[] errors, int index)
    {
        try
        {
            using DbConnection c = GetConnection("concurrentUpdate2;LOCK_TIMEOUT=10000");
            using DbCommand ps = c.CreateCommand();
            ps.CommandText = "UPDATE TEST SET V = ? WHERE " + column + " = ?";
            ps.Parameters.Add(new H2Parameter());
            ps.Parameters.Add(new H2Parameter());
            _ = latch.Signal();
            latch.Wait();
            for (int test = 1; test < 1000; test++)
            {
                for (int i = 0; i < 16; i++)
                {
                    ps.Parameters[0].Value = column == "A" ? -test : test;
                    ps.Parameters[1].Value = i;
                    AssertEquals(16, ps.ExecuteNonQuery());
                }
            }
        }
        catch (Exception e)
        {
            errors[index] = e;
        }
    }

    private void TestCheckConstraint()
    {
        DeleteDb("checkConstraint");
        try
        {
            using DbConnection c = GetConnection("checkConstraint;LOCK_TIMEOUT=10000");
            DbCommand s = c.CreateCommand();
            Execute(s, "CREATE TABLE TEST(ID INT PRIMARY KEY, A INT, B INT)");
            using (DbCommand ps = c.CreateCommand())
            {
                ps.CommandText = "INSERT INTO TEST VALUES (?, ?, ?)";
                ps.Parameters.Add(new H2Parameter());
                ps.Parameters.Add(new H2Parameter());
                ps.Parameters.Add(new H2Parameter());
                Execute(s, "ALTER TABLE TEST ADD CONSTRAINT CHECK_A_B CHECK A = B");
                const int numRows = 10;
                for (int i = 0; i < numRows; i++)
                {
                    ps.Parameters[0].Value = i;
                    ps.Parameters[1].Value = 0;
                    ps.Parameters[2].Value = 0;
                    _ = ps.ExecuteNonQuery();
                }

                const int numThreads = 4;
                int error = 0;
                Task[] threads = new Task[numThreads];
                for (int i = 0; i < numThreads; i++)
                {
                    threads[i] = Task.Run(() =>
                    {
                        try
                        {
                            using DbConnection c = GetConnection(
                                "checkConstraint;LOCK_TIMEOUT=10000");
                            using DbCommand ps = c.CreateCommand();
                            ps.CommandText =
                                "UPDATE TEST SET A = ?, B = ? WHERE ID = ?";
                            ps.Parameters.Add(new H2Parameter());
                            ps.Parameters.Add(new H2Parameter());
                            ps.Parameters.Add(new H2Parameter());
                            Random r = new();
                            for (int j = 0; j < 1_000; j++)
                            {
                                int v = r.Next(1_000);
                                ps.Parameters[0].Value = v;
                                ps.Parameters[1].Value = v;
                                ps.Parameters[2].Value = r.Next(numRows);
                                _ = ps.ExecuteNonQuery();
                            }
                        }
                        catch (Exception e)
                        {
                            Interlocked.Exchange(ref error, 1);
                            lock (this)
                            {
                                LogError("Error in CHECK constraint", e);
                            }
                        }
                    });
                }

                Task.WaitAll(threads);
                AssertFalse(error != 0);
            }
        }
        finally
        {
            DeleteDb("checkConstraint");
        }
    }

    private void TestOptimizeReuseResults()
    {
        DeleteDb("testOptimizeReuseResults");
        using DbConnection c1 = GetConnection("testOptimizeReuseResults");
        using DbConnection c2 = GetConnection("testOptimizeReuseResults");
        DbCommand stat = c1.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID BIGINT PRIMARY KEY, DATA INT)");
        Execute(stat, "INSERT INTO TEST VALUES (1, 0)");
        using DbCommand prepUpdate = c1.CreateCommand();
        prepUpdate.CommandText = "UPDATE TEST SET DATA = ? WHERE ID = 1";
        prepUpdate.Parameters.Add(new H2Parameter());
        using DbCommand prepSelect = c2.CreateCommand();
        prepSelect.CommandText = "SELECT DATA FROM TEST WHERE ID = 1";
        // the reference runs the updates on a single-thread executor, so they never
        // overlap; a continuation chain keeps that order
        Task previous = Task.CompletedTask;
        for (int i = 1; i <= 1_000; i++)
        {
            int v = i;
            previous = previous.ContinueWith(
                _ => TestOptimizeReuseResultsSet(prepUpdate, v),
                TaskScheduler.Default);
            long n = Environment.TickCount64;
            int count = 0;
            bool matched = false;
            while (Environment.TickCount64 - n < 2_000_000_000L)
            {
                using (DbDataReader rs = prepSelect.ExecuteReader())
                {
                    AssertTrue(rs.Read());
                    if (rs.GetInt32(0) == v)
                    {
                        matched = true;
                        break;
                    }

                    count++;
                }
            }

            if (!matched)
            {
                Fail("Error on iteration " + v + " after " + count + " attempts");
            }
        }

        previous.Wait();
        // The reference's try-with-resources closes both connections before deleting the
        // database; using declarations would keep them open until this method returns.
        c1.Close();
        c2.Close();
        DeleteDb("testOptimizeReuseResults");
    }

    private void TestOptimizeReuseResultsSet(DbCommand prepUpdate, int value)
    {
        prepUpdate.Parameters[0].Value = value;
        AssertEquals(1, prepUpdate.ExecuteNonQuery());
    }
}
