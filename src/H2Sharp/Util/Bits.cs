/*
 * Copyright 2004-2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Ported from org.h2.util.Bits.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Modified: MethodHandles VarHandles -> Guid.TryWriteBytes/TryReadBytes with
 * bigEndian: true; java.util.UUID -> Guid.
 *
 * Byte order note. H2's Bits.uuidToBytes writes most/least significant bits
 * big-endian, which produces the canonical UUID text byte order
 * ("123e4567-e89b-12d3-..." as raw bytes). System.Guid.ToByteArray() uses a
 * different layout (Data1..Data3 little-endian), so Guid.TryWriteBytes with
 * bigEndian: true is required to reproduce H2's wire format. Verified against
 * the reference JVM; see docs/notes/uuid-byte-order.md.
 *
 * Note: System.Guid has no bigEndian read overload, so BytesToUuid reads the
 * three leading fields big-endian and passes them to the (int, short, short,
 * byte[]) constructor, which is the exact inverse of the bigEndian write.
 * Verified by tests/BitsTests.cs against the recorded reference-JVM values.
 */
using System;
using System.Buffers.Binary;

namespace H2Sharp.Util;

/// <summary>
/// Manipulations with bytes and arrays.
/// </summary>
public static class Bits
{
    /// <summary>
    /// The number of bytes in a UUID in the H2 wire format.
    /// </summary>
    public const int UuidByteLength = 16;

    /// <summary>
    /// Converts UUID value to byte array in big-endian order.
    /// </summary>
    /// <param name="msb">most significant part of UUID</param>
    /// <param name="lsb">least significant part of UUID</param>
    /// <returns>byte array representation</returns>
    public static byte[] UuidToBytes(long msb, long lsb)
    {
        byte[] buff = new byte[UuidByteLength];
        BinaryPrimitives.WriteInt64BigEndian(buff.AsSpan(0), msb);
        BinaryPrimitives.WriteInt64BigEndian(buff.AsSpan(8), lsb);
        return buff;
    }

    /// <summary>
    /// Converts UUID value to byte array in big-endian order.
    /// </summary>
    /// <param name="uuid">UUID value</param>
    /// <returns>byte array representation</returns>
    public static byte[] UuidToBytes(Guid uuid)
    {
        byte[] buff = new byte[UuidByteLength];
        if (!uuid.TryWriteBytes(buff, bigEndian: true, out int written) || written != UuidByteLength)
        {
            throw new InvalidOperationException("Unexpected Guid byte length: " + written);
        }

        return buff;
    }

    /// <summary>
    /// Reads a big-endian UUID from exactly 16 bytes. This is the inverse of
    /// <see cref="UuidToBytes(Guid)"/>.
    /// </summary>
    /// <param name="bytes">the UUID bytes in H2 wire format</param>
    /// <returns>the UUID</returns>
    public static Guid BytesToUuid(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != UuidByteLength)
        {
            throw new ArgumentException("Expected 16 bytes, got " + bytes.Length, nameof(bytes));
        }

        // H2 wire order is Data1|Data2|Data3|Data4 in canonical text order.
        // Guid.ReadOnlySpan<byte> ctor expects Data1..Data3 little-endian, so read
        // the three leading fields big-endian and pass them explicitly.
        int data1 = unchecked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes[..4]));
        short data2 = unchecked((short)BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(4, 2)));
        short data3 = unchecked((short)BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(6, 2)));
        return new Guid(data1, data2, data3, bytes.Slice(8, 8).ToArray());
    }

    /// <summary>
    /// Extracts the most significant 64 bits of a UUID, matching
    /// java.util.UUID.getMostSignificantBits().
    /// </summary>
    /// <param name="uuid">the UUID</param>
    /// <returns>the most significant bits</returns>
    public static long Msb(Guid uuid)
        => BinaryPrimitives.ReadInt64BigEndian(UuidToBytes(uuid).AsSpan(0, 8));

    /// <summary>
    /// Extracts the least significant 64 bits of a UUID, matching
    /// java.util.UUID.getLeastSignificantBits().
    /// </summary>
    /// <param name="uuid">the UUID</param>
    /// <returns>the least significant bits</returns>
    public static long Lsb(Guid uuid)
        => BinaryPrimitives.ReadInt64BigEndian(UuidToBytes(uuid).AsSpan(8, 8));

    /// <summary>
    /// Reads an int from four bytes in network byte order, matching what
    /// <c>Bits.INT_VH_BE.get(bytes, offset)</c> does in the reference.
    /// </summary>
    /// <remarks>
    /// The reference uses a <c>VarHandle</c> over a big endian byte array view rather than
    /// <c>ByteBuffer</c>, which is the same operation without the buffer object. Java's byte
    /// is signed and this does not care: four bytes are four bytes, and the value is the same
    /// thirty two bits either way.
    /// <para>
    /// This is used everywhere from here on: a value's bytes, the MVStore page format, the
    /// geometry encoding and the crypto primitives all read and write ints big endian.
    /// </para>
    /// </remarks>
    /// <param name="bytes">the bytes to read from</param>
    /// <param name="offset">the zero based offset</param>
    /// <returns>the int</returns>
    public static int IntFromBigEndian(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(offset, 4));

    /// <summary>
    /// Writes an int as four bytes in network byte order, matching what
    /// <c>Bits.INT_VH_BE.set(bytes, offset, value)</c> does in the reference.
    /// </summary>
    /// <param name="bytes">the bytes to write to</param>
    /// <param name="offset">the zero based offset</param>
    /// <param name="value">the int</param>
    public static void IntToBigEndian(Span<byte> bytes, int offset, int value)
        => BinaryPrimitives.WriteInt32BigEndian(bytes.Slice(offset, 4), value);

    /// <summary>
    /// Reads a long from eight bytes in network byte order, matching
    /// <c>Bits.LONG_VH_BE.get(bytes, offset)</c>.
    /// </summary>
    /// <param name="bytes">the bytes to read from</param>
    /// <param name="offset">the zero based offset</param>
    /// <returns>the long</returns>
    public static long LongFromBigEndian(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadInt64BigEndian(bytes.Slice(offset, 8));

    /// <summary>
    /// Writes a long as eight bytes in network byte order, matching
    /// <c>Bits.LONG_VH_BE.set(bytes, offset, value)</c>.
    /// </summary>
    /// <param name="bytes">the bytes to write to</param>
    /// <param name="offset">the zero based offset</param>
    /// <param name="value">the long</param>
    public static void LongToBigEndian(Span<byte> bytes, int offset, long value)
        => BinaryPrimitives.WriteInt64BigEndian(bytes.Slice(offset, 8), value);
}
