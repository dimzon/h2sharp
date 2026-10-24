// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestMultiThreadedKernel.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; org.h2.util.Task is a .NET task
// and the Thread workers are tasks over the same stop flag and exception sink;
// JdbcUtils.closeSilently is a guarded Close. The reference's main() entry is the
// runner; xUnit runs Test() directly.
using System.Diagnostics;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// A multi-threaded test case.
/// </summary>
public sealed class TestMultiThreadedKernel : TestDb
{
    /// <summary>
    /// Stop the current thread.
    /// </summary>
    private volatile bool stop;

    /// <summary>
    /// The exception that occurred in the thread.
    /// </summary>
    private Exception? exception;

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb("multiThreadedKernel");
        TestConcurrentRead();
        TestCache();
        DeleteDb("multiThreadedKernel");
        string url = GetURL("multiThreadedKernel;DB_CLOSE_DELAY=-1", true);
        string user = GetUser(), password = GetPassword();
        int len = 3;
        Thread[] threads = new Thread[len];
        for (int i = 0; i < len; i++)
        {
            threads[i] = new Thread(() =>
            {
                DbConnection? conn = null;
                try
                {
                    for (int j = 0; j < 100 && !stop; j++)
                    {
                        conn = new H2Sharp.Data.H2Connection(
                            url + ";USER=" + user + ";PASSWORD=" + password);
                        conn.Open();
                        Work(conn);
                    }
                }
                catch (Exception e)
                {
                    exception = e;
                }
                finally
                {
                    try
                    {
                        conn?.Close();
                    }
                    catch (Exception)
                    {
                        // closeSilently
                    }
                }
            });
        }

        for (int i = 0; i < len; i++)
        {
            threads[i].Start();
        }

        Thread.Sleep(1000);
        stop = true;
        for (int i = 0; i < len; i++)
        {
            threads[i].Join();
        }

        DbConnection shutdownConnection = new H2Sharp.Data.H2Connection(
            url + ";USER=" + user + ";PASSWORD=" + password);
        shutdownConnection.Open();
        Execute(shutdownConnection.CreateCommand(), "shutdown");
        shutdownConnection.Close();
        if (exception is not null)
        {
            throw exception;
        }

        DeleteDb("multiThreadedKernel");
    }

    private void TestConcurrentRead()
    {
        int size = 2;
        int count = 1000;
        List<CancellationTokenSource> list = new(size);
        DbConnection[] connections = new DbConnection[size];
        string url = GetURL("multiThreadedKernel;CACHE_SIZE=16", true);
        for (int i = 0; i < size; i++)
        {
            DbConnection conn = new H2Sharp.Data.H2Connection(
                url + ";USER=" + GetUser() + ";PASSWORD=" + GetPassword());
            conn.Open();
            connections[i] = conn;
            if (i == 0)
            {
                DbCommand stat = conn.CreateCommand();
                Execute(stat, "drop table test if exists");
                Execute(stat, "create table test(id int primary key, name varchar) "
                    + "as select x, x || space(10) from system_range(1, " + count + ")");
            }

            Random random = new(i);
            CancellationTokenSource stopped = new();
            list.Add(stopped);
            _ = Task.Run(() =>
            {
                using DbCommand prep = conn.CreateCommand();
                prep.CommandText = "select * from test where id = ?";
                prep.Parameters.Add(new H2Parameter());
                while (!stopped.IsCancellationRequested)
                {
                    prep.Parameters[0].Value = random.Next(count);
                    using DbDataReader reader = prep.ExecuteReader();
                    while (reader.Read())
                    {
                        // just loop
                    }
                }
            }, stopped.Token);
        }

        Thread.Sleep(1000);
        foreach (CancellationTokenSource t in list)
        {
            t.Cancel();
        }

        for (int i = 0; i < size; i++)
        {
            connections[i].Close();
        }
    }

    private void TestCache()
    {
        int size = 3;
        int count = 100;
        List<CancellationTokenSource> list = new(size);
        DbConnection[] connections = new DbConnection[size];
        string url = GetURL("multiThreadedKernel;CACHE_SIZE=1", true);
        for (int i = 0; i < size; i++)
        {
            DbConnection conn = new H2Sharp.Data.H2Connection(
                url + ";USER=" + GetUser() + ";PASSWORD=" + GetPassword());
            conn.Open();
            connections[i] = conn;
            if (i == 0)
            {
                DbCommand stat = conn.CreateCommand();
                Execute(stat, "drop table test if exists");
                Execute(stat, "create table test(id int primary key, name varchar) "
                    + "as select x, space(3000) from system_range(1, " + count + ")");
            }

            Random random = new(i);
            CancellationTokenSource stopped = new();
            list.Add(stopped);
            _ = Task.Run(() =>
            {
                using DbCommand prep = conn.CreateCommand();
                prep.CommandText = "select * from test where id = ?";
                prep.Parameters.Add(new H2Parameter());
                while (!stopped.IsCancellationRequested)
                {
                    prep.Parameters[0].Value = random.Next(count);
                    using DbDataReader reader = prep.ExecuteReader();
                    while (reader.Read())
                    {
                        // just loop
                    }
                }
            }, stopped.Token);
        }

        Thread.Sleep(1000);
        foreach (CancellationTokenSource t in list)
        {
            t.Cancel();
        }

        for (int i = 0; i < size; i++)
        {
            connections[i].Close();
        }
    }

    /// <summary>
    /// The reference's private work: a temporary table of one row per connection, closed
    /// at once.
    /// </summary>
    /// <param name="conn">the connection</param>
    private void Work(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create local temporary table temp(id identity)");
        Execute(stat, "insert into temp values(1)");
        conn.Close();
    }

    /// <summary>
    /// The reference's getURL override, which appends the lock timeout.
    /// </summary>
    /// <param name="name">the database name</param>
    /// <param name="admin">true if the current user is an admin</param>
    /// <returns>the database URL</returns>
    protected override string GetURL(string name, bool admin)
    {
        return base.GetURL(name + ";LOCK_TIMEOUT=2000", admin);
    }
}
