// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.unit.TestMultiThreadedKernel.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): the reference class is both the test and its worker Runnable; here the
// worker is a local function, and the master's volatile stop flag is a
// CancellationTokenSource (as in the other ported concurrency tests). JDBC DriverManager
// is Driver.Load plus the ADO.NET connection; the reference's main() entry is the
// runner, xUnit runs Test() directly.
using Xunit;

namespace H2Sharp.Native.Tests.Unit;

/// <summary>
/// Tests the multi-threaded kernel feature.
/// </summary>
public sealed class TestMultiThreadedKernel : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb("multiThreadedKernel");
        int count = GetSize(2, 5);
        Task[] list = new Task[count];
        using CancellationTokenSource stopped = new();
        for (int i = 0; i < count; i++)
        {
            int id = i;
            string url = GetURL("multiThreadedKernel", true);
            list[i] = Task.Run(() => Run(url, id, stopped.Token));
        }

        Thread.Sleep(GetSize(2000, 5000));
        stopped.Cancel();
        Task.WaitAll(list);
        DeleteDb("multiThreadedKernel");
    }

    private void Run(string url, int id, CancellationToken token)
    {
        _ = Driver.Load();
        using DbConnection conn = CreateSharedConnection(url + ";LOCK_MODE=3;WRITE_DELAY=0");
        conn.Open();
        DbCommand stat = conn.CreateCommand();
        stat.CommandText = "CREATE TABLE TEST" + id
            + "(COL1 BIGINT AUTO_INCREMENT PRIMARY KEY, COL2 BIGINT)";
        _ = stat.ExecuteNonQuery();
        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "insert into TEST" + id + "(col2) values (?)";
        prep.Parameters.Add(new H2Parameter());
        for (int i = 0; !token.IsCancellationRequested; i++)
        {
            prep.Parameters[0].Value = (long)i;
            _ = prep.ExecuteNonQuery();
        }
    }
}

