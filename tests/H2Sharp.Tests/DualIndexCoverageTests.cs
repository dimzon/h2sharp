/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of DualIndex's simple overrides.
 */
using System;
using H2Sharp;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Tables;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class DualIndexCoverageTests
{
    [Fact]
    public void DualIndexOverridesAnswer()
    {
        Database db = new(ConnectionInfo.Memory("di_" + Guid.NewGuid().ToString("N")));
        SessionLocal session = new(db, "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
        DualTable dual = new(db);
        DualIndex idx = new(dual);
        _ = idx.GetCreateSql();
        _ = idx.GetRowCount(session);
        _ = idx.GetRowCountApproximation(session);
        Assert.True(idx.CanGetFirstOrLast());
        _ = idx.GetPlanSql();
        _ = idx.FindFirstOrLast(session, true);
        idx.Close(session);
        Assert.False(idx.NeedRebuild());
        session.Close();
    }
}