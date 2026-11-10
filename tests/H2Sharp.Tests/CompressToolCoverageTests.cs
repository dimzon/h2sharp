/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 91: the framing of CompressTool — the algorithm byte, the variable-size
 * length, the fallback to NO when compression does not gain anything, the buffer
 * above MaxBufferSize, the option string after the algorithm name, and the
 * compression errors an empty or truncated buffer produces.
 */
using System;
using System.Linq;
using H2Sharp.Api;
using H2Sharp.Compression;
using H2Sharp.Tools;
using Xunit;

namespace H2Sharp.Tests;

public sealed class CompressToolCoverageTests
{
    private static byte[] Pattern(int length)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < length; i++)
        {
            data[i] = (byte)(i % 251);
        }

        return data;
    }

    /// <summary>
    /// The round trip covers the widths of the variable-size length: 0/1 bytes below
    /// 0x80, two bytes at 200, three at 200 000 and four at 2 200 000; 200 000 also
    /// crosses MaxBufferSize (3 × IoBufferSizeCompress) and takes the large-buffer branch.
    /// Every algorithm string is exercised, including the case-insensitive NO and an
    /// options suffix for DEFLATE.
    /// </summary>
    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "LZF")]
    [InlineData(4, "DEFLATE")]
    [InlineData(5, null)]
    [InlineData(200, "LZF")]
    [InlineData(200, "DEFLATE")]
    [InlineData(200, "DEFLATE level 1")]
    [InlineData(200, "no")]
    [InlineData(200_000, "LZF")]
    [InlineData(2_200_000, "LZF")]
    public void RoundTripsMatchTheFraming(int length, string? algorithm)
    {
        CompressTool tool = CompressTool.GetInstance();
        byte[] data = Pattern(length);
        byte[] compressed = tool.Compress(data, algorithm);
        Assert.Equal(data, tool.Expand(compressed));

        // The static form writes into a caller-supplied buffer at an offset.
        byte[] output = new byte[length + 3];
        CompressTool.Expand(compressed, output, 3);
        Assert.Equal(data, output.Skip(3).ToArray());
    }

    /// <summary>
    /// Random bytes do not compress, so the tool stores them under the NO algorithm and
    /// the expanded value is the input itself.
    /// </summary>
    [Fact]
    public void IncompressibleDataFallsBackToNo()
    {
        Random random = new(1);
        byte[] data = new byte[1000];
        random.NextBytes(data);

        CompressTool tool = CompressTool.GetInstance();
        byte[] compressed = tool.Compress(data, "LZF");
        Assert.Equal(Compressors.No, compressed[0]);
        Assert.Equal(data, tool.Expand(compressed));
    }

    /// <summary>
    /// Every width of the Rice-coded integer: the terminator bytes 0xf0 and the five-byte
    /// form for negatives and values at or above 0x10000000.
    /// </summary>
    [Fact]
    public void VariableIntsUseEveryWidth()
    {
        int[] values =
        [
            0, 0x7f, 0x80, 0x3fff, 0x4000, 0x1f_ffff, 0x20_0000, 0xfff_ffff,
            0x1000_0000, int.MaxValue, -1, int.MinValue,
        ];
        foreach (int value in values)
        {
            byte[] buffer = new byte[5];
            int written = CompressTool.WriteVariableInt(buffer, 0, value);
            Assert.Equal(CompressTool.GetVariableIntLength(value), written);
            Assert.Equal(value, CompressTool.ReadVariableInt(buffer, 0));
        }
    }

    /// <summary>
    /// An empty frame, a frame whose length integer is cut off and a body the compressor
    /// cannot expand are all compression errors; an unknown algorithm name is refused
    /// before any byte is written.
    /// </summary>
    [Fact]
    public void BadFramesAndAlgorithmsAreRefused()
    {
        CompressTool tool = CompressTool.GetInstance();

        Assert.Equal(
            ErrorCode.CompressionError,
            Assert.Throws<H2SharpException>(() => tool.Expand([])).H2ErrorCode);
        Assert.Equal(
            ErrorCode.CompressionError,
            Assert.Throws<H2SharpException>(() => tool.Expand([Compressors.LZF])).H2ErrorCode);

        byte[] bad = [Compressors.LZF, 100, 1, 2, 3];
        Assert.Equal(
            ErrorCode.CompressionError,
            Assert.Throws<H2SharpException>(() => tool.Expand(bad)).H2ErrorCode);
        Assert.Equal(
            ErrorCode.CompressionError,
            Assert.Throws<H2SharpException>(() => CompressTool.Expand(bad, new byte[100], 0)).H2ErrorCode);

        Assert.Equal(
            ErrorCode.UnsupportedCompressionAlgorithm1,
            Assert.Throws<H2SharpException>(() => tool.Compress([1, 2, 3, 4, 5], "BZIP2")).H2ErrorCode);
    }
}
