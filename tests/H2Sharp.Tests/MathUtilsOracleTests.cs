/*
 * Copyright 2026 H2Sharp contributors.
 * Ported from none (new tests for H2Sharp).
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// Differential tests for <see cref="MathUtils"/>. Expected values come from the
/// reference JVM so that boundary behaviour (especially <c>NextPowerOf2</c>, where
/// the Java implementation relies on an unsigned shift) cannot drift silently.
/// </summary>
public sealed class MathUtilsOracleTests
{
    [JavaOracleTheory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 4)]
    [InlineData(5, 8)]
    [InlineData(1023, 1024)]
    [InlineData(1024, 1024)]
    [InlineData(1025, 2048)]
    [InlineData(0x20000000, 0x20000000)]
    [InlineData(0x20000001, 0x40000000)]
    [InlineData(0x40000000, 0x40000000)]
    public void NextPowerOf2_MatchesJava(int x, int expected)
        => Assert.Equal(expected, MathUtils.NextPowerOf2(x));

    [JavaOracleFact]
    public void NextPowerOf2_MatchesJava_AcrossInterestingInputs()
    {
        int[] inputs = BuildInputs();

        List<string> javaLines = RunOnReferenceJvm(inputs);
        List<string> csharpLines = new(inputs.Length);
        foreach (int x in inputs)
        {
            csharpLines.Add(MathUtils.NextPowerOf2(x).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.Equal(javaLines, csharpLines);
    }

    [Fact]
    public void NextPowerOf2_RejectsOutOfRange()
    {
        // Java throws IllegalArgumentException for x > 0x40000000.
        Assert.Throws<ArgumentException>(() => MathUtils.NextPowerOf2(0x40000001));
    }

    [Theory]
    [InlineData(8, 0, 0)]
    [InlineData(8, 1, 8)]
    [InlineData(8, 8, 8)]
    [InlineData(8, 9, 16)]
    [InlineData(16, 17, 32)]
    [InlineData(0, 0, 0)]
    public void RoundUpInt_MatchesDocumentedExamples(int blockSize, int input, int expected)
        => Assert.Equal(expected, MathUtils.RoundUpInt(input, blockSize));

    [Theory]
    [InlineData(8L, 0L, 0L)]
    [InlineData(8L, 1L, 8L)]
    [InlineData(8L, 9L, 16L)]
    [InlineData(4096L, 4095L, 4096L)]
    [InlineData(4096L, 4097L, 8192L)]
    public void RoundUpLong_MatchesDocumentedExamples(long blockSize, long input, long expected)
        => Assert.Equal(expected, MathUtils.RoundUpLong(input, blockSize));

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    [InlineData(long.MaxValue)]
    public void ConvertLongToInt_Saturates(long value)
    {
        int expected = value <= int.MinValue ? int.MinValue
            : value >= int.MaxValue ? int.MaxValue
            : (int)value;
        Assert.Equal(expected, MathUtils.ConvertLongToInt(value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void ConvertIntToShort_Saturates(int value)
    {
        short expected = value <= short.MinValue ? short.MinValue
            : value >= short.MaxValue ? short.MaxValue
            : (short)value;
        Assert.Equal(expected, MathUtils.ConvertIntToShort(value));
    }

    [JavaOracleFact]
    public void RoundUpInt_MatchesJava_AcrossInterestingInputs()
    {
        int[] xs = BuildInputs();
        int[] blockSizes = [1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024];

        List<string> javaLines = RunOnReferenceJvm(
            "for (int x : xs) for (int b : bs) "
            + "{ System.out.println(org.h2.util.MathUtils.roundUpInt(x, b)); }",
            xs, [.. blockSizes]);

        List<string> csharpLines = new(xs.Length * blockSizes.Length);
        foreach (int x in xs)
        {
            foreach (int b in blockSizes)
            {
                csharpLines.Add(
                    MathUtils.RoundUpInt(x, b).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        Assert.Equal(javaLines, csharpLines);
    }

    private static int[] BuildInputs()
    {
        List<int> inputs =
        [
            .. Enumerable.Range(0, 300),
            1023, 1024, 1025, 65535, 65536, 65537,
            0x1FFFFFFF, 0x20000000, 0x20000001, 0x3FFFFFFF, 0x40000000,
        ];
        return inputs.ToArray();
    }

    private static List<string> RunOnReferenceJvm(int[] inputs)
        => RunOnReferenceJvm(
            "for (int x : xs) { System.out.println(org.h2.util.MathUtils.nextPowerOf2(x)); }",
            inputs, []);

    /// <summary>
    /// Runs a statement body against <c>org.h2.util.MathUtils</c> on the reference JVM.
    /// The body must print one value per line; the inputs are exposed as
    /// <c>int[] xs</c> and, when supplied, <c>int[] bs</c>.
    /// </summary>
    private static List<string> RunOnReferenceJvm(
        string body, int[] xs, int[] bs)
    {
        if (JavaOracle.H2Jar is null)
        {
            throw new InvalidOperationException(
                "H2 jar not found. Build it with: "
                + "(cd reference/h2/h2 && mvn -B -q -DskipTests package)");
        }

        StringBuilder source = new();
        source.AppendLine("public class Oracle {");
        source.AppendLine("  public static void main(String[] a) {");
        source.Append("    int[] xs = {");
        source.AppendJoin(',', xs);
        source.AppendLine("};");
        if (bs.Length > 0)
        {
            source.Append("    int[] bs = {");
            source.AppendJoin(',', bs);
            source.AppendLine("};");
        }
        else
        {
            source.AppendLine("    int[] bs = null;");
        }

        source.Append("    " + body);
        source.AppendLine();
        source.AppendLine("  }");
        source.AppendLine("}");

        string output = JavaOracle.Run(source.ToString(), "Oracle", JavaOracle.H2Jar);
        string[] lines = JavaOracle.Normalize(output).Split('\n');
        List<string> result = new(lines.Length);
        foreach (string line in lines)
        {
            result.Add(line.Trim());
        }

        return result;
    }
}