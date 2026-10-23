// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestQueryCache.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands. The reference holds the
// prepared statement of iteration 1000 unclosed so the next prepare misses the cache;
// the ported loop keeps that command alive the same way and disposes it at the end.
// The proxy form of assertThrows (assertThrows(code, conn).prepareStatement(...))
// becomes the lambda form, because a sealed class cannot be proxied. The reference's
// main() entry is the runner; xUnit runs Test() directly.
using System.Diagnostics;
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Tests the query cache.
/// </summary>
public sealed class TestQueryCache : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb("queryCache");
        Test1();
        TestClearingCacheWithTableStructureChanges();
        DeleteDb("queryCache");
    }

    private void Test1()
    {
        using DbConnection conn = GetConnection("queryCache;QUERY_CACHE_SIZE=10");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id int, name varchar)");
        DbCommand prep;
        // query execution may be fast here but the parsing must be slow
        StringBuilder queryBuilder = new("select count(*) from test t1 where \n");
        for (int i = 0; i < 1000; i++)
        {
            if (i != 0)
            {
                _ = queryBuilder.Append(" and ");
            }

            _ = queryBuilder.Append(" TIMESTAMP '2005-12-31 23:59:59' = TIMESTAMP '2005-12-31 23:59:59' ");
        }

        string query = queryBuilder.ToString();
        // The first prepare of the query, never closed, exactly as the reference leaves
        // it; `keep` stays the unclosed prepare of iteration 1000 from then on.
        DbCommand? keep = conn.CreateCommand();
        keep.CommandText = query;
        try
        {
            int firstGreater = 0;
            int firstSmaller = 0;
            long time;
            long first = 0;
            // 1000 iterations to warm up and avoid JIT effects
            for (int i = 0; i < 1005; i++)
            {
                // this should both ensure results are not re-used
                // stat.execute("set mode regular");
                // stat.execute("create table x()");
                // stat.execute("drop table x");
                time = Stopwatch.GetTimestamp();
                prep = conn.CreateCommand();
                prep.CommandText = query;
                Execute(prep);
                prep.Dispose();
                stat.CommandText = query;
                using (DbDataReader rs = stat.ExecuteReader())
                {
                    _ = rs.Read();
                    int c = rs.GetInt32(0);
                    AssertEquals(0, c);
                }

                time = (Stopwatch.GetTimestamp() - time) * 1_000_000_000 / Stopwatch.Frequency;
                if (i == 1000)
                {
                    // take from cache and do not close,
                    // so that next iteration will have a cache miss
                    keep = conn.CreateCommand();
                    keep.CommandText = query;
                }
                else if (i == 1001)
                {
                    first = time;
                }
                else if (i > 1001)
                {
                    if (first > time)
                    {
                        firstGreater++;
                    }
                    else
                    {
                        firstSmaller++;
                    }
                }
            }

            // first prepare time must be always greater because of query cache,
            // but JVM is too unpredictable to assert that, so just check that
            // usually this is true
            AssertSmaller(firstSmaller, firstGreater);
            Execute(stat, "drop table test");
        }
        finally
        {
            keep?.Dispose();
        }
    }

    private void TestClearingCacheWithTableStructureChanges()
    {
        using DbConnection conn = GetConnection("queryCache;QUERY_CACHE_SIZE=10");
        AssertThrows(ErrorCode.TableOrViewNotFoundDatabaseEmpty1,
            () =>
            {
                using DbCommand command = conn.CreateCommand();
                command.CommandText = "SELECT * FROM TEST";
                using DbDataReader reader = command.ExecuteReader();
            });

        DbCommand stat = conn.CreateCommand();
        stat.CommandText = "CREATE TABLE TEST(col1 bigint, col2 varchar(255))";
        _ = stat.ExecuteNonQuery();
        DbCommand prep = conn.CreateCommand();
        prep.CommandText = "SELECT * FROM TEST";
        prep.Dispose();
        stat.CommandText = "DROP TABLE TEST";
        _ = stat.ExecuteNonQuery();
        AssertThrows(ErrorCode.TableOrViewNotFoundDatabaseEmpty1,
            () =>
            {
                using DbCommand command = conn.CreateCommand();
                command.CommandText = "SELECT * FROM TEST";
                using DbDataReader reader = command.ExecuteReader();
            });
    }
}
