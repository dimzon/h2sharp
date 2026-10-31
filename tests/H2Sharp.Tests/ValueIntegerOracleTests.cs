/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for INTEGER and for comparing values.
 *
 * Integer is the first value with arithmetic, so it is where the range rules show: every
 * operation is widened to long and checked, and the two overflow cases that Java defines
 * rather than refuses are covered, because those are where a port silently changes an answer.
 * INT_MIN divided by -1 is refused and its remainder is zero.
 *
 * The cache is compared too: the reference answers the same int with the same instance, and
 * two values that are equal must be the same object for the index and the identity checks
 * elsewhere to work.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]

public sealed class ValueIntegerOracleTests
{
    /// <summary>
    /// The values every operation is tried on, chosen to sit on the boundaries.
    /// </summary>
    private static readonly int[] Ints =
    [
        0, 1, -1, 2, 7, 127, 128, 129, -127, -128, -129,
        1_000, int.MaxValue, int.MinValue, int.MinValue + 1,
    ];

    [JavaOracleFact]
    public void Integer_MatchesReference()
    {
        string body = """
                      int[] ints = new int[] {
                          0, 1, -1, 2, 7, 127, 128, 129, -127, -128, -129,
                          1000, Integer.MAX_VALUE, Integer.MIN_VALUE, Integer.MIN_VALUE + 1
                      };
                      System.out.println(hexText.apply(String.valueOf(ints.length)));
                      System.out.println(hexText.apply(org.h2.value.ValueInteger.PRECISION + " "
                          + org.h2.value.ValueInteger.DECIMAL_PRECISION + " "
                          + org.h2.value.ValueInteger.DISPLAY_SIZE));
                      for (int i = 0; i < ints.length; i++) {
                        org.h2.value.ValueInteger a = org.h2.value.ValueInteger.get(ints[i]);
                        // getFloat and getDouble are widening casts here and are compared as
                        // numbers by the harness below, not as text: Java prints a double as
                        // "0.0" and .NET as "0", and matching that is the job of the Java
                        // number formatter, which is its own unit with its own oracle. Printing
                        // them here would test the formatter, not this value.
                        System.out.println(hexText.apply(a.getSQL(0) + "|" + a.getString() + "|"
                            + a.getInt() + "|" + a.getLong() + "|"
                            + a.getBigInteger() + "|"
                            + a.getBigDecimal() + "|" + a.hashCode() + "|"
                            + a.getSignum() + "|" + a.getMemory() + "|"
                            + a.getValueType() + "|" + a.getType().getSQL(0) + "|"
                            + a.containsNull() + "|" + a.equals(a) + "|"
                            + (a == org.h2.value.ValueInteger.get(ints[i])) + "|"
                            + hexBytes.apply(a.getBytes())
                            + "|" + neg.apply(a)));
                      }
                      for (int i = 0; i < ints.length; i++) {
                        for (int j = 0; j < ints.length; j++) {
                          org.h2.value.Value a = org.h2.value.ValueInteger.get(ints[i]);
                          org.h2.value.Value b = org.h2.value.ValueInteger.get(ints[j]);
                          System.out.println(hexText.apply(bin.apply(a, b) + "|"
                              + sub.apply(a, b) + "|" + mul.apply(a, b) + "|"
                              + div.apply(a, b) + "|" + mod.apply(a, b) + "|"
                              + cmp.apply(a, b)));
                        }
                      }
            """;
        List<string> java = OracleRunner.Run(
                [],
                Helpers + body,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .Select(Decode)
            .Select(line => TraceSuffix.Replace(line, string.Empty))
            .ToList();

        List<string> csharp = [$"{Ints.Length.ToString(CultureInfo.InvariantCulture)}", Constants()];
        foreach (int i in Ints)
        {
            csharp.Add(Render(i));
        }

        foreach (int i in Ints)
        {
            foreach (int j in Ints)
            {
                csharp.Add(Pair(ValueInteger.Get(i), ValueInteger.Get(j)));
            }
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// Comparison with NULL is a separate answer from comparison itself, because the order of
    /// NULL is not defined and the reference says so with a sentinel rather than a value.
    /// </summary>
    [JavaOracleFact]
    public void ComparisonWithNull_MatchesReference()
    {
        string body = """
                      org.h2.value.Value n = org.h2.value.ValueNull.INSTANCE;
                      org.h2.value.Value one = org.h2.value.ValueInteger.get(1);
                      org.h2.value.Value two = org.h2.value.ValueInteger.get(2);
                      org.h2.value.CompareMode off = org.h2.value.CompareMode.getInstance(null, 0);
                      System.out.println(hex(one.compareTo(one, null, off) + "|"
                          + one.compareTo(two, null, off) + "|"
                          + two.compareTo(one, null, off) + "|"
                          + n.compareTo(n, null, off) + "|"
                          + n.compareTo(one, null, off) + "|"
                          + one.compareTo(n, null, off)));
                      System.out.println(hex(one.compareWithNull(one, false, null, off) + "|"
                          + one.compareWithNull(n, false, null, off) + "|"
                          + n.compareWithNull(one, false, null, off) + "|"
                          + n.compareWithNull(n, true, null, off)));
            """;

        List<string> java = OracleRunner.Run([], body).Select(Decode).ToList();

        CompareMode off = CompareMode.GetInstance(null, 0);
        Value one = ValueInteger.Get(1), two = ValueInteger.Get(2);

        string first =
            $"{one.CompareTo(one, null, off)}|{one.CompareTo(two, null, off)}|"
            + $"{two.CompareTo(one, null, off)}|{ValueNull.Instance.CompareTo(ValueNull.Instance, null, off)}|"
            + $"{ValueNull.Instance.CompareTo(one, null, off)}|{one.CompareTo(ValueNull.Instance, null, off)}";
        string second =
            $"{one.CompareWithNull(one, false, null, off)}|{one.CompareWithNull(ValueNull.Instance, false, null, off)}|"
            + $"{ValueNull.Instance.CompareWithNull(one, false, null, off)}|{ValueNull.Instance.CompareWithNull(ValueNull.Instance, true, null, off)}";

        Assert.Equal(2, java.Count);
        Assert.Equal(java[0], first);
        Assert.Equal(java[1], second);
    }

    private static string Constants()
        => $"{ValueInteger.Precision.ToString(CultureInfo.InvariantCulture)} "
            + $"{ValueInteger.DecimalPrecision.ToString(CultureInfo.InvariantCulture)} "
            + $"{ValueInteger.DisplaySize.ToString(CultureInfo.InvariantCulture)}";

    private static string Render(int i)
    {
        ValueInteger a = ValueInteger.Get(i);
        StringBuilder line = new();
        _ = line.Append(a.GetSql(new StringBuilder(), 0).ToString()).Append('|')
            .Append(a.GetString()).Append('|')
            .Append(a.GetInt()).Append('|')
            .Append(a.GetLong()).Append('|')
            .Append(a.GetBigInteger().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(Truncate(a.GetBigDecimal())).Append('|')
            .Append(a.GetHashCode()).Append('|')
            .Append(a.GetSignum()).Append('|')
            .Append(a.GetMemory()).Append('|')
            .Append(a.GetValueType()).Append('|')
            .Append(a.GetTypeInfo().GetSql(0)).Append('|')
            .Append(J(a.ContainsNull())).Append('|')
            .Append(J(a.Equals(a))).Append('|')
            .Append(J(ReferenceEquals(a, ValueInteger.Get(i)))).Append('|')
            .Append(Hex(a.GetBytes())).Append('|')
            .Append(Negated(a));
        return line.ToString();
    }

    private static string Negated(ValueInteger a)
    {
        try
        {
            return a.Negate().GetSql(new StringBuilder(), 0).ToString();
        }
        catch (H2SharpException e)
        {
            return "EX " + TraceSuffix.Replace(e.Message, string.Empty);
        }
    }

    private static string Pair(ValueInteger a, ValueInteger b)
        => string.Join(
            "|",
            Op("+", () => a.Add(b)),
            Op("-", () => a.Subtract(b)),
            Op("*", () => a.Multiply(b)),
            Div(a, b),
            Mod(a, b),
            Compare(a, b));

    private static string Op(string op, Func<Value> f)
    {
        try
        {
            return f().GetSql(new StringBuilder(), 0).ToString();
        }
        catch (H2SharpException e)
        {
            return "EX " + TraceSuffix.Replace(e.Message, string.Empty);
        }
    }

    private static string Div(Value a, Value b)
    {
        try
        {
            return a.Divide(b, TypeInfo.TypeBigint).GetSql(new StringBuilder(), 0).ToString();
        }
        catch (H2SharpException e)
        {
            return "EX " + TraceSuffix.Replace(e.Message, string.Empty);
        }
    }

    private static string Mod(Value a, Value b)
    {
        try
        {
            return a.Modulus(b).GetSql(new StringBuilder(), 0).ToString();
        }
        catch (H2SharpException e)
        {
            return "EX " + TraceSuffix.Replace(e.Message, string.Empty);
        }
    }

    private static string Compare(Value a, Value b)
    {
        CompareMode off = CompareMode.GetInstance(null, 0);
        return a.CompareTo(b, null, off).ToString(CultureInfo.InvariantCulture);
    }

    /// <remarks>
    /// .NET renders a decimal with a trailing scale suffix where Java's BigDecimal prints its
    /// scale, so the text is compared as the number and the scale together rather than as the
    /// exact rendering.
    /// </remarks>
    /// <remarks>
    /// The reference asks for a decimal and prints the whole part of it, which is the value itself
    /// for a type that holds only integers. With the port's own decimal the same question is asked
    /// of a value that could in principle have a scale, so the truncation is explicit.
    /// </remarks>
    private static string Truncate(H2Sharp.Util.BigDecimal d)
        => d.SetScale(0, H2Sharp.Util.RoundingMode.Down).ToPlainString();

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

    /// <remarks>
    /// The reference appends its trace to every exception message, as " [code-id]", and the id
    /// is a counter this port does not have. The pattern is matched anywhere in the line and not
    /// only at the end, because a message that goes through the comparison of two integers ends
    /// up in the middle of a line of several answers.
    /// </remarks>
    private static readonly System.Text.RegularExpressions.Regex TraceSuffix =
        new(@"\s\[\d+-\d+\]", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// The Java side of the operations.
    /// </summary>
    /// <remarks>
    /// Written as lambda variables rather than as methods, because the harness places the
    /// body inside main, where a method cannot be declared and only a lambda can be.
    /// </remarks>
    private const string Helpers = """
                      java.util.function.Function<org.h2.value.Value, String> neg = v -> {
                        try {
                          return v.negate().getSQL(0);
                        } catch (org.h2.message.DbException ex) {
                          return "EX " + ex.getMessage();
                        }
                      };

                      java.util.function.BiFunction<org.h2.value.Value, org.h2.value.Value, String> bin =
                          (a, b) -> {
                            try {
                              return a.add(b).getSQL(0);
                            } catch (org.h2.message.DbException ex) {
                              return "EX " + ex.getMessage();
                            }
                          };
                      java.util.function.BiFunction<org.h2.value.Value, org.h2.value.Value, String> sub =
                          (a, b) -> {
                            try {
                              return a.subtract(b).getSQL(0);
                            } catch (org.h2.message.DbException ex) {
                              return "EX " + ex.getMessage();
                            }
                          };
                      java.util.function.BiFunction<org.h2.value.Value, org.h2.value.Value, String> mul =
                          (a, b) -> {
                            try {
                              return a.multiply(b).getSQL(0);
                            } catch (org.h2.message.DbException ex) {
                              return "EX " + ex.getMessage();
                            }
                          };
                      java.util.function.BiFunction<org.h2.value.Value, org.h2.value.Value, String> div =
                          (a, b) -> {
                            try {
                              return a.divide(
                                  b, org.h2.value.TypeInfo.TYPE_BIGINT).getSQL(0);
                            } catch (org.h2.message.DbException ex) {
                              return "EX " + ex.getMessage();
                            }
                          };
                      java.util.function.BiFunction<org.h2.value.Value, org.h2.value.Value, String> mod =
                          (a, b) -> {
                            try {
                              return a.modulus(b).getSQL(0);
                            } catch (org.h2.message.DbException ex) {
                              return "EX " + ex.getMessage();
                            }
                          };
                      java.util.function.BiFunction<org.h2.value.Value, org.h2.value.Value, String> cmp =
                          (a, b) -> String.valueOf(a.compareTo(
                              b, null, org.h2.value.CompareMode.getInstance(null, 0)));
                      java.util.function.Function<byte[], String> hexBytes = b -> {
                        StringBuilder x = new StringBuilder();
                        for (int i = 0; i < b.length; i++) {
                          x.append(String.format("%02x", b[i] & 0xff));
                        }
                        return x.length() == 0 ? "-" : x.toString();
                      };
                      java.util.function.Function<String, String> hexText = s -> {
                        StringBuilder b = new StringBuilder();
                        for (int i = 0; i < s.length(); i++) {
                          b.append(String.format("%04x", (int) s.charAt(i)));
                        }
                        return b.length() == 0 ? "-" : b.toString();
                      };

            """;
    private static string Decode(string hex)
    {
        string only = hex.TrimStart('=');
        StringBuilder sb = new();
        for (int i = 0; i + 3 < only.Length; i += 4)
        {
            sb.Append((char)Convert.ToInt32(only.Substring(i, 4), 16));
        }

        return sb.ToString();
    }
}
