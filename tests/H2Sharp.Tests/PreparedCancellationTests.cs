/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The row scan's checkpoints: every 128th scanned row asks the session whether the statement
 * was canceled and reports the statement's progress to the database event listener. The
 * reference's Prepared.setCurrentRowNumber does both.
 */
using System;
using System.Collections.Generic;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class PreparedCancellationTests
{
    [Fact]
    public void TheRowScanChecksCancellationEvery128Rows()
    {
        SessionLocal session = NewSession("cancel_scan");
        try
        {
            CommandContainer container = Assert.IsType<CommandContainer>(
                session.PrepareCommand("SELECT COUNT(*) FROM SYSTEM_RANGE(1, 1000)"));
            Prepared prepared = container.GetPreparedCommand();

            // The first 127 rows do not look at the cancellation flag.
            for (int i = 1; i <= 127; i++)
            {
                prepared.SetCurrentRowNumber(i);
            }

            session.Cancel();
            H2SharpException error = Assert.Throws<H2SharpException>(
                () => prepared.SetCurrentRowNumber(128));
            Assert.Equal(Api.ErrorCode.StatementWasCanceled, error.H2ErrorCode);
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TheRowScanReportsProgressEvery128Rows()
    {
        SessionLocal session = NewSession("progress_scan");
        try
        {
            RecordingListener listener = new();
            session.GetDatabase().SetDatabaseEventListener(listener);
            CommandContainer container = Assert.IsType<CommandContainer>(
                session.PrepareCommand("SELECT COUNT(*) FROM SYSTEM_RANGE(1, 1000)"));
            Prepared prepared = container.GetPreparedCommand();

            for (int i = 1; i <= 127; i++)
            {
                prepared.SetCurrentRowNumber(i);
            }

            Assert.Empty(listener.Events);
            prepared.SetCurrentRowNumber(128);
            prepared.SetCurrentRowNumber(129);
            (H2DatabaseEventState state, string name, long x, long max) = Assert.Single(listener.Events);
            Assert.Equal(H2DatabaseEventState.StatementProgress, state);
            Assert.Equal("SELECT COUNT(*) FROM SYSTEM_RANGE(1, 1000)", name);
            Assert.Equal(128L, x);
            Assert.Equal(0L, max);
        }
        finally
        {
            session.Close();
        }
    }

    private static SessionLocal NewSession(string name)
        => new(
            new Database(ConnectionInfo.Memory(name + "_" + Guid.NewGuid().ToString("N"))), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };

    private sealed class RecordingListener : IH2DatabaseEventListener
    {
        internal List<(H2DatabaseEventState State, string Name, long X, long Max)> Events { get; } = [];

        public void SetProgress(H2DatabaseEventState state, string name, long x, long max)
            => Events.Add((state, name, x, max));
    }
}
