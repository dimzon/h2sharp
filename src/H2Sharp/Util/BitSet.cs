using System;
using System.Numerics;
using System.Runtime.InteropServices;
using H2Sharp.MvStore.Tx;

namespace H2Sharp.Util;

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
public sealed class BitSet
{
    private long[] words;

    /// <summary>
    /// An empty set.
    /// </summary>
    internal BitSet() => words = [0];

    /// <summary>
    /// One past the highest set bit, or zero when nothing is set.
    /// </summary>
    /// <remarks>
    /// Scanned, not delegated: clearing a range leaves trailing zero words behind without
    /// shrinking the storage, and the helper reads the length off the last word, which would
    /// then answer too far. Only a scan finds the highest bit that is actually set.
    /// </remarks>
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
    /// <remarks>
    /// The arithmetic lives in <c>BitSetHelper</c>: one implementation for both the
    /// occupancy sets and the transaction snapshots, so the two cannot disagree on what
    /// a set bit is.
    /// </remarks>
    /// <param name="index">the position</param>
    /// <returns>true when set</returns>
    internal bool Get(int index) => BitSetHelper.Get(words, index);

    /// <summary>
    /// Set the bit at this position, growing the set if it has to.
    /// </summary>
    /// <remarks>
    /// <c>java.util.BitSet.set(int)</c>: the growth is the point, because a caller marking
    /// parameter N of a statement names the size by the highest bit it touches.
    /// </remarks>
    /// <param name="index">the position</param>
    internal void Set(int index) => Set(index, true);

    /// <summary>
    /// Set or clear the bit at this position, growing the set if it has to.
    /// </summary>
    /// <param name="index">the position</param>
    /// <param name="value">the new value</param>
    internal void Set(int index, bool value)
    {
        if (value)
        {
            SetRange(index, index + 1);
        }
        else
        {
            Clear(index);
        }
    }

    /// <summary>
    /// Clear the bit at this position.
    /// </summary>
    /// <remarks>
    /// Clearing near the top shrinks the reported <see cref="Length"/> the way the reference
    /// does: the highest word that becomes zero is dropped.
    /// </remarks>
    /// <param name="index">the position</param>
    internal void Clear(int index)
    {
        if (index < 0)
        {
            throw new IndexOutOfRangeException("index=" + index);
        }

        int wordIndex = index >> 6;
        if (wordIndex >= words.Length)
        {
            return;
        }

        // The storage keeps its size, as java.util.BitSet does: size() reports the words the
        // set has grown to, and only a fresh set reports zero after clearing. Length scans,
        // so it still answers zero.
        words[wordIndex] &= ~(1L << index);
    }

    /// <summary>
    /// Set every bit in a range, growing the set if it has to.
    /// </summary>
    /// <remarks>
    /// An empty range neither answers nor grows, as the reference returns before expanding.
    /// A reversed or negative range is refused rather than silently skipped.
    /// </remarks>
    /// <param name="from">the first position, inclusive</param>
    /// <param name="to">the last position, exclusive</param>
    internal void SetRange(int from, int to)
    {
        if (from < 0 || to < 0 || from > to)
        {
            throw new IndexOutOfRangeException("from=" + from + " to=" + to);
        }

        if (from == to)
        {
            return;
        }

        int start = from >> 6;
        int end = (to - 1) >> 6;
        EnsureCapacity(to - 1);
        long first = -1L << from;
        long last = -1L >>> -to;
        if (start == end)
        {
            words[start] |= first & last;
        }
        else
        {
            words[start] |= first;
            for (int i = start + 1; i < end; i++)
            {
                words[i] = -1L;
            }

            words[end] |= last;
        }
    }

    /// <summary>
    /// Clear every bit in a range. The set never grows for this.
    /// </summary>
    /// <remarks>
    /// Words past the end are already clear, so the range is clamped to the storage the same
    /// way the reference clamps to the words in use: clearing what is not there changes nothing.
    /// </remarks>
    /// <param name="from">the first position, inclusive</param>
    /// <param name="to">the last position, exclusive</param>
    internal void ClearRange(int from, int to)
    {
        if (from < 0 || to < 0 || from > to)
        {
            throw new IndexOutOfRangeException("from=" + from + " to=" + to);
        }

        if (from == to)
        {
            return;
        }

        int start = from >> 6;
        if (start >= words.Length)
        {
            return;
        }

        int end = (to - 1) >> 6;
        if (end >= words.Length)
        {
            to = Length;
            end = words.Length - 1;
        }

        long first = -1L << from;
        long last = -1L >>> -to;
        if (start == end)
        {
            words[start] &= ~(first & last);
        }
        else
        {
            words[start] &= ~first;
            for (int i = start + 1; i < end; i++)
            {
                words[i] = 0;
            }

            words[end] &= ~last;
        }
    }

    /// <summary>
    /// The first set bit at or after a position, or -1 when there is none.
    /// </summary>
    /// <remarks>
    /// The clamp stays here: the helper answers past-the-end callers, but a negative start
    /// is this class's contract to normalize, not the helper's.
    /// </remarks>
    /// <param name="from">where to start</param>
    /// <returns>the position, or -1</returns>
    internal int NextSetBit(int from)
    {
        if (from < 0)
        {
            from = 0;
        }

        return BitSetHelper.NextSetBit(words, from);
    }

    /// <summary>
    /// The first clear bit at or after a position, or -1 when everything from there on is set.
    /// </summary>
    /// <remarks>
    /// Same split as <see cref="NextSetBit"/>: the clamp is this class's, the scan is shared.
    /// </remarks>
    /// <param name="from">where to start</param>
    /// <returns>the position, or -1</returns>
    internal int NextClearBit(int from)
    {
        if (from < 0)
        {
            from = 0;
        }

        return BitSetHelper.NextClearBit(words, from);
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
    /// <remarks>
    /// Past the end everything is clear, so such a position answers itself. Trailing zero
    /// words read as all clear, which they are — unlike the set-bit search, this one cannot
    /// mistake them.
    /// </remarks>
    /// <param name="from">where to stop, inclusive</param>
    /// <returns>the position, or -1</returns>
    internal int PreviousClearBit(int from)
    {
        if (from < 0)
        {
            if (from == -1)
            {
                return -1;
            }

            throw new IndexOutOfRangeException("from=" + from);
        }

        int w = from >> 6;
        if (w >= words.Length)
        {
            return from;
        }

        ulong word = ~(ulong)words[w] & (ulong.MaxValue >>> -(from + 1));
        while (true)
        {
            if (word != 0)
            {
                return (w << 6) + 63 - BitOperations.LeadingZeroCount(word);
            }

            if (w-- == 0)
            {
                return -1;
            }

            word = ~(ulong)words[w];
        }
    }

    /// <summary>
    /// A set holding the bits of these bytes, read as little endian longs.
    /// </summary>
    /// <remarks>
    /// This is what the chunk occupancy is stored as: the whole block of pages as one hex value.
    /// Trailing zero bytes carry no information and are not part of the set, so a block with its last
    /// page deleted stores one byte fewer than a full one.
    /// </remarks>
    /// <param name="bytes">the bytes, lowest bit first</param>
    /// <returns>the set</returns>
    internal static BitSet ValueOf(byte[] bytes) => ValueOf(bytes.AsSpan());

    /// <summary>
    /// A set holding the bits of these bytes, read as little endian longs, without copying
    /// them out first. Only the words are kept.
    /// </summary>
    /// <param name="bytes">the bytes, lowest bit first</param>
    /// <returns>the set</returns>
    internal static BitSet ValueOf(ReadOnlySpan<byte> bytes)
    {
        BitSet set = new();
        if (bytes.Length != 0)
        {
            set.EnsureCapacity(bytes.Length * 8 - 1);
        }
        int whole = bytes.Length / sizeof(long);
        for (int i = 0; i < whole; i++)
        {
            set.words[i] = BitConverter.ToInt64(bytes.Slice(i * sizeof(long), sizeof(long)));
        }

        if ((bytes.Length & 7) != 0)
        {
            long last = 0;
            for (int i = whole * sizeof(long); i < bytes.Length; i++)
            {
                last |= (long)bytes[i] << (8 * (i - (whole * sizeof(long))));
            }

            set.words[whole] = last;
        }

        return set;
    }

    /// <summary>
    /// The bits as bytes, lowest bit first, without trailing zero bytes.
    /// </summary>
    /// <returns>the bytes</returns>
    internal byte[] ToByteArray()
    {
        // The loop this replaces read the words out little endian, which is the order the
        // bytes of a long already sit in.
        int used = (Length + 7) / 8;
        return MemoryMarshal.AsBytes(words.AsSpan())[..used].ToArray();
    }

    /// <summary>
    /// Clears every bit that is set in the other set, as <c>java.util.BitSet.andNot</c>.
    /// </summary>
    /// <remarks>
    /// Used when the object ids a committed transaction freed become available again. A word
    /// that becomes zero at the top is dropped, so <see cref="Capacity"/> shrinks with it, as
    /// the reference's <c>recalculateWordsInUse</c> does.
    /// </remarks>
    /// <param name="other">the bits to clear</param>
    internal void AndNot(BitSet other)
    {
        int common = Math.Min(words.Length, other.words.Length);
        for (int i = 0; i < common; i++)
        {
            words[i] &= ~other.words[i];
        }

        int length = words.Length;
        while (length > 1 && words[length - 1] == 0L)
        {
            length--;
        }

        if (length != words.Length)
        {
            Array.Resize(ref words, length);
        }
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
