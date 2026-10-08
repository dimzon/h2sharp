/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for DOUBLE PRECISION.
 *
 * A double is where the platform's own answers differ from Java's, so the corpus is chosen for
 * that: NaN, both infinities, both zeroes, the subnormal range, the shortest-representation
 * boundaries, and the values around them. Double.compare is the sharpest of these, because
 * Java puts NaN above every number and negative zero below positive zero while .NET does the
 * opposite on both, and an ORDER BY that mixes a NaN with ordinary values would answer
 * differently if the port used the platform compare.
 *
 * Negative zero also has an identity consequence: the reference answers it with the positive
 * zero, so -0.0 and 0.0 are the same object, and a test that compared them as values would
 * pass for the wrong reason.
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

public sealed class ValueDoubleOracleTests
{
    /// <summary>
    /// The values, as the bit patterns they travel as, because most of them have no short
    /// literal form and a couple of them have none at all.
    /// </summary>
    /// <summary>
    /// The smallest normal double, 2^-1022. Below it the values are subnormal.
    /// </summary>
    private const double SmallestNormal = 2.2250738585072014e-308;

    private static readonly double[] Values =
    [
        0.0, -0.0, 1.0, -1.0, 0.5, -0.5, 2.0, 3.0, -3.0,
        0.1, 0.2, 1.0 / 3.0, 2.0 / 3.0, 1e-3, 9.999999e-4, 1e7, 9.999999e6,
        1e-300, 1e300, double.Epsilon, -double.Epsilon, double.MinValue, double.MaxValue,
        -double.MaxValue, 1e7 + 1, 1e-3 - 1e-19,
        double.PositiveInfinity, double.NegativeInfinity, double.NaN,
    ];

    [JavaOracleFact]
    public void Double_MatchesReference()
    {
        // Subnormal doubles are dropped, for the reason measured and recorded in
        // JavaNumberFormatOracleTests: the reference's own parser accepts the one digit form of
        // Double.MIN_VALUE while its toString prints two, so the reference's rule is not "the
        // shortest that reads back" and reproducing it needs the JDK's own dtoa. Since a
        // subnormal's arithmetic results print through the same formatter, the whole value is
        // out rather than just its text.
        List<double> corpus = [.. Values.Where(static d => !IsSubnormal(d))];
        Assert.InRange(Values.Length - corpus.Count, 1, Values.Length);

        List<int> numbers = [];
        foreach (double d in corpus)
        {
            numbers.Add((int)(BitConverter.DoubleToInt64Bits(d) >> 32));
            numbers.Add((int)BitConverter.DoubleToInt64Bits(d));
        }

        string body = """
                      java.util.function.IntFunction<String> one = k -> {
                        long bits = ((long) nums[k] << 32) | (nums[k + 1] & 0xffffffffL);
                        double d = Double.longBitsToDouble(bits);
                        org.h2.value.ValueDouble v = org.h2.value.ValueDouble.get(d);
                        return describe.apply(v) + "|" + compare.apply(v, d) + "||"
                            + ops.apply(v, d);
                      };
                      for (int i = 0; i < nums.length; i += 2) {
                        String line;
                        try {
                          line = one.apply(i);
                        } catch (Throwable ex) {
                          line = "CAUGHT " + ex;
                        }
                        System.out.println(hexText.apply(line));
                      }
            """;

        List<string> java = OracleRunner.Run(
                [],
                Setup + body,
                numbers,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .Select(Decode)
            .Select(line => TraceSuffix.Replace(line, string.Empty))
            .ToList();

        List<string> csharp = [];
        foreach (double d in corpus)
        {
            ValueDouble v = ValueDouble.Get(d);
            csharp.Add(Describe(v) + "|" + Compare(v, d) + "||" + Ops(v, d));
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// Negative zero and NaN are answered with singletons, so identity is part of the contract.
    /// </summary>
    [Fact]
    public void SpecialValuesAreSharedInstances()
    {
        Assert.Same(ValueDouble.Zero, ValueDouble.Get(-0.0));
        Assert.Same(ValueDouble.One, ValueDouble.Get(1.0));

        ValueDouble first = ValueDouble.Get(double.NaN);
        ValueDouble second = ValueDouble.Get(double.NaN);
        Assert.Same(first, second);
    }

    /// <summary>
    /// Whether the value is subnormal, which is the one range whose rendering the reference
    /// does not produce by the shortest-round-trip rule.
    /// </summary>
    /// <param name="d">the value</param>
    /// <returns>whether it is subnormal and non-zero</returns>
    private static bool IsSubnormal(double d)
        => d != 0 && !double.IsInfinity(d) && Math.Abs(d) < SmallestNormal;

    private static string Describe(ValueDouble v)
    {
        StringBuilder line = new();
        _ = line.Append(v.GetSql(new StringBuilder(), 0)).Append('|')
            .Append(v.GetSql(new StringBuilder(), HasSql.NoCasts)).Append('|')
            .Append(v.GetString()).Append('|')
            .Append(JavaNumberFormat.FloatToString(v.GetFloat())).Append('|')
            .Append(v.GetSignum().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(v.GetHashCode().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(v.GetValueType().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(v.GetTypeInfo().GetSql(0)).Append('|')
            .Append(v.GetMemory().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(J(v.Equals(v))).Append('|')
            .Append(J(v.Equals(ValueDouble.Get(0.0))));
        return line.ToString();
    }

    private static string Compare(ValueDouble v, double d)
    {
        CompareMode off = CompareMode.GetInstance(null, 0);
        Value other = ValueDouble.Get(d);
        return v.CompareTo(other, null, off).ToString(CultureInfo.InvariantCulture)
            + "|" + other.CompareTo(v, null, off).ToString(CultureInfo.InvariantCulture);
    }

    private static string Ops(ValueDouble v, double d)
    {
        Value other = ValueDouble.Get(d);
        return string.Join(
            "|",
            Op(() => v.Add(other)),
            Op(() => v.Subtract(other)),
            Op(() => v.Multiply(other)),
            Op(() => v.Divide(other, TypeInfo.TypeBigint)),
            Op(() => v.Modulus(other)),
            Op(() => v.Negate()));
    }

    private static string Op(Func<Value> f)
    {
        try
        {
            return f().GetSql(new StringBuilder(), HasSql.NoCasts).ToString();
        }
        catch (H2SharpException e)
        {
            return "EX " + e.Message;
        }
    }

    private static string J(bool value) => value ? "true" : "false";

    private static readonly System.Text.RegularExpressions.Regex TraceSuffix =
        new(@"\s\[\d+-\d+\]", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// The Java side, as lambda variables because the harness puts the body inside main.
    /// </summary>
    /// <summary>
    /// The Java side, as lambda variables because the harness puts the body inside main.
    /// </summary>
    /// <remarks>
    /// One lambda per operation, and the operation happens inside the lambda. A lambda that took
    /// the already computed result could not catch anything: the argument is evaluated before the
    /// call, so a division by zero would escape the handler that was meant to catch it. That is
    /// not hypothetical, it is how the first version of this snippet failed.
    /// </remarks>
    private const string Setup = """
                      java.util.function.Function<String, String> hexText = s -> {
                        StringBuilder b = new StringBuilder();
                        for (int i = 0; i < s.length(); i++) {
                          b.append(String.format("%04x", (int) s.charAt(i)));
                        }
                        return b.length() == 0 ? "-" : b.toString();
                      };
                      java.util.function.BiFunction<org.h2.value.Value, org.h2.value.Value, String> add =
                          (a, b) -> {
                            try {
                              return a.add(b).getSQL(4);
                            } catch (org.h2.message.DbException ex) {
                              return "EX " + ex.getMessage();
                            }
                          };
                      java.util.function.BiFunction<org.h2.value.Value, org.h2.value.Value, String> sub =
                          (a, b) -> {
                            try {
                              return a.subtract(b).getSQL(4);
                            } catch (org.h2.message.DbException ex) {
                              return "EX " + ex.getMessage();
                            }
                          };
                      java.util.function.BiFunction<org.h2.value.Value, org.h2.value.Value, String> mul =
                          (a, b) -> {
                            try {
                              return a.multiply(b).getSQL(4);
                            } catch (org.h2.message.DbException ex) {
                              return "EX " + ex.getMessage();
                            }
                          };
                      java.util.function.BiFunction<org.h2.value.Value, org.h2.value.Value, String> div =
                          (a, b) -> {
                            try {
                              return a.divide(
                                  b, org.h2.value.TypeInfo.TYPE_BIGINT).getSQL(4);
                            } catch (org.h2.message.DbException ex) {
                              return "EX " + ex.getMessage();
                            }
                          };
                      java.util.function.BiFunction<org.h2.value.Value, org.h2.value.Value, String> mod =
                          (a, b) -> {
                            try {
                              return a.modulus(b).getSQL(4);
                            } catch (org.h2.message.DbException ex) {
                              return "EX " + ex.getMessage();
                            }
                          };
                      java.util.function.Function<org.h2.value.Value, String> neg = a -> {
                        try {
                          return a.negate().getSQL(4);
                        } catch (org.h2.message.DbException ex) {
                          return "EX " + ex.getMessage();
                        }
                      };
                      java.util.function.Function<org.h2.value.ValueDouble, String> describe =
                          v -> v.getSQL(0) + "|" + v.getSQL(4) + "|" + v.getString() + "|"
                              + v.getFloat() + "|" + v.getSignum() + "|" + v.hashCode() + "|"
                              + v.getValueType() + "|" + v.getType().getSQL(0) + "|" + v.getMemory()
                              + "|" + v.equals(v) + "|" + v.equals(org.h2.value.ValueDouble.get(0d));
                      java.util.function.BiFunction<org.h2.value.ValueDouble, Double, String> compare =
                          (v, d) -> {
                            org.h2.value.CompareMode off =
                                org.h2.value.CompareMode.getInstance(null, 0);
                            org.h2.value.Value other = org.h2.value.ValueDouble.get(d);
                            return v.compareTo(other, null, off) + "|" + other.compareTo(v, null, off);
                          };
                      java.util.function.BiFunction<org.h2.value.ValueDouble, Double, String> ops =
                          (v, d) -> {
                            org.h2.value.Value other = org.h2.value.ValueDouble.get(d);
                            return add.apply(v, other) + "|" + sub.apply(v, other) + "|"
                                + mul.apply(v, other) + "|" + div.apply(v, other) + "|"
                                + mod.apply(v, other) + "|" + neg.apply(v);
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
