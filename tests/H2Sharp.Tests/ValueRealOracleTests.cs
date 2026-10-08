/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for REAL.
 *
 * A single precision float is the one type here whose hash is a reproducible number, because the
 * reference takes the raw bit pattern and .NET hands over the same bits, so this corpus compares
 * it where every other type deliberately does not.
 *
 * The corpus is built around where a float and Java's answer about it part company: the three
 * values that are not numbers, which are written as quoted words rather than as literals, the two
 * zeroes, which the factory makes one value of, and the ends of the range, where the shortest
 * decimal that reads back is not the decimal anybody would have written.
 *
 * The subnormals are not in it, for the same reason they are not in the formatter's corpus: below
 * the smallest normal float the reference prints two significant digits where one reads back
 * (1.4E-45), and reproducing that means porting a dtoa from the JDK. The boundary itself is
 * compared, and JavaNumberFormatOracleTests guards the exclusion so that it cannot widen without
 * saying so.
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

public sealed class ValueRealOracleTests
{
    private static readonly string[] Values =
    [
        "0", "-0.0", "1", "-1", "0.5", "-0.5", "1.5", "3.14159", "-3.14159", "1e7", "1e-7",
        "1e20", "1e20", "1e-20", "3.4028235e38", "3.5e38", "1.17549435e-38",
        "1e-38", "0.1", "0.2", "0.3", "1234567", "12345678", "16777216", "16777217", "16777219",
        "1e10", "1e-10", "6.02214076e23", "1.602176634e-19", "0.7071067811865476",
        "Infinity", "-Infinity", "NaN", "2", "4", "8", "1e6", "0.001",
    ];

    [JavaOracleFact]
    public void Real_MatchesReference()
    {
        List<string> java = OracleRunner.Run(
                Values,
                Program,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .Select(Decode)
            .Select(line => TraceSuffix.Replace(line, string.Empty))
            .ToList();

        List<string> csharp = Values.Select(Describe).ToList();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i} ({Values[i]}):\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// One and both zeroes are the reference's own instances, a negative zero becomes a positive
    /// one, and a NaN is one value however many bit patterns there are.
    /// </summary>
    [Fact]
    public void OneBothZeroesAndNaNAreShared()
    {
        Assert.Same(ValueReal.One, ValueReal.Get(1f));
        Assert.Same(ValueReal.Zero, ValueReal.Get(0f));
        Assert.Same(ValueReal.Zero, ValueReal.Get(-0.0f));

        Value nan = ValueReal.Get(float.NaN);
        Assert.Same(nan, ValueReal.Get(float.NaN));

        // The hash is the raw bit pattern, which is a number and not an identity hash.
        Assert.Equal(BitConverter.SingleToInt32Bits(1.5f), ValueReal.Get(1.5f).GetHashCode());
        Assert.Equal(0, ValueReal.Zero.GetHashCode());

        // Equality is the comparison, so a NaN equals a NaN and the two zeroes would too if both
        // could still be created.
        Assert.True(nan.Equals(ValueReal.Get(float.NaN)));
        Assert.False(ValueReal.Get(1.5f).Equals(ValueReal.Get(1.5000001f)));
        Assert.False(ValueReal.One.Equals(ValueReal.Get(2f)));
    }

    private static string Describe(string text)
    {
        Value v = ValueReal.Get(float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture));
        return new StringBuilder(Sql(v, 0)).Append('|')
            .Append(Sql(v, HasSql.NoCasts)).Append('|')
            .Append(HexText(v.GetString())).Append('|')
            .Append(v.GetTypeInfo().GetSql(0)).Append('|')
            .Append(v.GetValueType().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(v.GetSignum().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(v.GetHashCode().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(JavaNumberFormat.FloatToString(v.GetFloat())).Append('|')
            .Append(JavaNumberFormat.DoubleToString(v.GetDouble())).Append('|')
            .Append(Get(() => v.GetBigDecimal().ToString())).Append('|')
            .Append(J(v.Equals(v))).Append('|')
            .Append(J(v.Equals(ValueReal.Get(1.5f)))).Append('|')
            .Append(Compare(v, 1.5f)).Append('|')
            .Append(Compare(v, float.NaN)).Append('|')
            .Append(v.Add(ValueReal.Get(2f)).GetString()).Append('|')
            .Append(v.Subtract(ValueReal.Get(2f)).GetString()).Append('|')
            .Append(v.Multiply(ValueReal.Get(2f)).GetString()).Append('|')
            .Append(Divide(v, 2f)).Append('|')
            .Append(Modulus(v, 2f))
            .ToString();
    }

    private static string Sql(Value v, int flags) => v.GetSql(new StringBuilder(), flags).ToString();

    private static string Divide(Value v, float divisor)
        => v.Divide(ValueReal.Get(divisor), TypeInfo.TypeReal).GetString() ?? string.Empty;

    private static string Modulus(Value v, float divisor)
        => v.Modulus(ValueReal.Get(divisor)).GetString() ?? string.Empty;

    private static string Compare(Value v, float other)
        => v.CompareTo(ValueReal.Get(other), null, CompareMode.GetInstance(null, 0))
            .ToString(CultureInfo.InvariantCulture);

    private static string Get(Func<string> f)
    {
        try
        {
            return f();
        }
        catch (H2SharpException e)
        {
            return "EX " + e.Message;
        }
    }

    private static string J(bool value) => value ? "true" : "false";

    private static string HexText(string? s)
    {
        if (s is null || s.Length == 0)
        {
            return "-";
        }

        StringBuilder b = new();
        foreach (char c in s)
        {
            _ = b.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
        }

        return b.ToString();
    }

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

        StringBuilder b = new();
        for (int i = 0; i + 3 < only.Length; i += 4)
        {
            _ = b.Append((char)Convert.ToInt32(only.Substring(i, 4), 16));
        }

        return b.ToString();
    }

    private static readonly System.Text.RegularExpressions.Regex TraceSuffix =
        new(@"\s\[\d+-\d+\]", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <remarks>
    /// One Java program, without lambdas: a snippet that carries its own functions is a snippet
    /// whose types have to be inferred, and javac answers an inference failure as a cascade of
    /// errors about symbols that are perfectly fine.
    /// </remarks>
    private const string Program = """
                      org.h2.value.CompareMode mode = org.h2.value.CompareMode.getInstance(null, 0);
                      for (int i = 0; i < inputs.length; i++) {
                        org.h2.value.ValueReal v = org.h2.value.ValueReal.get(Float.parseFloat(inputs[i]));
                        org.h2.value.ValueReal half = org.h2.value.ValueReal.get(1.5f);
                        StringBuilder b = new StringBuilder();
                        b.append(v.getSQL(new StringBuilder(), 0)).append('|');
                        b.append(v.getSQL(new StringBuilder(), org.h2.util.HasSQL.NO_CASTS)).append('|');
                        String text = v.getString();
                        if (text.length() == 0) {
                          b.append('-');
                        } else {
                          for (int j = 0; j < text.length(); j++) {
                            b.append(String.format("%04x", (int) text.charAt(j)));
                          }
                        }
                        b.append('|');
                        b.append(v.getType().getSQL(0)).append('|');
                        b.append(v.getValueType()).append('|');
                        b.append(v.getSignum()).append('|');
                        b.append(v.hashCode()).append('|');
                        b.append(Float.toString(v.getFloat())).append('|');
                        b.append(Double.toString(v.getDouble())).append('|');
                        try {
                          b.append(v.getBigDecimal().toString());
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage());
                        }
                        b.append('|');
                        b.append(v.equals(v)).append('|');
                        b.append(v.equals(half)).append('|');
                        b.append(v.compareTo(half, null, mode)).append('|');
                        b.append(v.compareTo(org.h2.value.ValueReal.get(Float.NaN), null, mode)).append('|');
                        b.append(v.add(org.h2.value.ValueReal.get(2f)).getString()).append('|');
                        b.append(v.subtract(org.h2.value.ValueReal.get(2f)).getString()).append('|');
                        b.append(v.multiply(org.h2.value.ValueReal.get(2f)).getString()).append('|');
                        try {
                          b.append(v.divide(org.h2.value.ValueReal.get(2f),
                              org.h2.value.TypeInfo.TYPE_REAL).getString());
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage());
                        }
                        b.append('|');
                        b.append(v.modulus(org.h2.value.ValueReal.get(2f)).getString());
                        System.out.println(hex(b.toString()));
                      }
            """;
}
