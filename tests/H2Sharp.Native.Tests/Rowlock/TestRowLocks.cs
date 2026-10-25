// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.rowlock.TestRowLocks.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; setAutoCommit(false) is an
// explicit transaction that each command carries, and commit() is that transaction's
// Commit with a fresh transaction begun afterwards. The reference's commit() while
// autocommit is still on has no ADO.NET shape (a connection without a transaction has
// nothing to commit) and is dropped. org.h2.util.Task is a .NET task; its stop flag is
// a CancellationTokenSource, as in the other ported concurrency tests. The reference's
// main() entry is the runner; xUnit runs Test() directly.
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Rowlock;

/// <summary>
/// Row level locking tests.
/// </summary>
public sealed class TestRowLocks : TestDb
{
    private DbCommand s1 = null!, s2 = null!;

    private DbConnection c1 = null!, c2 = null!;

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestCases();
        DeleteDb(GetTestName());
    }

    private void TestCases()
    {
        DeleteDb(GetTestName());
        c1 = GetConnection(GetTestName());
        s1 = c1.CreateCommand();
        Execute(s1, "SET LOCK_TIMEOUT 10000");
        Execute(s1, "CREATE TABLE TEST AS "
            + "SELECT X ID, 'Hello' NAME FROM SYSTEM_RANGE(1, 3)");
        // The reference's c1.commit() runs while autocommit is still on; a connection
        // without a transaction has nothing to commit here.
        using (DbTransaction transaction = c1.BeginTransaction())
        {
            s1.Transaction = transaction;
            Execute(s1, "UPDATE TEST SET NAME='Hallo' WHERE ID=1");

            c2 = GetConnection(GetTestName());
            using (DbTransaction transaction2 = c2.BeginTransaction())
            {
                s2 = c2.CreateCommand();
                s2.Transaction = transaction2;

                AssertEquals("Hallo", GetSingleValue(s1,
                    "SELECT NAME FROM TEST WHERE ID=1"));
                AssertEquals("Hello", GetSingleValue(s2,
                    "SELECT NAME FROM TEST WHERE ID=1"));

                Execute(s2, "UPDATE TEST SET NAME='Hallo' WHERE ID=2");
                AssertThrows(ErrorCode.LockTimeout1, s2,
                    "UPDATE TEST SET NAME='Hi' WHERE ID=1");
                transaction.Commit();
                transaction2.Commit();
            }
        }

        using (DbTransaction transaction = c1.BeginTransaction())
        using (DbTransaction transaction2 = c2.BeginTransaction())
        {
            s1.Transaction = transaction;
            s2.Transaction = transaction2;

            AssertEquals("Hallo", GetSingleValue(s1,
                "SELECT NAME FROM TEST WHERE ID=1"));
            AssertEquals("Hallo", GetSingleValue(s2,
                "SELECT NAME FROM TEST WHERE ID=1"));

            Execute(s2, "UPDATE TEST SET NAME='H1' WHERE ID=1");
            using CancellationTokenSource stopped = new();
            Task task = Task.Run(() =>
            {
                _ = stopped;
                Execute(s1, "UPDATE TEST SET NAME='H2' WHERE ID=1");
            }, stopped.Token);
            Thread.Sleep(100);
            transaction2.Commit();
            task.Wait();
            transaction.Commit();
        }

        using (DbTransaction transaction = c1.BeginTransaction())
        using (DbTransaction transaction2 = c2.BeginTransaction())
        {
            s1.Transaction = transaction;
            s2.Transaction = transaction2;
            AssertEquals("H2", GetSingleValue(s1,
                "SELECT NAME FROM TEST WHERE ID=1"));
            AssertEquals("H2", GetSingleValue(s2,
                "SELECT NAME FROM TEST WHERE ID=1"));
            transaction2.Commit();
            transaction.Commit();
        }

        c1.Close();
        c2.Close();
    }

    private static string? GetSingleValue(DbCommand stat, string sql)
    {
        stat.CommandText = sql;
        using DbDataReader rs = stat.ExecuteReader();
        return rs.Read() ? rs.GetString(0) : null;
    }
}
