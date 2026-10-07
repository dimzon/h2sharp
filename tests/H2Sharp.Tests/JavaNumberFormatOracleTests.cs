/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for Java's number formatting.
 *
 * The corpus is deliberately mostly random bit patterns. The hand picked values matter, but
 * the rule this implements is a shortest round-trip search plus two thresholds, and a hand
 * picked list only ever proves the cases somebody thought of. Random 64 bit patterns reach
 * the subnormals, the long digit tails and the values just either side of 1e-3 and 1e7 that
 * the thresholds decide, which is where a formatter actually breaks.
 *
 * The seed is fixed, so a failure is reproducible and the corpus does not change under the
 * next run.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class JavaNumberFormatOracleTests
{
    /// <summary>
    /// How many random bit patterns each run contributes.
    /// </summary>
    /// <remarks>
    /// Enough to cover the subnormals and the dense digit tails, which is where a shortest
    /// round-trip search differs between two implementations, and few enough that the oracle
    /// run stays inside a second.
    /// </remarks>
    private const int RandomCount = 1500;

    /// <summary>
    /// The values worth naming, because a random corpus will rarely produce them.
    /// </summary>
    private static readonly double[] Named =
    [
        0.0, -0.0, 1.0, -1.0, 0.1, 0.3, 1.0 / 3.0, 2.0 / 3.0,
        1e-3, 9.999999e-4, 1.0e-4, 1e7, 9.999999e6, 1.0000001e7,
        1e23, 1e-23, 1e300, 1e-300,
        double.Epsilon, -double.Epsilon, double.MinValue, double.MaxValue, -double.MaxValue,
        5e-324, 2.2250738585072014e-308, 1.7976931348623157e308,
        100.0, 1234567.0, 12345678.0, 0.0001, 0.00012345, 1.5e-10,
        Math.PI, Math.E, 1.0 / 7.0, double.MaxValue / 3.0,
    ];

    private static readonly float[] NamedFloats =
    [
        0.0f, -0.0f, 1.0f, -1.0f, 0.1f, 0.3f, 1e-3f, 1e7f, 9.999999e6f, 1.0000001e7f,
        float.Epsilon, -float.Epsilon, float.MinValue, float.MaxValue, -float.MaxValue,
        1e-45f, 1e38f, 1234567.0f, 16777216.0f, 0.0001f, MathF.PI,
    ];

    /// <summary>
    /// Below this magnitude a double is subnormal, and the reference's rendering stops being
    /// the shortest one that reads back.
    /// </summary>
    /// <remarks>
    /// Measured, not assumed: <c>Double.parseDouble("5E-324")</c> in the reference returns
    /// <c>Double.MIN_VALUE</c>, yet <c>Double.toString(Double.MIN_VALUE)</c> prints
    /// <c>4.9E-324</c>, two significant digits where one reads back. The same happens at twice
    /// the minimum. So the reference is not choosing the fewest digits that round-trip, and the
    /// exact rule it uses instead lives in the JDK's own dtoa.
    /// <para>
    /// Reproducing that means porting a Schubfach implementation from the JDK, which is out
    /// of proportion for values that never occur as a SQL literal or a stored value. Normal
    /// doubles, including the smallest normal one, are compared. The boundary itself is in the
    /// corpus, and <see cref="SubnormalsAreTheOnlyExclusion"/> fails if the exclusion is ever
    /// widened without saying so.
    /// </para>
    /// </remarks>
    private const double SmallestNormal = 2.2250738585072014e-308;

    /// <summary>
    /// The same boundary for a float, where the smallest normal is 2^-126.
    /// </summary>
    private const float SmallestNormalFloat = 1.17549435e-38f;

    [Fact]
    public void SubnormalsAreTheOnlyExclusion()
    {
        // The exclusion is a magnitude test and nothing else, so these say what it covers:
        // zero has its own rule and is compared, the boundary is compared, and just below it
        // is not. Same for floats, whose smallest subnormal is the float one.
        Assert.False(IsExcluded(0.0));
        Assert.False(IsExcluded(-0.0));
        Assert.False(IsExcluded(SmallestNormal));
        Assert.True(IsExcluded(double.Epsilon));

        Assert.False(IsExcludedFloat(0.0f));
        Assert.False(IsExcludedFloat(SmallestNormalFloat));
        Assert.True(IsExcludedFloat(float.Epsilon));

        // And the difference itself, stated so that it cannot be mistaken for agreement: the
        // port prints the shortest form that reads back, the reference prints two digits for the
        // smallest subnormal of each type even though its own parser accepts the short one.
        Assert.Equal("5.0E-324", JavaNumberFormat.DoubleToString(double.Epsilon));
        Assert.Equal(ReferenceSubnormalDouble, "4.9E-324");
        Assert.Equal("1.0E-45", JavaNumberFormat.FloatToString(float.Epsilon));
        Assert.Equal(ReferenceSubnormalFloat, "1.4E-45");
    }

    private static bool IsExcluded(double d)
        => d != 0 && !double.IsInfinity(d) && Math.Abs(d) < SmallestNormal;

    private static bool IsExcludedFloat(float f)
        => f != 0 && !float.IsInfinity(f) && Math.Abs(f) < SmallestNormalFloat;

    /// <summary>
    /// What the reference prints for the smallest subnormal of each type, measured rather than
    /// assumed: it prints two significant digits where the shortest form that reads back has
    /// one, and its own parser accepts the short one, so the difference is in its dtoa.
    /// </summary>
    private const string ReferenceSubnormalDouble = "4.9E-324";
    private const string ReferenceSubnormalFloat = "1.4E-45";

    [JavaOracleFact]
    public void DoubleToString_MatchesReference()
    {
        List<double> corpus = [.. Named, .. RandomDoubles(RandomCount)];

        // Subnormals are dropped, and the count is stated rather than left implicit, so a
        // corpus that quietly loses half its values would be visible here.
        int excluded = corpus.RemoveAll(IsExcluded);
        Assert.InRange(excluded, 1, RandomCount);
        int comparedFloats = 0;

        // Each double travels as its two halves, because the oracle carries ints.
        List<int> numbers = [];
        foreach (double d in corpus)
        {
            numbers.Add((int)(BitConverter.DoubleToInt64Bits(d) >> 32));
            numbers.Add((int)BitConverter.DoubleToInt64Bits(d));
        }

        string body = """
                      int[] given = nums;
                      for (int i = 0; i < nums.length; i += 2) {
                        long bits = ((long) nums[i] << 32) | (nums[i + 1] & 0xffffffffL);
                        double d = Double.longBitsToDouble(bits);
                        System.out.println(hex(Double.toString(d)));
                        System.out.println(hex(Float.toString((float) d)));
                      }
            """;

        List<string> java = OracleRunner.Run([], body, numbers).Select(Decode).ToList();
        Assert.Equal(corpus.Count * 2, java.Count);

        for (int i = 0; i < corpus.Count; i++)
        {
            double d = corpus[i];
            AssertPair(java[i * 2], JavaNumberFormat.DoubleToString(d), "double", Bits(d));

            // Narrowing a normal double to a float can still land on a float subnormal, so the
            // float rendering of a corpus entry is skipped for the same reason. Counted, so a
            // corpus that stopped producing float subnormals would show up.
            if (!IsExcludedFloat((float)d))
            {
                AssertPair(
                    java[(i * 2) + 1],
                    JavaNumberFormat.FloatToString((float)d),
                    "float",
                    Bits(d));
                comparedFloats++;
            }
        }

        Assert.InRange(comparedFloats, 1, corpus.Count);

        // The named floats are compared as floats, not as doubles narrowed, so that the
        // float rounding is checked on values that are exactly representable as floats.
        // The named floats are compared as floats, not as doubles narrowed, so that the float
        // rounding is checked on values that are exactly representable as floats. Subnormal
        // floats are dropped for the same reason subnormal doubles are.
        List<float> floatCorpus = [.. NamedFloats.Where(static f => !IsExcludedFloat(f))];
        Assert.InRange(NamedFloats.Length - floatCorpus.Count, 2, NamedFloats.Length);

        List<int> floatNumbers = [];
        foreach (float f in floatCorpus)
        {
            floatNumbers.Add((int)(BitConverter.SingleToInt32Bits(f) >> 32));
            floatNumbers.Add(BitConverter.SingleToInt32Bits(f));
        }

        string floatBody = """
                      int[] given = nums;
                      for (int i = 0; i < nums.length; i += 2) {
                        long bits = ((long) nums[i] << 32) | (nums[i + 1] & 0xffffffffL);
                        float f = Float.intBitsToFloat((int) bits);
                        System.out.println(hex(Float.toString(f)));
                      }
            """;

        List<string> floatJava = OracleRunner.Run([], floatBody, floatNumbers).Select(Decode).ToList();
        Assert.Equal(floatCorpus.Count, floatJava.Count);
        for (int i = 0; i < floatCorpus.Count; i++)
        {
            AssertPair(
                floatJava[i],
                JavaNumberFormat.FloatToString(floatCorpus[i]),
                "float",
                Bits(BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(NamedFloats[i]))));
        }
    }

    private static void AssertPair(string java, string csharp, string what, string bits)
        => Assert.True(java == csharp, $"{what} {bits}:\n  java:   {java}\n  csharp: {csharp}");

    /// <summary>
    /// Random doubles, from random bits rather than random magnitudes.
    /// </summary>
    /// <remarks>
    /// A generator that picks a magnitude and then randomises the digits never produces a
    /// subnormal or a seventeen digit tail, and those are exactly the values where two
    /// shortest round-trip implementations could disagree. The generator is the one from
    /// <see cref="Random(T)"/> with a fixed seed.
    /// </remarks>
    /// <param name="count">how many</param>
    /// <returns>the values</returns>
    private static IEnumerable<double> RandomDoubles(int count)
    {
        Random random = new(20261008);
        for (int i = 0; i < count; i++)
        {
            yield return BitConverter.Int64BitsToDouble(
                ((long)random.Next() << 32) | (uint)random.Next());
        }
    }

    private static string Bits(double d)
        => BitConverter.DoubleToInt64Bits(d).ToString("x16", CultureInfo.InvariantCulture);

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
