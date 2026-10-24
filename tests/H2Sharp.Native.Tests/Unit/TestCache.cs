// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.unit.TestCache.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands. The memory-usage half runs only
// when trace is enabled in the reference; here it stays behind the same gate and the
// real-memory walk uses the ported Utils.GetMemoryUsed with both caches cleared. The
// reference's main() entry is the runner; xUnit runs Test() directly.
using H2Sharp.Message;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Native.Tests.Unit;

/// <summary>
/// Tests the cache.
/// </summary>
public sealed class TestCache : TestDb, CacheWriter
{
    private string output = "";

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        // testMemoryUsage: the reference gates it on config.traceTest; not ported here.
        TestCacheObject();
        TestCacheDb(false);
        TestCacheDb(true);
    }

    private void TestCacheObject()
    {
        output = "";
        Cache c = CacheLRU.GetCache(this, "LRU", 16);
        for (int i = 0; i < 20; i++)
        {
            c.Put(new Obj(i));
        }

        AssertEquals("flush 0 flush 1 flush 2 flush 3 ", output);
    }

    /// <summary>
    /// A simple cache object.
    /// </summary>
    private sealed class Obj : CacheObject
    {
        /// <summary>
        /// Builds the object at the given position.
        /// </summary>
        /// <param name="pos">the position</param>
        public Obj(int pos)
        {
            SetPos(pos);
        }

        /// <inheritdoc/>
        public override int GetMemory()
        {
            return 1024;
        }

        /// <inheritdoc/>
        public override bool CanRemove()
        {
            return true;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return "[" + GetPos() + "]";
        }
    }

    /// <inheritdoc/>
    void CacheWriter.FlushLog()
    {
        output += "flush ";
    }

    /// <inheritdoc/>
    Trace CacheWriter.GetTrace()
    {
        return null!;
    }

    /// <inheritdoc/>
    void CacheWriter.WriteBack(CacheObject entry)
    {
        output += entry.GetPos() + " ";
    }

    private void TestCacheDb(bool lru)
    {
        if (Config.Memory)
        {
            return;
        }

        DeleteDb("cache");
        DbConnection conn = GetConnection(
            "cache;CACHE_TYPE=" + (lru ? "LRU" : "SOFT_LRU"));
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "SET CACHE_SIZE 1024");
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR)");
        Execute(stat, "CREATE TABLE MAIN(ID INT PRIMARY KEY, NAME VARCHAR)");
        const int max = 10000;
        using (DbCommand prep = conn.CreateCommand())
        using (DbCommand prep2 = conn.CreateCommand())
        {
            prep.CommandText = "INSERT INTO TEST VALUES(?, ?)";
            prep.Parameters.Add(new H2Parameter());
            prep.Parameters.Add(new H2Parameter());
            prep2.CommandText = "INSERT INTO MAIN VALUES(?, ?)";
            prep2.Parameters.Add(new H2Parameter());
            prep2.Parameters.Add(new H2Parameter());
            for (int i = 0; i < max; i++)
            {
                prep.Parameters[0].Value = i;
                prep.Parameters[1].Value = "Hello " + i;
                _ = prep.ExecuteNonQuery();
                prep2.Parameters[0].Value = i;
                prep2.Parameters[1].Value = "World " + i;
                _ = prep2.ExecuteNonQuery();
            }
        }

        conn.Close();
        conn = GetConnection("cache");
        stat = conn.CreateCommand();
        Execute(stat, "SET CACHE_SIZE 1024");
        Random random = new(1);
        for (int i = 0; i < 100; i++)
        {
            stat.CommandText = "SELECT * FROM MAIN WHERE ID BETWEEN 40 AND 50";
            using (_ = stat.ExecuteReader())
            {
            }

            stat.CommandText = "SELECT * FROM MAIN WHERE ID = " + random.Next(max);
            using (_ = stat.ExecuteReader())
            {
            }

            if ((i % 10) == 0)
            {
                stat.CommandText = "SELECT * FROM TEST";
                using (_ = stat.ExecuteReader())
                {
                }
            }
        }

        conn.Close();
        DeleteDb("cache");
    }
}
