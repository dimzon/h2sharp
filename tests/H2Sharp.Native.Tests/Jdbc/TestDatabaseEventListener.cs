// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.jdbc.TestDatabaseEventListener.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; the JDBC Properties bag is the
// connection string's options part, so the test helper takes the options as text; the
// class name in DATABASE_EVENT_LISTENER is the CLR type name; the state constants live
// in DatabaseEventListenerStates (a C# interface cannot declare constants).
//
// Blocked by the port: Database.SetEventListenerClass refuses every class name (the
// listener is never instantiated or called), so the test cannot pass until the listener
// wiring is ported. The trait keeps it in the suite as the defect's checklist entry.
using System.Data.Common;
using H2Sharp.Api;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Native.Tests.Jdbc;

/// <summary>
/// Tests the DatabaseEventListener interface.
/// </summary>
[Trait("Category", "BlockedByPort")]
public sealed class TestDatabaseEventListener : TestDb
{
    private static DbConnection OpenPort(string url, string? properties = null)
    {
        H2Connection connection = new(ToClassicConnectionString(
            properties is null ? url : url + ";" + properties));
        connection.Open();
        return connection;
    }

    /// <summary>
    /// A flag to mark that the given method was called.
    /// </summary>
    private static bool calledOpened, calledClosingDatabase, calledScan,
        calledCreateIndex, calledStatementStart, calledStatementEnd,
        calledStatementProgress;

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestInit();
        TestIndexRebuiltOnce();
        TestIndexNotRebuilt();
        TestCalled();
        TestCloseLog0(false);
        TestCloseLog0(true);
        TestCalledForStatement();
        DeleteDb("databaseEventListener");
    }

    /// <summary>
    /// Initialize the database after opening.
    /// </summary>
    public sealed class InitListener : DatabaseEventListener
    {
        private string databaseUrl = string.Empty;

        /// <inheritdoc/>
        public void Init(string url)
        {
            databaseUrl = url;
        }

        /// <inheritdoc/>
        public void Opened()
        {
            // using DriverManager.getConnection could result in a deadlock when using
            // the server mode, but within the same process
            using DbConnection conn = OpenPort(databaseUrl, "USER=sa;PASSWORD=sa");
            DbCommand stat = conn.CreateCommand();
            stat.CommandText = "create table if not exists test(id int)";
            _ = stat.ExecuteNonQuery();
        }
    }

    private void TestInit()
    {
        if (Config.Cipher is not null || Config.Memory)
        {
            return;
        }

        DeleteDb("databaseEventListener");
        string url = GetURL("databaseEventListener", true);
        url += ";DATABASE_EVENT_LISTENER='" + typeof(InitListener).FullName + "'";
        using DbConnection conn = OpenPort(url, "USER=sa;PASSWORD=sa");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "select * from test");
    }

    private void TestIndexRebuiltOnce()
    {
        if (Config.Memory)
        {
            return;
        }

        DeleteDb("databaseEventListener");
        string url = GetURL("databaseEventListener", true);
        string properties = "USER=" + GetUser() + ";PASSWORD=" + GetPassword();
        using (DbConnection conn = OpenPort(url, properties))
        {
            DbCommand stat = conn.CreateCommand();
            // the old.id index head is at position 0
            Execute(stat, "create table old(id identity) as select 1");
            // the test.id index head is at position 1
            Execute(stat, "create table test(id identity) as select 1");
        }

        using (DbConnection conn = OpenPort(url, properties))
        {
            DbCommand stat = conn.CreateCommand();
            // free up space at position 0
            Execute(stat, "drop table old");
            Execute(stat, "insert into test values(2)");
            Execute(stat, "checkpoint sync");
            Execute(stat, "shutdown immediately");
        }

        // now the index should be re-built
        using (DbConnection conn = OpenPort(url, properties))
        {
        }

        calledCreateIndex = false;
        properties += ";DATABASE_EVENT_LISTENER='" +
            typeof(MyDatabaseEventListener).FullName + "'";
        using (DbConnection conn = OpenPort(url, properties))
        {
        }

        AssertFalse(calledCreateIndex);
    }

    private void TestIndexNotRebuilt()
    {
        if (Config.Memory)
        {
            return;
        }

        DeleteDb("databaseEventListener");
        string url = GetURL("databaseEventListener", true);
        string properties = "USER=" + GetUser() + ";PASSWORD=" + GetPassword();
        using (DbConnection conn = OpenPort(url, properties))
        {
            DbCommand stat = conn.CreateCommand();
            // the old.id index head is at position 0
            Execute(stat, "create table old(id identity) as select 1");
            // the test.id index head is at position 1
            Execute(stat, "create table test(id identity) as select 1");
        }

        using (DbConnection conn = OpenPort(url, properties))
        {
            DbCommand stat = conn.CreateCommand();
            // free up space at position 0
            Execute(stat, "drop table old");
            // truncate, relocating to position 0
            Execute(stat, "truncate table test");
            Execute(stat, "insert into test select 1");
        }

        calledCreateIndex = false;
        properties += ";DATABASE_EVENT_LISTENER='" +
            typeof(MyDatabaseEventListener).FullName + "'";
        using (DbConnection conn = OpenPort(url, properties))
        {
        }

        AssertFalse(calledCreateIndex);
    }

    private void TestCloseLog0(bool shutdown)
    {
        if (Config.Memory)
        {
            return;
        }

        DeleteDb("databaseEventListener");
        string url = GetURL("databaseEventListener", true);
        string properties = "USER=" + GetUser() + ";PASSWORD=" + GetPassword();
        using (DbConnection conn = OpenPort(url, properties))
        {
            DbCommand stat = conn.CreateCommand();
            Execute(stat, "create table test(id int primary key, name varchar)");
            Execute(stat, "insert into test select x, space(1000) "
                + "from system_range(1,1000)");
            if (shutdown)
            {
                Execute(stat, "shutdown");
            }
        }

        calledOpened = false;
        calledScan = false;
        properties += ";DATABASE_EVENT_LISTENER='" +
            typeof(MyDatabaseEventListener).FullName + "'";
        using (DbConnection conn = OpenPort(url, properties))
        {
        }

        if (calledOpened)
        {
            AssertFalse(calledScan);
        }
    }

    private void TestCalled()
    {
        string properties = "USER=sa;PASSWORD=sa;DATABASE_EVENT_LISTENER='"
            + typeof(MyDatabaseEventListener).FullName + "'";
        calledOpened = false;
        calledClosingDatabase = false;
        _ = Driver.Load();
        string url = "jdbc:h2:mem:databaseEventListener";
        using (DbConnection conn = OpenPort(url, properties))
        {
        }

        AssertTrue(calledOpened);
        AssertTrue(calledClosingDatabase);
    }

    private void TestCalledForStatement()
    {
        string properties = "USER=sa;PASSWORD=sa;DATABASE_EVENT_LISTENER='"
            + typeof(MyDatabaseEventListener).FullName + "'";
        calledStatementStart = false;
        calledStatementEnd = false;
        calledStatementProgress = false;
        _ = Driver.Load();
        string url = "jdbc:h2:mem:databaseEventListener";
        using (DbConnection conn = OpenPort(url, properties))
        {
            DbCommand stat = conn.CreateCommand();
            Execute(stat, "create table test(id int primary key, name varchar)");
            Execute(stat, "select * from test");
        }

        AssertTrue(calledStatementStart);
        AssertTrue(calledStatementEnd);
        AssertTrue(calledStatementProgress);
    }

    /// <summary>
    /// The database event listener for this test.
    /// </summary>
    public sealed class MyDatabaseEventListener : DatabaseEventListener
    {
        /// <inheritdoc/>
        public void ClosingDatabase()
        {
            calledClosingDatabase = true;
        }

        /// <inheritdoc/>
        public void Opened()
        {
            calledOpened = true;
        }

        /// <inheritdoc/>
        public void SetProgress(int state, string name, long x, long max)
        {
            if (state == DatabaseEventListenerStates.StateScanFile)
            {
                calledScan = true;
            }

            if (state == DatabaseEventListenerStates.StateCreateIndex)
            {
                if (!name.StartsWith("SYS:", StringComparison.Ordinal))
                {
                    calledCreateIndex = true;
                }
            }

            if (state == DatabaseEventListenerStates.StateStatementStart)
            {
                if (name.Equals("select * from test", StringComparison.Ordinal))
                {
                    calledStatementStart = true;
                }
            }

            if (state == DatabaseEventListenerStates.StateStatementEnd)
            {
                if (name.Equals("select * from test", StringComparison.Ordinal))
                {
                    calledStatementEnd = true;
                }
            }

            if (state == DatabaseEventListenerStates.StateStatementProgress)
            {
                if (name.Equals("select * from test", StringComparison.Ordinal))
                {
                    calledStatementProgress = true;
                }
            }
        }
    }
}
