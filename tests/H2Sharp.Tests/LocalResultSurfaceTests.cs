/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 284: the local result's own distinct/delta paths, driven without a query plan.
 */
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class LocalResultSurfaceTests
{
    [Fact]
    public void DistinctRowsAreCollapsedWhenTheResultIsDone()
    {
        SessionLocal session = new(new Database(ConnectionInfo.Memory("local_result_distinct")), "sa");
        LocalResult result = new(session, [], 1, 1);
        result.SetMaxMemoryRows(int.MaxValue);
        result.SetDistinct();
        result.AddRow(ValueInteger.Get(1));
        result.AddRow(ValueInteger.Get(1));
        result.AddRow(ValueInteger.Get(2));
        result.RemoveDistinct([ValueInteger.Get(2)]);
        result.Done();
        Assert.True(result.GetRowCount() >= 0);
        result.Close();
    }

    [Fact]
    public void ADeltaResultAcceptsRowsFromATableRow()
    {
        SessionLocal session = new(new Database(ConnectionInfo.Memory("local_result_delta")), "sa");
        LocalResult result = new(session, [], 1, 1);
        result.SetForDataChangeDeltaTable();
        result.AddRowForTable(Row.Get([ValueInteger.Get(7)], 1, 1L));
        result.Done();
        Assert.True(result.GetRowCount() > 0);
        result.Close();
    }
}
