using System;
using System.Buffers.Binary;
using System.Text;
using H2Sharp.Util;

namespace H2Sharp.MvStore;

/// <summary>
/// An auto-resize buffer to write data into.
/// </summary>
/// <remarks>
/// The store serializes a page into this and then hands the bytes to the chunk, so the buffer has to
/// grow in the middle of writing and has to keep what was already written.
/// <para>
/// Growth is generous on purpose: at least <see cref="MinGrow"/>, and at least half of what the
/// buffer already holds. A buffer that grew by the smallest amount needed would reallocate on every
/// page, and a page is written one field at a time.
/// </para>
/// <para>
/// <see cref="Clear"/> keeps the buffer for reuse when it is not absurdly large, which is the whole
/// point of a separate <c>reuse</c> field: the steady state is that nothing is allocated at all.
/// </para>
/// </remarks>
public sealed class WriteBuffer
{
    /// <summary>
    /// The maximum size of the buffer in order to be re-used after a clear operation.
    /// </summary>
    private const int MaxReuseCapacity = 4 * 1024 * 1024;

    /// <summary>
    /// The minimum number of bytes to grow a buffer at a time.
    /// </summary>
    internal const int MinGrow = 1024 * 1024;

    /// <summary>
    /// The buffer that is used after a clear operation.
    /// </summary>
    private byte[]? reuse;

    /// <summary>
    /// The current buffer (might be replaced if it is too small).
    /// </summary>
    private byte[] buffer;

    private int position;

    private int limit;

    /// <summary>
    /// A buffer of the given size.
    /// </summary>
    /// <param name="initialSize">how many bytes to start with</param>
    public WriteBuffer(int initialSize)
    {
        reuse = new byte[initialSize];
        buffer = reuse;
        limit = initialSize;
    }

    /// <summary>
    /// A buffer of the default size.
    /// </summary>
    public WriteBuffer()
        : this(MinGrow)
    {
    }

    /// <summary>
    /// Write a variable size integer.
    /// </summary>
    /// <param name="x">the value</param>
    /// <returns>this</returns>
    public WriteBuffer PutVarInt(int x)
    {
        DataUtils.WriteVarInt(EnsureCapacity(5), ref position, x);
        return this;
    }

    /// <summary>
    /// Write a variable size long.
    /// </summary>
    /// <param name="x">the value</param>
    /// <returns>this</returns>
    public WriteBuffer PutVarLong(long x)
    {
        DataUtils.WriteVarLong(EnsureCapacity(10), ref position, x);
        return this;
    }

    /// <summary>
    /// Write the characters of a string in a format similar to UTF-8.
    /// </summary>
    /// <param name="s">the string</param>
    /// <param name="len">the number of characters to write</param>
    /// <returns>this</returns>
    public WriteBuffer PutStringData(string s, int len)
    {
        DataUtils.WriteStringData(EnsureCapacity(3 * len), ref position, s, len);
        return this;
    }

    /// <summary>
    /// Put a byte.
    /// </summary>
    /// <param name="x">the value</param>
    /// <returns>this</returns>
    public WriteBuffer Put(byte x)
    {
        _ = EnsureCapacity(1)[position++] = x;
        return this;
    }

    /// <summary>
    /// Put a character.
    /// </summary>
    /// <param name="x">the value</param>
    /// <returns>this</returns>
    public WriteBuffer PutChar(char x)
    {
        BinaryPrimitives.WriteUInt16BigEndian(EnsureCapacity(2).AsSpan(position, 2), (ushort)x);
        position += 2;
        return this;
    }

    /// <summary>
    /// Put a short.
    /// </summary>
    /// <param name="x">the value</param>
    /// <returns>this</returns>
    public WriteBuffer PutShort(short x)
    {
        BinaryPrimitives.WriteUInt16BigEndian(EnsureCapacity(2).AsSpan(position, 2), (ushort)x);
        position += 2;
        return this;
    }

    /// <summary>
    /// Put an integer.
    /// </summary>
    /// <param name="x">the value</param>
    /// <returns>this</returns>
    public WriteBuffer PutInt(int x)
    {
        BinaryPrimitives.WriteInt32BigEndian(EnsureCapacity(4).AsSpan(position, 4), x);
        position += 4;
        return this;
    }

    /// <summary>
    /// Put a long.
    /// </summary>
    /// <param name="x">the value</param>
    /// <returns>this</returns>
    public WriteBuffer PutLong(long x)
    {
        BinaryPrimitives.WriteInt64BigEndian(EnsureCapacity(8).AsSpan(position, 8), x);
        position += 8;
        return this;
    }

    /// <summary>
    /// Put a float.
    /// </summary>
    /// <param name="x">the value</param>
    /// <returns>this</returns>
    public WriteBuffer PutFloat(float x) => PutInt(BitConverter.SingleToInt32Bits(x));

    /// <summary>
    /// Put a double.
    /// </summary>
    /// <param name="x">the value</param>
    /// <returns>this</returns>
    public WriteBuffer PutDouble(double x) => PutLong(BitConverter.DoubleToInt64Bits(x));

    /// <summary>
    /// Put a byte array.
    /// </summary>
    /// <param name="bytes">the value</param>
    /// <returns>this</returns>
    public WriteBuffer Put(byte[] bytes) => Put(bytes, 0, bytes.Length);

    /// <summary>
    /// Put a part of a byte array.
    /// </summary>
    /// <param name="bytes">the value</param>
    /// <param name="offset">the source offset</param>
    /// <param name="length">the number of bytes</param>
    /// <returns>this</returns>
    public WriteBuffer Put(byte[] bytes, int offset, int length)
    {
        bytes.AsSpan(offset, length).CopyTo(EnsureCapacity(length).AsSpan(position, length));
        position += length;
        return this;
    }

    /// <summary>
    /// Put the contents of a span.
    /// </summary>
    /// <param name="src">the source</param>
    /// <returns>this</returns>
    public WriteBuffer Put(ReadOnlySpan<byte> src)
    {
        src.CopyTo(EnsureCapacity(src.Length).AsSpan(position, src.Length));
        position += src.Length;
        return this;
    }

    /// <summary>
    /// Set the limit, possibly growing the buffer.
    /// </summary>
    /// <param name="newLimit">the new limit</param>
    /// <returns>this</returns>
    public WriteBuffer Limit(int newLimit)
    {
        _ = EnsureCapacity(newLimit - position);
        limit = newLimit;
        return this;
    }

    /// <summary>
    /// Get the capacity.
    /// </summary>
    /// <returns>the capacity</returns>
    public int Capacity() => buffer.Length;

    /// <summary>
    /// Set the position.
    /// </summary>
    /// <param name="newPosition">the new position</param>
    /// <returns>this</returns>
    public WriteBuffer Position(int newPosition)
    {
        position = newPosition;
        return this;
    }

    /// <summary>
    /// Get the limit.
    /// </summary>
    /// <returns>the limit</returns>
    public int Limit() => limit;

    /// <summary>
    /// Get the current position.
    /// </summary>
    /// <returns>the position</returns>
    public int Position() => position;

    /// <summary>
    /// Copy the data into the destination array.
    /// </summary>
    /// <param name="dst">the destination array</param>
    /// <returns>this</returns>
    public WriteBuffer Get(byte[] dst)
    {
        buffer.AsSpan(position, dst.Length).CopyTo(dst);
        position += dst.Length;
        return this;
    }

    /// <summary>
    /// Update an integer at the given index.
    /// </summary>
    /// <param name="index">the index</param>
    /// <param name="value">the value</param>
    /// <returns>this</returns>
    public WriteBuffer PutInt(int index, int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(index, 4), value);
        return this;
    }

    /// <summary>
    /// Update a short at the given index.
    /// </summary>
    /// <param name="index">the index</param>
    /// <param name="value">the value</param>
    /// <returns>this</returns>
    public WriteBuffer PutShort(int index, short value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(index, 2), (ushort)value);
        return this;
    }

    /// <summary>
    /// Clear the buffer after use.
    /// </summary>
    /// <remarks>
    /// A buffer that grew past the reuse limit is dropped rather than kept, and one that grew within
    /// it becomes the one to keep. That is the reference's arrangement: keeping a multi-megabyte
    /// buffer alive for a store that mostly writes small pages is a leak with extra steps.
    /// </remarks>
    /// <returns>this</returns>
    public WriteBuffer Clear()
    {
        if (limit > MaxReuseCapacity)
        {
            buffer = reuse!;
        }
        else if (!ReferenceEquals(buffer, reuse))
        {
            reuse = buffer;
        }

        limit = buffer.Length;
        position = 0;
        return this;
    }

    /// <summary>
    /// The bytes written so far.
    /// </summary>
    /// <returns>what was written</returns>
    public byte[] ToArray() => buffer[..position];

    private byte[] EnsureCapacity(int len)
    {
        if (limit - position < len)
        {
            Grow(len);
        }

        return buffer;
    }

    private void Grow(int additional)
    {
        int needed = additional - (limit - position);

        // grow at least MinGrow
        long grow = Math.Max(needed, MinGrow);

        // grow at least 50% of the current size
        grow = Math.Max(buffer.Length / 2, grow);

        // the new capacity is at most int.MaxValue
        int newCapacity = (int)Math.Min(int.MaxValue, (long)buffer.Length + grow);
        if (newCapacity < needed)
        {
            throw new OutOfMemoryException(
                "Capacity: " + newCapacity.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " needed: " + needed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        byte[] grown;
        try
        {
            grown = new byte[newCapacity];
        }
        catch (OutOfMemoryException)
        {
            throw new OutOfMemoryException(
                "Capacity: " + newCapacity.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        buffer.AsSpan(0, position).CopyTo(grown);
        buffer = grown;
        limit = newCapacity;
        if (newCapacity <= MaxReuseCapacity)
        {
            reuse = buffer;
        }
    }
}
