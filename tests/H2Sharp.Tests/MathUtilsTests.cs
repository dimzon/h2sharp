/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The utility arithmetic and the entropy sources. The deterministic helpers are pinned by
 * their reference answers; the random ones cannot be compared value for value, so what is
 * checked is what the reference promises: the range, that attempts differ, and that the seed
 * is not empty.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class MathUtilsTests
{
    [Theory]
    [InlineData(0, 8, 0)]
    [InlineData(1, 8, 8)]
    [InlineData(8, 8, 8)]
    [InlineData(9, 8, 16)]
    [InlineData(16, 8, 16)]
    [InlineData(17, 8, 24)]
    [InlineData(100, 8, 104)]
    [InlineData(0, 1, 0)]
    [InlineData(5, 1, 5)]
    public void RoundUpIntRoundsToTheBlock(int value, int block, int expected)
        => Assert.Equal(expected, MathUtils.RoundUpInt(value, block));

    [Theory]
    [InlineData(0L, 4096L, 0L)]
    [InlineData(1L, 4096L, 4096L)]
    [InlineData(4096L, 4096L, 4096L)]
    [InlineData(4097L, 4096L, 8192L)]
    public void RoundUpLongRoundsToTheBlock(long value, long block, long expected)
        => Assert.Equal(expected, MathUtils.RoundUpLong(value, block));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(5, 8)]
    [InlineData(1023, 1024)]
    [InlineData(1024, 1024)]
    [InlineData(0x40000000, 0x40000000)]
    public void NextPowerOf2RoundsUp(int value, int expected)
        => Assert.Equal(expected, MathUtils.NextPowerOf2(value));

    [Fact]
    public void NextPowerOf2RefusesValuesPastTheLargestPower()
    {
        _ = Assert.Throws<ArgumentException>(() => MathUtils.NextPowerOf2(0x40000001));
        _ = Assert.Throws<ArgumentException>(() => MathUtils.NextPowerOf2(int.MaxValue));
    }

    [Theory]
    [InlineData(long.MinValue, int.MinValue)]
    [InlineData(-1L, -1)]
    [InlineData(0L, 0)]
    [InlineData(1L, 1)]
    [InlineData((long)int.MaxValue - 1, int.MaxValue - 1)]
    [InlineData((long)int.MaxValue, int.MaxValue)]
    [InlineData((long)int.MaxValue + 1, int.MaxValue)]
    [InlineData(long.MaxValue, int.MaxValue)]
    public void ConvertLongToIntSaturates(long value, int expected)
        => Assert.Equal(expected, MathUtils.ConvertLongToInt(value));

    [Theory]
    [InlineData(int.MinValue, short.MinValue)]
    [InlineData(-1, -1)]
    [InlineData(0, 0)]
    [InlineData((int)short.MaxValue - 1, short.MaxValue - 1)]
    [InlineData((int)short.MaxValue, short.MaxValue)]
    [InlineData((int)short.MaxValue + 1, short.MaxValue)]
    [InlineData(int.MaxValue, short.MaxValue)]
    public void ConvertIntToShortSaturates(int value, short expected)
        => Assert.Equal(expected, MathUtils.ConvertIntToShort(value));

    [Fact]
    public void TheAlternativeSeedIsNotEmptyAndDiffersBetweenCalls()
    {
        byte[] first = MathUtils.GenerateAlternativeSeed();
        byte[] second = MathUtils.GenerateAlternativeSeed();

        Assert.True(first.Length > 64, "the seed should carry the timing loop as well");
        // Two seeds drawn milliseconds apart cannot be equal: the timing loop alone differs.
        Assert.False(first.SequenceEqual(second), "two seeds were identical");
    }

    [Fact]
    public void TheSecureRandomLongDrawsMoreThanOneValue()
    {
        HashSet<long> values = [];
        for (int i = 0; i < 64; i++)
        {
            values.Add(MathUtils.SecureRandomLong());
        }

        Assert.True(values.Count > 1, "all drawn values were identical");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void TheSecureRandomIntStaysInRange(int lowerThan)
    {
        for (int i = 0; i < 64; i++)
        {
            int value = MathUtils.SecureRandomInt(lowerThan);
            Assert.InRange(value, 0, lowerThan - 1);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void ThePseudoRandomIntStaysInRange(int lowerThan)
    {
        for (int i = 0; i < 64; i++)
        {
            int value = MathUtils.RandomInt(lowerThan);
            Assert.InRange(value, 0, lowerThan - 1);
        }
    }

    [Fact]
    public void ThePseudoRandomBytesFillTheArray()
    {
        byte[] bytes = new byte[32];
        MathUtils.RandomBytes(bytes);

        Assert.Contains(bytes, b => b != 0);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 1)]
    [InlineData(16, 16)]
    public void TheSecureRandomBytesHaveTheRequestedLength(int requested, int expectedLength)
        => Assert.Equal(expectedLength, MathUtils.SecureRandomBytes(requested).Length);
}
