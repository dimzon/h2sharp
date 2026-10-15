using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Type;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// The object serializer against the reference: the bytes it writes and the values it reads back.
/// </summary>
/// <remarks>
/// Every value that goes into a stored map travels through here, so a difference is not a rendering
/// difference but a store this port cannot read and the reference cannot read either way round.
/// <para>
/// The corpus is chosen per tag, not per type: the small forms, the fixed forms, the zero and the one
/// each take a different branch, and a branch that is never taken is a branch that is never checked.
/// What the Java-serialization fallback does is not compared, because there is no equivalent here and
/// writing one would produce bytes nothing else reads.
/// </para>
/// </remarks>
[Collection(OracleCollection.Name)]
public sealed class ObjectDataTypeOracleTests
{
    [Fact]
    public void ObjectValuesAgreeWithTheReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                Program(),
                numbers: null,
                extraJavaArgs: ["-Duser.language=en"],
                packageName: "org.h2.mvstore.type")
            .ToList();

        List<string> csharp = [.. Lines()];

        Compare(java, csharp);
    }

    private static string Program() =>
        """
        {
        class R {
        static String hex(byte[] b) {
          return org.h2.util.StringUtils.convertBytesToHex(b);
        }

        static String showDate(Object v) {
          if (v instanceof java.util.Date) {
            return String.valueOf(((java.util.Date) v).getTime());
          }
          if (v instanceof byte[]) {
            return "bytes:" + hex((byte[]) v);
          }
          if (v != null && v.getClass().isArray()) {
            return "array:" + java.util.Arrays.deepToString(new Object[] { v })
                .replaceFirst("^\\[\\[", "").replaceFirst("\\]\\]$", "");
          }
          return String.valueOf(v);
        }

        static String show(Object v, org.h2.mvstore.type.ObjectDataType t) {
          org.h2.mvstore.WriteBuffer b = new org.h2.mvstore.WriteBuffer(8);
          t.write(b, v);
          byte[] all = new byte[b.position()];
          b.position(0);
          b.get(all);
          return hex(all) + "|" + R.showDate(t.read(
              java.nio.ByteBuffer.wrap(all))) + "|" + t.getMemory(v);
        }
        }

        org.h2.mvstore.type.ObjectDataType t = new org.h2.mvstore.type.ObjectDataType();
        Object[] values = {
          null, Boolean.TRUE, Boolean.FALSE, (byte) 0, (byte) 1, (byte) -1, (byte) 127, (byte) -128,
          (short) 0, (short) 1, (short) -1, (short) 32767, (short) -32768,
          'a', '€', 0, 1, -1, 15, 16, 127, 128, 2097151, 2097152, 2147483647, -2147483648,
          0L, 1L, -1L, 7L, 8L, 281474976710655L, 281474976710656L,
          9223372036854775807L, -9223372036854775808L,
          0f, 1f, -1.5f, 3.14f, -0.0f,
          0d, 1d, -1.5d, 3.14159265358979d, -0.0d,
          java.math.BigInteger.ZERO, java.math.BigInteger.ONE,
          new java.math.BigInteger("-5"), new java.math.BigInteger("123456789012345678901234567890"),
          java.math.BigDecimal.ZERO, java.math.BigDecimal.ONE,
          new java.math.BigDecimal("2.50"), new java.math.BigDecimal("123456789.123456789"),
          "", "a", "hello", "привет", "0123456789abcdef", "0123456789abcdefg",
          java.util.UUID.fromString("123e4567-e89b-12d3-a456-426614174000"),
          new java.util.Date(0L), new java.util.Date(1700000000000L),
          new byte[0], new byte[] { 1, 2, 3 },
          new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 },
          new int[] { 1, 2, 3 }, new long[] { 4L, 5L },
          new Object[] { 1, "two", 3L },
          new java.math.BigInteger("-1"), new java.math.BigInteger("-2"),
          new java.math.BigInteger("4611686018427387904"),
          new java.math.BigInteger("-4611686018427387904"),
          new java.math.BigInteger("-4611686018427387905"),
          new java.math.BigInteger("9223372036854775807"),
          new java.math.BigInteger("9223372036854775808"),
          new java.math.BigInteger("-9223372036854775808"),
          new java.math.BigInteger("-9223372036854775809"),
          new java.math.BigInteger("18446744073709551616"),
          new java.math.BigDecimal("-4611686018427387904"),
          new java.math.BigDecimal("-9223372036854775808"),
          new java.math.BigDecimal("-0.01"),
        };
        for (int i = 0; i < values.length; i++) {
          System.out.println("v" + i + "|" + R.show(values[i], t));
        }

        System.out.println("cmp|" + t.compare(1, 2) + "|" + t.compare(2, 1) + "|" + t.compare(1, 1)
            + "|" + t.compare(1, "a") + "|" + t.compare("a", 1) + "|" + t.compare(null, null)
            + "|" + t.compare(null, 1));

        Integer[] ints = { 5, 10, 20 };
        System.out.println("search|" + t.binarySearch(10, ints, 3, 0) + "|"
            + t.binarySearch(15, ints, 3, 0) + "|" + t.binarySearch(10, ints, 3, 2));
        System.out.println("storage|" + t.createStorage(0).length + "|" + t.createStorage(3).length);
        }
        """;

    private static IEnumerable<string> Lines()
    {
        ObjectDataType t = new();
        object?[] values =
        [
            null, true, false, (sbyte)0, (sbyte)1, (sbyte)-1, (sbyte)127, (sbyte)-128,
            (short)0, (short)1, (short)-1, (short)32767, (short)-32768,
            'a', '€', 0, 1, -1, 15, 16, 127, 128, 2097151, 2097152, int.MaxValue, int.MinValue,
            0L, 1L, -1L, 7L, 8L, 281474976710655L, 281474976710656L,
            long.MaxValue, long.MinValue,
            0f, 1f, -1.5f, 3.14f, -0.0f,
            0d, 1d, -1.5d, 3.14159265358979d, -0.0d,
            BigInteger.Zero, BigInteger.One,
            new BigInteger(-5), BigInteger.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture),
            Util.BigDecimal.Zero, Util.BigDecimal.One,
            Util.BigDecimal.Parse("2.50"), Util.BigDecimal.Parse("123456789.123456789"),
            "", "a", "hello", "привет", "0123456789abcdef", "0123456789abcdefg",
            Guid.Parse("123e4567-e89b-12d3-a456-426614174000"),
            DateTime.UnixEpoch, DateTime.UnixEpoch.AddMilliseconds(1700000000000L),
            Array.Empty<byte>(), new byte[] { 1, 2, 3 },
            new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 },
            new int[] { 1, 2, 3 }, new long[] { 4L, 5L },
            new object?[] { 1, "two", 3L },
            new BigInteger(-1), new BigInteger(-2),
            BigInteger.Parse("4611686018427387904", CultureInfo.InvariantCulture),
            BigInteger.Parse("-4611686018427387904", CultureInfo.InvariantCulture),
            BigInteger.Parse("-4611686018427387905", CultureInfo.InvariantCulture),
            new BigInteger(long.MaxValue),
            BigInteger.Parse("9223372036854775808", CultureInfo.InvariantCulture),
            new BigInteger(long.MinValue),
            BigInteger.Parse("-9223372036854775809", CultureInfo.InvariantCulture),
            BigInteger.Parse("18446744073709551616", CultureInfo.InvariantCulture),
            Util.BigDecimal.Parse("-4611686018427387904"),
            Util.BigDecimal.Parse("-9223372036854775808"),
            Util.BigDecimal.Parse("-0.01"),
        ];

        for (int i = 0; i < values.Length; i++)
        {
            yield return "v" + i.ToString(CultureInfo.InvariantCulture) + "|" + Show(values[i], t);
        }

        yield return "cmp|" + t.Compare(1, 2) + "|" + t.Compare(2, 1) + "|" + t.Compare(1, 1)
            + "|" + t.Compare(1, "a") + "|" + t.Compare("a", 1) + "|" + t.Compare(null, null)
            + "|" + t.Compare(null, 1);

        object?[] ints = [5, 10, 20];
        yield return "search|" + t.BinarySearch(10, ints, 3, 0) + "|" + t.BinarySearch(15, ints, 3, 0)
            + "|" + t.BinarySearch(10, ints, 3, 2);
        yield return "storage|" + t.CreateStorage(0).Length + "|" + t.CreateStorage(3).Length;
    }

    private static string Show(object? value, ObjectDataType t)
    {
        WriteBuffer b = new(8);
        t.Write(b, value);
        byte[] all = b.ToArray();
        int position = 0;
        object? back = t.Read(all, ref position);
        return Hex.BytesToHex(all) + "|" + Describe(back) + "|" + t.GetMemory(value);
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        byte[] bytes => "bytes:" + Hex.BytesToHex(bytes),
        Array array => "array:" + string.Join(", ", array.Cast<object?>().Select(Describe)),
        DateTime date => ((long)(date.ToUniversalTime() - DateTime.UnixEpoch).TotalMilliseconds)
            .ToString(CultureInfo.InvariantCulture),
        Guid guid => guid.ToString("D", CultureInfo.InvariantCulture),
        float f => JavaFloat(f),
        double d => JavaDouble(d),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null",
    };

    /// <summary>
    /// A float the way <c>Float.toString</c> spells it, which always shows the point.
    /// </summary>
    private static string JavaFloat(float value)
    {
        string text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('E') ? text : text + ".0";
    }

    /// <summary>
    /// A double the way <c>Double.toString</c> spells it, which always shows the point.
    /// </summary>
    private static string JavaDouble(double value)
    {
        string text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('E') ? text : text + ".0";
    }

    private static void Compare(List<string> java, List<string> csharp)
    {
        if (java.Count != csharp.Count)
        {
            int common = Math.Min(java.Count, csharp.Count);
            int first = -1;
            for (int i = 0; i < common; i++)
            {
                if (java[i] != csharp[i])
                {
                    first = i;
                    break;
                }
            }

            Assert.Fail(
                "строк: java=" + java.Count.ToString(CultureInfo.InvariantCulture)
                + " csharp=" + csharp.Count.ToString(CultureInfo.InvariantCulture)
                + "\n  первое различие: " + first.ToString(CultureInfo.InvariantCulture)
                + "\n    java:   " + (first >= 0 ? java[first] : "-")
                + "\n    csharp: " + (first >= 0 ? csharp[first] : "-")
                + "\n  последняя java:   " + (java.Count > 0 ? java[^1] : "-")
                + "\n  последняя csharp: " + (csharp.Count > 0 ? csharp[^1] : "-"));
        }

        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }
}
