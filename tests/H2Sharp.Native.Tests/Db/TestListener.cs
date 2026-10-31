// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestListener.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands, setBinaryStream is a Stream
// parameter value, and the listener registers itself under this class's own full name,
// which is what getClass().getName() answers in the reference. The listener interface
// is the ADO-level IH2DatabaseEventListener.
//
// Modified (port gap, named): the engine does not read the database_event_listener URL
// option yet, and crash(conn) is the power-off seam of TestBase, which refuses; the
// test stops there until both are ported. The reference's main() entry is the runner;
// xUnit runs Test() directly.
using System.Diagnostics;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Tests the DatabaseEventListener.
/// </summary>
public sealed class TestListener : TestDb, IH2DatabaseEventListener
{
    private long last;

    private H2DatabaseEventState? lastState;

    private string? databaseUrl;

    /// <summary>
    /// Builds the test and starts its clock, the way the reference's constructor does.
    /// </summary>
    public TestListener()
    {
        start = last = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Whether this test is enabled in the current configuration.
    /// </summary>
    /// <returns><c>true</c> when the test runs</returns>
    public override bool IsEnabled()
    {
        if (Config.Networked || Config.Cipher is not null)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    [Trait("Category", "BlockedByPort")]
    public override void Test()
    {
        DeleteDb("listener");
        DbConnection conn;
        conn = GetConnection("listener");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR)");
        DbCommand prep = conn.CreateCommand();
        prep.CommandText = "INSERT INTO TEST VALUES(?, 'Test' || SPACE(100))";
        prep.Parameters.Add(new H2Parameter());
        int len = GetSize(100, 100000);
        for (int i = 0; i < len; i++)
        {
            prep.Parameters[0].Value = i;
            _ = prep.ExecuteNonQuery();
        }

        Crash(conn);

        conn = GetConnection("listener;database_event_listener='"
            + GetType().FullName + "'");
        conn.Close();
        DeleteDb("listener");
    }

    /// <inheritdoc/>
    void IH2DatabaseEventListener.ExceptionThrown(DbException e, string sql)
    {
        LogError("exceptionThrown sql=" + sql, e);
    }

    /// <inheritdoc/>
    void IH2DatabaseEventListener.SetProgress(H2DatabaseEventState state, string name, long current, long max)
    {
        long time = Stopwatch.GetTimestamp();
        if (state == lastState && time < last + Stopwatch.Frequency)
        {
            return;
        }

        if (state == H2DatabaseEventState.StatementStart
            || state == H2DatabaseEventState.StatementEnd
            || state == H2DatabaseEventState.StatementProgress)
        {
            return;
        }

        if (name.Length > 30)
        {
            name = "..." + name[^30..];
        }

        last = time;
        lastState = state;
        string stateName;
        switch (state)
        {
            case H2DatabaseEventState.ScanFile:
                stateName = "Scan " + name;
                break;
            case H2DatabaseEventState.CreateIndex:
                stateName = "Create Index " + name;
                break;
            case H2DatabaseEventState.Recover:
                stateName = "Recover";
                break;
            default:
                LogError("unknown state: " + state, null);
                stateName = "? " + name;
                break;
        }

        Thread.Sleep(1);
        PrintTime("state: " + stateName + " "
            + (100 * current / max) + " " + (time - start) * 1000 / Stopwatch.Frequency);
    }

    /// <inheritdoc/>
    void IH2DatabaseEventListener.ClosingDatabase()
    {
        if (databaseUrl!.ToUpperInvariant().Contains("CIPHER", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            using DbConnection conn = new H2Connection(ToClassicConnectionString(databaseUrl));
            conn.Open();
            DbCommand command = conn.CreateCommand();
            Execute(command, "DROP TABLE TEST2");
        }
        catch (DbException e)
        {
            Console.Error.WriteLine(e);
        }
    }

    /// <inheritdoc/>
    void IH2DatabaseEventListener.Init(string url)
    {
        databaseUrl = url;
    }

    /// <inheritdoc/>
    void IH2DatabaseEventListener.Opened()
    {
        if (databaseUrl!.ToUpperInvariant().Contains("CIPHER", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            using DbConnection conn = new H2Connection(ToClassicConnectionString(databaseUrl));
            conn.Open();
            DbCommand command = conn.CreateCommand();
            Execute(command, "CREATE TABLE IF NOT EXISTS TEST2(ID INT)");
        }
        catch (DbException e)
        {
            Console.Error.WriteLine(e);
        }
    }
}

