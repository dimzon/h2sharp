/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Round trips through the generic object type.
 *
 * The readers this covers had no test at all: the short, char, int and long readers copied
 * their bytes through a fresh array per element, the UUID reader and writer through two arrays
 * per value, and the short byte array through a loop. They now read straight from the buffer,
 * and a round trip that does not come back is the only thing that says so.
 */
using System;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Type;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ObjectDataTypeRoundTripTests
{
    private static readonly Guid[] Guids =
    [
        Guid.Empty,
        Guid.Parse("123e4567-e89b-12d3-a456-426614174000"),
        Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
        Guid.Parse("80000000-0000-0000-0000-000000000000"),
        Guid.Parse("00000000-8000-8000-8000-000000000000"),
    ];

    [Fact]
    public void UuidsComeBackWithTheWireBytes()
    {
        // The tail is checked against the halves, which the oracle pins: the writer has to put
        // the most significant bits first and big endian, or the tail differs. The tag byte is
        // covered by the read, which dispatches on it.
        ObjectDataType type = new();
        foreach (Guid guid in Guids)
        {
            WriteBuffer buffer = new(32);
            type.Write(buffer, guid);
            byte[] written = buffer.ToArray();

            Assert.Equal(17, written.Length);
            Assert.Equal(
                Bits.UuidToBytes(Bits.Msb(guid), Bits.Lsb(guid)),
                written[1..]);

            int position = 0;
            object? back = type.Read(written, ref position);
            Assert.Equal(written.Length, position);
            Assert.Equal(guid, back);
        }
    }

    [Fact]
    public void ShortsAndCharsComeBack()
    {
        ObjectDataType type = new();
        object[] values =
        [
            (short)0, (short)1, (short)-1, short.MinValue, short.MaxValue,
            'a', 'Z', '\0', 'ÿ', 'Ω', char.MaxValue,
        ];
        foreach (object value in values)
        {
            WriteBuffer buffer = new(16);
            type.Write(buffer, value);
            byte[] written = buffer.ToArray();

            int position = 0;
            object? back = type.Read(written, ref position);
            Assert.Equal(written.Length, position);
            Assert.Equal(value, back);
        }
    }

    [Fact]
    public void PrimitiveArraysComeBack()
    {
        // Every branch of the array reader: the reinterpreted bytes, the fixed readers, and
        // the per-byte fallback they sit next to.
        ObjectDataType type = new();
        object[] values =
        [
            new sbyte[] { },
            new sbyte[] { 0, 1, -1, sbyte.MinValue, sbyte.MaxValue },
            new short[] { 0, 1, -1, short.MinValue, short.MaxValue },
            new char[] { 'a', '\0', 'ÿ', char.MaxValue },
            new int[] { 0, 1, -1, int.MinValue, int.MaxValue },
            new long[] { 0L, 1L, -1L, long.MinValue, long.MaxValue },
            new float[] { 0f, 1.5f, -0f, float.NaN, float.PositiveInfinity },
            new double[] { 0d, -2.25d, double.NaN, double.NegativeInfinity },
        ];
        foreach (object value in values)
        {
            WriteBuffer buffer = new(64);
            type.Write(buffer, value);
            byte[] written = buffer.ToArray();

            int position = 0;
            object? back = type.Read(written, ref position);
            Assert.Equal(written.Length, position);
            Assert.Equal(value, back);
        }
    }

    [Fact]
    public void ByteArraysComeBackSigned()
    {
        // A byte array takes Java's signed byte slot on purpose: the bytes on disk do not know
        // about signedness, so what comes back is the same bits as sbytes.
        ObjectDataType type = new();
        WriteBuffer buffer = new(64);
        type.Write(buffer, new byte[] { 0, 1, 255 });
        byte[] written = buffer.ToArray();

        int position = 0;
        object? back = type.Read(written, ref position);
        Assert.Equal(written.Length, position);
        Assert.Equal(new sbyte[] { 0, 1, -1 }, back);
    }
}
