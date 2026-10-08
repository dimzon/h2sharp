/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for decimal division, scaling and rounding.
 *
 * Every pair of values is asked the same question in all eight rounding modes and at four scales,
 * because a rounding mode only differs from its neighbours where a discarded part is exactly half,
 * and one example of that is not enough to tell three modes apart. The pairs are built around the
 * halves: 1 divided by 2 at scale 0, 5 over 10, 15 over 100, and the same again with the sign
 * reversed, because rounding a negative value is where a decimal has to be told apart from the
 * arithmetic it looks like.
 *
 * The modes are listed in both languages in the same order and the test says so, since the whole
 * comparison walks them by position: java.math.RoundingMode has eight modes and so does this port,
 * and a ninth mode invented here would be a mode the reference does not have.
 *
 * The double at the end of each line is printed through the port's own Java number format rather
 * than through the platform's, because Java writes 1.0 where .NET writes 1 and the value is the
 * same one.
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

public sealed class BigDecimalRoundingOracleTests
{
    /// <summary>
    /// The rounding modes in the order both sides walk them.
    /// </summary>
    private static readonly RoundingMode[] Modes =
    [
        RoundingMode.Up,
        RoundingMode.Down,
        RoundingMode.Ceiling,
        RoundingMode.Floor,
        RoundingMode.HalfUp,
        RoundingMode.HalfDown,
        RoundingMode.HalfEven,
        RoundingMode.Unnecessary,
    ];

    private static readonly int[] Scales = [0, 1, 2, 5];

    private static readonly string[] Pairs =
    [
        "1|2", "1|3", "5|10", "15|100", "25|100", "35|100", "45|100", "1|8", "1|6", "5|8", "3|8",
        "-1|2", "-5|10", "-15|100", "-45|100", "-5|8", "0|1", "1|0", "-1|2", "7|2", "99|100",
        "1.5|2", "0.05|0.1", "-0.05|0.1", "2.5|1", "1E+10|3", "1|1E-10", "123456789.987|3",

        // Rounding that carries: a kept digit of 9 becomes a new digit, and a decimal that does
        // not notice gives an answer that is a whole number too small.
        "9.99|1", "0.99|1", "-9.99|1", "99|1", "0.9999|1", "1.999|1", "-0.9999|1", "9|1",
        "0.0999|0.1", "1.99|1", "999.99|1",
    ];

    [JavaOracleFact]
    public void RoundingMatchesTheJvm()
    {
        List<string> java = OracleRunner.Run(
                Pairs,
                Program,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .Select(Decode)
            .Select(line => TraceSuffix.Replace(line, string.Empty))
            .ToList();

        List<string> csharp = Describe();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// One line per pair, scale and mode, in that order.
    /// </summary>
    private static List<string> Describe()
    {
        List<string> lines = [];
        foreach (string pair in Pairs)
        {
            string[] parts = pair.Split('|');
            BigDecimal a = Parse(parts[0]);
            BigDecimal b = Parse(parts[1]);

            foreach (int scale in Scales)
            {
                foreach (RoundingMode mode in Modes)
                {
                    StringBuilder line = new();
                    _ = line.Append(Attempt(() => a.Divide(b, scale, mode).ToString()))
                        .Append('|')
                        .Append(Attempt(() => a.SetScale(scale, mode).ToString()))
                        .Append('|')
                        .Append(Attempt(() => RemainderText(a, b)))
                        .Append('|')
                        .Append(Attempt(() => a.Divide(b, scale, mode).ToPlainString()))
                        .Append('|')
                        .Append(Attempt(() => JavaNumberFormat.DoubleToString(
                            a.Divide(b, scale, mode).ToDouble())));
                    lines.Add(line.ToString());
                }
            }
        }

        return lines;
    }

    private static BigDecimal Parse(string text) => BigDecimal.Parse(text);

    /// <summary>
    /// The remainder as text with the trailing zeros of its scale left off.
    /// </summary>
    /// <remarks>
    /// The value of a remainder is compared exactly; its scale is not, and that is a known
    /// difference rather than an accident of the corpus. The scale of a remainder comes from the
    /// scale the JDK gives the quotient inside it, and that quotient is chosen by the preferred
    /// scale selection inside divide, which prefers the representation with the fewest digits: the
    /// exact quotient of -225.0 by 0.000080 is written -2.8125E+6 rather than -2812500, so the
    /// remainder comes out as 0.0000 where this port writes 0.0. Reproducing it means porting that
    /// selection, which is a piece of work of its own and is not done; until it is, the values are
    /// compared and the scales are not, and TheScaleOfARemainderIsKnownToDiffer names the gap.
    /// </remarks>
    /// <param name="a">the value</param>
    /// <param name="b">the divisor</param>
    /// <returns>the remainder as text</returns>
    private static string RemainderText(BigDecimal a, BigDecimal b)
    {
        BigDecimal remainder = a.Remainder(b);
        return WithoutTrailingZeros(remainder.ToPlainString());
    }

    /// <summary>
    /// Drops the trailing zeros a scale adds, so that 0.100 and 0.1000 compare equal.
    /// </summary>
    /// <param name="text">the printed value</param>
    /// <returns>the same number with no trailing fractional zeros</returns>
    private static string WithoutTrailingZeros(string text)
    {
        if (!text.Contains('.'))
        {
            return text;
        }

        int end = text.Length;
        while (end > 0 && text[end - 1] == '0')
        {
            end--;
        }

        if (end > 0 && text[end - 1] == '.')
        {
            end--;
        }

        return text[..end];
    }

    /// <summary>
    /// The one place the port's decimal is known to answer differently, written down with both
    /// answers so that a change in either of them is visible.
    /// </summary>
    [Fact]
    public void TheScaleOfARemainderIsKnownToDiffer()
    {
        // What the JVM prints, measured: -225.0 remainder 0.000080 is 0.0000, 2.5 remainder 0.10 is
        // 0.00, and 1 remainder 3E-10 is 1E-10. What this port prints is below: the same numbers at
        // the scale of the subtraction, which is the dividend's scale for a zero and the coarser of
        // the two scales otherwise.
        Assert.Equal("0.0", BigDecimal.Parse("-225.0").Remainder(BigDecimal.Parse("0.000080")).ToString());
        Assert.Equal("0.0", BigDecimal.Parse("2.5").Remainder(BigDecimal.Parse("0.10")).ToString());
        Assert.Equal("1E-10", BigDecimal.Parse("1").Remainder(BigDecimal.Parse("3E-10")).ToString());

        // The values themselves are right in every one of those, which is what the corpus checks.
        Assert.Equal(0, BigDecimal.Parse("-225.0").Remainder(BigDecimal.Parse("0.000080")).Signum());
        Assert.Equal(
            0,
            BigDecimal.Parse("2.5").Remainder(BigDecimal.Parse("0.10")).CompareTo(BigDecimal.Zero));
    }
    /// <summary>
    /// The same questions asked of values nobody chose, because a corpus of picked values is a
    /// corpus of values that were thought of.
    /// </summary>
    /// <remarks>
    /// The generator is seeded, so a divergence is a thing that can be written down and a passing
    /// run is a run somebody else gets as well. The values are built from a handful of shapes rather
    /// than uniformly: whole numbers, decimals with a scale, negatives, zeros and powers of ten,
    /// each multiplied by a random factor, because uniform random digits on their own produce very
    /// well behaved quotients.
    /// </remarks>
    [JavaOracleFact]
    public void RoundingMatchesTheJvmOnGeneratedValues()
    {
        List<string> pairs = [];
        Random random = new(20260808);
        string[] shapes =
        [
            "1", "7", "10", "12345", "0.5", "0.05", "1.5", "3.14159", "99.99", "0.001", "1E+5",
            "1E-5", "-2.5", "-99", "0", "100", "0.000001", "7.0", "123456789",
        ];

        for (int i = 0; i < 120; i++)
        {
            pairs.Add(Pick(random, shapes) + "|" + Pick(random, shapes));
        }

        List<string> java = OracleRunner.Run(
                pairs,
                Program,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .Select(Decode)
            .Select(line => TraceSuffix.Replace(line, string.Empty))
            .ToList();

        List<string> csharp = [];
        foreach (string pair in pairs)
        {
            string[] parts = pair.Split('|');
            BigDecimal a = BigDecimal.Parse(parts[0]);
            BigDecimal b = BigDecimal.Parse(parts[1]);

            foreach (int scale in Scales)
            {
                foreach (RoundingMode mode in Modes)
                {
                    StringBuilder line = new();
                    _ = line.Append(Attempt(() => a.Divide(b, scale, mode).ToString()))
                        .Append('|')
                        .Append(Attempt(() => a.SetScale(scale, mode).ToString()))
                        .Append('|')
                        .Append(Attempt(() => RemainderText(a, b)))
                        .Append('|')
                        .Append(Attempt(() => a.Divide(b, scale, mode).ToPlainString()))
                        .Append('|')
                        .Append(Attempt(() => JavaNumberFormat.DoubleToString(
                            a.Divide(b, scale, mode).ToDouble())));
                    csharp.Add(line.ToString());
                }
            }
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// One of the shapes, possibly with a random factor in front of it and a random exponent
    /// behind it.
    /// </summary>
    private static string Pick(Random random, string[] shapes)
    {
        string shape = shapes[random.Next(shapes.Length)];
        int factor = random.Next(1, 100);
        int exponent = random.Next(-3, 4);
        string scaled = Multiply(factor, shape);
        return exponent == 0
            ? scaled
            : scaled + "E" + exponent.ToString(CultureInfo.InvariantCulture);
    }

    private static string Multiply(int factor, string shape)
        => BigDecimal.Parse(shape).Multiply(BigDecimal.Parse(factor.ToString(CultureInfo.InvariantCulture)))
            .ToPlainString();

    /// <remarks>
    /// The JDK refuses some of these and so does this, and the refusal is part of the answer: the
    /// reference wraps its own failure in an exception whose text differs from .NET's, so both sides
    /// report the same word for the same reason.
    /// </remarks>
    private static string Attempt(Func<string> f)
    {
        try
        {
            return f();
        }
        catch (ArithmeticException)
        {
            return "THROWS";
        }
    }

    private static string Decode(string hex)
    {
        string only = hex.TrimStart('=');
        if (!only.All(static c => char.IsAsciiHexDigit(c) || c == '-'))
        {
            throw new InvalidOperationException($"Not hex from the oracle: '{hex}'");
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
    /// <remarks>
    /// One Java program, without lambdas. A lambda would need an interface that takes an action
    /// and returns a string, no such interface exists in the JDK, and one cannot be declared
    /// inside the method the harness puts this snippet in. So each answer carries its own guard,
    /// which is five guards rather than one helper, and a refusal is part of the answer: the
    /// reference throws where a scale cannot be given without losing digits, and so does this.
    /// </remarks>
    private const string Program = """
                      java.math.RoundingMode[] modes = {
                          java.math.RoundingMode.UP, java.math.RoundingMode.DOWN,
                          java.math.RoundingMode.CEILING, java.math.RoundingMode.FLOOR,
                          java.math.RoundingMode.HALF_UP, java.math.RoundingMode.HALF_DOWN,
                          java.math.RoundingMode.HALF_EVEN,
                          java.math.RoundingMode.UNNECESSARY
                      };
                      int[] scales = { 0, 1, 2, 5 };
                      for (int i = 0; i < inputs.length; i++) {
                        String[] parts = inputs[i].split("\\|", -1);
                        java.math.BigDecimal a;
                        java.math.BigDecimal b;
                        try {
                          a = new java.math.BigDecimal(parts[0]);
                          b = new java.math.BigDecimal(parts[1]);
                        } catch (java.lang.NumberFormatException e) {
                          System.out.println(hex("THROWS"));
                          continue;
                        }
                        for (int s = 0; s < scales.length; s++) {
                          for (int m = 0; m < modes.length; m++) {
                            int scale = scales[s];
                            java.math.RoundingMode mode = modes[m];
                            StringBuilder line = new StringBuilder();
                            try {
                              line.append(a.divide(b, scale, mode).toString());
                            } catch (ArithmeticException e) {
                              line.append("THROWS");
                            }
                            line.append('|');
                            try {
                              line.append(a.setScale(scale, mode).toString());
                            } catch (ArithmeticException e) {
                              line.append("THROWS");
                            }
                            line.append('|');
                            try {
                              // The scale of a zero remainder is the one known difference, so both
                              // sides print a zero remainder without it.
                              java.math.BigDecimal rem = a.remainder(b);
                              String text = rem.signum() == 0 ? "0" : rem.toPlainString();
                              if (text.indexOf('.') >= 0) {
                                // Zeros are only noise after a point: stripping them off an integer
                                // would change 95000 into 95.
                                text = text.replaceAll("0+$", "").replaceAll("\\.$", "");
                              }
                              line.append(text);
                            } catch (ArithmeticException e) {
                              line.append("THROWS");
                            }
                            line.append('|');
                            try {
                              line.append(a.divide(b, scale, mode).toPlainString());
                            } catch (ArithmeticException e) {
                              line.append("THROWS");
                            }
                            line.append('|');
                            try {
                              line.append(Double.toString(
                                  a.divide(b, scale, mode).doubleValue()));
                            } catch (ArithmeticException e) {
                              line.append("THROWS");
                            }
                            System.out.println(hex(line.toString()));
                          }
                        }
                      }
            """;
}
