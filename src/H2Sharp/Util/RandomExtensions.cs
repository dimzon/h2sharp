/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The non-negative integer contract of System.Random, enforced for any implementation.
 */
using System;

namespace H2Sharp.Util;

/// <summary>
/// The non-negative integer methods of <see cref="Random"/>, with the sign bit cleared for any
/// implementation.
/// </summary>
/// <remarks>
/// The JDK's <c>nextInt()</c> and <c>nextLong()</c> are full signed values; only their bounded
/// forms promise a non-negative result. .NET's <see cref="Random.Next()"/> and
/// <see cref="Random.NextInt64()"/> promise a non-negative result instead. These extensions make
/// that promise hold even for a generator that answers with a negative value.
/// <para>
/// The full signed JDK value is still needed in one place: the ANY_VALUE bit filter uses
/// <c>nextLong()</c> as a bit pattern. <see cref="NextSignedInt64"/> is that value and deliberately
/// does not clear the sign; using it elsewhere would change the meaning of the number.
/// </para>
/// </remarks>
public static class RandomExtensions
{
    /// <summary>
    /// The next non-negative 32-bit value.
    /// </summary>
    /// <param name="random">the generator</param>
    /// <returns>a value in the non-negative half of the 32-bit range</returns>
    public static int NextNonNegativeInt32(this Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return random.Next() & int.MaxValue;
    }

    /// <summary>
    /// The next non-negative 32-bit value below <paramref name="maxValue"/>.
    /// </summary>
    /// <param name="random">the generator</param>
    /// <param name="maxValue">the exclusive upper bound, which must not be negative</param>
    /// <returns>a value in <c>[0, maxValue)</c></returns>
    public static int NextNonNegativeInt32(this Random random, int maxValue)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        if (maxValue == 0)
        {
            return 0;
        }

        int value = random.Next(maxValue);
        return value >= 0 && value < maxValue
            ? value
            : (int)(NextNonNegativeInt64(random) % maxValue);
    }

    /// <summary>
    /// The next non-negative 64-bit value.
    /// </summary>
    /// <param name="random">the generator</param>
    /// <returns>a value in the non-negative half of the 64-bit range</returns>
    public static long NextNonNegativeInt64(this Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return random.NextInt64() & long.MaxValue;
    }

    /// <summary>
    /// The next non-negative 64-bit value below <paramref name="maxValue"/>.
    /// </summary>
    /// <param name="random">the generator</param>
    /// <param name="maxValue">the exclusive upper bound, which must not be negative</param>
    /// <returns>a value in <c>[0, maxValue)</c></returns>
    public static long NextNonNegativeInt64(this Random random, long maxValue)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        if (maxValue == 0)
        {
            return 0;
        }

        long value = random.NextInt64(maxValue);
        return value >= 0 && value < maxValue
            ? value
            : NextNonNegativeInt64(random) % maxValue;
    }

    /// <summary>
    /// The next full signed 64-bit value, which is the JDK's <c>nextLong()</c> bit pattern.
    /// </summary>
    /// <remarks>
    /// This one does not clear the sign bit: ANY_VALUE uses the value as a mask, and a non-negative
    /// value would change which rows the filter keeps. It is not a general-purpose number method.
    /// </remarks>
    /// <param name="random">the generator</param>
    /// <returns>a value over the whole signed 64-bit range</returns>
    public static long NextSignedInt64(this Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return random.NextInt64(long.MinValue, long.MaxValue);
    }
}
