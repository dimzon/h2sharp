// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestExclusive.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; org.h2.util.Task is a .NET task
// with a cancellation source in the same shape (a call that runs until the parent lets
// it), and the proxy form of assertThrows becomes the lambda form, because a sealed
// class cannot be proxied. The reference's main() entry is the runner; xUnit runs
// Test() directly.
//
// Out of scope by decision (D-034 in docs/APPROVED_DEVIATIONS.md): for an embedded
// engine the cross-session enforcement of SET EXCLUSIVE has no consumer. The command
// itself is ported and works for its own session; what the reference additionally does
// and the port does not: refuse new connections with DATABASE_IS_IN_EXCLUSIVE_MODE,
// wait in other sessions' statements, close other sessions for SET EXCLUSIVE 2, and
// clear the mode when the owning session disconnects.
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test for the exclusive mode.
/// </summary>
public sealed class TestExclusive : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    [Trait("Category", "BlockedByPort")]
    public override void Test()
    {
        TestSetExclusiveTrueFalse();
        TestSetExclusiveGetExclusive();
    }

    private void TestSetExclusiveTrueFalse()
    {
        DeleteDb("exclusive");
        DbConnection conn = GetConnection("exclusive");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "set exclusive true");
        AssertThrows(ErrorCode.DatabaseIsInExclusiveMode, () => _ = GetConnection("exclusive"));

        Execute(stat, "set exclusive false");
        DbConnection conn2 = GetConnection("exclusive");
        DbCommand stat2 = conn2.CreateCommand();
        Execute(stat, "set exclusive true");
        int state = 0;
        using CancellationTokenSource stopped = new();
        Task task = Task.Run(() =>
        {
            Execute(stat2, "select * from dual");
            if (state != 1)
            {
                // The reference builds a java.lang.Error and prints it without throwing
                // it; the printed exception is the same record.
                Console.Error.WriteLine(new Exception("unexpected state: " + state));
            }
        }, stopped.Token);
        Volatile.Write(ref state, 1);
        Execute(stat, "set exclusive false");
        task.Wait();
        Execute(stat, "set exclusive true");
        conn.Close();

        // check that exclusive mode is off when disconnected
        Execute(stat2, "select * from dual");
        conn2.Close();
        DeleteDb("exclusive");
    }

    private void TestSetExclusiveGetExclusive()
    {
        DeleteDb("exclusive");
        using (DbConnection connection = GetConnection("exclusive"))
        {
            AssertFalse(GetExclusiveMode(connection));

            SetExclusiveMode(connection, 1);
            AssertTrue(GetExclusiveMode(connection));

            SetExclusiveMode(connection, 0);
            AssertFalse(GetExclusiveMode(connection));

            // Setting to existing mode should not throws exception
            SetExclusiveMode(connection, 0);
            AssertFalse(GetExclusiveMode(connection));

            SetExclusiveMode(connection, 1);
            AssertTrue(GetExclusiveMode(connection));

            // Setting to existing mode throws exception
            SetExclusiveMode(connection, 1);
            AssertTrue(GetExclusiveMode(connection));

            SetExclusiveMode(connection, 2);
            AssertTrue(GetExclusiveMode(connection));

            SetExclusiveMode(connection, 0);
            AssertFalse(GetExclusiveMode(connection));
        }
    }

    private static void SetExclusiveMode(DbConnection connection, int exclusiveMode)
    {
        using DbCommand statement = connection.CreateCommand();
        statement.CommandText = "SET EXCLUSIVE " + exclusiveMode;
        _ = statement.ExecuteNonQuery();
    }

    private static bool GetExclusiveMode(DbConnection connection)
    {
        bool exclusiveMode = false;
        using DbCommand statement = connection.CreateCommand();
        statement.CommandText =
            "SELECT SETTING_VALUE FROM INFORMATION_SCHEMA.SETTINGS WHERE SETTING_NAME = 'EXCLUSIVE'";
        using (DbDataReader result = statement.ExecuteReader())
        {
            if (result.Read())
            {
                exclusiveMode = result.GetBoolean(0);
            }
        }

        return exclusiveMode;
    }
}
