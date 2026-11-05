/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The Fletcher-32 contract is the scalar reference; a vector path was attempted and
 * withdrawn (32-bit lanes required, Vector256 widening unavailable — see the note on
 * DataUtils.GetFletcher32). These tests pin the byte-identical contract across the
 * reduction-window and odd-length boundaries so that any future vector formulation must
 * match them exactly.
 */
using System;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.Tests;

public sealed class FletcherVectorParityTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(17)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(719)]
    [InlineData(720)]
    [InlineData(721)]
    [InlineData(1439)]
    [InlineData(1440)]
    [InlineData(1441)]
    [InlineData(4096)]
    [InlineData(4097)]
    public void ContractIsStableAcrossBoundaries(int length)
    {
        Random random = new(12345 + length);
        byte[] bytes = new byte[length];
        random.NextBytes(bytes);

        Assert.Equal(
            DataUtils.GetFletcher32Scalar(bytes),
            DataUtils.GetFletcher32(bytes));
    }

    [Fact]
    public void ContractIsStableAcrossManySeeds()
    {
        for (int seed = 0; seed < 64; seed++)
        {
            Random random = new(seed);
            int length = random.Next(0, 4096);
            byte[] bytes = new byte[length];
            random.NextBytes(bytes);
            Assert.Equal(
                DataUtils.GetFletcher32Scalar(bytes),
                DataUtils.GetFletcher32(bytes));
        }
    }
}
