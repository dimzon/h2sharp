// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.jdbc.TestDatabaseEventListener.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands. The reference loads the listener's
// class from the connection string and observes the database lifecycle; this port hands the
// listener to the data source as an object (H2DataSource.SetDatabaseEventListener), and the
// test uses a recording implementation to verify the events the port wires: opened, closing,
// statement progress and exceptions. The file-scan/index-rebuild states of the reference test
// are not exercised: the operations that raise them are not ported.
using System.Collections.Generic;
using H2Sharp;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Native.Tests.Jdbc;

/// <summary>
/// Tests the DatabaseEventListener interface wired through the data source.
/// </summary>
public sealed class TestDatabaseEventListener : TestDb
{
    private sealed class RecordingEventListener : IH2DatabaseEventListener
    {
        internal readonly List<string> Calls = [];

        internal bool OpenedCalled => Calls.Contains("opened");

        internal bool ClosingCalled => Calls.Contains("closingDatabase");

        internal int StatementStartCount { get; private set; }

        internal int StatementEndCount { get; private set; }

        internal bool ExceptionThrownCalled => Calls.Contains("exceptionThrown");

        /// <inheritdoc/>
        public void Init(string url) => Calls.Add("init");

        /// <inheritdoc/>
        public void Opened() => Calls.Add("opened");

        /// <inheritdoc/>
        public void ClosingDatabase() => Calls.Add("closingDatabase");

        /// <inheritdoc/>
        public void SetProgress(H2DatabaseEventState state, string name, long x, long max)
        {
            if (state == H2DatabaseEventState.StatementStart)
            {
                StatementStartCount++;
            }
            else if (state == H2DatabaseEventState.StatementEnd)
            {
                StatementEndCount++;
            }

            Calls.Add("progress:" + state);
        }

        /// <inheritdoc/>
        public void ExceptionThrown(System.Data.Common.DbException e, string sql)
            => Calls.Add("exceptionThrown");
    }

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb("databaseEventListener");
        string url = ToClassicConnectionString(GetURL("databaseEventListener", admin: true));
        RecordingEventListener listener = new();
        using H2DataSource dataSource = new H2DataSourceBuilder(url).Build();
        dataSource.SetDatabaseEventListener(listener);

        using (H2Connection conn = dataSource.CreateConnection(url))
        {
            conn.Open();
            Assert.True(listener.OpenedCalled);

            using H2Command stat = (H2Command)conn.CreateCommand();
            stat.CommandText = "SELECT 1";
            using (H2DataReader reader = (H2DataReader)stat.ExecuteReader())
            {
                _ = reader.Read();
            }

            Assert.True(listener.StatementStartCount >= 1);
            Assert.True(listener.StatementEndCount >= 1);
            Assert.False(listener.ExceptionThrownCalled);

            // A prepare-time syntax error is refused before the command runs, so the listener's
            // exceptionThrown is exercised with a runtime failure: a duplicate key.
            stat.CommandText = "CREATE TABLE t(id int primary key)";
            _ = stat.ExecuteNonQuery();
            stat.CommandText = "INSERT INTO t VALUES (1)";
            _ = stat.ExecuteNonQuery();
            stat.CommandText = "INSERT INTO t VALUES (1)";
            try
            {
                _ = stat.ExecuteNonQuery();
            }
            catch (H2SharpException)
            {
                // expected
            }

            Assert.True(listener.ExceptionThrownCalled);
        }

        Assert.False(listener.ClosingCalled);
        dataSource.Dispose();
        Assert.True(listener.ClosingCalled);
        DeleteDb("databaseEventListener");
    }
}
