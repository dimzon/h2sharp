/*
 * Ported from java.util.Random (the JDK).
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;

namespace H2Sharp.JavaLegacy;

/// <summary>
/// The random generator of the JDK: a 48-bit linear congruential generator with the multiplier and
/// the increment of the specification, and the same way of turning it into numbers.
/// </summary>
/// <remarks>
/// The reference's <c>SessionLocal.getRandom()</c> returns a <c>java.util.Random</c>, and
/// <c>RAND(seed)</c> reseeds it before drawing a number. A generator of one's own would draw a
/// different number for the same seed, so the generator is ported too and the session hands out
/// this one, exactly as the reference hands out the JDK's.
/// <para>
/// Принятое проектное решение — максимально переиспользовать уже существующие классы и
/// контракты: генератор наследует <see cref="System.Random"/> и реализует его виртуальные
/// методы, а не вводит параллельный API. Поэтому порт просит случайные числа через
/// стандартный API, а тест может передать сессии любой <see cref="System.Random"/>, не только
/// этот класс. Переопределения сохраняют битовые последовательности JDK; полный знаковый
/// <c>nextLong()</c> используется ограниченными перегрузками и полнодиапазонным вызовом
/// ANY_VALUE.
/// </para>
/// </remarks>
public class JavaRandom : Random
{
    private const long Multiplier = 0x5DEEC_E66D;

    private const long Addend = 0xB;

    private const long Mask = (1L << 48) - 1;

    private const double DoubleUnit = 1.0 / (1L << 53);

    private const float SingleUnit = 1.0f / (1 << 24);

    private long seed;

    /// <summary>
    /// A generator with the given seed.
    /// </summary>
    /// <param name="seed">the seed</param>
    public JavaRandom(long seed)
        : base(0)
    {
        SetSeed(seed);
    }

    /// <summary>
    /// Reseeds the generator.
    /// </summary>
    /// <remarks>
    /// The reference calls <c>setSeed(int)</c>; Java has only <c>setSeed(long)</c>, which widens
    /// the int with its sign, and so does this. <see cref="System.Random"/> has no reseed method,
    /// so the JDK's own operation stays on this class.
    /// </remarks>
    /// <param name="seed">the seed</param>
    public virtual void SetSeed(long seed)
    {
        this.seed = (seed ^ Multiplier) & Mask;
    }

    private int NextBits(int bits)
    {
        seed = ((seed * Multiplier) + Addend) & Mask;
        return (int)(seed >> (48 - bits));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The JDK has no non-negative <c>nextInt()</c>; this is the JDK's top 31 bits, which is what
    /// a <see cref="System.Random.Next()"/> caller expects.
    /// </remarks>
    public override int Next() => NextBits(31);

    /// <inheritdoc/>
    public override int Next(int maxValue)
    {
        if (maxValue < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxValue));
        }

        if (maxValue == 0)
        {
            return 0;
        }

        return NextInt(maxValue);
    }

    /// <inheritdoc/>
    public override int Next(int minValue, int maxValue)
    {
        if (minValue > maxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(minValue));
        }

        if (minValue == maxValue)
        {
            return minValue;
        }

        long range = (long)maxValue - minValue;
        if (range <= int.MaxValue)
        {
            return (int)(minValue + (long)NextInt((int)range));
        }

        // The range is wider than an int, so draw a 32-bit value and reject the tail.
        uint unsignedRange = (uint)range;
        uint value;
        do
        {
            value = (uint)NextInt64();
        }
        while (value >= unsignedRange);

        return (int)(minValue + value);
    }

    /// <summary>
    /// The JDK's <c>nextInt(bound)</c>.
    /// </summary>
    private int NextInt(int bound)
    {
        int r = NextBits(31);
        int m = bound - 1;
        if ((bound & m) == 0)
        {
            return (int)((bound * (long)r) >> 31);
        }

        for (int u = r; u - (r = u % bound) + m < 0; u = NextBits(31))
        {
        }

        return r;
    }

    /// <inheritdoc/>
    public override long NextInt64() => NextJavaLong() & long.MaxValue;

    /// <summary>
    /// The JDK's <c>nextLong()</c>, the full signed 64-bit value.
    /// </summary>
    private long NextJavaLong() => ((long)NextBits(32) << 32) + NextBits(32);

    /// <inheritdoc/>
    public override long NextInt64(long maxValue)
    {
        if (maxValue < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxValue));
        }

        if (maxValue == 0)
        {
            return 0;
        }

        return NextLong(maxValue);
    }

    /// <inheritdoc/>
    public override long NextInt64(long minValue, long maxValue)
    {
        if (minValue > maxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(minValue));
        }

        if (minValue == maxValue)
        {
            return minValue;
        }

        long range = unchecked(maxValue - minValue);
        if (range > 0)
        {
            return minValue + NextLong(range);
        }

        long value;
        do
        {
            value = NextJavaLong();
        }
        while (value < minValue || value >= maxValue);

        return value;
    }

    /// <summary>
    /// The JDK's <c>nextLong(bound)</c>.
    /// </summary>
    private long NextLong(long bound)
    {
        long r = NextJavaLong();
        long m = bound - 1;
        if ((bound & m) == 0L)
        {
            return r & m;
        }

        long u = (long)((ulong)r >> 1);
        while (unchecked(u + m - (r = u % bound)) < 0L)
        {
            u = (long)((ulong)NextJavaLong() >> 1);
        }

        return r;
    }

    /// <inheritdoc/>
    public override double NextDouble()
        => (((long)NextBits(26) << 27) + NextBits(27)) * DoubleUnit;

    /// <inheritdoc/>
    public override float NextSingle() => NextBits(24) * SingleUnit;

    /// <inheritdoc/>
    public override void NextBytes(byte[] buffer) => FillBytes(buffer);

    /// <inheritdoc/>
    public override void NextBytes(Span<byte> buffer) => FillBytes(buffer);

    private void FillBytes(Span<byte> bytes)
    {
        int i = 0;
        while (i < bytes.Length)
        {
            int word = NextBits(32);
            int n = Math.Min(bytes.Length - i, 4);
            for (int k = 0; k < n; k++)
            {
                bytes[i + k] = (byte)(word >> (24 - (8 * k)));
            }

            i += n;
        }
    }

    /// <inheritdoc/>
    protected override double Sample() => NextDouble();
}

