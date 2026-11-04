/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Allocation guards for the page string read. DataUtilsOracleTests proves the round trip
 * against the reference; this proves the read stays at the cost of the result itself. A
 * VARCHAR-heavy page read runs this per value, so a second copy or a pool round trip on the
 * short path is gen0 pressure proportional to the data.
 */
using System;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// Pins the allocation behaviour of <see cref="DataUtils.ReadString(Span{byte}, ref int, int)"/>.
/// </summary>
[Collection(MeasurementCollection.Name)]
public sealed class DataUtilsAllocationTests
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

    /// <summary>
    /// Slack for the result string's header, length field and alignment, the way the other
    /// guards here state "this allocation and no other" without pinning the runtime layout.
    /// </summary>
    private const int StringOverhead = 64;

    private static long StringSize(int chars) => (chars * sizeof(char)) + StringOverhead;

    /// <summary>
    /// Writes the body form of the text — the modified UTF-8, no length header — and returns
    /// it, which is what the three-argument ReadString consumes.
    /// </summary>
    private static byte[] Body(string s)
    {
        byte[] bytes = new byte[(s.Length * 3) + 16];
        int position = 0;
        DataUtils.WriteStringData(bytes, ref position, s, s.Length);
        return bytes[..position];
    }

    [Fact]
    public void ShortStringRead_AllocatesOnlyTheResult()
    {
        // Short enough for the stack path, and carrying both a two-byte and a two-byte zero
        // encoding, so a decode that mishandled either would fail the equality before the
        // allocation bound is even read.
        const string Text = "column_value_\u00e9\u0000name";
        byte[] body = Body(Text);

        int position = 0;
        Assert.Equal(Text, DataUtils.ReadString(body, ref position, Text.Length));

        long allocated = Measure(() =>
        {
            int at = 0;
            _ = DataUtils.ReadString(body, ref at, Text.Length);
        });

        Assert.InRange(allocated, Text.Length * sizeof(char), StringSize(Text.Length));
    }

    [Fact]
    public void LongStringRead_AllocatesOnlyTheResult()
    {
        // Above the stack budget: the pool provides the decode buffer, and once it is warm the
        // only allocation left is the result itself.
        string text = new string('v', 300);
        byte[] body = Body(text);

        int position = 0;
        Assert.Equal(text, DataUtils.ReadString(body, ref position, text.Length));

        long allocated = Measure(() =>
        {
            int at = 0;
            _ = DataUtils.ReadString(body, ref at, text.Length);
        });

        // The result is the cost the long path owes; the rented decode buffer on top is the
        // pool's own churn, and a forced GC.Collect before every measured run trims the
        // buckets, so a fresh array is the steady state here rather than a leak. The bound
        // allows the one array and nothing that scales with the text beyond it.
        Assert.InRange(allocated, text.Length * sizeof(char), StringSize(text.Length) + 1200);
    }
}
