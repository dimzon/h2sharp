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

    [Fact]
    public void SerializedObjectMemoryTerminatesAtTheAverageSize()
    {
        // An object with no typed home goes through SerializedObjectType. Its owner has to be
        // the same ObjectDataType: a fresh owner makes every type switch produce another
        // instance, so "t == this" never holds and GetMemory recurses until the process dies.
        ObjectDataType type = new();
        Assert.Equal(10_000, type.GetMemory(new object()));
    }

    private static object? RoundTrip(ObjectDataType type, object? value)
    {
        WriteBuffer buffer = new(256);
        type.Write(buffer, value);
        byte[] written = buffer.ToArray();
        int position = 0;
        object? back = type.Read(written, ref position);
        Assert.Equal(written.Length, position);
        return back;
    }

    [Fact]
    public void EveryScalarTagComesBack()
    {
        ObjectDataType type = new();
        object?[] values =
        [
            true, false,
            0, 1, -1, 7, int.MinValue, int.MaxValue,
            0L, 1L, -1L, 7L, long.MinValue, long.MaxValue,
            0f, 1f, 1.5f, -2.5f, float.NaN, float.PositiveInfinity,
            0d, 1d, 2.5d, -3.5d, double.NaN, double.NegativeInfinity,
            System.Numerics.BigInteger.Zero,
            System.Numerics.BigInteger.One,
            new System.Numerics.BigInteger(-5),
            System.Numerics.BigInteger.Parse("123456789012345678901234567890"),
            BigDecimal.Zero,
            BigDecimal.One,
            BigDecimal.FromLong(1_234_567),
            new BigDecimal(12_345, 2),
            "", "a", "0123456789abcde", "0123456789abcdef", new string('x', 40), "Ω≈ç√",
            Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
            new DateTime(2024, 1, 2, 3, 4, 5, 678),
            DateTime.UnixEpoch,
        ];
        foreach (object? value in values)
        {
            object? back = RoundTrip(type, value);
            if (value is DateTime dateTime)
            {
                // The wire form is milliseconds since the epoch, so the value comes back UTC.
                Assert.Equal(DateTimeKind.Utc, ((DateTime)back!).Kind);
                Assert.Equal(dateTime.ToUniversalTime(), ((DateTime)back!).ToUniversalTime());
            }
            else
            {
                Assert.Equal(value, back);
            }

            Assert.True(type.GetMemory(value) >= 0);
        }

        Assert.Equal(0, type.Compare(1, 1));
        Assert.True(type.Compare(1, 2) < 0);
        Assert.True(type.Compare("b", "a") > 0);
        Assert.Equal(3, type.CreateStorage(3).Length);
    }

    [Fact]
    public void ReferenceArraysComeBackElementByElement()
    {
        ObjectDataType type = new();

        string[] strings = ["a", "bb", "ccc", ""];
        string[] stringsBack = Assert.IsType<string[]>(RoundTrip(type, strings));
        Assert.Equal(strings, stringsBack);

        object?[] mixed = [1, "a", null, true];
        object?[] mixedBack = Assert.IsType<object?[]>(RoundTrip(type, mixed));
        Assert.Equal(mixed, mixedBack);
    }

    [Fact]
    public void UnsupportedObjectsAreRefused()
    {
        ObjectDataType type = new();
        _ = Assert.Throws<ArgumentException>(() => ObjectDataType.Serialize(new object()));
        _ = Assert.Throws<ArgumentException>(() => ObjectDataType.Deserialize([1, 2, 3]));
    }
}
