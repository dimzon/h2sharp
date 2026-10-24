// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestSessionsLocks.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; setAutoCommit(false) is an
// explicit transaction, commit() is the wrapper's commit, and
// setTransactionIsolation(SERIALIZABLE) is the level the next transaction begins with —
// the standard ADO.NET surface. The reference's main() entry is the runner; xUnit runs
// Test() directly.
using System.Data;
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Tests the meta data tables information_schema.locks and sessions.
/// </summary>
public sealed class TestSessionsLocks : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestCancelStatement();
        TestLocks();
        TestAbortStatement();
        DeleteDb("sessionsLocks");
    }

    private void TestLocks()
    {
        DeleteDb("sessionsLocks");
        DbConnection conn = GetConnection("sessionsLocks");
        DbCommand stat = conn.CreateCommand();
        stat.CommandText = "select * from information_schema.locks "
            + "order by session_id";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertFalse(rs.Read());
        }

        DbConnection conn2 = GetConnection("sessionsLocks");
        DbCommand stat2 = conn2.CreateCommand();
        Execute(stat2, "create table test(id int primary key, name varchar)");
        using (DbTransaction transaction = conn2.BeginTransaction())
        {
            stat2.Transaction = transaction;
            Execute(stat2, "insert into test values(1, 'Hello')");
            stat.CommandText = "select * from information_schema.locks "
                + "order by session_id";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals("PUBLIC", rs.GetString(rs.GetOrdinal("TABLE_SCHEMA")));
                AssertEquals("TEST", rs.GetString(rs.GetOrdinal("TABLE_NAME")));
                _ = rs.GetString(rs.GetOrdinal("SESSION_ID"));
                AssertEquals("READ", rs.GetString(rs.GetOrdinal("LOCK_TYPE")));
                AssertFalse(rs.Read());
            }

            transaction.Commit();
        }

        // setTransactionIsolation(SERIALIZABLE): the level the next transaction begins
        // with
        using (DbTransaction transaction = conn2.BeginTransaction(IsolationLevel.Serializable))
        {
            stat2.Transaction = transaction;
            Execute(stat2, "SELECT * FROM TEST");
            stat.CommandText = "select * from information_schema.locks "
                + "order by session_id";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertFalse(rs.Read());
            }

            transaction.Commit();
        }

        stat.CommandText = "select * from information_schema.locks "
            + "order by session_id";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertFalse(rs.Read());
        }

        conn.Close();
        conn2.Close();
    }

    private void TestCancelStatement()
    {
        DeleteDb("sessionsLocks");
        DbConnection conn = GetConnection("sessionsLocks");
        DbCommand stat = conn.CreateCommand();
        stat.CommandText = "select * from information_schema.sessions "
            + "order by SESSION_START, SESSION_ID";
        int sessionId;
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            sessionId = rs.GetInt32(rs.GetOrdinal("SESSION_ID"));
            _ = rs.GetString(rs.GetOrdinal("USER_NAME"));
            _ = rs.GetDateTime(rs.GetOrdinal("SESSION_START"));
            _ = rs.GetString(rs.GetOrdinal("EXECUTING_STATEMENT"));
            _ = rs.IsDBNull(rs.GetOrdinal("EXECUTING_STATEMENT_START"))
                ? default : rs.GetDateTime(rs.GetOrdinal("EXECUTING_STATEMENT_START"));
            AssertFalse(rs.Read());
        }

        DbConnection conn2 = GetConnection("sessionsLocks");
        DbCommand stat2 = conn2.CreateCommand();
        stat.CommandText = "select * from information_schema.sessions "
            + "order by SESSION_START, SESSION_ID";
        int otherId;
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(sessionId, rs.GetInt32(rs.GetOrdinal("SESSION_ID")));
            AssertTrue(rs.Read());
            otherId = rs.GetInt32(rs.GetOrdinal("SESSION_ID"));
            AssertTrue(otherId != sessionId);
            AssertFalse(rs.Read());
        }

        Execute(stat2, "set throttle 1");
        bool[] done = [false];
        Thread runnable = new(() =>
        {
            try
            {
                stat2.CommandText = "select count(*) from "
                    + "system_range(1, 10000000) t1, system_range(1, 10000000) t2";
                using DbDataReader reader = stat2.ExecuteReader();
                while (reader.Read())
                {
                    // just loop
                }

                Console.Error.WriteLine(new Exception("Unexpected success"));
            }
            catch (DbException)
            {
                done[0] = true;
            }
        });
        runnable.Start();
        while (true)
        {
            Thread.Sleep(100);
            stat.CommandText = "CALL CANCEL_SESSION(" + otherId + ")";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertTrue(rs.Read());
                if (rs.GetBoolean(0))
                {
                    for (int i = 0; i < 20; i++)
                    {
                        Thread.Sleep(100);
                        if (done[0])
                        {
                            break;
                        }
                    }

                    AssertTrue(done[0]);
                    break;
                }
            }
        }

        conn2.Close();
        conn.Close();
    }

    private void TestAbortStatement()
    {
        DeleteDb("sessionsLocks");
        DbConnection conn = GetConnection("sessionsLocks");
        DbCommand stat = conn.CreateCommand();
        stat.CommandText = "select session_id() as ID";
        int sessionId;
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            sessionId = rs.GetInt32(rs.GetOrdinal("ID"));
        }

        // Setup session to be aborted
        DbConnection conn2 = GetConnection("sessionsLocks");
        DbCommand stat2 = conn2.CreateCommand();
        Execute(stat2, "create table test(id int primary key, name varchar)");
        using (DbTransaction transaction = conn2.BeginTransaction())
        {
            stat2.Transaction = transaction;
            Execute(stat2, "insert into test values(1, 'Hello')");
            transaction.Commit();
        }

        using (DbTransaction transaction = conn2.BeginTransaction())
        {
            stat2.Transaction = transaction;
            // grab a lock
            Execute(stat2, "update test set name = 'Again' where id = 1");
        }

        using (DbTransaction transaction = conn2.BeginTransaction())
        {
            stat2.Transaction = transaction;
            stat2.CommandText = "select session_id() as ID";
            using (DbDataReader rs = stat2.ExecuteReader())
            {
                AssertTrue(rs.Read());
            }

            int otherId;
            stat.CommandText = "select session_id() as ID";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertTrue(rs.Read());
                otherId = rs.GetInt32(rs.GetOrdinal("ID"));
                AssertTrue(otherId != sessionId);
                AssertFalse(rs.Read());
            }

            // expect one lock
            AssertEquals(1, GetLockCountForSession(stat, otherId));
            stat.CommandText = "CALL ABORT_SESSION(" + otherId + ")";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertTrue(rs.GetBoolean(0));
            }

            // expect the lock to be released along with its session
            AssertEquals(0, GetLockCountForSession(stat, otherId));
            stat.CommandText = "CALL ABORT_SESSION(" + otherId + ")";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertFalse("Session is expected to be already aborted", rs.GetBoolean(0));
            }

            // using the connection for the aborted session is expected to throw an
            // exception
            AssertThrows(ErrorCode.DatabaseCalledAtShutdown,
                () => Query(stat2, "select count(*) from test"));
        }

        conn2.Close();
        conn.Close();
    }

    private int GetLockCountForSession(DbCommand stmnt, int otherId)
    {
        stmnt.CommandText =
            "select count(*) from information_schema.locks where session_id = " + otherId;
        using DbDataReader rs = stmnt.ExecuteReader();
        AssertTrue(rs.Read());
        return rs.GetInt32(0);
    }

    /// <summary>
    /// The reference's stat.executeQuery(sql); the rows are read and dropped.
    /// </summary>
    private static void Query(DbCommand stat, string sql)
    {
        stat.CommandText = sql;
        using DbDataReader rs = stat.ExecuteReader();
        while (rs.Read())
        {
            // just loop
        }
    }
}
