using System;
using System.Numerics;

namespace H2Sharp.MvStore.Tx;

/// <summary>
/// The sliver of bit-set behavior the transaction store needs, over plain long arrays.
/// </summary>
public static class BitSetHelper
{
    private const int AddressBitsPerWord = 6;
    private const int BitsPerWord = 1 << AddressBitsPerWord;
    private const long WordMask = unchecked((long)0xffffffffffffffffL);

    /// <summary>
    /// Whether the bit is set. Past the end reads as clear.
    /// </summary>
    public static bool Get(long[] bits, int bitIndex)
    {
        int wordIndex = WordIndex(bitIndex);
        return wordIndex < bits.Length && (bits[wordIndex] & (1L << bitIndex)) != 0;
    }

    /// <summary>
    /// Flips the bit, answering a new array: the set stays immutable.
    /// </summary>
    /// <remarks>
    /// Always copies, even when the size does not change: <c>Array.Resize</c> keeps the same
    /// array when the size matches, and flipping in place would corrupt every snapshot
    /// sharing it. The reference's <c>Arrays.copyOf</c> copies unconditionally.
    /// </remarks>
    public static long[] Flip(long[] bits, int bitIndex)
    {
        int wordIndex = WordIndex(bitIndex);

        int length = bits.Length;
        while (--length > wordIndex && bits[length] == 0L)
        {
        }

        int newLength = Math.Max(length, wordIndex) + 1;
        long[] result = new long[newLength];
        Array.Copy(bits, result, Math.Min(bits.Length, newLength));
        result[wordIndex] ^= 1L << bitIndex;
        return result;
    }

    /// <summary>
    /// The first set bit at or after the index, or -1.
    /// </summary>
    public static int NextSetBit(long[] bits, int bitIndex)
    {
        int wordIndex = WordIndex(bitIndex);
        if (wordIndex >= bits.Length)
        {
            return -1;
        }

        long word = bits[wordIndex] & (WordMask << bitIndex);

        while (true)
        {
            if (word != 0)
            {
                return (wordIndex * BitsPerWord) + BitOperations.TrailingZeroCount((ulong)word);
            }

            if (++wordIndex == bits.Length)
            {
                return -1;
            }

            word = bits[wordIndex];
        }
    }

    /// <summary>
    /// The first clear bit at or after the index. Past the end, everything reads as clear.
    /// </summary>
    public static int NextClearBit(long[] bits, int bitIndex)
    {
        int wordIndex = WordIndex(bitIndex);
        if (wordIndex >= bits.Length)
        {
            return bitIndex;
        }

        long word = ~bits[wordIndex] & (WordMask << bitIndex);

        while (true)
        {
            if (word != 0)
            {
                return (wordIndex * BitsPerWord) + BitOperations.TrailingZeroCount((ulong)word);
            }

            if (++wordIndex == bits.Length)
            {
                return bits.Length * BitsPerWord;
            }

            word = ~bits[wordIndex];
        }
    }

    /// <summary>
    /// One past the highest set bit, or zero when nothing is set.
    /// </summary>
    public static int Length(long[] bits)
    {
        int wordsInUse = bits.Length;
        if (wordsInUse == 0)
        {
            return 0;
        }

        return BitsPerWord * (wordsInUse - 1) +
            (BitsPerWord - BitOperations.LeadingZeroCount((ulong)bits[wordsInUse - 1]));
    }

    private static int WordIndex(int index) => index >> AddressBitsPerWord;
}
