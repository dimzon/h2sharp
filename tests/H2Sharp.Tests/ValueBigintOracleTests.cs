/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for BIGINT.
 *
 * BIGINT is where the reference's overflow rules live. They are sign tests on the operands and
 * a division check on the product, all exact in two's complement, and the grid is built so that
 * every one of them fires: the extremes, one either side of them, the powers of ten around the
 * places where a product overflows, and the pairs whose signs differ, since that is the
 * condition each sign test keys on.
 *
 * The cross type comparison matters as much as the arithmetic. An INTEGER and a BIGINT are
 * different types, so comparing them goes through the promotion table and then through the
 * comparison of the wider type, and the promotion table is what decides the answer.
 *
 * Every operation on the Java side sits inside a lambda that catches DbException, because an
 * uncaught one would end the whole run at the first overflow and the harness would report it as
 * a single THROWS line instead of saying which pair overflowed.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ValueBigintOracleTests
{
    /// <summary>
    /// The values every operation is tried on.
    /// </summary>
    private static readonly long[] Values =
    [
        0, 1, -1, 2, -2, 100, -100,
        long.MaxValue, long.MaxValue - 1, long.MinValue, long.MinValue + 1,
        3_037_000_499, -3_037_000_499, 4_037_000_000, -4_037_000_000,
        9_223_372_036, -9_223_372_036,
        int.MaxValue, int.MinValue,
    ];

    [JavaOracleFact]
    public void Bigint_MatchesReference()
    {
        string body = """
                      for (int i = 0; i < values.length; i++) {
                        for (int j = 0; j < values.length; j++) {
                          org.h2.value.Value a = org.h2.value.ValueBigint.get(values[i]);
                          org.h2.value.Value b = org.h2.value.ValueBigint.get(values[j]);
                          System.out.println(hexText.apply(
                              add.apply(a, b) + "|" + sub.apply(a, b) + "|"
                              + mul.apply(a, b) + "|" + div.apply(a, b) + "|"
                              + mod.apply(a, b) + "|"
                              + cmp.apply(a, b)));
                        }
                      }
            """;

        List<string> java = OracleRunner.Run(
                [],
                Setup + Declare + body,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .Select(Decode)
            .Select(StripTrace)
            .ToList();

        List<string> csharp = [];
        foreach (long left in Values)
        {
            foreach (long right in Values)
            {
                csharp.Add(Pair(ValueBigint.Get(left), ValueBigint.Get(right)));
            }
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// The constants and the single value descriptions, one line per value.
    /// </summary>
    [JavaOracleFact]
    public void BigintValues_MatchReference()
    {
        string body = """
                      java.lang.reflect.Field precision =
                          org.h2.value.ValueBigint.class.getDeclaredField("PRECISION");
                      precision.setAccessible(true);
                      System.out.println(hexText.apply(precision.getInt(null) + " "
                          + org.h2.value.ValueBigint.DECIMAL_PRECISION + " "
                          + org.h2.value.ValueBigint.DISPLAY_SIZE + " "
                          + org.h2.value.ValueBigint.MIN.getSQL(0) + " "
                          + org.h2.value.ValueBigint.MAX.getSQL(0)));
                      for (int i = 0; i < values.length; i++) {
                        System.out.println(hexText.apply(describe.apply(values[i])));
                      }
            """;

        List<string> java = OracleRunner.Run(
                [],
                Setup + Declare + body,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .Select(Decode)
            .Select(StripTrace)
            .ToList();

        List<string> csharp = [Constants()];
        foreach (long value in Values)
        {
            csharp.Add(Render(value));
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// An INTEGER and a BIGINT are different types, so the comparison goes through promotion.
    /// </summary>
    [JavaOracleFact]
    public void MixedIntegerComparison_MatchesReference()
    {
        string body = """
                      for (int i = 0; i < ints.length; i++) {
                        for (int j = 0; j < longs.length; j++) {
                          org.h2.value.Value a = org.h2.value.ValueInteger.get(ints[i]);
                          org.h2.value.Value b = org.h2.value.ValueBigint.get(longs[j]);
                          System.out.println(hexText.apply(cmp.apply(a, b) + "|"
                              + cmp.apply(b, a)));
                        }
                      }
            """;

        List<string> java = OracleRunner.Run([], Setup + Declare + body).Select(Decode).ToList();

        int[] ints = [0, 1, -1, 127, -128, int.MaxValue, int.MinValue];
        long[] longs =
            [0, 1, -1, 100, -100, long.MaxValue, long.MinValue, int.MaxValue, int.MinValue, 4294967296];

        List<string> csharp = [];
        foreach (int i in ints)
        {
            foreach (long j in longs)
            {
                csharp.Add(Compare(ValueInteger.Get(i), ValueBigint.Get(j))
                    + "|"
                    + Compare(ValueBigint.Get(j), ValueInteger.Get(i)));
            }
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    private static string Constants()
        => $"{ValueBigint.Precision.ToString(CultureInfo.InvariantCulture)} "
            + $"{ValueBigint.DecimalPrecision.ToString(CultureInfo.InvariantCulture)} "
            + $"{ValueBigint.DisplaySize.ToString(CultureInfo.InvariantCulture)} "
            + $"{ValueBigint.Min.GetSql(new StringBuilder(), 0)} "
            + $"{ValueBigint.Max.GetSql(new StringBuilder(), 0)}";

    /// <remarks>
    /// Wrapped in a refusal handler, like the Java side. Narrowing a BIGINT to an int can be
    /// refused, and when it is, the whole description becomes the refusal: that is what the
    /// reference's own accessor chain does when it is reached through the converting path.
    /// </remarks>
    private static string Render(long value)
    {
        try
        {
            return RenderCore(value);
        }
        catch (H2SharpException e)
        {
            return "EX " + e.Message;
        }
    }

    private static string RenderCore(long value)
    {
        ValueBigint a = ValueBigint.Get(value);
        StringBuilder line = new();
        _ = line.Append(a.GetSql(new StringBuilder(), 0)).Append('|')
            .Append(a.GetSql(new StringBuilder(), HasSql.NoCasts)).Append('|')
            .Append(a.GetString()).Append('|')
            .Append(a.GetLong()).Append('|')
            .Append(a.GetInt()).Append('|')
            .Append(a.GetBigInteger().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(a.GetBigDecimal().ToString()).Append('|')
            .Append(JavaNumberFormat.FloatToString(a.GetFloat())).Append('|')
            .Append(JavaNumberFormat.DoubleToString(a.GetDouble())).Append('|')
            .Append(a.GetHashCode()).Append('|')
            .Append(a.GetSignum()).Append('|')
            .Append(a.GetMemory()).Append('|')
            .Append(a.GetValueType()).Append('|')
            .Append(a.GetTypeInfo().GetSql(0)).Append('|')
            .Append(Hex(a.GetBytes())).Append('|')
            .Append(Negated(a)).Append('|')
            .Append(J(ReferenceEquals(a, ValueBigint.Get(value)))).Append('|')
            .Append(J(a.Equals(a))).Append('|')
            .Append(J(a.ContainsNull()));
        return line.ToString();
    }

    private static string Pair(ValueBigint a, ValueBigint b)
        => string.Join(
            "|",
            Op(() => a.Add(b)),
            Op(() => a.Subtract(b)),
            Op(() => a.Multiply(b)),
            Div(a, b),
            Mod(a, b),
            Compare(a, b));

    /// <remarks>
    /// Two kinds of refusal, and the difference matters. A DbException is the engine saying no,
    /// with a code and a message, and both sides produce the same text. Anything else is the
    /// runtime refusing: the reference lets an ArithmeticException out of long.MinValue modulo
    /// minus one, and .NET raises an OverflowException for the same pair, so both are reported
    /// as a plain THROWS rather than as text that could not match.
    /// </remarks>
    private static string Op(Func<Value> f) => Try(() => f().GetSql(new StringBuilder(), HasSql.NoCasts));

    private static string Div(Value a, Value b)
        => Try(() => a.Divide(b, TypeInfo.TypeBigint).GetSql(new StringBuilder(), HasSql.NoCasts));

    private static string Mod(Value a, Value b)
        => Try(() => a.Modulus(b).GetSql(new StringBuilder(), HasSql.NoCasts));

    private static string Negated(ValueBigint a)
        => Try(() => a.Negate().GetSql(new StringBuilder(), HasSql.NoCasts));

    private static string Try(Func<StringBuilder> f)
    {
        try
        {
            return f().ToString();
        }
        catch (H2SharpException e)
        {
            return "EX " + e.Message;
        }
        catch (OverflowException)
        {
            return "THROWS";
        }
    }
    private static string Compare(Value a, Value b)
        => a.CompareTo(b, null, CompareMode.GetInstance(null, 0))
            .ToString(CultureInfo.InvariantCulture);

    private static string Hex(byte[]? bytes)
    {
        if (bytes is null)
        {
            return "-";
        }

        StringBuilder b = new();
        foreach (byte x in bytes)
        {
            _ = b.Append(x.ToString("x2", CultureInfo.InvariantCulture));
        }

        return b.ToString();
    }

    private static string J(bool value) => value ? "true" : "false";

    private static readonly System.Text.RegularExpressions.Regex TraceSuffix =
        new(@"\s\[\d+-\d+\]", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static string StripTrace(string line) => TraceSuffix.Replace(line, string.Empty);

    /// <summary>
    /// The arrays, written on both sides and asserted to be the same length by the comparison.
    /// </summary>
    private const string Declare = """
                      long[] values = new long[] {
                          0, 1, -1, 2, -2, 100, -100,
                          Long.MAX_VALUE, Long.MAX_VALUE - 1, Long.MIN_VALUE,
                          Long.MIN_VALUE + 1, 3037000499L, -3037000499L, 4037000000L,
                          -4037000000L, 9223372036L, -9223372036L,
                          Integer.MAX_VALUE, Integer.MIN_VALUE
                      };
                      int[] ints = new int[] { 0, 1, -1, 127, -128, Integer.MAX_VALUE,
                          Integer.MIN_VALUE };
                      long[] longs = new long[] { 0, 1, -1, 100L, -100L, Long.MAX_VALUE,
                          Long.MIN_VALUE, Integer.MAX_VALUE, Integer.MIN_VALUE, 4294967296L };

            """;

    /// <summary>
    /// The lambdas, since the harness puts the body inside main and a method cannot be
    /// declared there.
    /// </summary>
    private const string Setup = """
                      java.util.function.Function<String, String> hexText = s -> {
                        StringBuilder b = new StringBuilder();
                        for (int i = 0; i < s.length(); i++) {
                          b.append(String.format("%04x", (int) s.charAt(i)));
                        }
                        return b.length() == 0 ? "-" : b.toString();
                      };
                      java.util.function.Function<byte[], String> hexBytes = b -> {
                        StringBuilder x = new StringBuilder();
                        for (int i = 0; i < b.length; i++) {
                          x.append(String.format("%02x", b[i] & 0xff));
                        }
                        return x.length() == 0 ? "-" : x.toString();
                      };
                      java.util.function.BiFunction<org.h2.value.Value, org.h2.value.Value, String> add =
                          (a, b) -> {
                            try {
                              return a.add(b).getSQL(4);
                            } catch (org.h2.message.DbException ex) {
                              return "EX " + ex.getMessage();
                              } catch (RuntimeException ex) {
                              return "THROWS";
                            }
                          };
                      java.util.function.BiFunction<org.h2.value.Value, org.h2.value.Value, String> sub =
                          (a, b) -> {
                            try {
                              return a.subtract(b).getSQL(4);
                            } catch (org.h2.message.DbException ex) {
                              return "EX " + ex.getMessage();
                              } catch (RuntimeException ex) {
                              return "THROWS";
                            }
                          };
                      java.util.function.BiFunction<org.h2.value.Value, org.h2.value.Value, String> mul =
                          (a, b) -> {
                            try {
                              return a.multiply(b).getSQL(4);
                            } catch (org.h2.message.DbException ex) {
                              return "EX " + ex.getMessage();
                              } catch (RuntimeException ex) {
                              return "THROWS";
                            }
                          };
                      java.util.function.BiFunction<org.h2.value.Value, org.h2.value.Value, String> div =
                          (a, b) -> {
                            try {
                              return a.divide(b, org.h2.value.TypeInfo.TYPE_BIGINT).getSQL(4);
                            } catch (org.h2.message.DbException ex) {
                              return "EX " + ex.getMessage();
                              } catch (RuntimeException ex) {
                              return "THROWS";
                            }
                          };
                      java.util.function.BiFunction<org.h2.value.Value, org.h2.value.Value, String> mod =
                          (a, b) -> {
                            try {
                              return a.modulus(b).getSQL(4);
                            } catch (org.h2.message.DbException ex) {
                              return "EX " + ex.getMessage();
                              } catch (RuntimeException ex) {
                              return "THROWS";
                            }
                          };
                      java.util.function.Function<org.h2.value.Value, String> neg = a -> {
                        try {
                          return a.negate().getSQL(4);
                        } catch (org.h2.message.DbException ex) {
                          return "EX " + ex.getMessage();
                          } catch (RuntimeException ex) {
                          return "THROWS";
                        }
                      };
                      java.util.function.BiFunction<org.h2.value.Value, org.h2.value.Value, String> cmp =
                          (a, b) -> String.valueOf(a.compareTo(
                              b, null, org.h2.value.CompareMode.getInstance(null, 0)));
                      java.util.function.LongFunction<String> describe = i -> {
                        try {
                          org.h2.value.ValueBigint a = org.h2.value.ValueBigint.get(i);
                          return a.getSQL(0) + "|" + a.getSQL(4) + "|" + a.getString() + "|"
                              + a.getLong() + "|" + a.getInt() + "|" + a.getBigInteger() + "|"
                              + a.getBigDecimal() + "|" + a.getFloat() + "|" + a.getDouble() + "|"
                              + a.hashCode() + "|" + a.getSignum() + "|" + a.getMemory() + "|"
                              + a.getValueType() + "|" + a.getType().getSQL(0) + "|"
                              + hexBytes.apply(a.getBytes()) + "|" + neg.apply(a) + "|"
                              + (a == org.h2.value.ValueBigint.get(i)) + "|" + a.equals(a) + "|"
                              + a.containsNull();
                        } catch (RuntimeException ex) {
                          return "EX " + ex.getMessage();
                        }
                      };

            """;

    private static string Decode(string hex)
    {
        string only = hex.TrimStart('=');
        if (!only.All(static c => char.IsAsciiHexDigit(c) || c == '-'))
        {
            throw new InvalidOperationException($"Not hex from the oracle: '{hex}'");
        }

        if (only == "-")
        {
            return string.Empty;
        }

        StringBuilder sb = new();
        for (int i = 0; i + 3 < only.Length; i += 4)
        {
            sb.Append((char)Convert.ToInt32(only.Substring(i, 4), 16));
        }

        return sb.ToString();
    }
}
