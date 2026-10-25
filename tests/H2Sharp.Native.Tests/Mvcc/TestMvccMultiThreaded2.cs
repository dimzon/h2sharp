// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.mvcc.TestMvccMultiThreaded2.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements/result sets are ADO.NET commands/readers;
// setAutoCommit(false) is an explicit transaction that each command carries, and
// commit() is that transaction's Commit. The reference's worker Thread subclass is a
// local function run through Task.Run, and CountDownLatch is CountdownEvent. The
// reference's main() entry is the runner; xUnit runs Test() directly.
using Xunit;

namespace H2Sharp.Native.Tests.Mvcc;

/// <summary>
/// Additional MVCC (multi version concurrency) test cases.
/// </summary>
public sealed class TestMvccMultiThreaded2 : TestDb
{
    private const int TestThreadCount = 100;

    private const int TestTimeSeconds = 60;

    private const string Url = ";LOCK_TIMEOUT=120000";

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestSelectForUpdateConcurrency();
    }

    private int GetTestDuration()
    {
        // to save some testing time
        return Config.Big ? TestTimeSeconds : TestTimeSeconds / 10;
    }

    private void TestSelectForUpdateConcurrency()
    {
        DeleteDb(GetTestName());
        using (DbConnection conn = GetConnection(GetTestName() + Url))
        using (DbTransaction transaction = conn.BeginTransaction())
        {
            DbCommand smtm = conn.CreateCommand();
            smtm.Transaction = transaction;
            smtm.CommandText = "CREATE TABLE test ("
                + "entity_id INTEGER NOT NULL PRIMARY KEY, "
                + "lastUpdated INTEGER NOT NULL)";
            _ = smtm.ExecuteNonQuery();

            using DbCommand ps = conn.CreateCommand();
            ps.Transaction = transaction;
            ps.CommandText = "INSERT INTO test (entity_id, lastUpdated) VALUES (?, ?)";
            ps.Parameters.Add(new H2Parameter());
            ps.Parameters.Add(new H2Parameter());
            ps.Parameters[0].Value = 1;
            ps.Parameters[1].Value = 100;
            _ = ps.ExecuteNonQuery();
            ps.Parameters[0].Value = 2;
            ps.Parameters[1].Value = 200;
            _ = ps.ExecuteNonQuery();
            transaction.Commit();
        }

        using CountdownEvent latch = new(TestThreadCount + 1);
        Task[] threads = new Task[TestThreadCount];
        bool[] ok = new bool[TestThreadCount];
        int[] processed = new int[TestThreadCount];
        for (int i = 0; i < TestThreadCount; i++)
        {
            int index = i;
            threads[i] = Task.Run(() => SelectForUpdate(latch, index, ok, processed));
        }

        _ = latch.Signal();

        int minProcessed = int.MaxValue, maxProcessed = 0, totalProcessed = 0;
        bool allOk = true;
        for (int i = 0; i < TestThreadCount; i++)
        {
            threads[i].Wait();
            allOk &= ok[i];
            totalProcessed += processed[i];
            if (processed[i] > maxProcessed)
            {
                maxProcessed = processed[i];
            }

            if (processed[i] < minProcessed)
            {
                minProcessed = processed[i];
            }
        }

        // The reference prints these statistics only with DISPLAY_STATS (false).
        _ = minProcessed;
        _ = maxProcessed;
        _ = totalProcessed;

        DeleteDb(GetTestName());

        AssertTrue(allOk);
    }

    /// <summary>
    /// The reference's SelectForUpdate worker thread: selects rows for update until the
    /// test duration passes.
    /// </summary>
    /// <param name="latch">the start latch</param>
    /// <param name="index">the worker index</param>
    /// <param name="ok">the per-worker success flag</param>
    /// <param name="processed">the per-worker iteration counter</param>
    private void SelectForUpdate(CountdownEvent latch, int index, bool[] ok, int[] processed)
    {
        long start = Environment.TickCount64;
        bool done = false;
        try
        {
            using DbConnection conn = GetConnection(GetTestName() + Url);
            // give the other threads a chance to start up before going into the work loop
            _ = latch.Signal();
            latch.Wait();

            using DbCommand ps = conn.CreateCommand();
            ps.CommandText = "SELECT * FROM test WHERE entity_id = ? FOR UPDATE";
            ps.Parameters.Add(new H2Parameter());
            while (!done)
            {
                int value;
                if ((processed[index] & 1) == 0)
                {
                    ps.Parameters[0].Value = "1";
                    value = 100;
                }
                else
                {
                    ps.Parameters[0].Value = "2";
                    value = 200;
                }

                using DbTransaction transaction = conn.BeginTransaction();
                ps.Transaction = transaction;
                using (DbDataReader rs = ps.ExecuteReader())
                {
                    AssertTrue(rs.Read());
                    AssertTrue(rs.GetInt32(1) == value);
                }

                transaction.Commit();
                processed[index]++;

                long now = Environment.TickCount64;
                if (now - start > 1000 * GetTestDuration())
                {
                    done = true;
                }
            }

            ok[index] = true;
        }
        catch (Exception e)
        {
            LogError("SQL error from thread " + index, e);
            throw;
        }
    }
}
