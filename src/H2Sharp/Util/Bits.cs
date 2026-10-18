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
using System.Numerics;

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
        UuidToBytes(uuid, buff);
        return buff;
    }

    /// <summary>
    /// Writes a UUID in big-endian order into a span, without allocating.
    /// </summary>
    /// <param name="uuid">UUID value</param>
    /// <param name="destination">at least sixteen bytes</param>
    public static void UuidToBytes(Guid uuid, Span<byte> destination)
    {
        if (!uuid.TryWriteBytes(destination, bigEndian: true, out int written)
            || written != UuidByteLength)
        {
            throw new InvalidOperationException("Unexpected Guid byte length: " + written);
        }
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
        // Guid.ReadOnlySpan<byte> ctor expects Data1..Data3 little-endian, so the three
        // leading fields are reordered and the tail copied as is. No allocation.
        Span<byte> ordered = stackalloc byte[UuidByteLength];
        ordered[0] = bytes[3];
        ordered[1] = bytes[2];
        ordered[2] = bytes[1];
        ordered[3] = bytes[0];
        ordered[4] = bytes[5];
        ordered[5] = bytes[4];
        ordered[6] = bytes[7];
        ordered[7] = bytes[6];
        bytes.Slice(8, 8).CopyTo(ordered[8..]);
        return new Guid(ordered);
    }

    /// <summary>
    /// Extracts the most significant 64 bits of a UUID, matching
    /// java.util.UUID.getMostSignificantBits().
    /// </summary>
    /// <remarks>
    /// The write is one 16-byte copy onto the stack, not a heap allocation; the fields of a
    /// Guid are not publicly reachable, so there is no cheaper arithmetic route (issue #12 B3,
    /// recorded as not actionable: the win the audit promised is stack movement the runtime
    /// already does in one native write per call).
    /// </remarks>
    /// <param name="uuid">the UUID</param>
    /// <returns>the most significant bits</returns>
    public static long Msb(Guid uuid)
    {
        Span<byte> buff = stackalloc byte[UuidByteLength];
        UuidToBytes(uuid, buff);
        return BinaryPrimitives.ReadInt64BigEndian(buff[..8]);
    }

    /// <summary>
    /// Extracts the least significant 64 bits of a UUID, matching
    /// java.util.UUID.getLeastSignificantBits().
    /// </summary>
    /// <param name="uuid">the UUID</param>
    /// <returns>the least significant bits</returns>
    public static long Lsb(Guid uuid)
    {
        Span<byte> buff = stackalloc byte[UuidByteLength];
        UuidToBytes(uuid, buff);
        return BinaryPrimitives.ReadInt64BigEndian(buff[8..]);
    }

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

    /// <summary>
    /// Minimal two's complement big-endian, the shape <c>java.math.BigInteger</c> writes:
    /// the same conversion both store types use for the same question, so the two formats
    /// agree byte for byte.
    /// </summary>
    /// <remarks>
    /// Sized exactly and left uninitialized: the writer fills every byte, so zeroing first
    /// would be memset for nothing. Should the size ever disagree, the old
    /// convert-and-reverse answers instead of a short array.
    /// </remarks>
    /// <param name="value">the value</param>
    /// <returns>the minimal big-endian bytes</returns>
    public static byte[] ToBigEndian(BigInteger value)
    {
        byte[] bytes = GC.AllocateUninitializedArray<byte>(ByteCount(value));
        if (!value.TryWriteBytes(bytes, out int written, isUnsigned: false, isBigEndian: true)
            || written != bytes.Length)
        {
            byte[] little = value.ToByteArray();
            System.Array.Reverse(little);
            return little;
        }

        return bytes;
    }

    /// <summary>
    /// How many bytes the minimal two's complement big-endian form takes: one more than
    /// the bit length needs, rounded up, which is the sign byte when the top bit is taken
    /// and the single zero byte otherwise.
    /// </summary>
    /// <param name="value">the value</param>
    /// <returns>the byte count</returns>
    public static int ByteCount(BigInteger value) => ((int)value.GetBitLength() + 8) / 8;

    /// <summary>
    /// Java's <c>Integer.reverse</c>: every bit of the value, in the opposite order.
    /// </summary>
    /// <remarks>
    /// Java-integer member carried here because Java calls it on its Integer class directly
    /// (the float and double types of the MVStore write their bit patterns reversed). The BCL
    /// has byte-order reversal only (BinaryPrimitives.ReverseEndianness), no bit reversal, so
    /// the mask ladder — the same steps the reference stack's intrinsic lowers to — is the
    /// implementation on every target.
    /// </remarks>
    /// <param name="value">the bits</param>
    /// <returns>the bits in reverse order</returns>
    public static int Reverse(int value)
    {
        uint v = (uint)value;
        v = ((v & 0x55555555) << 1) | ((v >> 1) & 0x55555555);
        v = ((v & 0x33333333) << 2) | ((v >> 2) & 0x33333333);
        v = ((v & 0x0f0f0f0f) << 4) | ((v >> 4) & 0x0f0f0f0f);
        return (int)((v << 24) | ((v & 0xff00) << 8) | ((v >> 8) & 0xff00) | (v >> 24));
    }

    /// <summary>
    /// Java's <c>Long.reverse</c>: every bit of the value, in the opposite order.
    /// </summary>
    /// <param name="value">the bits</param>
    /// <returns>the bits in reverse order</returns>
    public static long Reverse(long value)
    {
        ulong v = (ulong)value;
        v = ((v & 0x5555555555555555UL) << 1) | ((v >> 1) & 0x5555555555555555UL);
        v = ((v & 0x3333333333333333UL) << 2) | ((v >> 2) & 0x3333333333333333UL);
        v = ((v & 0x0f0f0f0f0f0f0f0fUL) << 4) | ((v >> 4) & 0x0f0f0f0f0f0f0f0fUL);
        v = ((v & 0x00ff00ff00ff00ffUL) << 8) | ((v >> 8) & 0x00ff00ff00ff00ffUL);
        v = ((v & 0x0000ffff0000ffffUL) << 16) | ((v >> 16) & 0x0000ffff0000ffffUL);
        return (long)((v << 32) | (v >> 32));
    }

    /// <summary>
    /// The inverse of <see cref="ToBigEndian"/>.
    /// </summary>
    /// <param name="bytes">the minimal big-endian bytes</param>
    /// <returns>the value</returns>
    public static BigInteger FromBigEndian(ReadOnlySpan<byte> bytes)
        => new(bytes, isUnsigned: false, isBigEndian: true);
}
