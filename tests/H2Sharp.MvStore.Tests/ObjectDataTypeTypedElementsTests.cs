/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The typed-element readers and comparison fallbacks of the auto-detected data types: an
 * array written once reads back element by element, and comparing values of different
 * kinds falls through the same ladder the reference walks — same kind, class ids, bytes,
 * comparables, and finally the refusal to serialize.
 */
using System;
using System.Linq;
using System.Numerics;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Type;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class ObjectDataTypeTypedElementsTests
{
    private static (object? Read, int _) RoundTrip(object? value)
    {
        var ot = new ObjectDataType();
        var buff = new WriteBuffer();
        ot.Write(buff, value);
        byte[] bytes = buff.ToArray();
        int position = 0;
        object? read = ot.Read(bytes, ref position);
        return (read, position);
    }

    public static TheoryData<object> TypedArrays() => new()
    {
        new bool[] { true, false },
        new sbyte[] { -1, 2 },
        new byte[] { 1, 200 },
        new char[] { 'a', 'б' },
        new short[] { -300, 400 },
        new int[] { -5, 6 },
        new long[] { -7L, 8L },
        new float[] { -1.5f, 2.5f },
        new double[] { -2.5d, 3.5d },
        new BigInteger[] { BigInteger.MinusOne, new BigInteger(12) },
        new[] { BigDecimal.Parse("-1.5"), BigDecimal.Parse("2.5") },
        new string[] { "a", "b" },
        new Guid[] { Guid.Parse("00000000-0000-0000-0000-000000000001") },
        new DateTime[] { new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc) },
        new int[][] { [1, 2], [3] },
    };

    [Theory]
    [MemberData(nameof(TypedArrays))]
    public void TypedArrayElementsReadOneByOne(object array)
    {
        (object? read, _) = RoundTrip(array);
        if (array is byte[] bytes)
        {
            // A short byte array is Java's signed bytes on disk: the bits come back the
            // same, the CLR element kind does not.
            sbyte[] signed = Assert.IsType<sbyte[]>(read);
            Assert.Equal(bytes.Select(b => (sbyte)b), signed);
            return;
        }

        Assert.IsAssignableFrom<Array>(read);
        Assert.Equal(((Array)array).Length, ((Array)read!).Length);
        Assert.Equal(0, new ObjectDataType().Compare(array, read));
    }

    [Fact]
    public void ShortByteArrayReadsAsSigned()
    {
        (object? read, _) = RoundTrip(new byte[] { 1, 2, 3 });
        Assert.IsType<sbyte[]>(read);
        Assert.Equal(new sbyte[] { 1, 2, 3 }, (sbyte[])read!);
    }

    [Fact]
    public void UnknownComponentClassIsRefusedByName()
    {
        Assert.ThrowsAny<MvStoreException>(() => RoundTrip(new NotAStoreType?[] { null, null }));
    }

    private sealed class NotAStoreType
    {
    }

    [Fact]
    public void CompareFallsThroughTheReferenceLadder()
    {
        var ot = new ObjectDataType();

        // Same array is equal; different kinds fall back to the base comparer.
        Assert.Equal(0, ot.Compare(new int[] { 1 }, new int[] { 1 }));
        _ = ot.Compare(new int[] { 1 }, new long[] { 1 });
        _ = ot.Compare(new int[] { 1 }, "x");
        // A value no serializer takes is refused by name.
        Assert.ThrowsAny<Exception>(() => ot.Compare(new NotAStoreType(), new NotAStoreType()));

        // The comparable is the raw shape: any IComparable answers, as the reference's
        // erasure does.
        Assert.True(ot.Compare(new Ordered(1), new Ordered(2)) < 0);

        // byte arrays compare unsigned element by element.
        Assert.True(ot.Compare(new byte[] { 1 }, new byte[] { 2 }) < 0);
        Assert.True(ot.Compare(new byte[] { 2 }, new byte[] { 1 }) > 0);
        Assert.Equal(0, ot.Compare(new byte[] { 1, 2 }, new byte[] { 1, 2 }));

        // Primitive elements compare in place.
        Assert.True(ot.Compare(new int[] { 1, 2 }, new int[] { 1, 3 }) < 0);

        // An IComparable pair answers through the comparable itself.
        var a = new Ordered(1);
        var b = new Ordered(2);
        Assert.True(ot.Compare(a, b) < 0);
        Assert.True(ot.Compare(b, a) > 0);
    }

    private sealed class Ordered : IComparable<Ordered>
    {
        internal Ordered(int value) => Value = value;

        internal int Value { get; }

        public int CompareTo(Ordered? other) => Value.CompareTo(other!.Value);
    }
}
