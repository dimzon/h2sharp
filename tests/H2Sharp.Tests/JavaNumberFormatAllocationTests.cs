/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Allocation guards for the number formatter.
 *
 * JavaNumberFormatOracleTests proves the text matches the reference. This proves it stays
 * cheap, which matters more here than elsewhere in the port: the formatting is a search, it
 * runs once per number that reaches SQL or a message, and a search that allocates a closure
 * per step turns a gen0 workload into a gen2 one without anything failing.
 *
 * The numbers here are deliberately loose. They exist to catch an order of magnitude, not to
 * pin a byte count that a runtime update would break.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// Pins the allocation behaviour of the number formatter.
/// </summary>
[Collection(MeasurementCollection.Name)]

public sealed class JavaNumberFormatAllocationTests
{
    /// <summary>
    /// The smallest allocation the action ever costs on this thread.
    /// </summary>
    /// <remarks>
    /// The minimum of several runs, not the average and not a single one. A first call pays
    /// for JIT and for tiered compilation promoting the method later, which is why the action is
    /// warmed up, but tiering can still land on the second or third call and would make a single
    /// measurement flap. Taking the smallest run measures the steady state, which is the thing
    /// the guard is about.
    /// </remarks>
    /// <param name="action">the action to measure</param>
    /// <returns>the smallest allocation it made</returns>
    private static long Measure(Action action)
    {
        for (int i = 0; i < 3; i++)
        {
            action();
        }

        long smallest = long.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            action();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (allocated < smallest)
            {
                smallest = allocated;
            }
        }

        return smallest;
    }

    /// <summary>
    /// The values the search is measured on, chosen so that one value ends the search early
    /// and one forces it to the last digit.
    /// </summary>
    /// <remarks>
    /// 1.5 is one digit, so the search stops after one step; 0.1 needs seventeen, so it walks
    /// the whole range. Measuring only the easy case would hide the cost that matters.
    /// </remarks>
    private static readonly double[] Searched =
    [
        0.1, 0.3, 1.0 / 3.0, Math.PI, 1e-300, 1.7976931348623157e308, 5e-324, -0.1, 2.0 / 3.0,
    ];

    [Fact]
    public void SearchDoesNotAllocatePerStep()
    {
        const int Count = 200;

        long allocated = Measure(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                foreach (double d in Searched)
                {
                    _ = JavaNumberFormat.DoubleToString(d);
                }
            }
        });

        // One result string per call, plus the intermediate strings the search formats. If the
        // search allocated a closure or a boxed delegate per step this would be several times
        // larger; a hundred bytes per call is roughly one string and the E form.
        Assert.InRange(allocated, Count * 8, Count * (long)Searched.Length * 200);
    }

    [Fact]
    public void WholeNumbersStayCheap()
    {
        const int Count = 2000;

        long allocated = Measure(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                _ = JavaNumberFormat.DoubleToString(i);
                _ = JavaNumberFormat.FloatToString(i);
            }
        });

        // Small integers are one digit, so the search exits immediately and the cost is the
        // result string alone. Anything per-step would show here as well.
        Assert.InRange(allocated, Count, Count * 200);
    }

    [Fact]
    public void RepeatedFormattingOfTheSameValueIsStable()
    {
        // Not an allocation test but a property the search must have for the guard above to
        // mean anything: the same input always produces the same text, so a cache could be
        // added later without changing behaviour.
        double[] values = [0.1, 1.0 / 3.0, 1e-300, double.MaxValue, -0.0];
        foreach (double d in values)
        {
            string first = JavaNumberFormat.DoubleToString(d);
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(first, JavaNumberFormat.DoubleToString(d));
            }
        }
    }

    [Fact]
    public void IntegersDoNotNeedTheSearch()
    {
        // A sanity check on the corpus of the allocation guard: the easy values really are
        // easy, so the guard is measuring the cheap path it claims to.
        long easy = Measure(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                _ = JavaNumberFormat.DoubleToString(2.0);
            }
        });

        long hard = Measure(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                _ = JavaNumberFormat.DoubleToString(2.0 / 3.0);
            }
        });

        Assert.True(
            easy <= hard,
            $"a one digit value should not cost more than a seventeen digit one: {easy} vs {hard}");
    }
}
