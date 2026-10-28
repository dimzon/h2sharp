// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.jdbc.TestConnection.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands. getTransactionIsolation /
// setTransactionIsolation are the level a transaction begins with and the transaction's
// own IsolationLevel (the standard ADO.NET surface); getSchema/setSchema are the
// SCHEMA() function and the SET SCHEMA statement, which carry the same value. The
// reference's main() entry is the runner; xUnit runs Test() directly.
//
// Modified (deferred, see MIGRATION_STATUS.md): the client-info halves
// (setClientInfo/getClientInfo) are JDBC-only connection metadata, and the
// commit/rollback-on-autocommit runners turn on the JDBC autocommit semantics and the
// FORCE_AUTOCOMMIT_OFF_ON_COMMIT system property. Both are skipped.
using System.Data;
using H2Sharp.Api;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Native.Tests.Jdbc;

/// <summary>
/// Tests the client info.
/// </summary>
public sealed class TestConnection : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        // The client-info halves and the commit/rollback-on-autocommit runners:
        // deferred, see the header and MIGRATION_STATUS.md.
        TestTransactionIsolationSetAndGet();
        TestSetGetSchema();
        TestLockTimeout();
        TestIgnoreUnknownSettings();
        TestTimeZone();
    }

    /// <summary>
    /// The reference's testTimeZone: two sessions opened while the JVM default zone
    /// differs, and one with the zone in the URL. The JVM default is the time zone seam
    /// of the connection here: each session is opened under its own zone provider, the
    /// way the reference's TimeZone.setDefault carries each opening.
    /// </summary>
    private void TestTimeZone()
    {
        DeleteDb("timeZone");
        string tz1 = "Europe/London", tz2 = "Europe/Paris", tz3 = "Asia/Tokyo";
        try
        {
            _ = GetConnection("timeZone");
            DbConnection c1 = GetConnection("timeZone");
            ((IH2Test)c1).TimeZoneProviderProvider = () => TimeZoneProvider.OfId(tz1);
            DbConnection c2 = GetConnection("timeZone");
            ((IH2Test)c2).TimeZoneProviderProvider = () => TimeZoneProvider.OfId(tz2);
            using (DbConnection c3 = GetConnection("timeZone;TIME ZONE=" + tz3))
            {
                CheckTimeZone(tz1, c1);
                CheckTimeZone(tz2, c2);
                CheckTimeZone(tz3, c3);
            }

            c1.Close();
            c2.Close();
        }
        finally
        {
            DeleteDb("timeZone");
        }
    }

    private void CheckTimeZone(string expected, DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();
        stat.CommandText =
            "SELECT SETTING_VALUE FROM INFORMATION_SCHEMA.SETTINGS WHERE SETTING_NAME = 'TIME ZONE'";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(expected, rs.GetString(0));
        }
    }

    private void TestTransactionIsolationSetAndGet()
    {
        DeleteDb("transactionIsolation");
        try
        {
            using DbConnection conn = GetConnection("transactionIsolation");
            // TRANSACTION_READ_COMMITTED: the level a transaction begun without one runs
            // at.
            using (DbTransaction transaction = conn.BeginTransaction())
            {
                AssertEquals(IsolationLevel.ReadCommitted, transaction.IsolationLevel);
            }

            // setTransactionIsolation(level): the level the next transaction begins with,
            // read back from the transaction itself.
            foreach (IsolationLevel level in new[]
            {
                IsolationLevel.ReadUncommitted,
                IsolationLevel.RepeatableRead,
                IsolationLevel.Snapshot,
                IsolationLevel.Serializable,
            })
            {
                using DbTransaction transaction = conn.BeginTransaction(level);
                AssertEquals(level, transaction.IsolationLevel);
            }
        }
        finally
        {
            DeleteDb("transactionIsolation");
        }
    }

    private void TestSetGetSchema()
    {
        DeleteDb("schemaSetGet");
        using DbConnection conn = GetConnection("schemaSetGet");
        DbCommand s = conn.CreateCommand();
        Execute(s, "create schema my_test_schema");
        Execute(s, "create table my_test_schema.my_test_table(id int, nave varchar) as values (1, 'a')");
        AssertEquals("PUBLIC", GetSchema(conn));
        AssertThrows(ErrorCode.TableOrViewNotFound1, s, "select * from my_test_table");
        AssertThrows(ErrorCode.SchemaNotFound1,
            () => Execute(conn.CreateCommand(), "SET SCHEMA my_test_table"));
        Execute(conn.CreateCommand(), "SET SCHEMA MY_TEST_SCHEMA");
        AssertEquals("MY_TEST_SCHEMA", GetSchema(conn));
        s.CommandText = "select * from my_test_table";
        using (DbDataReader rs = s.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
            AssertEquals("a", rs.GetString(1));
            AssertFalse(rs.Read());
        }

        AssertThrows(ErrorCode.SchemaNotFound1,
            () => Execute(conn.CreateCommand(), "SET SCHEMA NON_EXISTING_SCHEMA"));
        AssertEquals("MY_TEST_SCHEMA", GetSchema(conn));
        Execute(s, "create schema \"otheR_schEma\"");
        Execute(s, "create table \"otheR_schEma\".my_test_table(id int, nave varchar) as values (2, 'b')");
        Execute(conn.CreateCommand(), "SET SCHEMA \"otheR_schEma\"");
        AssertEquals("otheR_schEma", GetSchema(conn));
        s.CommandText = "select * from my_test_table";
        using (DbDataReader rs = s.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertEquals("b", rs.GetString(1));
            AssertFalse(rs.Read());
        }

        Execute(s, "SET SCHEMA \"MY_TEST_SCHEMA\"");
        AssertEquals("MY_TEST_SCHEMA", GetSchema(conn));
    }

    /// <summary>
    /// The reference's Connection.getSchema: the current schema of the session, which
    /// the SCHEMA() function answers.
    /// </summary>
    /// <param name="conn">the connection</param>
    /// <returns>the schema name</returns>
    private string GetSchema(DbConnection conn)
    {
        DbCommand command = conn.CreateCommand();
        command.CommandText = "CALL SCHEMA()";
        using DbDataReader rs = command.ExecuteReader();
        AssertTrue(rs.Read());
        return rs.GetString(0);
    }

    private void TestLockTimeout()
    {
        DeleteDb("lockTimeout");
        try
        {
            using DbConnection conn1 = GetConnection("lockTimeout");
            using DbConnection conn2 = GetConnection("lockTimeout;LOCK_TIMEOUT=6000");
            using DbTransaction t1 = conn1.BeginTransaction();
            using DbTransaction t2 = conn2.BeginTransaction();
            DbCommand s1 = conn1.CreateCommand();
            s1.Transaction = t1;
            DbCommand s2 = conn2.CreateCommand();
            s2.Transaction = t2;
            Execute(s1, "CREATE TABLE TEST(ID INT PRIMARY KEY, V INT) AS VALUES (1, 2)");
            t1.Commit();
            s1.Transaction = null;
            Execute(s2, "INSERT INTO TEST VALUES (2, 4)");
            Execute(s1, "UPDATE TEST SET V = 3 WHERE ID = 1");
            Execute(s2, "SET LOCK_TIMEOUT 50");
            long n = Environment.TickCount64;
            AssertThrows(ErrorCode.LockTimeout1, s2, "UPDATE TEST SET V = 4 WHERE ID = 1");
            if (Environment.TickCount64 - n > 5_000)
            {
                Fail("LOCK_TIMEOUT wasn't set");
            }
        }
        finally
        {
            DeleteDb("lockTimeout");
        }
    }

    private void TestIgnoreUnknownSettings()
    {
        DeleteDb("ignoreUnknownSettings");
        try
        {
            AssertThrows(ErrorCode.UnsupportedSetting1,
                () => _ = GetConnection("ignoreUnknownSettings;A=1"));
            using (DbConnection c = GetConnection("ignoreUnknownSettings;IGNORE_UNKNOWN_SETTINGS=TRUE;A=1"))
            {
            }
        }
        finally
        {
            DeleteDb("ignoreUnknownSettings");
        }
    }
}
