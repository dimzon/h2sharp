/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The database object itself: identity, session bookkeeping, the read-only switch, the
 * event listener callbacks, the closing state, and the default NULL ordering.
 */
using System;
using System.Collections.Generic;
using H2Sharp;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Message;
using Xunit;

namespace H2Sharp.Tests;

public sealed class DatabaseSurfaceTests
{
    private static Database NewDatabase(string name)
        => new(ConnectionInfo.Memory(name + "_" + Guid.NewGuid().ToString("N")));

    [Fact]
    public void GettersAnswerAndSessionsAreCounted()
    {
        Database db = NewDatabase("db_surface");
        Assert.True(db.IsMemory());
        Assert.False(db.IsReadOnly());
        Assert.NotNull(db.GetConnectionInfo());
        Assert.NotNull(db.GetMvStore());
        Assert.False(db.IsCaseInsensitiveIdentifiers());
        Assert.Equal(DefaultNullOrdering.LOW, db.GetDefaultNullOrdering());
        Assert.Equal(0, db.GetSessionCount());

        SessionLocal session = new(db, "sa");
        Assert.Equal(1, db.GetSessionCount());
        session.Close();
        Assert.Equal(0, db.GetSessionCount());
        db.Close();
    }

    [Fact]
    public void ReadOnlyDatabaseRefusesWrites()
    {
        Database db = NewDatabase("db_readonly");
        db.SetReadOnly(true);
        Assert.True(db.IsReadOnly());
        H2SharpException error = Assert.Throws<H2SharpException>(() => db.CheckWritingAllowed());
        Assert.Equal(Api.ErrorCode.DatabaseIsReadOnly, error.H2ErrorCode);
        db.SetReadOnly(false);
        db.CheckWritingAllowed();
        db.Close();
    }

    [Fact]
    public void ProgressAndExceptionsReachTheListenerOnce()
    {
        Database db = NewDatabase("db_listener");
        RecordingListener listener = new();
        db.SetDatabaseEventListener(listener);
        db.SetProgress(H2DatabaseEventState.ScanFile, "T", 5, 10);
        H2SharpException problem = H2Sharp.Message.DbException.Get(Api.ErrorCode.DatabaseIsClosed, "X");
        db.ExceptionThrown(problem, "SELECT 1");
        Assert.Equal(["progress|ScanFile|T|5|10"], listener.Events);
        Assert.Same(problem, listener.LastException);
        Assert.Equal("SELECT 1", listener.LastSql);

        // A listener's own failure is the listener's business: the call swallows it rather
        // than failing the operation that reported progress.
        db.SetDatabaseEventListener(new ThrowingListener());
        db.SetProgress(H2DatabaseEventState.CreateIndex, "I", 1, 2);
        db.ExceptionThrown(problem, "SELECT 2");
        db.Close();
    }

    [Fact]
    public void TimestampOutsideAStatementIsRefused()
    {
        Database db = NewDatabase("db_clock");
        H2SharpException unsupported = Assert.Throws<H2SharpException>(() => db.CurrentTimestamp());
        Assert.Equal(Api.ErrorCode.FeatureNotSupported1, unsupported.H2ErrorCode);
        H2SharpException zone = Assert.Throws<H2SharpException>(() => db.CurrentTimeZone());
        Assert.Equal(Api.ErrorCode.FeatureNotSupported1, zone.H2ErrorCode);
        db.Close();
        // Closing twice is the same close; CheckClosed then answers with the closed error.
        db.Close();
        Assert.Throws<H2SharpException>(() => db.CheckClosed());
    }

    [Fact]
    public void DefaultNullOrderingChangesAndUnlockMetaIsANoOp()
    {
        Database db = NewDatabase("db_null_order");
        db.SetDefaultNullOrdering(DefaultNullOrdering.HIGH);
        Assert.Equal(DefaultNullOrdering.HIGH, db.GetDefaultNullOrdering());
        db.SetDefaultNullOrdering(DefaultNullOrdering.FIRST);
        Assert.Equal(DefaultNullOrdering.FIRST, db.GetDefaultNullOrdering());
        SessionLocal session = new(db, "sa");
        db.UnlockMeta(session);
        session.Close();
        db.Close();
    }

    private sealed class RecordingListener : IH2DatabaseEventListener
    {
        internal List<string> Events { get; } = [];

        internal Exception? LastException { get; private set; }

        internal string? LastSql { get; private set; }

        public void SetProgress(H2DatabaseEventState state, string name, long x, long max)
            => Events.Add("progress|" + state + "|" + name + "|" + x + "|" + max);

        public void ExceptionThrown(System.Data.Common.DbException e, string sql)
        {
            LastException = e;
            LastSql = sql;
        }
    }

    private sealed class ThrowingListener : IH2DatabaseEventListener
    {
        public void SetProgress(H2DatabaseEventState state, string name, long x, long max)
            => throw new InvalidOperationException("listener failure");

        public void ExceptionThrown(System.Data.Common.DbException e, string sql)
            => throw new InvalidOperationException("listener failure");
    }
}
