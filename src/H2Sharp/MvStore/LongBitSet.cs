using System;
using System.Numerics;

namespace H2Sharp.MvStore;

/// <summary>
/// A growable bit set, standing in for <c>java.util.BitSet</c>.
/// </summary>
/// <remarks>
/// .NET has no bit set in the BCL and the obvious stand-ins are wrong here. The free space map needs
/// <c>nextClearBit</c>, which is defined as the first <em>clear</em> bit at or after a position: bits
/// past the end of the set count as clear, so it can answer with an index beyond what has been set.
/// That is what makes "no more used blocks" answerable at all.
/// <para>
/// The awkward corner is the difference between two sizes. <see cref="Length"/> is one past the
/// highest set bit, which is what a caller asking "how far does this reach" wants.
/// <see cref="Capacity"/> is the allocated size, which is what the reference's <c>size()</c> returns
/// and what its <c>previousSetBit(size() - 1)</c> depends on. Mixing them up produces an answer that
/// is right for an empty set and wrong for every other one.
/// </para>
/// </remarks>
internal sealed class LongBitSet
{
    private long[] words;

    /// <summary>
    /// An empty set.
    /// </summary>
    internal LongBitSet() => words = [0];

    /// <summary>
    /// One past the highest set bit, or zero when nothing is set.
    /// </summary>
    internal int Length
    {
        get
        {
            for (int i = words.Length - 1; i >= 0; i--)
            {
                if (words[i] != 0)
                {
                    return (i << 6) + (64 - BitOperations.LeadingZeroCount((ulong)words[i]));
                }
            }

            return 0;
        }
    }

    /// <summary>
    /// The number of bits the set has room for.
    /// </summary>
    internal int Capacity => words.Length << 6;

    /// <summary>
    /// How many bits are set.
    /// </summary>
    /// <returns>the count</returns>
    internal int Cardinality()
    {
        int count = 0;
        foreach (long word in words)
        {
            count += BitOperations.PopCount((ulong)word);
        }

        return count;
    }

    /// <summary>
    /// Forget everything.
    /// </summary>
    internal void ClearAll() => words = [0];

    /// <summary>
    /// Whether the bit at this position is set.
    /// </summary>
    /// <param name="index">the position</param>
    /// <returns>true when set</returns>
    internal bool Get(int index)
    {
        int w = index >> 6;
        return w < words.Length && (words[w] & (1L << (index & 63))) != 0;
    }

    /// <summary>
    /// Set every bit in a range, growing the set if it has to.
    /// </summary>
    /// <param name="from">the first position, inclusive</param>
    /// <param name="to">the last position, exclusive</param>
    internal void SetRange(int from, int to)
    {
        EnsureCapacity(to - 1);
        for (int i = from; i < to; i++)
        {
            words[i >> 6] |= 1L << (i & 63);
        }
    }

    /// <summary>
    /// Clear every bit in a range. The set never grows for this.
    /// </summary>
    /// <param name="from">the first position, inclusive</param>
    /// <param name="to">the last position, exclusive</param>
    internal void ClearRange(int from, int to)
    {
        int end = Math.Min(to, Capacity);
        for (int i = Math.Max(from, 0); i < end; i++)
        {
            words[i >> 6] &= ~(1L << (i & 63));
        }
    }

    /// <summary>
    /// The first set bit at or after a position, or -1 when there is none.
    /// </summary>
    /// <param name="from">where to start</param>
    /// <returns>the position, or -1</returns>
    internal int NextSetBit(int from)
    {
        if (from < 0)
        {
            from = 0;
        }

        int w = from >> 6;
        if (w >= words.Length)
        {
            return -1;
        }

        long word = words[w] & (-1L << (from & 63));
        while (true)
        {
            if (word != 0)
            {
                return (w << 6) + BitOperations.TrailingZeroCount((ulong)word);
            }

            if (++w == words.Length)
            {
                return -1;
            }

            word = words[w];
        }
    }

    /// <summary>
    /// The first clear bit at or after a position, or -1 when everything from there on is set.
    /// </summary>
    /// <param name="from">where to start</param>
    /// <returns>the position, or -1</returns>
    internal int NextClearBit(int from)
    {
        if (from < 0)
        {
            from = 0;
        }

        int w = from >> 6;
        while (w < words.Length)
        {
            long word = ~words[w] & (-1L << (from & 63));
            if (word != 0)
            {
                return (w << 6) + BitOperations.TrailingZeroCount((ulong)word);
            }

            from = (w + 1) << 6;
            w++;
        }

        return from;
    }

    /// <summary>
    /// The highest set bit at or before a position, or -1 when there is none.
    /// </summary>
    /// <param name="from">where to stop, inclusive</param>
    /// <returns>the position, or -1</returns>
    internal int PreviousSetBit(int from)
    {
        if (from < 0)
        {
            return -1;
        }

        int w = from >> 6;
        if (w >= words.Length)
        {
            w = words.Length - 1;
            from = (w << 6) + 63;
            if (from < 0)
            {
                return -1;
            }
        }

        while (w >= 0)
        {
            long word = words[w];
            if (from < (w << 6) + 63)
            {
                int bit = (from & 63) + 1;
                if (bit < 64)
                {
                    word &= (1L << bit) - 1;
                }
            }

            if (word != 0)
            {
                return (w << 6) + (63 - BitOperations.LeadingZeroCount((ulong)word));
            }

            w--;
            from = ((w + 1) << 6) + 63;
        }

        return -1;
    }

    /// <summary>
    /// The highest clear bit at or before a position, or -1 when everything up to it is set.
    /// </summary>
    /// <param name="from">where to stop, inclusive</param>
    /// <returns>the position, or -1</returns>
    internal int PreviousClearBit(int from)
    {
        if (from < 0)
        {
            return -1;
        }

        for (int i = from; i >= 0; i--)
        {
            if (!Get(i))
            {
                return i;
            }
        }

        return -1;
    }

    private void EnsureCapacity(int bits)
    {
        int needed = (bits >> 6) + 1;
        if (needed > words.Length)
        {
            Array.Resize(ref words, needed);
        }
    }
}
