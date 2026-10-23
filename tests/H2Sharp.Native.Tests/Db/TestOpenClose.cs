// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestOpenClose.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; org.h2.util.Task is a .NET task;
// the JDK file calls of test1_1 are their System.IO counterparts. The reference's main()
// entry is the runner; xUnit runs Test() directly.
//
// Modified (port gaps, named): testErrorMessageWrongSplit needs the split file system
// (a "split:" URL and split-part files), and testBackup / testBackupWithYoungDeadChunks
// need BACKUP TO and the Restore tool — none of which are ported. The three are skipped
// here until they are; the rest of the file is ported.
using System.Diagnostics;
using H2Sharp.Api;
using H2Sharp.Engine;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Tests opening and closing a database.
/// </summary>
public sealed class TestOpenClose : TestDb
{
    private int nextId = 10;

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        // testErrorMessageWrongSplit, testBackup, testBackupWithYoungDeadChunks:
        // skipped, see the header (split file system, BACKUP TO and the Restore tool
        // are not ported).
        TestErrorMessageLocked();
        TestCloseDelay();
        TestCase();
        TestReconnectFast();
        Test1_1();
        DeleteDb("openClose");
    }

    private void TestErrorMessageLocked()
    {
        if (Config.Memory)
        {
            return;
        }

        DeleteDb("openClose");
        DbConnection conn;
        conn = GetConnection("jdbc:h2:" + GetBaseDir() + "/openClose;FILE_LOCK=FS");
        AssertThrows(ErrorCode.DatabaseAlreadyOpen1,
            () => _ = GetConnection("jdbc:h2:" + GetBaseDir() + "/openClose;FILE_LOCK=FS;OPEN_NEW=TRUE"));
        conn.Close();
    }

    private void TestCloseDelay()
    {
        DeleteDb("openClose");
        string url = GetURL("openClose;DB_CLOSE_DELAY=1", true);
        string user = GetUser(), password = GetPassword();
        DbConnection conn = new H2Connection(url + ";USER=" + user + ";PASSWORD=" + password);
        conn.Open();
        conn.Close();
        Thread.Sleep(950);
        long time = Stopwatch.GetTimestamp();
        while ((Stopwatch.GetTimestamp() - time) * 1000 / Stopwatch.Frequency < 100)
        {
            conn = new H2Connection(url + ";USER=" + user + ";PASSWORD=" + password);
            conn.Open();
            conn.Close();
        }

        conn = new H2Connection(url + ";USER=" + user + ";PASSWORD=" + password);
        conn.Open();
        Execute(conn.CreateCommand(), "SHUTDOWN");
        conn.Close();
    }

    private void TestReconnectFast()
    {
        if (Config.Memory)
        {
            return;
        }

        DeleteDb("openClose");
        string user = GetUser(), password = GetPassword();
        string url = GetURL("openClose;DATABASE_EVENT_LISTENER='"
            + typeof(MyDatabaseEventListener).FullName + "'", true);
        DbConnection conn = new H2Connection(url + ";USER=" + user + ";PASSWORD=" + password);
        conn.Open();
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID IDENTITY, NAME VARCHAR)");
        Execute(stat, "SET MAX_MEMORY_UNDO 100000");
        Execute(stat, "CREATE INDEX IDXNAME ON TEST(NAME)");
        Execute(stat, "INSERT INTO TEST SELECT X, X || ' Data' "
            + "FROM SYSTEM_RANGE(1, 1000)");
        conn.Close();
        conn = new H2Connection(url + ";USER=" + user + ";PASSWORD=" + password);
        conn.Open();
        stat = conn.CreateCommand();
        stat.CommandText = "SELECT * FROM SYSTEM_RANGE(1, 1)";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            if (rs.Read())
            {
                _ = rs.GetString(0);
            }
        }

        conn.Close();
        conn = new H2Connection(url + ";USER=" + user + ";PASSWORD=" + password);
        conn.Open();
        stat = conn.CreateCommand();
        // stat.execute("SET DB_CLOSE_DELAY 0");
        Execute(stat, "SHUTDOWN");
        conn.Close();
    }

    private void TestCase()
    {
        if (Config.Memory)
        {
            return;
        }

        DeleteDb("openClose");
        string url = GetURL("openClose;FILE_LOCK=NO", true);
        string user = GetUser(), password = GetPassword();
        DbConnection conn = new H2Connection(url + ";USER=" + user + ";PASSWORD=" + password);
        conn.Open();
        Execute(conn.CreateCommand(), "drop table employee if exists");
        Execute(conn.CreateCommand(),
            "create table employee(id int primary key, name varchar, salary int)");
        conn.Close();
        // previously using getSize(200, 1000);
        // but for Ubuntu, the default ulimit is 1024,
        // which breaks the test
        int len = GetSize(10, 50);
        Task[] tasks = new Task[len];
        using CancellationTokenSource stopped = new();
        for (int i = 0; i < len; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                DbConnection c = new H2Connection(url + ";USER=" + user + ";PASSWORD=" + password);
                c.Open();
                DbCommand prep = c.CreateCommand();
                prep.CommandText = "insert into employee values(?, ?, 0)";
                int id = GetNextId();
                prep.Parameters.Add(new H2Parameter { Value = id });
                prep.Parameters.Add(new H2Parameter { Value = "employee " + id });
                _ = prep.ExecuteNonQuery();
                c.Close();
            }, stopped.Token);
        }

        Task.WaitAll(tasks);
        conn = new H2Connection(url + ";USER=" + user + ";PASSWORD=" + password);
        conn.Open();
        DbCommand query = conn.CreateCommand();
        query.CommandText = "select count(*) from employee";
        using (DbDataReader rs = query.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(len, rs.GetInt32(0));
        }

        conn.Close();
    }

    /// <summary>
    /// The reference's synchronized getNextId.
    /// </summary>
    /// <returns>the next id</returns>
    private int GetNextId()
    {
        lock (this)
        {
            return nextId++;
        }
    }

    private void Test1_1()
    {
        string old = Path.Combine(GetBaseDir(), "db" + Constants.SuffixOldDatabaseFile);
        _ = File.Create(old);
        try
        {
            AssertThrows(ErrorCode.FileVersionError1,
                () => _ = GetConnection("jdbc:h2:" + GetBaseDir() + "/db"));
        }
        finally
        {
            File.Delete(old);
        }
    }

    /// <summary>
    /// A database event listener used in this test.
    /// </summary>
    public sealed class MyDatabaseEventListener : H2Sharp.Api.DatabaseEventListener
    {
        /// <inheritdoc/>
        void H2Sharp.Api.DatabaseEventListener.ExceptionThrown(DbException e, string sql)
        {
            throw new AssertionError("unexpected: " + e + " sql: " + sql);
        }

        /// <inheritdoc/>
        void H2Sharp.Api.DatabaseEventListener.SetProgress(int state, string name, long current, long max)
        {
            string stateName;
            switch (state)
            {
                case DatabaseEventListenerStates.StateScanFile:
                    stateName = "Scan " + name + " " + current + "/" + max;
                    if (current > 0)
                    {
                        throw new AssertionError("unexpected: " + stateName);
                    }

                    break;
                case DatabaseEventListenerStates.StateStatementStart:
                    break;
                case DatabaseEventListenerStates.StateCreateIndex:
                    stateName = "Create Index " + name + " " + current + "/" + max;
                    if (!"SYS:SYS_ID".Equals(name, StringComparison.Ordinal))
                    {
                        throw new AssertionError("unexpected: " + stateName);
                    }

                    break;
                case DatabaseEventListenerStates.StateRecover:
                    stateName = "Recover " + current + "/" + max;
                    break;
                default:
                    stateName = "?";
                    break;
            }
        }
    }
}
