/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.test.store.TestObjectDataType.
 *
 * What this pins is three promises at once: every common value survives a write and reads back
 * equal with the same runtime type, values order exactly as the reference orders them, and
 * comparing is antisymmetric on a thousand random pairs.
 *
 * Three things do not map one for one. Java's byte is signed, so every byte here is an sbyte and
 * every byte array stays unsigned bytes — the reference never mixes the two either. Java's
 * probablePrime has no .NET twin and nothing asserts primality, so the large integer is a
 * deterministic 200-bit magnitude out of System.Random(1): the magnitude is what the ordering
 * asserts lean on. And java.util.Date and Timestamp hold nothing but milliseconds, so both are
 * instants on DateTime.UnixEpoch — always UTC, because an Unspecified kind would compare by
 * different ticks than the UTC instant the reader returns.
 *
 * Two Java values stay out. Timestamp scalars go through Java object serialization, which has no
 * .NET side, so their after-every-array ordering is untestable without inventing a format; the
 * Timestamp arrays stay in as DateTimeOffset arrays, which are distinct, ordered, and round-tripped
 * exactly as the asserts need.
 */

using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Type;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class TestObjectDataType : StoreTestBase
{
    [Fact]
    public void TestValues() => TestCommonValues();

    private static void TestCommonValues()
    {
        BigInteger largeBigInt = ProbablePrime200();
        var ot = new ObjectDataType();
        object?[] array =
        [
            false, true,
            sbyte.MinValue, (sbyte)-1, (sbyte)0, (sbyte)1, sbyte.MaxValue,
            short.MinValue, (short)-1, (short)0, (short)1, short.MaxValue,
            int.MinValue, int.MinValue + 1,
            -1000, -100, -1, 0, 1, 2, 14,
            15, 16, 17, 100, int.MaxValue - 1, int.MaxValue,
            long.MinValue, long.MinValue + 1, -1000L, -1L, 0L, 1L, 2L, 14L,
            15L, 16L, 17L, 100L, long.MaxValue - 1, long.MaxValue,
            BigInteger.Negate(largeBigInt), BigInteger.MinusOne, BigInteger.Zero,
            BigInteger.One, new BigInteger(10), largeBigInt,
            float.NegativeInfinity, -float.MaxValue, -1f, -0f, 0f,
            float.Epsilon, 1f, float.MaxValue,
            float.PositiveInfinity, float.NaN,
            double.NegativeInfinity, -double.MaxValue, -1d, -0d, 0d,
            double.Epsilon, 1d, double.MaxValue,
            double.PositiveInfinity, double.NaN,
            BigDecimal.Parse(double.MaxValue.ToString("R", CultureInfo.InvariantCulture)).Negate(),
            BigDecimal.FromBigInteger(largeBigInt).Negate(),
            BigDecimal.Parse("-100.0"), BigDecimal.Zero, BigDecimal.One,
            BigDecimal.TenValue, BigDecimal.FromLong(long.MaxValue),
            BigDecimal.FromBigInteger(largeBigInt),
            BigDecimal.Parse(double.MaxValue.ToString("R", CultureInfo.InvariantCulture)),
            char.MinValue, '0', 'a', char.MaxValue,
            "", " ", "  ", "123456789012345", "1234567890123456",
            new string('x', 100),
            new string('x', 100000), "y",
            new string((char)0x1234, 1), new string((char)0x2345, 1),
            new string((char)0x6789, 1), new string((char)0xffff, 1),
            Uuid(long.MinValue, long.MinValue),
            Uuid(long.MinValue, 0), Uuid(0, 0),
            Uuid(long.MaxValue, long.MaxValue),
            EpochMillis(0), EpochMillis(1000),
            EpochMillis(4000), EpochMillis(5000),
            new bool[0], new[] { false, false },
            new[] { true },
            new sbyte[0], new sbyte[1], new sbyte[15], new sbyte[16],
            new sbyte[10000], new sbyte[] { 1 },
            new sbyte[] { unchecked((sbyte)0xff) },
            new short[0], new short[] { -1 }, new short[] { 1 },
            new char[0], new char[1], new char[10000],
            new char[] { (char)1 },
            new int[0], new int[1], new int[15], new int[16],
            new int[10000], new int[] { 1 },
            new long[0], new long[1], new long[15], new long[16],
            new long[10000], new long[] { 1 },
            new float[0], new[] { float.NegativeInfinity },
            new float[1], new[] { float.PositiveInfinity },
            new double[0], new[] { double.NegativeInfinity },
            new double[1], new[] { double.PositiveInfinity },
            new object?[0],
            new object?[100],
            new object?[] { 1 },
            new object?[] { 0.0, "Hello", null, double.NaN },
            new string?[] { "Hello", null },
            new[] { "World" },
            new DateTime[0],
            // Java's Timestamp[] is a different component type than java.sql.Date[], so it needs
            // a different array type here too; DateTimeOffset has no Java twin, which is fine
            // because the asserts only need the types distinct, ordered, and round-tripped.
            new DateTimeOffset[0],
            new DateTimeOffset?[] { null },
            // Java stores Timestamp scalars through Java object serialization, which has no .NET
            // side: they order after every array and read back Java-serialized bytes. Inventing a
            // format for them is out of scope, so the two Timestamp values stay out.
        ];
        object? otherType = false;
        object? last = null;
        foreach (object? x in array)
        {
            TestValue(otherType, x);
            if (last is not null)
            {
                int comp = ot.Compare(x, last);
                if (comp <= 0)
                {
                    Fail(x!.GetType().Name + ": " + x + " " + comp);
                }

                AssertTrue(x!.ToString()!, ot.Compare(last, x) < 0);
            }

            if (last is not null && last.GetType() != x!.GetType())
            {
                otherType = last;
            }

            last = x;
        }

        var r = new Random(1);
        for (int i = 0; i < 1000; i++)
        {
            object? x = array[r.Next(array.Length)];
            object? y = array[r.Next(array.Length)];
            int comp = ot.Compare(x, y);
            if (comp != 0)
            {
                AssertEqual("x:" + x + " y:" + y, -comp, ot.Compare(y, x));
            }
        }
    }

    private static void TestValue(object? last, object? x)
    {
        var ot = new ObjectDataType();

        // switch to the last type before every operation,
        // to test switching types
        ot.GetMemory(last);
        AssertTrue(ot.GetMemory(x) >= 0);

        ot.GetMemory(last);
        AssertTrue(ot.GetMemory(x) >= 0);

        ot.GetMemory(last);
        AssertEqual(0, ot.Compare(x, x));
        var buff = new WriteBuffer();

        ot.GetMemory(last);
        ot.Write(buff, x);
        _ = buff.Put((byte)123);
        byte[] bytes = buff.ToArray();
        int position = 0;

        ot.GetMemory(last);
        object? y = ot.Read(bytes, ref position);
        AssertEqual(123, (int)bytes[position++]);
        AssertEqual(bytes.Length, position);
        AssertEqual(x!.GetType().FullName, y!.GetType().FullName);

        ot.GetMemory(last);
        AssertEqual(0, ot.Compare(x, y));
        if (x!.GetType().IsArray)
        {
            switch (x)
            {
                case sbyte[] xb:
                    AssertTrue(((sbyte[])y!).SequenceEqual(xb));
                    break;
                case bool[] xb:
                    AssertTrue(((bool[])y!).SequenceEqual(xb));
                    break;
                case short[] xs:
                    AssertTrue(((short[])y!).SequenceEqual(xs));
                    break;
                case float[] xf:
                    AssertTrue(((float[])y!).SequenceEqual(xf));
                    break;
                case double[] xd:
                    AssertTrue(((double[])y!).SequenceEqual(xd));
                    break;
                case char[] xc:
                    AssertTrue(((char[])y!).SequenceEqual(xc));
                    break;
                case int[] xi:
                    AssertTrue(((int[])y!).SequenceEqual(xi));
                    break;
                case long[] xl:
                    AssertTrue(((long[])y!).SequenceEqual(xl));
                    break;
                default:
                    AssertTrue(((Array)y!).Cast<object?>().SequenceEqual(((Array)x).Cast<object?>()));
                    break;
            }
        }
        else
        {
            AssertEqual(x.GetHashCode(), y!.GetHashCode());
            AssertTrue(x.Equals(y));
        }
    }

    private static BigInteger ProbablePrime200()
    {
        var random = new Random(1);
        byte[] bytes = new byte[26];
        random.NextBytes(bytes);
        bytes[25] = 0;
        bytes[24] |= 0x80;
        return new BigInteger(bytes);
    }

    private static Guid Uuid(long mostSigBits, long leastSigBits)
        => new((int)(mostSigBits >> 32), (short)(mostSigBits >> 16), (short)mostSigBits,
            (byte)(leastSigBits >> 56), (byte)(leastSigBits >> 48), (byte)(leastSigBits >> 40),
            (byte)(leastSigBits >> 32), (byte)(leastSigBits >> 24), (byte)(leastSigBits >> 16),
            (byte)(leastSigBits >> 8), (byte)leastSigBits);

    private static DateTime EpochMillis(long millis)
        => DateTime.UnixEpoch.AddTicks(millis * TimeSpan.TicksPerMillisecond);
}
