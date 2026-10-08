/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the decimal.
 *
 * The corpus is built for the two things that are easy to get wrong and impossible to guess: what
 * the JDK accepts as a decimal, and how it prints one back. Both were measured on the JVM rather
 * than read off a specification, because the printing rules in particular are a set of thresholds
 * that a paraphrase gets wrong in a way no test of ordinary values would notice.
 *
 * So the corpus carries the boundaries. 0.000001 prints as itself and 0.0000001 as 1E-7: the step
 * between them is one digit, and it is where the rule turns from plain to scientific. A negative
 * scale prints in scientific notation however small it is, because the zeros it stands for are not
 * in the unscaled value. A scale larger than the number of digits fills in the gap with zeros.
 * And the inputs that are not decimals at all are here too, since the tokenizer has to refuse
 * exactly what the JDK refuses, and .NET would have been happy with a good many of them.
 *
 * The corpus is handed to the reference as input rather than written out again in the snippet.
 * Two lists of the same values in two languages is a list that drifts, and it did drift: the
 * first version of this file had three values the snippet was missing and failed on the count
 * before it failed on anything worth failing on.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class BigDecimalOracleTests
{
    /// <summary>
    /// Values chosen to sit on the boundaries of the printed form.
    /// </summary>
    private static readonly string[] Values =
    [
        "0", "-0", "0.0", "-0.0", "0.000", "1", "-1", "1.0", "1.00", "-1.00", "10", "100",
        "0.1", "0.01", "0.001", "0.0001", "0.00001", "0.000001", "0.0000001", "0.00000001",
        "0.000000001", "1E-7", "1E-6", "1E-5", "1.5E-7", "1.5E-6", "1.5E-5", "9.999999E-7",
        "9.9999999E-7", "9999999E-13", "12345678901234567890", "123456789012345678901234567890",
        "1234567890", "1234567", "12345678", "1E+7", "1E+8", "1E+15", "1E+16", "1.0E+7",
        "1.00E+7", "1234567890E+1", "12345678901234567890E+10", "0E+10", "0E-10", "0.0E+10",
        "1E-2147483646", "2147483647", "-2147483648", "0.1E+1", "1E", "E1", "1.0E", "--1", "1-",
        "1.2.3", "1,2", " 1", "1 ", "1_000", "NaN", "Infinity", "-Infinity", "0x10", "1d", "1f",
        "", "abc", "+", "-", ".", "-.", "+1", "007", "1e", "1e+", "1e-", "1.2e3", "1.2E-3",
        "0.00000012345", "1234567890123456789012345678901234567890", "1.7976931348623157E+308",
        "4.9E-324", "1.4E-45", "0.1000000000000000000000000001",
        "-9999999999999999999999999999999999999999", "1E-100", "1E+100",
    ];

    /// <summary>
    /// Values that must be refused.
    /// </summary>
    /// <remarks>
    /// This list is compared with the JDK and not asserted here. It was once a plain list of
    /// assertions about this port, and it held "1." on the strength of a comment in the parser that
    /// said the JDK refused it; the JDK does not, and the tokenizer oracle found the difference on
    /// <c>SELECT 1.</c>. A list of what must be refused is a claim about the reference, so it is
    /// checked against the reference: the two sides are asked the same question and the answers
    /// have to be the same.
    /// </remarks>
    private static readonly string[] Rejected =
    [
        "", " ", "abc", "1.2.3", "1,2", " 1", "1 ", "NaN", "Infinity", "-Infinity", "0x10",
        "1e", "1e+", "1e-", "E1", "E", "1E1e1", "--1", "+-1", "1-", ".", "-.", "..",
        "1d", "1f", "1D", "1F", "1_0",
    ];

    /// <summary>
    /// Values that look refused and are not, because the grammar allows a point with nothing after
    /// it and a point with nothing before it.
    /// </summary>
    private static readonly string[] Accepted =
    [
        "1.", "+1.", "-1.", ".5", "-.5", "+.5", "1.e5", ".5e2", "1.5E+3", ".0", "0.",

        // The digits are not the ASCII ten. The JDK reads a decimal with Character.digit, which
        // is every character of the category DECIMAL_DIGIT_NUMBER, so this is one hundred and
        // twenty three and not a piece of text to refuse. It was on the refused list until the
        // differential version of that list asked the JDK about it.
        "١٢٣", "١٢٣.٤٥", "٣e١",
    ];

    [JavaOracleFact]
    public void PrintedFormMatchesTheJvm()
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
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// The JDK refuses what it refuses, and .NET's parser would not have refused most of it.
    /// </summary>
    [JavaOracleFact]
    public void TheJdkAndThisPortRefuseTheSameText()
    {
        // Both lists at once, so that a value which moved from one to the other cannot hide: the
        // JDK is asked about every one of them and the answer has to be what this port does.
        string[] all = [.. Rejected, .. Accepted];

        List<string> java = OracleRunner.Run(
                all,
                """
                for (int i = 0; i < inputs.length; i++) {
                  try {
                    java.math.BigDecimal b = new java.math.BigDecimal(inputs[i]);
                    System.out.println("OK " + b.toString());
                  } catch (NumberFormatException e) {
                    System.out.println("REFUSED");
                  }
                }
                """,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(line => line.Trim())
            .ToList();

        Assert.Equal(all.Length, java.Count);
        for (int i = 0; i < all.Length; i++)
        {
            // The harness prefixes every line with '=' so that an empty line is a visible
            // difference; the prefix is not part of the answer.
            string answer = java[i].StartsWith('=') ? java[i][1..] : java[i];
            bool parsed = BigDecimal.TryParse(all[i], out BigDecimal? result, out _);
            if (answer.StartsWith("OK ", StringComparison.Ordinal))
            {
                Assert.True(parsed, $"the JDK reads '{all[i]}' as {answer[3..]} and this port refuses it");
                Assert.Equal(answer[3..], result!.ToString());
            }
            else
            {
                Assert.False(parsed, $"this port reads '{all[i]}' and the JDK says '{answer}'");
                Assert.Null(result);
            }
        }
    }

    /// <summary>
    /// A refusal is a refusal with an exception, because that is what every caller of the parse
    /// catches, and a parse that returns null instead would move the exception into every caller.
    /// </summary>
    [Fact]
    public void RefusedTextIsRefusedHereToo()
    {
        foreach (string text in Rejected)
        {
            Assert.False(BigDecimal.TryParse(text, out BigDecimal? result, out _), $"accepted '{text}'");
            Assert.Null(result);
            Assert.Throws<FormatException>(() => BigDecimal.Parse(text));
        }

        foreach (string text in Accepted)
        {
            Assert.True(BigDecimal.TryParse(text, out BigDecimal? result, out _), $"refused '{text}'");
            Assert.NotNull(result);
        }
    }

    /// <summary>
    /// A scale makes no difference to the order of two values and all the difference to their
    /// equality, which is the JDK's rule and the reason a NUMERIC column can hold 1.0 and 1.00 as
    /// one number while the values themselves stay apart.
    /// </summary>
    [Fact]
    public void OrderIgnoresTheScaleAndEqualityDoesNot()
    {
        BigDecimal onePointZero = BigDecimal.Parse("1.0");
        BigDecimal oneHundredth = BigDecimal.Parse("1.00");

        Assert.Equal(0, onePointZero.CompareTo(oneHundredth));
        Assert.False(onePointZero.Equals(oneHundredth));
        Assert.NotEqual(0, onePointZero.GetHashCode());

        Assert.Equal(-1, BigDecimal.Parse("1").CompareTo(BigDecimal.Parse("1.0001")));
        Assert.Equal(1, BigDecimal.Parse("9").CompareTo(BigDecimal.Parse("-9")));
        Assert.Equal(0, BigDecimal.Parse("0.0").CompareTo(BigDecimal.Parse("-0.00")));

        // Scales far apart still compare exactly, which is what the unscaled values are for.
        Assert.Equal(-1, BigDecimal.Parse("1E-100").CompareTo(BigDecimal.Parse("1")));
        Assert.Equal(-1, BigDecimal.Parse("1").CompareTo(BigDecimal.Parse("1E+100")));
    }

    [Fact]
    public void PrecisionIsTheWidthOfTheNumber()
    {
        Assert.Equal(1, BigDecimal.Parse("0").Precision());
        Assert.Equal(1, BigDecimal.Parse("0.000").Precision());
        Assert.Equal(2, BigDecimal.Parse("0.0012").Precision());
        Assert.Equal(2, BigDecimal.Parse("12").Precision());
        Assert.Equal(1, BigDecimal.Parse("0.000000000001").Precision());
        Assert.Equal(40, BigDecimal.Parse("1234567890123456789012345678901234567890").Precision());

        Assert.Equal(0, BigDecimal.Parse("1").Scale);
        Assert.Equal(2, BigDecimal.Parse("1.00").Scale);
        Assert.Equal(-7, BigDecimal.Parse("1E+7").Scale);
        Assert.Equal(3, BigDecimal.Parse("0.001").Scale);
    }

    [Fact]
    public void StrippingZerosCanLeaveANegativeScale()
    {
        Assert.Equal("1E+7", BigDecimal.Parse("10000000").StripTrailingZeros().ToString());
        Assert.Equal("1", BigDecimal.Parse("1.000").StripTrailingZeros().ToString());
        Assert.Equal("0", BigDecimal.Parse("0.000").StripTrailingZeros().ToString());
        Assert.Equal("-1.5", BigDecimal.Parse("-1.500").StripTrailingZeros().ToString());
    }

    private static string Describe(string text)
    {
        // The reference says only whether it refused, and so does this: where the JDK would have
        // blamed the character is not something the reference can report, so it is in the
        // exception message and not in the comparison.
        if (!BigDecimal.TryParse(text, out BigDecimal? v, out _))
        {
            return "REJECT";
        }

        BigDecimal stripped = v!.StripTrailingZeros();
        return new StringBuilder(v.ToString()).Append('|')
            .Append(stripped.ToString()).Append('|')
            .Append(v.Precision().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(v.Scale.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(v.Signum().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(stripped.Scale.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(J(v.Equals(v)))
            .ToString();
    }

    /// <remarks>
    /// Java concatenates a boolean as "true" and .NET as "True", so every boolean in this file
    /// goes through here rather than through an append.
    /// </remarks>
    private static string J(bool value) => value ? "true" : "false";

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

    private static readonly System.Text.RegularExpressions.Regex TraceSuffix =
        new(@"\s\[\d+-\d+\]", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <remarks>
    /// One Java program, without lambdas: a snippet that carries its own functions is a snippet
    /// whose types have to be inferred, and javac answers an inference failure as a cascade of
    /// errors about symbols that are perfectly fine.
    /// </remarks>
    private const string Program = """
                      for (int i = 0; i < inputs.length; i++) {
                        String s = inputs[i];
                        try {
                          java.math.BigDecimal v = new java.math.BigDecimal(s);
                          java.math.BigDecimal stripped = v.stripTrailingZeros();
                          StringBuilder b = new StringBuilder();
                          b.append(v.toString()).append('|');
                          b.append(stripped.toString()).append('|');
                          b.append(v.precision()).append('|');
                          b.append(v.scale()).append('|');
                          b.append(v.signum()).append('|');
                          b.append(stripped.scale()).append('|');
                          b.append(v.equals(v));
                          System.out.println(hex(b.toString()));
                        } catch (java.lang.NumberFormatException e) {
                          System.out.println(hex("REJECT"));
                        }
                      }
            """;
}
