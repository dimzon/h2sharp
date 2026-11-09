/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 67: direct coverage of SessionLocal's getters/setters and the
 * close/rollback/transaction paths that the SQL suites do not reach.
 */
using System;
using H2Sharp;
using H2Sharp.Engine;
using H2Sharp.Tables;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class SessionLocalCoverageTests
{
    private static SessionLocal NewSession(string name)
        => new(new Database(ConnectionInfo.Memory(name)), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };

    [Fact]
    public void QuirksModeAndParameters()
    {
        SessionLocal s = NewSession("sl_qp_" + Guid.NewGuid().ToString("N"));
        s.SetQuirksMode(true);
        Assert.True(s.IsQuirksMode());
        s.SetQuirksMode(false);

        Value v = ValueInteger.Get(42);
        s.SetParameter("p", v);
        Assert.True(s.IsParameterSet("p"));
        Assert.Equal(42, s.GetParameterValue("p").GetInt());
        _ = s.Parameters;

        s.SetCurrentRow(null);
        Assert.Throws<TODOException>(() => _ = s.GetRowValue(
            new Column("X", TypeInfo.TypeInteger, null, 0)));

        _ = s.HasPreparedTransaction();
        _ = s.GetLocalTempTableIndexes();
        s.ClearViewIndexCache();
        _ = s.IsParsingCreateView();
        _ = s.CurrentTimeZone();
        _ = s.GetDynamicSettings();
        _ = s.GetThrottle();
        s.SetThrottle(5);
        _ = s.GetThrottle();
        s.ClearQueryCache();

        s.Close();
    }

    [Fact]
    public void CancelAndClosePaths()
    {
        SessionLocal s = NewSession("sl_cc_" + Guid.NewGuid().ToString("N"));
        s.Cancel();
        s.Close();
        Assert.True(s.IsClosed());

        SessionLocal s2 = NewSession("sl_ab_" + Guid.NewGuid().ToString("N"));
        s2.Abort();
        Assert.True(s2.IsClosed());
    }

    [Fact]
    public void PreparedTransactionClosePath()
    {
        SessionLocal s = NewSession("sl_pt_" + Guid.NewGuid().ToString("N"));
        s.PrepareCommit("tx1");
        Assert.True(s.HasPreparedTransaction());
        s.Close();
    }

    [Fact]
    public void CheckReadRightThrowsTODO()
    {
        SessionLocal s = NewSession("sl_cr_" + Guid.NewGuid().ToString("N"));
        Assert.Throws<TODOException>(() => s.CheckReadRight(null));
        s.Close();
    }
}