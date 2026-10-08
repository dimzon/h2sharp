/*
 * Ported from java.util.Random (the JDK), for tests that are ported from the reference's own
 * test suite and have to see the same bytes.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */

namespace H2Sharp.Tests;

/// <summary>
/// The random generator of the JDK: a 48-bit linear congruential generator with the multiplier and
/// the increment of the specification, and the same way of turning it into bytes.
/// </summary>
/// <remarks>
/// It is here because a test ported from the reference's own suite has to be given the *same*
/// data, and a random buffer of one's own would be a different test. The generator is not
/// cryptographic and is not used as if it were; it is here to be predictable, which is the whole
/// point of it in a test.
/// </remarks>
internal sealed class JavaRandom
{
    private const long Multiplier = 0x5DEEC_E66D;

    private const long Addend = 0xB;

    private const long Mask = (1L << 48) - 1;

    private long seed;

    /// <summary>
    /// A generator with the given seed.
    /// </summary>
    /// <param name="seed">the seed</param>
    internal JavaRandom(long seed)
    {
        this.seed = (seed ^ Multiplier) & Mask;
    }

    private int Next(int bits)
    {
        seed = ((seed * Multiplier) + Addend) & Mask;
        return (int)(seed >> (48 - bits));
    }

    /// <summary>
    /// The next 32 bits, which is four bytes of the buffer at a time, most significant first.
    /// </summary>
    internal int NextInt() => Next(32);

    /// <summary>
    /// A number in <c>[0, bound)</c>.
    /// </summary>
    /// <param name="bound">the bound, and the reference only ever asks for a power of two</param>
    internal int NextInt(int bound) => (int)((bound * (long)Next(31)) >> 31);

    /// <summary>
    /// Fills the buffer with the next bytes of the generator: one 32-bit word at a time, most
    /// significant byte first, and the tail of a word goes on filling where the buffer ends.
    /// </summary>
    /// <param name="bytes">where to fill</param>
    internal void NextBytes(byte[] bytes)
    {
        int i = 0;
        while (i < bytes.Length)
        {
            int word = NextInt();
            int n = Math.Min(bytes.Length - i, 4);
            for (int k = 0; k < n; k++)
            {
                bytes[i + k] = (byte)(word >> (24 - (8 * k)));
            }

            i += n;
        }
    }
}
