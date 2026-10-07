/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Allocation guards for the value layer.
 *
 * These pin behaviour that a refactor would break quietly and a functional test would not
 * notice. Two things are guarded here and both were measured against the reference's own
 * shape:
 *
 * Small integers and small bigints are preallocated, so asking for one allocates nothing at
 * all. A query with a literal in it builds one of these per row parsed, and a megabyte of
 * garbage per million rows is the difference between gen0 pressure and a collection.
 *
 * A bigint outside the preallocated range costs one object, even when the shared cache is
 * about to answer with an equal value that is already there. That is what the reference does,
 * because it builds the candidate and then asks the cache, and the number is recorded rather
 * than improved: a deviation that saves the allocation is a deviation from the reference, and
 * that is a decision to make on purpose, not in passing.
 *
 * Rendering is free when the caller reuses its builder, which is how the engine calls it, so
 * that is guarded too.
 */
using System;
using System.Globalization;
using System.Text;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// Pins the allocation behaviour of the value layer.
/// </summary>
public sealed class ValueAllocationTests
{
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

    private const int Count = 100_000;

    [Fact]
    public void PreallocatedSmallValuesAllocateNothing()
    {
        long integers = Measure(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                _ = ValueInteger.Get(i % 128);
            }
        });

        long bigints = Measure(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                _ = ValueBigint.Get(i % 90);
            }
        });

        Assert.Equal(0, integers);
        Assert.Equal(0, bigints);
    }

    [Fact]
    public void NullAllocatesNothing()
    {
        long allocated = Measure(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                _ = ValueNull.Instance;
            }
        });

        Assert.Equal(0, allocated);
    }

    /// <summary>
    /// One object for a value outside the preallocated range, which is what the reference
    /// costs as well.
    /// </summary>
    [Fact]
    public void BigintsOutsideTheRangeCostOneObject()
    {
        long allocated = Measure(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                _ = ValueBigint.Get(1_000_000L + i);
            }
        });

        // Recorded rather than improved: the reference builds the candidate and then asks the
        // cache, so it pays the same. If this is ever changed, the change is on purpose and
        // this bound is what says so.
        Assert.InRange(allocated, Count, Count * 64L);
    }

    [Fact]
    public void RenderingIntoAReusedBuilderAllocatesNothing()
    {
        StringBuilder reused = new();
        Value v = ValueInteger.Get(42);

        long allocated = Measure(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                reused.Clear();
                _ = v.GetSql(reused, 0);
                _ = v.GetString();
            }
        });

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ArithmeticOnCachedValuesAllocatesNothing()
    {
        // The interesting case: a loop over literals, which is what a query does, must not
        // allocate once the values are inside the preallocated range.
        Value a = ValueInteger.Get(100);
        Value b = ValueInteger.Get(7);

        long allocated = Measure(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                _ = a.Add(b);
                _ = a.Subtract(b);
                _ = a.Multiply(b);
                _ = a.Divide(b, TypeInfo.TypeBigint);
                _ = a.Modulus(b);
            }
        });

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void TheCacheAnswersWithTheSameInstance()
    {
        // Not an allocation test, but the property the cache exists for: equal values are the
        // same object once they are in the shared cache, which is what makes a repeated literal
        // cheap.
        Value first = ValueBigint.Get(1_000_000L);
        Value second = ValueBigint.Get(1_000_000L);
        Assert.True(ReferenceEquals(first, second));

        Value.ClearCache();
        Value third = ValueBigint.Get(1_000_000L);
        Assert.Equal(first, third);
        Assert.True(
            first.GetHashCode().ToString(CultureInfo.InvariantCulture)
                == third.GetHashCode().ToString(CultureInfo.InvariantCulture));
    }
}