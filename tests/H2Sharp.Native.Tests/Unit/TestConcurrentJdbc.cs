// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.unit.TestConcurrentJdbc.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands, the CountDownLatch is a ManualResetEventSlim, and the reference's Task is a .NET task with the exception kept in a field. The loop runs against one in-memory database per iteration, and the worker ends with the allowed closed-object errors.
using H2Sharp.Api;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Native.Tests.Unit;

/// <summary>
/// Test concurrent access to JDBC objects.
/// </summary>
public sealed class TestConcurrentJdbc
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public void Test()
    {
        string url = "jdbc:h2:mem:";
        for (int i = 0; i < 50; i++)
        {
            int x = i % 4;
            using H2Connection conn = new(url);
            conn.Open();
            using DbCommand stat = conn.CreateCommand();
            stat.CommandText = "create table test(id int primary key)";
            _ = stat.ExecuteNonQuery();
            string sql = (x % 6) switch
            {
                0 => "select 1",
                1 or 2 => "delete from test",
                _ => "",
            };
            using DbCommand prep = conn.CreateCommand();
            prep.CommandText = sql;
            using ManualResetEventSlim executedUpdate = new(false);
            Exception? workerException = null;
            Task t = Task.Run(() =>
            {
                try
                {
                    while (conn.State != ConnectionState.Closed)
                    {
                        executedUpdate.Set();
                        switch (x % 6)
                        {
                            case 0:
                                using (DbDataReader reader = prep.ExecuteReader())
                                {
                                    while (reader.Read())
                                    {
                                        // just loop
                                    }
                                }

                                break;
                            case 1:
                            case 2:
                                _ = prep.ExecuteNonQuery();
                                break;
                            case 3:
                                using (DbDataReader reader = stat.ExecuteReader())
                                {
                                    while (reader.Read())
                                    {
                                        // just loop
                                    }
                                }

                                break;
                            case 4:
                            case 5:
                                _ = stat.ExecuteNonQuery();
                                break;
                        }
                    }
                }
                catch (Exception e)
                {
                    workerException = e;
                }
            });
            // Wait until the concurrent task has started
            executedUpdate.Wait();
            conn.Close();
            t.Wait();
            if (workerException is not null)
            {
                if (workerException is H2SharpException h2)
                {
                    if (h2.H2ErrorCode != ErrorCode.ObjectClosed
                        && h2.H2ErrorCode != ErrorCode.DatabaseIsClosed
                        && h2.H2ErrorCode != ErrorCode.StatementWasCanceled
                        && h2.H2ErrorCode != ErrorCode.DatabaseCalledAtShutdown)
                    {
                        throw workerException;
                    }
                }
                else
                {
                    throw workerException;
                }
            }
        }
    }
}
