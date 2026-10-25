// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.mvcc.TestMvcc4.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements/result sets are ADO.NET commands/readers;
// setAutoCommit(false) is an explicit transaction that each command carries, and
// commit() is that transaction's Commit. The reference's background Thread is a .NET
// Task, and CountDownLatch is ManualResetEventSlim (the reference waits for one
// count-down). The reference's main() entry is the runner; xUnit runs Test() directly.
using System.Data;
using Xunit;

namespace H2Sharp.Native.Tests.Mvcc;

/// <summary>
/// Additional MVCC (multi version concurrency) test cases.
/// </summary>
public sealed class TestMvcc4 : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestSelectForUpdateAndUpdateConcurrency();
    }

    private void TestSelectForUpdateAndUpdateConcurrency()
    {
        DeleteDb("mvcc4");
        using DbConnection setup = GetConnection("mvcc4");
        using (DbTransaction transaction = setup.BeginTransaction())
        {
            DbCommand s = setup.CreateCommand();
            s.Transaction = transaction;
            s.CommandText = "CREATE TABLE test ("
                + "entity_id VARCHAR(100) NOT NULL PRIMARY KEY, "
                + "lastUpdated TIMESTAMP NOT NULL)";
            _ = s.ExecuteNonQuery();

            using DbCommand ps = setup.CreateCommand();
            ps.Transaction = transaction;
            ps.CommandText = "INSERT INTO test (entity_id, lastUpdated) VALUES (?, ?)";
            ps.Parameters.Add(new H2Parameter());
            ps.Parameters.Add(new H2Parameter());
            for (int i = 0; i < 2; i++)
            {
                ps.Parameters[0].Value = "" + i;
                ps.Parameters[1].Value = DateTime.Now;
                _ = ps.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        using DbConnection c1 = GetConnection("mvcc4;LOCK_TIMEOUT=10000");
        using (DbTransaction transaction = c1.BeginTransaction())
        using (ManualResetEventSlim executedUpdate = new(false))
        {
            Task thread = Task.Run(() =>
            {
                using DbConnection c2 = GetConnection("mvcc4");
                using DbTransaction transaction2 = c2.BeginTransaction();
                using (DbCommand ps = c2.CreateCommand())
                {
                    ps.Transaction = transaction2;
                    ps.CommandText = "SELECT * FROM test WHERE entity_id = ? FOR UPDATE";
                    ps.Parameters.Add(new H2Parameter { Value = "1" });
                    using (DbDataReader rs = ps.ExecuteReader())
                    {
                        _ = rs.Read();
                    }
                }

                executedUpdate.Set();
                // interrogate the "blocker_id" metadata field instead of relying on
                // stack traces to determine when the session is blocking
                using DbCommand stmt = c2.CreateCommand();
                stmt.Transaction = transaction2;
                stmt.CommandText =
                    "SELECT * FROM INFORMATION_SCHEMA.SESSIONS WHERE BLOCKER_ID = SESSION_ID()";
                while (true)
                {
                    using DbDataReader resultSet = stmt.ExecuteReader();
                    if (resultSet.Read())
                    {
                        break;
                    }
                }

                transaction2.Commit();
            });

            // wait until the concurrent update has executed, but not yet committed
            executedUpdate.Wait();

            // an update that waits for the lock held by the background task
            using (DbCommand ps = c1.CreateCommand())
            {
                ps.Transaction = transaction;
                ps.CommandText = "UPDATE test SET lastUpdated = ?";
                ps.Parameters.Add(new H2Parameter { Value = DateTime.Now });
                AssertEquals(2, ps.ExecuteNonQuery());
            }

            transaction.Commit();
            thread.Wait();
        }

        using DbConnection verify = GetConnection("mvcc4");
        using (DbTransaction transaction = verify.BeginTransaction())
        {
            using DbCommand ps = verify.CreateCommand();
            ps.Transaction = transaction;
            ps.CommandText = "SELECT COUNT(*) FROM test";
            using (DbDataReader rs = ps.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals(2, rs.GetInt32(0));
            }

            transaction.Commit();
        }
    }
}
