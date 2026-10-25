// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.mvcc.TestMvccMultiThreaded.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements/result sets are ADO.NET commands/readers;
// setAutoCommit(false) is an explicit transaction that each command carries, and
// commit() is that transaction's Commit. org.h2.util.Task is a .NET task whose stop flag
// is a CancellationTokenSource (as in the other ported concurrency tests), and
// CyclicBarrier is System.Threading.Barrier. The reference's main() entry is the
// runner; xUnit runs Test() directly.
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Mvcc;

/// <summary>
/// Multi-threaded MVCC (multi version concurrency) test cases.
/// </summary>
public sealed class TestMvccMultiThreaded : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestConcurrentSelectForUpdate();
        TestMergeWithUniqueKeyViolation();
        TestConcurrentMerge();
        TestConcurrentUpdate();
    }

    private void TestConcurrentSelectForUpdate()
    {
        DeleteDb(GetTestName());
        using DbConnection conn = GetConnection(GetTestName());
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id int not null primary key, updated int not null)");
        Execute(stat, "insert into test(id, updated) values(1, 100)");
        using CancellationTokenSource stopped = new();
        const int count = 3;
        Task[] tasks = new Task[count];
        for (int i = 0; i < count; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                using DbConnection conn = GetConnection(GetTestName());
                DbCommand stat = conn.CreateCommand();
                stat.CommandText = "select * from test where id=1 for update";
                while (!stopped.IsCancellationRequested)
                {
                    using DbDataReader rs = stat.ExecuteReader();
                    while (rs.Read())
                    {
                        // just loop
                    }
                }
            }, stopped.Token);
        }

        for (int i = 0; i < 10; i++)
        {
            Thread.Sleep(100);
            stat.CommandText = "select * from test";
            using DbDataReader rs = stat.ExecuteReader();
            AssertTrue(rs.Read());
        }

        stopped.Cancel();
        Task.WaitAll(tasks);
        DeleteDb(GetTestName());
    }

    private void TestMergeWithUniqueKeyViolation()
    {
        DeleteDb(GetTestName());
        using DbConnection conn = GetConnection(GetTestName());
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(x int primary key, y int unique)");
        Execute(stat, "insert into test values(1, 1)");
        AssertThrows(ErrorCode.DuplicateKey1, stat, "merge into test values(2, 1)");
        Execute(stat, "merge into test values(1, 2)");
    }

    private void TestConcurrentMerge()
    {
        DeleteDb(GetTestName());
        const int len = 3;
        DbConnection[] connList = new DbConnection[len];
        for (int i = 0; i < len; i++)
        {
            connList[i] = GetConnection(GetTestName() + ";LOCK_TIMEOUT=500");
        }

        try
        {
            DbCommand create = connList[0].CreateCommand();
            Execute(create, "create table test(id int primary key, name varchar)");
            using CancellationTokenSource stopped = new();
            Task[] tasks = new Task[len];
            for (int i = 0; i < len; i++)
            {
                DbConnection c = connList[i];
                tasks[i] = Task.Run(() =>
                {
                    while (!stopped.IsCancellationRequested)
                    {
                        using DbTransaction transaction = c.BeginTransaction();
                        using DbCommand command = c.CreateCommand();
                        command.Transaction = transaction;
                        command.CommandText = "merge into test values(1, 'x')";
                        _ = command.ExecuteNonQuery();
                        transaction.Commit();
                    }
                }, stopped.Token);
            }

            Thread.Sleep(1000);
            stopped.Cancel();
            Task.WaitAll(tasks);
        }
        finally
        {
            for (int i = 0; i < len; i++)
            {
                connList[i].Close();
            }
        }

        DeleteDb(GetTestName());
    }

    private void TestConcurrentUpdate()
    {
        DeleteDb(GetTestName());
        const int len = 2;
        DbConnection[] connList = new DbConnection[len];
        for (int i = 0; i < len; i++)
        {
            connList[i] = GetConnection(GetTestName());
        }

        try
        {
            DbCommand create = connList[0].CreateCommand();
            Execute(create, "create table test(id int primary key, v int)");
            Execute(create, "insert into test values(0, 0)");
            const int count = 1000;
            Task[] tasks = new Task[len];
            using Barrier barrier = new(len);
            for (int i = 0; i < len; i++)
            {
                DbConnection c = connList[i];
                tasks[i] = Task.Run(() =>
                {
                    for (int a = 0; a < count; a++)
                    {
                        using (DbTransaction transaction = c.BeginTransaction())
                        {
                            using DbCommand select = c.CreateCommand();
                            select.Transaction = transaction;
                            select.CommandText = "select v from test for update";
                            using (DbDataReader rs = select.ExecuteReader())
                            {
                                AssertTrue(rs.Read());
                            }

                            using DbCommand update = c.CreateCommand();
                            update.Transaction = transaction;
                            update.CommandText = "update test set v=v+1";
                            _ = update.ExecuteNonQuery();
                            transaction.Commit();
                        }

                        barrier.SignalAndWait();
                    }
                });
            }

            Task.WaitAll(tasks);
            DbCommand verify = connList[0].CreateCommand();
            verify.CommandText = "select v from test";
            using (DbDataReader rs = verify.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals(count * len, rs.GetInt32(0));
            }
        }
        finally
        {
            for (int i = 0; i < len; i++)
            {
                connList[i].Close();
            }
        }
    }
}
