/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for decimal arithmetic.
 *
 * The values are handed to the reference as input, two per line, and every operation is asked for
 * in one pass so that a divergence names the pair that caused it. The corpus is built around the
 * places where exact decimal arithmetic stops being obvious: equal scales, differing scales, a sum
 * that cancels out and therefore has to decide what scale a zero keeps, zero on one side with a
 * scale difference, values that do not fit in a machine word on either side, and scales that are
 * negative because the value has zeros the unscaled value does not have.
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

[Collection(OracleCollection.Name)]

public sealed class BigDecimalArithmeticOracleTests
{
    private static readonly string[] Pairs =
    [
        "1|1", "1.0|1.00", "1.5|1.50", "1.5|-1.50", "1|-1", "1.0|-1", "0|0", "0.00|0",
        "0|1.5", "1.5|0", "0.00|-0.00", "-0|0.0", "1|0.000001", "0.000001|1",
        "-1.5|2.5", "2.5|-1.5", "99999999999999999999|1", "1|99999999999999999999",
        "12345678901234567890|98765432109876543210", "-12345678901234567890|98765432109876543210",
        "1E+7|1", "1E+7|1E+7", "1E-7|1E-7", "1E+100|1E-100", "1E-100|1",
        "0.1|0.2", "0.000001|0.000001", "100|0.0001", "1.0000|0.00001",
        "3|7", "1|3", "2|3", "100|3", "7|2", "1|8", "1|6", "5|8", "1|12",
        "0.1|3", "1|0.3", "-1|3", "1|-3", "1000000|7",
        "999999999999999999999999999999|1", "1E+20|1E-20", "0.0000001|0.0000001",
        "1.7976931348623157E+308|1.7976931348623157E+308",
        "123456789.123456789|0.000000001",
    ];

    [JavaOracleFact]
    public void ArithmeticMatchesTheJvm()
    {
        List<string> java = OracleRunner.Run(
                Pairs,
                Program,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .Select(Decode)
            .Select(line => TraceSuffix.Replace(line, string.Empty))
            .ToList();

        List<string> csharp = Pairs.Select(Describe).ToList();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i} ({Pairs[i]}):\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    private static string Describe(string pair)
    {
        string[] parts = pair.Split('|');
        BigDecimal a = BigDecimal.Parse(parts[0]);
        BigDecimal b = BigDecimal.Parse(parts[1]);

        return new StringBuilder()
            .Append(a.Add(b).ToString()).Append('|')
            .Append(a.Subtract(b).ToString()).Append('|')
            .Append(a.Multiply(b).ToString()).Append('|')
            .Append(a.Add(b).Scale.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(a.Multiply(b).Scale.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(a.Add(b).CompareTo(b).ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(a.Add(b).Signum().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(a.Add(b).Precision().ToString(CultureInfo.InvariantCulture))
            .ToString();
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
    private const string Program = """
                      for (int i = 0; i < inputs.length; i++) {
                        String[] parts = inputs[i].split("\\|", -1);
                        java.math.BigDecimal a = new java.math.BigDecimal(parts[0]);
                        java.math.BigDecimal b = new java.math.BigDecimal(parts[1]);
                        java.math.BigDecimal sum = a.add(b);
                        java.math.BigDecimal product = a.multiply(b);
                        StringBuilder line = new StringBuilder();
                        line.append(sum.toString()).append('|');
                        line.append(a.subtract(b).toString()).append('|');
                        line.append(product.toString()).append('|');
                        line.append(sum.scale()).append('|');
                        line.append(product.scale()).append('|');
                        line.append(sum.compareTo(b)).append('|');
                        line.append(sum.signum()).append('|');
                        line.append(sum.precision());
                        System.out.println(hex(line.toString()));
                      }
            """;
}
