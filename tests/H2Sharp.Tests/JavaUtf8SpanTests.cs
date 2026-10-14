/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The span forms of the UTF-8 codec: decoding a slice answers what decoding its array
 * does, and encoding into a caller buffer answers what the allocating form does.
 *
 * The corpus is decoded both ways on purpose: the array form is pinned against the
 * reference elsewhere, so agreement here carries that pinning over to the span form,
 * including the malformed shapes (overlong, surrogate, truncated) where the two could
 * plausibly disagree.
 */
using System;
using System.Buffers;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

[Collection(MeasurementCollection.Name)]

public sealed class JavaUtf8SpanTests
{
    private static readonly byte[][] Corpus =
    [
        [],
        [0x61],
        [0x61, 0x62, 0x63],
        [0xC3, 0xA9],
        [0xE2, 0x82, 0xAC],
        [0xF0, 0x9F, 0x98, 0x80],
        [0xFF],
        [0x80],
        [0xC0, 0xAF],
        [0xED, 0xA0, 0x80],
        [0xE2, 0x82],
        [0xF0, 0x9F, 0x98],
        [0x61, 0xFF, 0x62, 0xC3, 0xA9, 0x63],
    ];

    private static readonly string[] Texts =
    [
        "",
        "abc",
        "caf\u00e9",
        "a\u20acb",
        "\U0001F600",
        "a\ud800b",
        "\ud800",
        "plain ascii text that stays on the fast path",
    ];

    [Fact]
    public void DecodingASliceMatchesDecodingItsArray()
    {
        foreach (byte[] bytes in Corpus)
        {
            Assert.Equal(JavaUtf8.GetString(bytes), JavaUtf8.GetString(bytes.AsSpan()));
        }
    }

    [Fact]
    public void DecodingASliceOfALargerArrayMatches()
    {
        // A slice that starts and ends mid-array: the span form must not read past its ends.
        byte[] bytes = [0x61, 0xC3, 0xA9, 0x62, 0xFF, 0x63];
        Assert.Equal("a\u00e9b", JavaUtf8.GetString(bytes.AsSpan(0, 4)));
        Assert.Equal("\u00e9b", JavaUtf8.GetString(bytes.AsSpan(1, 3)));
        Assert.Equal("b", JavaUtf8.GetString(bytes.AsSpan(3, 1)));
    }

    [Fact]
    public void EncodingIntoABufferMatchesTheAllocatingForm()
    {
        foreach (string text in Texts)
        {
            byte[] expected = JavaUtf8.GetBytes(text);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(expected.Length);
            try
            {
                Assert.True(JavaUtf8.TryGetBytes(text, buffer, out int written));
                Assert.Equal(expected.Length, written);
                Assert.Equal(expected, buffer.AsSpan(0, written).ToArray());
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    [Fact]
    public void AShortBufferFailsWithoutWriting()
    {
        string text = "caf\u00e9";
        byte[] buffer = new byte[JavaUtf8.GetBytes(text).Length - 1];
        Assert.False(JavaUtf8.TryGetBytes(text, buffer, out int written));
        Assert.Equal(0, written);
    }

    [Fact]
    public void EncodingIntoABufferAllocatesNothing()
    {
        string text = "caf\u00e9 \U0001F600 done";
        byte[] buffer = JavaUtf8.GetBytes(text);
        long allocated = Measure(() => Assert.True(JavaUtf8.TryGetBytes(text, buffer, out _)));
        Assert.Equal(0, allocated);
    }

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
}
