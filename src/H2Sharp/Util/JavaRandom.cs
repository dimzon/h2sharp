/*
 * Ported from java.util.Random (the JDK).
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
namespace H2Sharp.Util;

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
/// The class is not sealed and its methods are virtual, so that a test can hand out a generator
/// that answers with the numbers it wants. The JDK's <c>java.util.Random</c> is not final
/// either, and the reference's own tests subclass it for the same reason.
/// </para>
/// </remarks>
public class JavaRandom
{
    private const long Multiplier = 0x5DEEC_E66D;

    private const long Addend = 0xB;

    private const long Mask = (1L << 48) - 1;

    private const double DoubleUnit = 1.0 / (1L << 53);

    private long seed;

    /// <summary>
    /// A generator with the given seed.
    /// </summary>
    /// <param name="seed">the seed</param>
    public JavaRandom(long seed)
    {
        SetSeed(seed);
    }

    /// <summary>
    /// Reseeds the generator.
    /// </summary>
    /// <remarks>
    /// The reference calls <c>setSeed(int)</c>; Java has only <c>setSeed(long)</c>, which widens
    /// the int with its sign, and so does this.
    /// </remarks>
    /// <param name="seed">the seed</param>
    public virtual void SetSeed(long seed)
    {
        this.seed = (seed ^ Multiplier) & Mask;
    }

    private int Next(int bits)
    {
        seed = ((seed * Multiplier) + Addend) & Mask;
        return (int)(seed >> (48 - bits));
    }

    /// <summary>
    /// The next 32 bits.
    /// </summary>
    /// <returns>the next 32 bits</returns>
    public virtual int NextInt() => Next(32);

    /// <summary>
    /// A number in <c>[0, bound)</c>.
    /// </summary>
    /// <param name="bound">the bound, which must be positive</param>
    /// <returns>the number</returns>
    public virtual int NextInt(int bound)
    {
        if (bound <= 0)
        {
            throw new System.ArgumentOutOfRangeException(nameof(bound), "bound must be positive");
        }

        int r = Next(31);
        int m = bound - 1;
        if ((bound & m) == 0)
        {
            return (int)((bound * (long)r) >> 31);
        }

        for (int u = r; u - (r = u % bound) + m < 0; u = Next(31))
        {
        }

        return r;
    }

    /// <summary>
    /// The next 64 bits.
    /// </summary>
    /// <returns>the next 64 bits</returns>
    public virtual long NextLong() => ((long)Next(32) << 32) + Next(32);

    /// <summary>
    /// The next boolean.
    /// </summary>
    /// <returns>the next boolean</returns>
    public virtual bool NextBoolean() => Next(1) != 0;

    /// <summary>
    /// A number in <c>[0, 1)</c>.
    /// </summary>
    /// <returns>the number</returns>
    public virtual double NextDouble() => (((long)Next(26) << 27) + Next(27)) * DoubleUnit;

    /// <summary>
    /// Fills the buffer with the next bytes of the generator: one 32-bit word at a time, most
    /// significant byte first, and the tail of a word goes on filling where the buffer ends.
    /// </summary>
    /// <param name="bytes">where to fill</param>
    public virtual void NextBytes(byte[] bytes)
    {
        int i = 0;
        while (i < bytes.Length)
        {
            int word = NextInt();
            int n = System.Math.Min(bytes.Length - i, 4);
            for (int k = 0; k < n; k++)
            {
                bytes[i + k] = (byte)(word >> (24 - (8 * k)));
            }

            i += n;
        }
    }
}
