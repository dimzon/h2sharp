/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The row format: what a value looks like in a page.
 *
 * This is compared as bytes against the reference, because that is the whole contract. The
 * corpus walks the *compressed* forms rather than the plain ones, because the plain form is the
 * obvious one and the compressed ones are where the format lives:
 *
 * * FALSE, TRUE and the integers 0..15 cost one byte each, with the tag itself carrying the
 *   value (32 + x). A negative integer costs a tag plus the absolute value, so -1 is two bytes
 *   while 1 is one — sign-magnitude would have made 1 cost two.
 * * BIGINT repeats the trick one size up (0..7), REAL and DOUBLE one size up again (0 and 1).
 * * NUMERIC has zero and one as single bytes, and everything that fits in 63 bits as a varint.
 * * Strings shorter than 32 characters carry the length in the tag; longer ones repeat it.
 *
 * The round trip is checked too, and that is the part that pays for itself: it is what found
 * that the binary reader was not advancing past the bytes it had read, and that the sixteen
 * bytes of a UUID are the msb and lsb most significant first and not the platform's order. Both
 * are invisible in a comparison that only writes, and both are a row read back as something
 * else.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Db;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ValueDataTypeOracleTests
{
    [JavaOracleFact]
    public void RowFormat_MatchesReference()
    {
        string body = """
                      int[] values = {
                          0, 1, 2, 15, 16, 17, 100, 127, 128, 32767, 65535,
                          -1, -2, -15, -16, -100, -32768, -1000000
                      };
                      org.h2.mvstore.db.ValueDataType t = new org.h2.mvstore.db.ValueDataType();
                      for (int v : values) {
                        System.out.println("int " + v + " " + written.apply(put.apply(t, org.h2.value.ValueInteger.get(v))));
                        System.out.println("big " + v + " " + written.apply(put.apply(t, org.h2.value.ValueBigint.get(v))));
                      }
                      long[] longs = { 0L, 1L, 7L, 8L, 100L, Long.MAX_VALUE, Long.MIN_VALUE + 1, -1L };
                      for (long v : longs) {
                        System.out.println("long " + v + " " + written.apply(put.apply(t, org.h2.value.ValueBigint.get(v))));
                      }
                      org.h2.value.Value[] more = {
                          org.h2.value.ValueBoolean.TRUE, org.h2.value.ValueBoolean.FALSE,
                          org.h2.value.ValueNull.INSTANCE,
                          org.h2.value.ValueTinyint.get((byte) 7), org.h2.value.ValueTinyint.get((byte) -7),
                          org.h2.value.ValueSmallint.get((short) 300),
                          org.h2.value.ValueReal.get(0f), org.h2.value.ValueReal.get(1f),
                          org.h2.value.ValueReal.get(1.5f),
                          org.h2.value.ValueDouble.get(0d), org.h2.value.ValueDouble.get(1d),
                          org.h2.value.ValueDouble.get(-2.25d),
                          org.h2.value.ValueVarchar.get(""), org.h2.value.ValueVarchar.get("a"),
                          org.h2.value.ValueVarchar.get("abc"),
                          org.h2.value.ValueVarchar.get("0123456789012345678901234567890"),
                          org.h2.value.ValueVarchar.get("01234567890123456789012345678901"),
                          org.h2.value.ValueVarcharIgnoreCase.get("abc"),
                          org.h2.value.ValueChar.get("x"),
                          org.h2.value.ValueBinary.get(new byte[] { 1, 2 }),
                          org.h2.value.ValueVarbinary.get(new byte[] { 3, 4, 5 }),
                          org.h2.value.ValueUuid.get(new java.util.UUID(1L, 2L)),
                          org.h2.value.ValueDate.parse("2020-02-29"),
                          org.h2.value.ValueTime.parse("12:34:56", null),
                          org.h2.value.ValueNumeric.ZERO, org.h2.value.ValueNumeric.ONE,
                          org.h2.value.ValueNumeric.get(java.math.BigDecimal.valueOf(-42)),
                          org.h2.value.ValueNumeric.get(java.math.BigDecimal.valueOf(42)),
                          org.h2.value.ValueNumeric.get(new java.math.BigDecimal("9223372036854775807")),
                          org.h2.value.ValueNumeric.get(new java.math.BigDecimal("9223372036854775808")),
                          org.h2.value.ValueNumeric.get(new java.math.BigDecimal("123456789012345678901234567890")),
                          org.h2.value.ValueNumeric.get(new java.math.BigDecimal("-123456789012345678901234567890")),
                          org.h2.value.ValueNumeric.get(new java.math.BigDecimal("1234567890.12345678901234567890"))
                      };
                      for (org.h2.value.Value v : more) {
                        System.out.println(v.getValueType() + " " + written.apply(put.apply(t, v)));
                      }
            """;

        List<string> java = OracleRunner.Run([], Setup + body);
        List<string> csharp = Describe();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [Fact]
    public void EveryWrittenValueComesBack()
    {
        ValueDataType type = new(null, CompareMode.GetInstance(null, 0), null, null);

        foreach (Value v in Values())
        {
            WriteBuffer buffer = new(64);
            type.Write(buffer, v);
            byte[] written = buffer.ToArray();
            int position = 0;
            Value back = type.Read(written, ref position);

            Assert.Equal(written.Length, position);
            Assert.True(v.Equals(back), $"{v.GetValueType()} {Hex(written)} -> {back.GetValueType()} {back.GetString()}");
        }
    }

    private static Value[] Values() =>
    [
        ValueBoolean.True, ValueBoolean.False, ValueNull.Instance,
        ValueInteger.Get(0), ValueInteger.Get(15), ValueInteger.Get(-1), ValueInteger.Get(1_000_000),
        ValueBigint.Get(0), ValueBigint.Get(7), ValueBigint.Get(-1), ValueBigint.Get(long.MaxValue),
        ValueTinyint.Get(7), ValueTinyint.Get(-7), ValueSmallint.Get(300),
        ValueReal.Get(0f), ValueReal.Get(1f), ValueReal.Get(1.5f),
        ValueDouble.Get(0d), ValueDouble.Get(1d), ValueDouble.Get(-2.25d),
        ValueVarchar.Get(string.Empty), ValueVarchar.Get("a"), ValueVarchar.Get("abc"),
        ValueVarchar.Get("0123456789012345678901234567890"),
        ValueVarchar.Get("01234567890123456789012345678901"),
        ValueVarcharIgnoreCase.Get("abc"), ValueChar.Get("x"),
        ValueBinary.Get([1, 2]), ValueVarbinary.Get([3, 4, 5]),
        ValueUuid.Get(1L, 2L),
        ValueDate.FromDateValue(DateTimeUtils.DateValueFromLocalSeconds(1582934096L)),
        ValueTime.FromNanos(45_296_000_000_000L),
        ValueNumeric.Zero, ValueNumeric.One,
        ValueNumeric.Get(new BigDecimal(-42, 0)),
        ValueNumeric.Get(new BigDecimal(42, 0)),
        ValueNumeric.Get(BigDecimal.Parse("9223372036854775808")),
        ValueNumeric.Get(BigDecimal.Parse("-123456789012345678901234567890")),
    ];

    private static List<string> Describe()
    {
        List<string> lines = [];
        ValueDataType type = new(null, CompareMode.GetInstance(null, 0), null, null);

        // The same corpus the snippet writes, in the same order: the comparison is line by line,
        // so a list that is one item longer is not a mismatch to read, it is a list that drifted.
        int[] ints = [0, 1, 2, 15, 16, 17, 100, 127, 128, 32767, 65535, -1, -2, -15, -16, -100, -32768, -1000000];
        foreach (int v in ints)
        {
            lines.Add("int " + v + " " + Written(type, ValueInteger.Get(v)));
            lines.Add("big " + v + " " + Written(type, ValueBigint.Get(v)));
        }

        long[] longs = [0L, 1L, 7L, 8L, 100L, long.MaxValue, long.MinValue + 1, -1L];
        foreach (long v in longs)
        {
            lines.Add("long " + v + " " + Written(type, ValueBigint.Get(v)));
        }

        Value[] more =
        [
            ValueBoolean.True, ValueBoolean.False, ValueNull.Instance,
            ValueTinyint.Get(7), ValueTinyint.Get(-7), ValueSmallint.Get(300),
            ValueReal.Get(0f), ValueReal.Get(1f), ValueReal.Get(1.5f),
            ValueDouble.Get(0d), ValueDouble.Get(1d), ValueDouble.Get(-2.25d),
            ValueVarchar.Get(string.Empty), ValueVarchar.Get("a"), ValueVarchar.Get("abc"),
            ValueVarchar.Get("0123456789012345678901234567890"),
            ValueVarchar.Get("01234567890123456789012345678901"),
            ValueVarcharIgnoreCase.Get("abc"), ValueChar.Get("x"),
            ValueBinary.Get([1, 2]), ValueVarbinary.Get([3, 4, 5]),
            ValueUuid.Get(1L, 2L),
            ValueDate.Parse("2020-02-29"),
            ValueTime.FromNanos(45_296_000_000_000L),
            ValueNumeric.Zero, ValueNumeric.One,
            ValueNumeric.Get(new BigDecimal(-42, 0)),
            ValueNumeric.Get(new BigDecimal(42, 0)),
            ValueNumeric.Get(BigDecimal.Parse("9223372036854775807")),
            ValueNumeric.Get(BigDecimal.Parse("9223372036854775808")),
            ValueNumeric.Get(BigDecimal.Parse("123456789012345678901234567890")),
            ValueNumeric.Get(BigDecimal.Parse("-123456789012345678901234567890")),
            ValueNumeric.Get(BigDecimal.Parse("1234567890.12345678901234567890")),
        ];
        foreach (Value v in more)
        {
            lines.Add(v.GetValueType() + " " + Written(type, v));
        }

        return lines;
    }

    private static string Written(ValueDataType type, Value v)
    {
        WriteBuffer buffer = new(64);
        type.Write(buffer, v);
        return Hex(buffer.ToArray());
    }

    private static string Hex(byte[] bytes)
    {
        StringBuilder builder = new(bytes.Length * 2);
        foreach (byte b in bytes)
        {
            _ = builder.Append(b.ToString("x2"));
        }

        return builder.ToString();
    }

    private const string Setup = """
                      java.util.function.BiFunction<org.h2.mvstore.db.ValueDataType, org.h2.value.Value,
                          org.h2.mvstore.WriteBuffer> put = (t, v) -> {
                        org.h2.mvstore.WriteBuffer w = new org.h2.mvstore.WriteBuffer(64);
                        t.write(w, v);
                        return w;
                      };
                      java.util.function.Function<org.h2.mvstore.WriteBuffer, String> written = w -> {
                        java.nio.ByteBuffer bb = w.getBuffer();
                        int n = bb.position();
                        bb.flip();
                        byte[] arr = new byte[n];
                        bb.get(arr);
                        StringBuilder sb = new StringBuilder();
                        for (byte x : arr) {
                          sb.append(String.format("%02x", x & 0xff));
                        }
                        return sb.toString();
                      };
            """;
}
