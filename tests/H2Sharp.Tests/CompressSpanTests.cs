/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The span expand path every compressed page read goes through.
 *
 * The array forms are pinned against the reference elsewhere; what is pinned here is that
 * the span forms answer the same bytes, because a page hands them a slice of its buffer
 * rather than a copy. The vectors are fixed so that a codec that stopped round-tripping
 * fails here and not somewhere down a page read.
 */
using System;
using H2Sharp.Compression;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.Tests;

public sealed class CompressSpanTests
{
    private static readonly byte[] Text =
        System.Text.Encoding.Latin1.GetBytes(
            string.Concat(System.Linq.Enumerable.Repeat(
                "the database stores each page compressed as one block of ", 40)));

    [Fact]
    public void SpanExpandMatchesArrayExpand()
    {
        Compressor[] compressors = [new CompressLZF(), new CompressDeflate(), new CompressNo()];
        foreach (Compressor compressor in compressors)
        {
            byte[] packed = new byte[Text.Length * 2];
            int end = compressor.Compress(Text, 0, Text.Length, packed, 0);

            byte[] fromArray = new byte[Text.Length];
            compressor.Expand(packed, 0, end, fromArray, 0, Text.Length);

            byte[] fromSpan = new byte[Text.Length];
            compressor.Expand(new ReadOnlyMemory<byte>(packed, 0, end), fromSpan);

            Assert.Equal(Text, fromArray);
            Assert.Equal(Text, fromSpan);
        }
    }

    [Fact]
    public void SpanExpandRefusesGarbage()
    {
        Assert.Throws<MvStoreException>(
            () => new CompressDeflate().Expand(
                new ReadOnlyMemory<byte>([1, 2, 3, 4, 5, 6, 7, 8]), new byte[8]));
    }

    [Fact]
    public void IncompressibleDataFallsBackToAGrowingBuffer()
    {
        // Random bytes grow under deflate past the rented input-sized buffer, so the fixed
        // buffer refuses and the growing fallback takes over. The round trip still holds.
        byte[] random = new byte[65536];
        new Random(42).NextBytes(random);

        CompressDeflate deflate = new();
        byte[] packed = new byte[random.Length * 2];
        int end = deflate.Compress(random, 0, random.Length, packed, 0);

        byte[] back = new byte[random.Length];
        deflate.Expand(new ReadOnlyMemory<byte>(packed, 0, end), back);
        Assert.Equal(random, back);
    }

    [Fact]
    public void CompressedStoreRoundTripsThroughPages()
    {
        // Both levels, so both span expands run behind page reads: fast is LZF, high is
        // deflate. The values compress, so the pages the reopen reads are compressed ones.
        foreach (bool high in new[] { false, true })
        {
            RoundTripCompressed(high);
        }
    }

    private static void RoundTripCompressed(bool high)
    {
        string file = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "h2sharp-compress-span-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            MVStore.Builder builder = new MVStore.Builder().FileName(file);
            _ = high ? builder.CompressHigh() : builder.Compress();
            using (MVStore store = builder.Open())
            {
                MVMap<int, string> numbers = store.OpenMap<int, string>("numbers");
                for (int i = 0; i < 500; i++)
                {
                    numbers.Put(i, "value-" + i + new string('x', i % 200));
                }

                store.Commit();
            }

            using (MVStore store = new MVStore.Builder().FileName(file).ReadOnly().Open())
            {
                MVMap<int, string> numbers = store.OpenMap<int, string>("numbers");
                for (int i = 0; i < 500; i++)
                {
                    Assert.Equal("value-" + i + new string('x', i % 200), numbers.Get(i));
                }
            }
        }
        finally
        {
            try
            {
                System.IO.File.Delete(file);
            }
            catch (System.IO.IOException)
            {
            }
        }
    }
}
