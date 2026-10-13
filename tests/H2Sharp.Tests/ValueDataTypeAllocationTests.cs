/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Allocation guard for reading byte strings out of the store.
 *
 * A VARBINARY or BINARY on a page used to cost two byte arrays: one out of the page buffer
 * and one cloned into the value. The buffer array is private to the read, so the value takes
 * it as is, and one ten kilobyte payload costs one ten kilobyte array rather than two.
 */
using System;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Db;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(MeasurementCollection.Name)]

public sealed class ValueDataTypeAllocationTests
{
    private const int PayloadBytes = 10_000;

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

    [Fact]
    public void ReadingAVarbinaryCopiesItsBytesOnce()
    {
        // Past the object cache limit, so the cache stays out of the measurement.
        byte[] payload = new byte[PayloadBytes];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i * 31);
        }

        ValueDataType type = new(null, CompareMode.GetInstance(null, 0), null, null);
        WriteBuffer buffer = new(64);
        type.Write(buffer, ValueVarbinary.Get(payload));
        byte[] written = buffer.ToArray();

        int position = 0;
        Value back = type.Read(written, ref position);
        Assert.Equal(written.Length, position);
        Assert.Equal(payload, ((ValueVarbinary)back).GetBytes());

        long allocated = Measure(() =>
        {
            int at = 0;
            _ = type.Read(written, ref at);
        });

        // One payload array plus the value object. Two arrays would be twice the payload.
        Assert.True(allocated < PayloadBytes + 1_000, $"allocated {allocated}");
    }
}
