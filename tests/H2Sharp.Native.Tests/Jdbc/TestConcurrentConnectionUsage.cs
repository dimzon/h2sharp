// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.jdbc.TestConcurrentConnectionUsage.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): the reference drives the same connection from several threads through
// org.h2.util.Task; the ported test uses .NET tasks and a cancellation source in the
// same shape: a loop that runs until the parent stops it. setAutoCommit is the explicit
// transaction of ADO.NET, and setBinaryStream is a Stream parameter value. The
// reference's main() entry is the runner; xUnit runs Test() directly.
namespace H2Sharp.Native.Tests.Jdbc;

/// <summary>
/// Test concurrent usage of the same connection.
/// </summary>
public sealed class TestConcurrentConnectionUsage : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestAutoCommit();
        TestSessionUsage(false);
        TestSessionUsage(true);
    }

    private void TestAutoCommit()
    {
        DeleteDb(GetTestName());
        DbConnection conn = GetConnection(GetTestName());
        DbCommand p1 = conn.CreateCommand();
        p1.CommandText = "select 1 from dual";
        using CancellationTokenSource stopped = new();
        Task task = Task.Run(() =>
        {
            while (!stopped.IsCancellationRequested)
            {
                using DbDataReader reader = p1.ExecuteReader();
                while (reader.Read())
                {
                    // just loop
                }

                using (DbTransaction transaction = conn.BeginTransaction())
                {
                    transaction.Commit();
                }
            }
        }, stopped.Token);
        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "select ? from dual";
        prep.Parameters.Add(new H2Parameter { Value = new MemoryStream(new byte[1024]) });
        for (int i = 0; i < 10; i++)
        {
            using DbDataReader reader = prep.ExecuteReader();
            while (reader.Read())
            {
                // just loop
            }
        }

        stopped.Cancel();
        task.Wait();
        conn.Close();
    }

    private void TestSessionUsage(bool useLegacy)
    {
        DeleteDb(GetTestName());
        using DbConnection infoConn = GetConnection(GetTestName() + ";OLD_INFORMATION_SCHEMA=" + useLegacy);
        DbCommand stmt = infoConn.CreateCommand();
        Execute(stmt, "CREATE TABLE t(id INT PRIMARY KEY)");

        int threadCount = 8;
        Task[] tasks = new Task[threadCount];
        using CancellationTokenSource stopped = new();
        for (int i = 0; i < threadCount; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                using DbConnection txConn = GetConnection(GetTestName());
                DbCommand st = txConn.CreateCommand();
                while (!stopped.IsCancellationRequested)
                {
                    st.CommandText = "MERGE INTO t(id) KEY (id) VALUES (0)";
                    _ = st.ExecuteNonQuery();
                }
            }, stopped.Token);
        }

        for (int i = 0; i < 1_000; i++)
        {
            stmt.CommandText = "SELECT ISOLATION_LEVEL FROM INFORMATION_SCHEMA.SESSIONS";
            using DbDataReader reader = stmt.ExecuteReader();
            while (reader.Read())
            {
                // just loop
            }
        }

        stopped.Cancel();
        Task.WaitAll(tasks);
    }
}
