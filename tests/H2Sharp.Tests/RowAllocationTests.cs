/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Allocation guards for the row paths. RowOracleTests proves the behaviour matches Java;
 * these prove the behaviour stays cheap. HasSameValues runs once per row on the
 * "value unchanged" check of UPDATE, so a box here is a box on every updated row.
 */
using System;
using H2Sharp.Result;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// Pins the allocation behaviour of the hot row comparisons.
/// </summary>
[Collection(MeasurementCollection.Name)]
public sealed class RowAllocationTests
{
    /// <summary>
    /// Runs the action a few times to force lazy statics and get past tiered recompilation,
    /// then measures one more run on the current thread. Same discipline as
    /// <c>StringUtilsAllocationTests</c>: a measurement that lands on a recompilation is a
    /// measurement of the compiler, not of the method.
    /// </summary>
    private static long Measure(Action action)
    {
        for (int i = 0; i < 3; i++)
        {
            action();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static Row RowOf(params Value?[] values) => Row.Get(values, SearchRow.MemoryCalculate);

    [Fact]
    public void HasSameValues_EqualRowsAllocateNothing()
    {
        Row row = RowOf(ValueInteger.Get(1), ValueVarchar.Get("x"));
        Row other = RowOf(ValueInteger.Get(1), ValueVarchar.Get("x"));

        Assert.True(row.HasSameValues(other));
        Assert.Equal(0, Measure(() => row.HasSameValues(other)));
    }

    [Fact]
    public void HasSameValues_UnequalRowsAllocateNothing()
    {
        Row row = RowOf(ValueInteger.Get(1), ValueVarchar.Get("x"));
        Row other = RowOf(ValueInteger.Get(1), ValueVarchar.Get("y"));

        Assert.False(row.HasSameValues(other));
        Assert.Equal(0, Measure(() => row.HasSameValues(other)));
    }

    [Fact]
    public void HasSameValues_DifferentLengthsAllocateNothing()
    {
        Row row = RowOf(ValueInteger.Get(1), ValueVarchar.Get("x"));
        Row other = RowOf(ValueInteger.Get(1), ValueVarchar.Get("x"), ValueInteger.Get(2));

        Assert.False(row.HasSameValues(other));
        Assert.Equal(0, Measure(() => row.HasSameValues(other)));
    }
}
