// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.jdbc.TestCachedQueryResults.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): the reference runs its callables on an ExecutorService; the ported
// test runs them as .NET tasks. setAutoCommit(false) and commit() are the explicit
// transaction of ADO.NET, and every command inside it carries the transaction. The
// reference's main() entry is the runner; xUnit runs Test() directly.
using System.Collections.Concurrent;

namespace H2Sharp.Native.Tests.Jdbc;

/// <summary>
/// Tests the cached query results: many sessions take turns reading and updating one
/// counter row under a row lock, and every session must see every written value exactly
/// once.
/// </summary>
public sealed class TestCachedQueryResults : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        string name = "TestCachedQueryResults";
        DeleteDb(name);
        ConcurrentDictionary<int, byte> concurrentSet = [];

        using DbConnection mainConn = GetConnection(name);
        string createTableQuery = "CREATE TABLE IF NOT EXISTS Counter (id INT PRIMARY KEY, counter INT) "
            + "AS SELECT * FROM (Values (1,0))";

        using (DbCommand stmt = mainConn.CreateCommand())
        {
            stmt.CommandText = createTableQuery;
            _ = stmt.ExecuteNonQuery();
        }

        int threadsCount = 5;
        int taskCount = 100_000;

        // The reference runs the callables on a fixed pool of threadsCount threads.
        _ = Parallel.For(0, taskCount, new ParallelOptions
        {
            MaxDegreeOfParallelism = threadsCount,
        }, _ =>
        {
            try
            {
                using DbConnection conn = GetConnection(name);
                using DbTransaction transaction = conn.BeginTransaction();

                string selectQuery = "SELECT counter FROM Counter WHERE id = 1";
                string lockRowQuery = "SELECT counter FROM Counter WHERE id = 1 FOR UPDATE WAIT 0.5";

                using DbCommand selectCounterStmt = conn.CreateCommand();
                selectCounterStmt.Transaction = transaction;
                selectCounterStmt.CommandText = selectQuery;
                int countBefore = QueryCounter(selectCounterStmt); // Select counter before lock
                _ = countBefore;

                using DbCommand lockStmt = conn.CreateCommand();
                lockStmt.Transaction = transaction;
                lockStmt.CommandText = lockRowQuery;
                int countAtLock = QueryCounter(lockStmt);     // Lock row and select

                int countAfter = QueryCounter(selectCounterStmt);  // select after lock
                if (countAfter != countAtLock)
                {
                    Println(countAfter + " != " + countAtLock + " " + Environment.CurrentManagedThreadId);
                }

                if (!concurrentSet.TryAdd(countAfter, 0))
                {
                    // lost update warning, if concurrentSet already contains current value
                    Println("LOST UPDATE! value: " + countAfter);
                }

                using DbCommand updateStmt = conn.CreateCommand();
                updateStmt.Transaction = transaction;
                updateStmt.CommandText = "UPDATE Counter SET counter = ? WHERE id = 1";
                updateStmt.Parameters.Add(new H2Parameter { Value = countAfter + 1 }); // Update counter++
                _ = updateStmt.ExecuteNonQuery();

                transaction.Commit();
            }
            catch (DbException e)
            {
                Println(e.Message ?? "");
            }
        });

        AssertEquals(taskCount, concurrentSet.Count);
    }

    private static int QueryCounter(DbCommand stmt)
    {
        int value = -1;
        using DbDataReader rs1 = stmt.ExecuteReader();
        if (rs1.Read())
        {
            value = rs1.GetInt32(0);
        }

        return value;
    }
}
