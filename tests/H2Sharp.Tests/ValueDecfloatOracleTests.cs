/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for DECFLOAT.
 *
 * A DECFLOAT is the only value in the port that is not a number: the reference keeps three
 * singletons for the infinities and the NaN, tells them apart by identity, and stores nothing at
 * all for them. Every operation below therefore has a branch that is about identity rather than
 * about a decimal, and those branches are what the corpus is built to reach: a finite value against
 * each of the three, each of the three against a finite value, and the three against each other.
 *
 * The corpus is also built around the scale a division is given, because the reference computes
 * that scale from the precision and the scale of both operands rather than asking for one, and
 * two quotient types are compared so that the arithmetic of the scale is visible and not only the
 * answer it produced once.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ValueDecfloatOracleTests
{
    private static readonly string[] Values =
    [
        "0", "1", "-1", "1.5", "0.1", "0.5", "-0.5", "100", "1E+34", "1E-34", "0.0000001",
        "1234567890123456789012345678901234", "9999999999999999999999999999999999",
        "0.0000000000000000000000000000000001", "-0", "2.5", "0.0000000001",
        "Infinity", "-Infinity", "NaN",
    ];

    [JavaOracleFact]
    public void Decfloat_MatchesReference()
    {
        List<string> java = OracleRunner.Run(
                Values,
                Program,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .Select(Decode)
            .ToList();

        List<string> csharp = Values.Select(Describe).ToList();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(
                java[i] == csharp[i],
                $"line {i} ({Values[i]}):\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// The three values that are not numbers are the three singletons, so two of them are equal
    /// only when they are the same object, and a hash of one is the hash of the object rather than
    /// of anything about it.
    /// </summary>
    [Fact]
    public void TheValuesThatAreNotNumbersAreTheSameObjectOrTheyAreNotEqual()
    {
        Assert.False(ValueDecfloat.PositiveInfinity.Equals(ValueDecfloat.NegativeInfinity));
        Assert.False(ValueDecfloat.PositiveInfinity.Equals(ValueDecfloat.NotANumber));
        Assert.True(ValueDecfloat.NotANumber.Equals(ValueDecfloat.NotANumber));
        Assert.True(ValueDecfloat.PositiveInfinity.Equals(ValueDecfloat.PositiveInfinity));

        // A finite value is never equal to one that is not a number, not even zero.
        Assert.False(ValueDecfloat.Zero.Equals(ValueDecfloat.NotANumber));
        Assert.True(ValueDecfloat.Zero.Equals(ValueDecfloat.Get(BigDecimal.Parse("0.000"))));

        Assert.False(ValueDecfloat.PositiveInfinity.IsFinite());
        Assert.False(ValueDecfloat.NotANumber.IsFinite());
        Assert.True(ValueDecfloat.Get(BigDecimal.Parse("1.5")).IsFinite());

        // A value that is not a number has no decimal to give, and the error says which
        // conversion it was.
        H2SharpException error = Assert.Throws<H2SharpException>(
            () => ValueDecfloat.PositiveInfinity.GetBigDecimal());
        Assert.Contains("DECFLOAT", error.Message, StringComparison.Ordinal);

        // Its type is one digit wide, which is the width of the word that stands for it.
        Assert.Equal(1, ValueDecfloat.NotANumber.GetTypeInfo().GetPrecision());
        Assert.Equal(0, ValueDecfloat.NotANumber.GetTypeInfo().GetScale());

        // The type of a DECFLOAT always declares a scale of zero, whatever the value's own scale
        // is: the reference writes the zero there rather than the scale, so 1E+34 is DECFLOAT(1)
        // and prints as 1E+34. A NUMERIC reports its scale, and the two types must not be read
        // off each other.
        Value wide = ValueDecfloat.Get(BigDecimal.Parse("1E+34"));
        Assert.Equal(1, wide.GetTypeInfo().GetPrecision());
        Assert.Equal(0, wide.GetTypeInfo().GetScale());
        Assert.Equal("1E+34", wide.GetString());
        Assert.Equal(5, ValueDecfloat.Get(BigDecimal.Parse("1234.5")).GetTypeInfo().GetPrecision());
        Assert.Equal(0, ValueDecfloat.Get(BigDecimal.Parse("1234.5")).GetTypeInfo().GetScale());
        Assert.Equal(
            "1234567890123456789012345678901234",
            ValueDecfloat.Get(BigDecimal.Parse("1234567890123456789012345678901234")).GetString());
        Assert.Equal(
            34,
            ValueDecfloat.Get(BigDecimal.Parse("1234567890123456789012345678901234"))
                .GetTypeInfo().GetPrecision());
    }

    /// <summary>
    /// Zero kept for the value zero, one kept for the value one, and trailing zeros left off, so
    /// that two spellings of one number are one value.
    /// </summary>
    [Fact]
    public void ZeroAndOneAreKeptAndTrailingZerosAreLeftOff()
    {
        Assert.Same(ValueDecfloat.Zero, ValueDecfloat.Get(BigDecimal.Zero));
        Assert.Same(ValueDecfloat.Zero, ValueDecfloat.Get(BigDecimal.Parse("0.000")));
        Assert.Same(ValueDecfloat.One, ValueDecfloat.Get(BigDecimal.One));
        Assert.Same(ValueDecfloat.One, ValueDecfloat.Get(BigDecimal.Parse("1.00")));
        Assert.Equal(
            "1.5",
            ValueDecfloat.Get(BigDecimal.Parse("1.500")).GetString());
        Assert.Equal(
            ValueDecfloat.Get(BigDecimal.Parse("1.5")),
            ValueDecfloat.Get(BigDecimal.Parse("1.5000")));
    }

    /// <summary>
    /// A division by zero is refused with the error the reference gives, which names the value
    /// that was divided.
    /// </summary>
    [Fact]
    public void DivisionByZeroIsRefused()
    {
        TypeInfo type = new(Value.Decfloat, 34, 0, null);

        H2SharpException divide = Assert.Throws<H2SharpException>(
            () => ValueDecfloat.One.Divide(ValueDecfloat.Zero, type));
        Assert.Equal((int)ErrorCode.DivisionByZero1, divide.H2ErrorCode);

        // The same for a modulus, and the message names the value that was divided rather than
        // the one that was divided by.
        H2SharpException modulus = Assert.Throws<H2SharpException>(
            () => ValueDecfloat.One.Modulus(ValueDecfloat.Zero));
        Assert.Equal((int)ErrorCode.DivisionByZero1, modulus.H2ErrorCode);
        Assert.Contains("1", divide.Message, StringComparison.Ordinal);
    }

    private static string Describe(string text)
    {
        Value v;
        try
        {
            v = Make(text);
        }
        catch (H2SharpException)
        {
            return "REJECT";
        }

        Value onePointFive = ValueDecfloat.Get(BigDecimal.Parse("1.5"));
        Value zero = ValueDecfloat.Zero;
        Value infinity = ValueDecfloat.PositiveInfinity;
        Value minusInfinity = ValueDecfloat.NegativeInfinity;
        Value nan = ValueDecfloat.NotANumber;

        return v.GetSql(new StringBuilder(), 0).Append('|')
            .Append(v.GetSql(new StringBuilder(), HasSql.NoCasts)).Append('|')
            .Append(HexText(v.GetString())).Append('|')
            .Append(v.GetTypeInfo().GetSql(0)).Append('|')
            .Append(Num(v.GetTypeInfo().GetPrecision())).Append('|')
            .Append(Num(v.GetTypeInfo().GetScale())).Append('|')
            .Append(Num(v.GetMemory())).Append('|')
            .Append(Num(v.GetValueType())).Append('|')
            .Append(Num(v.GetSignum())).Append('|')
            .Append(J(((ValueDecfloat)v).IsFinite())).Append('|')
            .Append(J(v.Equals(v))).Append('|')
            .Append(J(v.Equals(onePointFive))).Append('|')
            .Append(J(v.Equals(zero))).Append('|')
            .Append(J(v.Equals(infinity))).Append('|')
            .Append(J(v.Equals(nan))).Append('|')
            .Append(Decimal(v)).Append('|')
            .Append(Num(v.CompareTo(onePointFive, null, CompareMode.GetInstance(null, 0)))).Append('|')
            .Append(Num(v.CompareTo(zero, null, CompareMode.GetInstance(null, 0)))).Append('|')
            .Append(Num(v.CompareTo(infinity, null, CompareMode.GetInstance(null, 0)))).Append('|')
            .Append(Num(v.CompareTo(minusInfinity, null, CompareMode.GetInstance(null, 0)))).Append('|')
            .Append(Num(v.CompareTo(nan, null, CompareMode.GetInstance(null, 0)))).Append('|')
            .Append(Num(v.Add(onePointFive).CompareTo(zero, null, CompareMode.GetInstance(null, 0)))).Append('|')
            .Append(Num(v.Add(infinity).CompareTo(infinity, null, CompareMode.GetInstance(null, 0)))).Append('|')
            .Append(Num(v.Subtract(infinity).CompareTo(zero, null, CompareMode.GetInstance(null, 0)))).Append('|')
            .Append(Num(v.Multiply(minusInfinity).CompareTo(zero, null, CompareMode.GetInstance(null, 0)))).Append('|')
            .Append(Num(v.Multiply(nan).CompareTo(nan, null, CompareMode.GetInstance(null, 0)))).Append('|')
            .Append(Num(v.Divide(onePointFive, new TypeInfo(Value.Decfloat, 34, 0, null))
                .CompareTo(zero, null, CompareMode.GetInstance(null, 0)))).Append('|')
            .Append(HexText(v.Divide(onePointFive, new TypeInfo(Value.Decfloat, 34, 0, null)).GetString()))
            .Append('|')
            .Append(HexText(v.Divide(onePointFive, new TypeInfo(Value.Decfloat, 10, 2, null)).GetString()))
            .Append('|')
            .Append(HexText(v.Modulus(onePointFive).GetString())).Append('|')
            .Append(Float(v.GetFloat())).Append('|')
            .Append(Double(v.GetDouble()))
            .ToString();
    }

    /// <summary>
    /// The value a word stands for: a decimal, or one of the three that are not numbers.
    /// </summary>
    private static Value Make(string text) => text switch
    {
        "Infinity" => ValueDecfloat.PositiveInfinity,
        "-Infinity" => ValueDecfloat.NegativeInfinity,
        "NaN" => ValueDecfloat.NotANumber,
        _ => ValueDecfloat.Get(BigDecimal.Parse(text)),
    };

    /// <summary>
    /// The decimal of a value, or a marker where there is none: the reference throws, and a test
    /// that only compared answers would lose the throw.
    /// </summary>
    private static string Decimal(Value v)
    {
        try
        {
            return v.GetBigDecimal().ToString();
        }
        catch (H2SharpException)
        {
            return "none";
        }
    }

    private static string Num(long n) => n.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The bits of a float, so that the NaN of the two infinities' arithmetic is the NaN Java
    /// would answer with rather than any NaN.
    /// </summary>
    private static string Float(float f)
        => BitConverter.SingleToInt32Bits(f).ToString("x8", CultureInfo.InvariantCulture);

    private static string Double(double d)
        => BitConverter.DoubleToInt64Bits(d).ToString("x16", CultureInfo.InvariantCulture);

    private static string J(bool value) => value ? "true" : "false";

    /// <summary>
    /// The text of a value, with an empty string written as a dash so that the field is there.
    /// The whole line is hex on the way back from the reference, so the text needs no encoding of
    /// its own and a reader of a failure sees the words rather than four digits per letter.
    /// </summary>
    private static string HexText(string? s)
        => s is null || s.Length == 0 ? "-" : s;

    private static string Decode(string hex)
    {
        string only = hex.TrimStart('=');
        if (!only.All(static c => char.IsAsciiHexDigit(c) || c == '-' || c == ' '))
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

    /// <remarks>
    /// One Java program, without lambdas, because a snippet that carries its own functions is a
    /// snippet whose types have to be inferred and javac answers an inference failure as a cascade
    /// of errors about symbols that are perfectly fine.
    /// </remarks>
    private const string Program = """
              org.h2.value.CompareMode mode = org.h2.value.CompareMode.getInstance(null, 0);
              java.util.function.Function<String, String> shown = s -> {
                if (s == null || s.length() == 0) {
                  return "-";
                }
                return s;
              };
              org.h2.value.ValueDecfloat onePointFive = org.h2.value.ValueDecfloat.get(
                  new java.math.BigDecimal("1.5"));
              org.h2.value.ValueDecfloat zero = org.h2.value.ValueDecfloat.ZERO;
              org.h2.value.ValueDecfloat infinity = org.h2.value.ValueDecfloat.POSITIVE_INFINITY;
              org.h2.value.ValueDecfloat minusInfinity = org.h2.value.ValueDecfloat.NEGATIVE_INFINITY;
              org.h2.value.ValueDecfloat nan = org.h2.value.ValueDecfloat.NAN;
              org.h2.value.TypeInfo wide = org.h2.value.TypeInfo.getTypeInfo(
                  org.h2.value.Value.DECFLOAT, 34, 0, null);
              org.h2.value.TypeInfo narrow = org.h2.value.TypeInfo.getTypeInfo(
                  org.h2.value.Value.DECFLOAT, 10, 2, null);
              for (int i = 0; i < inputs.length; i++) {
                String text = inputs[i];
                org.h2.value.ValueDecfloat v;
                if (text.equals("Infinity")) {
                  v = infinity;
                } else if (text.equals("-Infinity")) {
                  v = minusInfinity;
                } else if (text.equals("NaN")) {
                  v = nan;
                } else {
                  try {
                    v = org.h2.value.ValueDecfloat.get(new java.math.BigDecimal(text));
                  } catch (org.h2.message.DbException e) {
                    System.out.println(hex("REJECT"));
                    continue;
                  }
                }
                StringBuilder b = new StringBuilder();
                b.append(v.getSQL(new StringBuilder(), 0)).append('|');
                b.append(v.getSQL(new StringBuilder(), org.h2.util.HasSQL.NO_CASTS)).append('|');
                b.append(shown.apply(v.getString()));
                b.append('|');
                b.append(v.getType().getSQL(0)).append('|');
                b.append(v.getType().getPrecision()).append('|');
                b.append(v.getType().getScale()).append('|');
                b.append(v.getMemory()).append('|');
                b.append(v.getValueType()).append('|');
                b.append(v.getSignum()).append('|');
                b.append(v.isFinite()).append('|');
                b.append(v.equals(v)).append('|');
                b.append(v.equals(onePointFive)).append('|');
                b.append(v.equals(zero)).append('|');
                b.append(v.equals(infinity)).append('|');
                b.append(v.equals(nan)).append('|');
                try {
                  b.append(v.getBigDecimal().toString());
                } catch (RuntimeException e) {
                  b.append("none");
                }
                b.append('|');
                b.append(v.compareTo(onePointFive, null, mode)).append('|');
                b.append(v.compareTo(zero, null, mode)).append('|');
                b.append(v.compareTo(infinity, null, mode)).append('|');
                b.append(v.compareTo(minusInfinity, null, mode)).append('|');
                b.append(v.compareTo(nan, null, mode)).append('|');
                b.append(v.add(onePointFive).compareTo(zero, null, mode)).append('|');
                b.append(v.add(infinity).compareTo(infinity, null, mode)).append('|');
                b.append(v.subtract(infinity).compareTo(zero, null, mode)).append('|');
                b.append(v.multiply(minusInfinity).compareTo(zero, null, mode)).append('|');
                b.append(v.multiply(nan).compareTo(nan, null, mode)).append('|');
                b.append(v.divide(onePointFive, wide).compareTo(zero, null, mode)).append('|');
                b.append(shown.apply(v.divide(onePointFive, wide).getString())).append('|');
                b.append(shown.apply(v.divide(onePointFive, narrow).getString())).append('|');
                b.append(shown.apply(v.modulus(onePointFive).getString())).append('|');
                b.append(String.format("%08x", java.lang.Float.floatToIntBits(v.getFloat()))).append('|');
                b.append(String.format("%016x", java.lang.Double.doubleToLongBits(v.getDouble())));
                System.out.println(hex(b.toString()));
              }
        """;
}
