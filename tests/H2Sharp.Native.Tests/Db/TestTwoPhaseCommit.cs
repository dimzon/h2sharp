// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestTwoPhaseCommit.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; setAutoCommit(false) is an
// explicit transaction the statements carry, and commit()/rollback() are the COMMIT /
// ROLLBACK statements, which is what the reference's JDBC commit runs on the engine.
// The reference's main() entry is the runner; xUnit runs Test() directly.
//
// Modified (port gap, named): crash(conn) is the power-off seam
// (TestBase.setPowerOffCount through the session), which the port does not have yet;
// the stub refuses, so the test stops there until the seam is ported.
namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Tests for the two-phase-commit feature.
/// </summary>
public sealed class TestTwoPhaseCommit : TestDb
{
    /// <summary>
    /// Whether this test is enabled in the current configuration.
    /// </summary>
    /// <returns><c>true</c> when the test runs</returns>
    public override bool IsEnabled()
    {
        if (Config.Memory || Config.Networked)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb("twoPhaseCommit");

        Prepare();
        OpenWith(true);
        Test_(true);

        Prepare();
        OpenWith(false);
        Test_(false);

        TestInDoubtAfterShutdown();

        DeleteDb("twoPhaseCommit");
    }

    /// <summary>
    /// The reference's test(boolean); the underscore keeps it apart from the xUnit entry
    /// of the same name, which the reference overloads too but xUnit forbids.
    /// </summary>
    /// <param name="rolledBack">whether the transaction was rolled back</param>
    private void Test_(bool rolledBack)
    {
        using DbConnection conn = GetConnection("twoPhaseCommit");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "SET WRITE_DELAY 0");
        stat.CommandText = "SELECT * FROM TEST ORDER BY ID";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
            AssertEquals("Hello", rs.GetString(1));
            if (!rolledBack)
            {
                AssertTrue(rs.Read());
                AssertEquals(2, rs.GetInt32(0));
                AssertEquals("World", rs.GetString(1));
            }

            AssertFalse(rs.Read());
        }
    }

    private void OpenWith(bool rollback)
    {
        using DbConnection conn = GetConnection("twoPhaseCommit");
        DbCommand stat = conn.CreateCommand();
        List<string> list = [];
        stat.CommandText = "SELECT * FROM INFORMATION_SCHEMA.IN_DOUBT";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            while (rs.Read())
            {
                list.Add(rs.GetString(rs.GetOrdinal("TRANSACTION_NAME")));
            }
        }

        foreach (string s in list)
        {
            if (rollback)
            {
                Execute(stat, "ROLLBACK TRANSACTION " + s);
            }
            else
            {
                Execute(stat, "COMMIT TRANSACTION " + s);
            }
        }
    }

    private void Prepare()
    {
        DeleteDb("twoPhaseCommit");
        using DbConnection conn = GetConnection("twoPhaseCommit");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "SET WRITE_DELAY 0");
        using DbTransaction transaction = conn.BeginTransaction();
        stat.Transaction = transaction;
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR)");
        Execute(stat, "INSERT INTO TEST VALUES(1, 'Hello')");
        Execute(stat, "COMMIT");
        Execute(stat, "INSERT INTO TEST VALUES(2, 'World')");
        Execute(stat, "PREPARE COMMIT XID_TEST_TRANSACTION_WITH_LONG_NAME");
        Crash(conn);
    }

    private void TestInDoubtAfterShutdown()
    {
        if (Config.Memory)
        {
            return;
        }

        DeleteDb("twoPhaseCommit");
        DbConnection conn = GetConnection("twoPhaseCommit");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST (ID INT PRIMARY KEY)");
        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            Execute(stat, "INSERT INTO TEST VALUES (1)");
            Execute(stat, "PREPARE COMMIT \"#1\"");
            Execute(stat, "COMMIT");
        }

        Execute(stat, "SHUTDOWN IMMEDIATELY");
        conn = GetConnection("twoPhaseCommit");
        stat = conn.CreateCommand();
        stat.CommandText = "SELECT TRANSACTION_NAME, TRANSACTION_STATE FROM INFORMATION_SCHEMA.IN_DOUBT";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertFalse(rs.Read());
        }

        stat.CommandText = "SELECT ID FROM TEST";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            Execute(stat, "INSERT INTO TEST VALUES (2)");
            Execute(stat, "PREPARE COMMIT \"#2\"");
            Execute(stat, "ROLLBACK");
        }

        Execute(stat, "SHUTDOWN IMMEDIATELY");
        conn = GetConnection("twoPhaseCommit");
        stat = conn.CreateCommand();
        stat.CommandText = "SELECT TRANSACTION_NAME, TRANSACTION_STATE FROM INFORMATION_SCHEMA.IN_DOUBT";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertFalse(rs.Read());
        }

        stat.CommandText = "SELECT ID FROM TEST";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            Execute(stat, "INSERT INTO TEST VALUES (3)");
            Execute(stat, "PREPARE COMMIT \"#3\"");
        }

        Execute(stat, "SHUTDOWN IMMEDIATELY");
        conn = GetConnection("twoPhaseCommit");
        stat = conn.CreateCommand();
        stat.CommandText = "SELECT TRANSACTION_NAME, TRANSACTION_STATE FROM INFORMATION_SCHEMA.IN_DOUBT";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("#3", rs.GetString(rs.GetOrdinal("TRANSACTION_NAME")));
            AssertEquals("IN_DOUBT", rs.GetString(rs.GetOrdinal("TRANSACTION_STATE")));
        }

        stat.CommandText = "SELECT ID FROM TEST";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        conn.Close();
        DeleteDb("twoPhaseCommit");
    }
}
