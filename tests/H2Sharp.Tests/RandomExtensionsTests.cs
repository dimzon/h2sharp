/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The non-negative Random extensions, against a generator that violates the .NET contract.
 */
using System;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class RandomExtensionsTests
{
    [Fact]
    public void TheSignBitIsClearedForAnyImplementation()
    {
        NegativeRandom random = new();

        Assert.Equal(int.MaxValue, random.NextNonNegativeInt32());
        Assert.Equal(long.MaxValue, random.NextNonNegativeInt64());
        Assert.Equal(7, random.NextNonNegativeInt32(10));
        Assert.Equal(7, random.NextNonNegativeInt64(10));
    }

    [Fact]
    public void TheSignedDrawKeepsTheSign()
    {
        Assert.Equal(-1, new NegativeRandom().NextSignedInt64());
    }

    private sealed class NegativeRandom : Random
    {
        public override int Next() => -1;

        public override int Next(int maxValue) => -1;

        public override long NextInt64() => -1;

        public override long NextInt64(long maxValue) => -1;

        public override long NextInt64(long minValue, long maxValue) => -1;
    }
}
