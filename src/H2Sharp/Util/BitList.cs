/*
 * A growing list of bits, copied from the standalone BitList project
 * (C:\Work\listofbit\src\BitList\BitList.cs) and kept here so that the engine
 * carries no dependency outside this repository.
 *
 * The reference marks the parameters a statement uses in a java.util.BitSet,
 * which grows to the highest bit that is set, and reads the number of
 * parameters as the length of that set. System.Collections.BitArray has a
 * fixed size and cannot grow, so this list is what the port marks the
 * parameters in.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace H2Sharp.Util;

/// <summary>
/// A mutable list of <see cref="bool"/> values that stores every value in a single bit
/// of a packed bit array (<c>ulong[]</c>) instead of one byte or object reference per element.
/// </summary>
/// <remarks>
/// <para>
/// The bit layout is exactly the layout of <see cref="System.Collections.BitArray"/>:
/// the value at index <c>i</c> lives in bit <c>i % 64</c> of word <c>i / 64</c>.
/// </para>
/// <para>
/// The instance is <b>not</b> thread-safe; concurrent mutation requires external synchronization.
/// </para>
/// </remarks>
public sealed class BitList : IList<bool>, IReadOnlyList<bool>, IEquatable<BitList>
{
    private const int BitsPerWord = 64;

    private ulong[] _words;
    private int _count;

    /// <summary>Creates an empty list.</summary>
    public BitList()
        : this(0)
    {
    }

    /// <summary>Creates an empty list with room for <paramref name="capacity"/> values.</summary>
    /// <param name="capacity">Initial capacity, expressed in values (not bytes).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is negative.</exception>
    public BitList(int capacity)
    {
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be non-negative.");
        }

        _words = capacity == 0 ? [] : new ulong[WordsFor(capacity)];
        _count = 0;
    }

    /// <summary>Creates a list containing a copy of <paramref name="values"/>.</summary>
    /// <param name="values">Values to copy. The sequence is enumerated once.</param>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    public BitList(IEnumerable<bool> values)
        : this(0)
    {
        if (values is null)
        {
            ArgumentNullException.ThrowIfNull(values);
        }

        if (values is BitList other)
        {
            EnsureCapacity(other._count);
            Array.Copy(other._words, _words, WordsFor(other._count));
            _count = other._count;
            return;
        }

        foreach (bool value in values)
        {
            Add(value);
        }
    }

    /// <summary>Creates a list from the bits of <paramref name="bits"/>.</summary>
    /// <param name="bits">Source bit array.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bits"/> is <see langword="null"/>.</exception>
    public static BitList FromBitArray(BitArray bits)
    {
        if (bits is null)
        {
            ArgumentNullException.ThrowIfNull(bits);
        }

        var result = new BitList(bits.Length);
        for (int i = 0; i < bits.Length; i++)
        {
            result.SetBit(i, bits[i]);
        }

        result._count = bits.Length;
        return result;
    }

    /// <inheritdoc />
    public int Count => _count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <summary>Gets or sets the value at the specified index.</summary>
    /// <param name="index">Zero-based index of the value.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is negative or greater than or equal to <see cref="Count"/>.
    /// </exception>
    public bool this[int index]
    {
        get
        {
            ThrowIfIndexOutOfRange(index);
            return GetBit(index);
        }
        set
        {
            ThrowIfIndexOutOfRange(index);
            SetBit(index, value);
        }
    }

    /// <inheritdoc />
    public void Add(bool item)
    {
        EnsureCapacity(_count + 1);
        SetBit(_count, item);
        _count++;
    }

    /// <summary>Adds all values of <paramref name="items"/> to the end of the list.</summary>
    /// <param name="items">Values to append.</param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    public void AddRange(IEnumerable<bool> items)
    {
        if (items is null)
        {
            ArgumentNullException.ThrowIfNull(items);
        }

        foreach (bool item in items)
        {
            Add(item);
        }
    }

    /// <inheritdoc />
    public void Insert(int index, bool item)
    {
        if (index < 0 || index > _count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Index must be within the list bounds.");
        }

        EnsureCapacity(_count + 1);
        if (index < _count)
        {
            ShiftUp(index);
        }

        SetBit(index, item);
        _count++;
    }

    /// <inheritdoc />
    public bool Remove(bool item)
    {
        int index = IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    /// <inheritdoc />
    public void RemoveAt(int index)
    {
        ThrowIfIndexOutOfRange(index);

        if (index + 1 < _count)
        {
            ShiftDown(index + 1);
        }

        _count--;
        SetBit(_count, false);
    }

    /// <inheritdoc />
    public void Clear()
    {
        Array.Clear(_words, 0, _words.Length);
        _count = 0;
    }

    /// <inheritdoc />
    public bool Contains(bool item) => IndexOf(item) >= 0;

    /// <inheritdoc />
    public int IndexOf(bool item)
    {
        for (int i = 0; i < _count; i++)
        {
            if (GetBit(i) == item)
            {
                return i;
            }
        }

        return -1;
    }

    /// <inheritdoc />
    public int LastIndexOf(bool item)
    {
        for (int i = _count - 1; i >= 0; i--)
        {
            if (GetBit(i) == item)
            {
                return i;
            }
        }

        return -1;
    }

    /// <inheritdoc />
    public void CopyTo(bool[] array, int arrayIndex)
    {
        if (array is null)
        {
            ArgumentNullException.ThrowIfNull(array);
        }

        if (arrayIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(arrayIndex), arrayIndex, "Index must be non-negative.");
        }

        if (array.Length - arrayIndex < _count)
        {
            throw new ArgumentException("The destination array is too small.", nameof(array));
        }

        for (int i = 0; i < _count; i++)
        {
            array[arrayIndex + i] = GetBit(i);
        }
    }

    /// <summary>Copies the values into a new <see cref="bool"/> array.</summary>
    /// <returns>An array of length <see cref="Count"/> holding the values in order.</returns>
    public bool[] ToArray()
    {
        var result = new bool[_count];
        CopyTo(result, 0);
        return result;
    }

    /// <summary>Copies the values into a new <see cref="BitArray"/>.</summary>
    /// <returns>A bit array of length <see cref="Count"/> holding the values in order.</returns>
    public BitArray ToBitArray()
    {
        var result = new BitArray(_count);
        for (int i = 0; i < _count; i++)
        {
            result[i] = GetBit(i);
        }

        return result;
    }

    /// <inheritdoc />
    public IEnumerator<bool> GetEnumerator() => new Enumerator(this);

    IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

    /// <inheritdoc />
    public bool Equals(BitList? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other._count != _count)
        {
            return false;
        }

        int fullWords = _count >> 6;
        for (int i = 0; i < fullWords; i++)
        {
            if (_words[i] != other._words[i])
            {
                return false;
            }
        }

        int remainder = _count & 63;
        if (remainder != 0)
        {
            ulong mask = (1UL << remainder) - 1UL;
            if ((_words[fullWords] & mask) != (other._words[fullWords] & mask))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as BitList);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            uint hash = 2166136261u;
            hash = (hash ^ (uint)_count) * 16777619u;

            int fullWords = _count >> 6;
            for (int i = 0; i < fullWords; i++)
            {
                hash = (hash ^ (uint)_words[i]) * 16777619u;
                hash = (hash ^ (uint)(_words[i] >> 32)) * 16777619u;
            }

            int remainder = _count & 63;
            if (remainder != 0)
            {
                ulong tail = _words[fullWords] & ((1UL << remainder) - 1UL);
                hash = (hash ^ (uint)tail) * 16777619u;
                hash = (hash ^ (uint)(tail >> 32)) * 16777619u;
            }

            return (int)hash;
        }
    }

    /// <summary>Determines whether two lists hold the same values in the same order.</summary>
    public static bool operator ==(BitList? left, BitList? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Determines whether two lists differ in content.</summary>
    public static bool operator !=(BitList? left, BitList? right) => !(left == right);

    /// <summary>Allocation-free enumerator over the stored values.</summary>
    public struct Enumerator : IEnumerator<bool>
    {
        private readonly BitList _list;
        private int _index;
        private bool _current;

        internal Enumerator(BitList list)
        {
            _list = list;
            _index = 0;
            _current = default;
        }

        /// <inheritdoc />
        public readonly bool Current => _current;

        readonly object IEnumerator.Current => _current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            if (_index < _list._count)
            {
                _current = _list.GetBit(_index);
                _index++;
                return true;
            }

            _current = default;
            return false;
        }

        /// <inheritdoc />
        public void Reset()
        {
            _index = 0;
            _current = default;
        }

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }
    }

    private static int WordsFor(int bitCount) => (int)(((long)bitCount + BitsPerWord - 1) / BitsPerWord);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool GetBit(int index)
    {
        ulong word = _words[index >> 6];
        return ((word >> (index & 63)) & 1UL) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetBit(int index, bool value)
    {
        int word = index >> 6;
        ulong mask = 1UL << (index & 63);
        if (value)
        {
            _words[word] |= mask;
        }
        else
        {
            _words[word] &= ~mask;
        }
    }

    private void EnsureCapacity(int requiredBits)
    {
        int requiredWords = WordsFor(requiredBits);
        if (requiredWords <= _words.Length)
        {
            return;
        }

        long doubled = (long)_words.Length * 2;
        int capacity = (int)Math.Min(Math.Max(doubled, requiredWords), int.MaxValue);
        if (capacity < requiredWords)
        {
            capacity = requiredWords;
        }

        Array.Resize(ref _words, capacity);
    }

    /// <summary>
    /// Moves the bits of the range <c>[start, count)</c> one position up, to <c>[start + 1, count + 1)</c>.
    /// Bits at or above <see cref="_count"/> are always zero, so no value is lost.
    /// </summary>
    private void ShiftUp(int start)
    {
        int first = start >> 6;
        int last = (_count - 1) >> 6;

        // The top bit of the range moves out of its word only when it sits at bit 63.
        int top = ((_count - 1) & 63) == 63 ? last + 1 : last;

        for (int word = top; word > first; word--)
        {
            ulong carry = (_words[word - 1] >> 63) & 1UL;
            _words[word] = (_words[word] << 1) | carry;
        }

        ulong lowMask = (start & 63) == 0 ? 0UL : (1UL << (start & 63)) - 1UL;
        ulong value = _words[first];
        _words[first] = (value & lowMask) | ((value & ~lowMask) << 1);
    }

    /// <summary>
    /// Moves the bits of the range <c>[start, count)</c> one position down, to <c>[start - 1, count - 1)</c>.
    /// </summary>
    private void ShiftDown(int start)
    {
        int first = start >> 6;
        int offset = start & 63;
        int last = (_count - 1) >> 6;

        // Bit 0 of the next word is the source of bit 63 of this one, but only when the
        // range really reaches into the next word.
        ulong carry = first < last ? _words[first + 1] & 1UL : 0UL;

        if (offset == 0)
        {
            // The range starts at a word boundary: its first bit lands on bit 63 of the previous word.
            _words[first - 1] = (_words[first - 1] & ~(1UL << 63)) | ((_words[first] & 1UL) << 63);
            _words[first] = (_words[first] >> 1) | (carry << 63);
        }
        else
        {
            // Bits below the removed position stay, the removed bit itself is dropped,
            // and the range bits move one position down.
            ulong preservedMask = (1UL << (offset - 1)) - 1UL;
            ulong rangeMask = ~((1UL << offset) - 1UL);
            ulong value = _words[first];
            _words[first] = (value & preservedMask) | ((value & rangeMask) >> 1) | (carry << 63);
        }

        for (int word = first + 1; word < last; word++)
        {
            ulong carryIn = _words[word + 1] & 1UL;
            _words[word] = (_words[word] >> 1) | (carryIn << 63);
        }

        if (last > first)
        {
            // The topmost bit of the range leaves the word; the bits above it are always zero.
            _words[last] >>= 1;
        }
    }

    private void ThrowIfIndexOutOfRange(int index)
    {
        if ((uint)index >= (uint)_count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Index must be within the list bounds.");
        }
    }
}
