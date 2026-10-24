// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestMemoryUsage.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands. The reference's main() entry is
// the runner; xUnit runs Test() directly.
//
// Modified (deferred, see MIGRATION_STATUS.md): testOpenCloseConnections injects a
// near-OOM through TestBase.eatMemory and recovers — the managed heap has no such
// controlled starvation, and an OutOfMemoryException in .NET is not a state the process
// recovers from deterministically. The part is skipped; the rest of the file (whose
// memory questions are measured, not injected) is ported.
using System.Diagnostics;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Tests the memory usage of the cache.
/// </summary>
public sealed class TestMemoryUsage : TestDb
{
    private DbConnection? conn;

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        // testOpenCloseConnections: deferred, see the header and MIGRATION_STATUS.md
        // (the eatMemory OOM injection has no managed counterpart).
        TestCreateIndex();
        TestClob();
        TestReconnectOften();

        DeleteDb("memoryUsage");
        Reconnect();
        InsertUpdateSelectDelete();
        Reconnect();
        InsertUpdateSelectDelete();
        conn!.Close();

        DeleteDb("memoryUsage");
    }

    private void TestClob()
    {
        if (Config.Memory || !Config.Big)
        {
            return;
        }

        DeleteDb("memoryUsageClob");
        conn = GetConnection("memoryUsageClob;WRITE_DELAY=0");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "SET MAX_LENGTH_INPLACE_LOB 8192");
        Execute(stat, "SET CACHE_SIZE 8000");
        Execute(stat, "CREATE TABLE TEST(ID IDENTITY, DATA CLOB)");
        try
        {
            long baseMemory = Util.Utils.GetMemoryUsed();
            for (int i = 0; i < 4; i++)
            {
                Execute(stat, "INSERT INTO TEST(DATA) "
                    + "SELECT SPACE(8000) FROM SYSTEM_RANGE(1, 800)");
                long used = Util.Utils.GetMemoryUsed();
                if ((used - baseMemory) > 3 * 8192)
                {
                    Fail("Used: " + (used - baseMemory) + " i: " + i);
                }
            }
        }
        finally
        {
            // The reference's freeMemory: the memory list is empty here, so the walk is
            // the collection alone.
            GC.Collect();
            conn!.Close();
        }
    }

    private void TestCreateIndex()
    {
        if (Config.Memory)
        {
            return;
        }

        DeleteDb("memoryUsage");
        conn = GetConnection("memoryUsage");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id int, name varchar(255))");
        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "insert into test values(?, space(200))";
            prep.Parameters.Add(new H2Parameter());
            int len = GetSize(10000, 100000);
            for (int i = 0; i < len; i++)
            {
                prep.Parameters[0].Value = i;
                _ = prep.ExecuteNonQuery();
            }
        }

        long baseMemory = Util.Utils.GetMemoryUsed();
        Execute(stat, "create index idx_test_id on test(id)");
        for (int i = 0; ; i++)
        {
            GC.Collect();
            long used = Util.Utils.GetMemoryUsed() - baseMemory;
            if (used <= GetSize(7500, 12000))
            {
                break;
            }

            if (i < 16)
            {
                continue;
            }

            Fail("Used: " + used);
        }

        Execute(stat, "drop table test");
        conn.Close();
    }

    private void TestReconnectOften()
    {
        DeleteDb("memoryUsage");
        DbConnection conn1 = GetConnection("memoryUsage");
        int len = GetSize(1, 2000);
        PrintTimeMemory("start", 0);
        long time = Stopwatch.GetTimestamp();
        for (int i = 0; i < len; i++)
        {
            DbConnection conn2 = GetConnection("memoryUsage");
            conn2.Close();
            if (i % 10000 == 0)
            {
                PrintTimeMemory("connect", (Stopwatch.GetTimestamp() - time) * 1000 / Stopwatch.Frequency);
            }
        }

        PrintTimeMemory("connect", (Stopwatch.GetTimestamp() - time) * 1000 / Stopwatch.Frequency);
        conn1.Close();
    }

    private void Reconnect()
    {
        if (conn is not null)
        {
            conn.Close();
        }

        conn = GetConnection("memoryUsage");
    }

    private void InsertUpdateSelectDelete()
    {
        DbCommand stat = conn!.CreateCommand();
        int len = GetSize(1, 2000);

        // insert
        long time = Stopwatch.GetTimestamp();
        Execute(stat, "DROP TABLE IF EXISTS TEST");
        Trace("drop=" + ((Stopwatch.GetTimestamp() - time) * 1000 / Stopwatch.Frequency));
        Execute(stat, "CREATE CACHED TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR(255))");
        DbCommand prep = conn.CreateCommand();
        prep.CommandText = "INSERT INTO TEST VALUES(?, 'Hello World')";
        prep.Parameters.Add(new H2Parameter());
        PrintTimeMemory("start", 0);
        time = Stopwatch.GetTimestamp();
        for (int i = 0; i < len; i++)
        {
            prep.Parameters[0].Value = i;
            _ = prep.ExecuteNonQuery();
            if (i % 50000 == 0)
            {
                Trace("  " + (100 * i / len) + "%");
            }
        }

        PrintTimeMemory("insert", (Stopwatch.GetTimestamp() - time) * 1000 / Stopwatch.Frequency);

        // update
        time = Stopwatch.GetTimestamp();
        prep = conn.CreateCommand();
        prep.CommandText = "UPDATE TEST SET NAME='Hallo Welt' || ID WHERE ID = ?";
        prep.Parameters.Add(new H2Parameter());
        for (int i = 0; i < len; i++)
        {
            prep.Parameters[0].Value = i;
            _ = prep.ExecuteNonQuery();
            if (i % 50000 == 0)
            {
                Trace("  " + (100 * i / len) + "%");
            }
        }

        PrintTimeMemory("update", (Stopwatch.GetTimestamp() - time) * 1000 / Stopwatch.Frequency);

        // select
        time = Stopwatch.GetTimestamp();
        prep = conn.CreateCommand();
        prep.CommandText = "SELECT * FROM TEST WHERE ID = ?";
        prep.Parameters.Add(new H2Parameter());
        for (int i = 0; i < len; i++)
        {
            prep.Parameters[0].Value = i;
            using (DbDataReader rs = prep.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertFalse(rs.Read());
            }

            if (i % 50000 == 0)
            {
                Trace("  " + (100 * i / len) + "%");
            }
        }

        PrintTimeMemory("select", (Stopwatch.GetTimestamp() - time) * 1000 / Stopwatch.Frequency);

        // select randomized
        Random random = new(1);
        time = Stopwatch.GetTimestamp();
        prep = conn.CreateCommand();
        prep.CommandText = "SELECT * FROM TEST WHERE ID = ?";
        prep.Parameters.Add(new H2Parameter());
        for (int i = 0; i < len; i++)
        {
            prep.Parameters[0].Value = random.Next(len);
            using (DbDataReader rs = prep.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertFalse(rs.Read());
            }

            if (i % 50000 == 0)
            {
                Trace("  " + (100 * i / len) + "%");
            }
        }

        PrintTimeMemory("select randomized", (Stopwatch.GetTimestamp() - time) * 1000 / Stopwatch.Frequency);

        // delete
        time = Stopwatch.GetTimestamp();
        prep = conn.CreateCommand();
        prep.CommandText = "DELETE FROM TEST WHERE ID = ?";
        prep.Parameters.Add(new H2Parameter());
        for (int i = 0; i < len; i++)
        {
            prep.Parameters[0].Value = random.Next(len);
            _ = prep.ExecuteNonQuery();
            if (i % 50000 == 0)
            {
                Trace("  " + (100 * i / len) + "%");
            }
        }

        PrintTimeMemory("delete", (Stopwatch.GetTimestamp() - time) * 1000 / Stopwatch.Frequency);
    }
}
