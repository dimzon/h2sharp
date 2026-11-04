/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Allocation guard for Data.CopyString. The old shape built its char and byte scratch
 * buffers per call; the rented shape pays for them once and the pool churns only when a
 * forced collection trims it. Ten calls make the difference visible: the old shape paid
 * the buffers ten times over, the rented one does not pay them once.
 */
using System;
using System.IO;
using H2Sharp.Engine;
using Xunit;
using PageData = H2Sharp.Store.Data;

namespace H2Sharp.Tests;

/// <summary>
/// Pins the allocation behaviour of <see cref="Data.CopyString"/>.
/// </summary>
[Collection(MeasurementCollection.Name)]
public sealed class DataCopyStringAllocationTests
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

    [Fact]
    public void CopyString_PaysForTheScratchOnce()
    {
        // Several blocks: the loop crosses IoBufferSize more than once, so a per-block or
        // per-call buffer would show up multiplied.
        const int Calls = 10;
        char[] text = new char[3 * Constants.IoBufferSize];
        Array.Fill(text, 'x');
        string payload = new(text);

        byte[] sink = new byte[3 * Constants.IoBufferSize * Calls];
        using MemoryStream target = new(sink, writable: true);

        long allocated = Measure(() =>
        {
            for (int i = 0; i < Calls; i++)
            {
                target.Position = 0;
                PageData.CopyString(new StringReader(payload), target);
            }
        });

        // The two scratch buffers are the only shape this method is allowed to touch, and a
        // forced GC.Collect before every measured run trims the pool into re-renting them
        // once per measured run - rounded up to the pool's power-of-two buckets, which is
        // where the 8K char and 16K byte figures come from. Ten calls at the old per-call
        // shape cost the buffers ten times over.
        Assert.True(
            allocated < (2 * Constants.IoBufferSize * sizeof(char))
                + (2 * 3 * Constants.IoBufferSize) + 1024,
            $"allocated {allocated} bytes for {Calls} calls");
    }
}
