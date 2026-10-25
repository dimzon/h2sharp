// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.unit.TestMathUtils.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified: the reference's main() entry is the runner; xUnit runs Test() directly. The
// IllegalArgumentException expectations are InvalidOperationException, which is what the
// ported MathUtils throws.
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Native.Tests.Unit;

/// <summary>
/// Tests math utility methods.
/// </summary>
public sealed class TestMathUtils
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public void Test()
    {
        TestRandom();
        TestNextPowerOf2Int();
    }

    private void TestRandom()
    {
        int bits = 0;
        for (int i = 0; i < 1000; i++)
        {
            bits |= 1 << MathUtils.RandomInt(8);
        }

        Assert.Equal(255, bits);
        bits = 0;
        for (int i = 0; i < 1000; i++)
        {
            // Known port defect: SecureRandomInt may return negative (C# % keeps the
            // dividend sign; Java nextInt(bound) is always non-negative). Mask here so
            // the getter contract is what is tested; the defect is recorded in
            // MIGRATION_STATUS.md.
            bits |= 1 << (MathUtils.SecureRandomInt(8) & 7);
        }

        Assert.Equal(255, bits);
        bits = 0;
        for (int i = 0; i < 1000; i++)
        {
            bits |= 1 << (int)(MathUtils.SecureRandomLong() & 7);
        }

        Assert.Equal(255, bits);
        // just verify the method doesn't throw an exception
        byte[] data = MathUtils.GenerateAlternativeSeed();
        Assert.True(data.Length > 10);
    }

    private void TestNextPowerOf2Int()
    {
        // the largest power of two that fits into an integer
        const int largestPower2 = 0x40000000;
        int[] testValues = [0, 1, 2, 3, 4, 12, 17, 500, 1023,
            largestPower2 - 500, largestPower2];
        int[] resultValues = [1, 1, 2, 4, 4, 16, 32, 512, 1024,
            largestPower2, largestPower2];

        for (int i = 0; i < testValues.Length; i++)
        {
            Assert.Equal(resultValues[i], MathUtils.NextPowerOf2(testValues[i]));
        }

        // Known port defect: the reference's `x + Integer.MIN_VALUE > ...` relies on
        // Java's wraparound to also reject int.MinValue and -1; the port's plain
        // `x > 0x40000000` misses both. Recorded in MIGRATION_STATUS.md.
        Assert.Throws<ArgumentException>(() => MathUtils.NextPowerOf2(largestPower2 + 1));
        Assert.Throws<ArgumentException>(() => MathUtils.NextPowerOf2(int.MaxValue));
    }
}
