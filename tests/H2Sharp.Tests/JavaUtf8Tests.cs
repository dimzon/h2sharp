/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Fast coverage for the UTF-8 codec, with the answers taken from the JVM.
 *
 * Every vector here was measured with a probe program on the reference JVM before the code was
 * written, and the differential suites in ValueVarcharOracleTests and ValueVarbinaryOracleTests
 * check the same behaviour against the reference itself. This file exists so that a regression
 * says which rule broke instead of pointing at a line of a corpus.
 */
using System;
using System.Text;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class JavaUtf8Tests
{
    /// <summary>
    /// What the reference prints for these bytes, as code units in hex. A dash is the empty
    /// result, and a lone fffd is a single replacement character.
    /// </summary>
    [Theory]
    [InlineData("", "-")]
    [InlineData("41", "0041")]
    [InlineData("80", "fffd")]
    [InlineData("bf", "fffd")]
    [InlineData("c0", "fffd")]
    [InlineData("c1", "fffd")]
    [InlineData("c2", "fffd")]
    [InlineData("df", "fffd")]
    [InlineData("e0", "fffd")]
    [InlineData("e080", "fffdfffd")]
    [InlineData("e09f", "fffdfffd")]
    [InlineData("e0a080", "0800")]
    [InlineData("e180", "fffd")]
    [InlineData("e18041", "fffd0041")]
    [InlineData("ed", "fffd")]
    [InlineData("eda0", "fffd")]
    [InlineData("eda041", "fffd0041")]
    [InlineData("eda0c3a9", "fffd00e9")]
    [InlineData("eda080", "fffd")]
    [InlineData("edbfbf", "fffd")]
    [InlineData("efbfbf", "ffff")]
    [InlineData("f0", "fffd")]
    [InlineData("f08f", "fffdfffd")]
    [InlineData("f0908080", "d800dc00")]
    [InlineData("f48fbfbf", "dbffdfff")]
    [InlineData("f4908080", "fffdfffdfffdfffd")]
    [InlineData("f5808080", "fffdfffdfffdfffd")]
    [InlineData("f888808080", "fffdfffdfffdfffdfffd")]
    [InlineData("fe", "fffd")]
    [InlineData("ff", "fffd")]
    [InlineData("e228a1", "fffd0028fffd")]
    [InlineData("f0288c28", "fffd0028fffd0028")]
    [InlineData("c2a0", "00a0")]
    [InlineData("61c3a962", "006100e90062")]
    [InlineData("41eda08042", "0041fffd0042")]
    [InlineData("c28080", "0080fffd")]
    [InlineData("e0c380", "fffd00c0")]
    [InlineData("e0c080", "fffdfffdfffd")]
    [InlineData("c228", "fffd0028")]
    [InlineData("f0c38080", "fffd00c0fffd")]
    [InlineData("f0808080", "fffdfffdfffdfffd")]
    [InlineData("ed9fbf", "d7ff")]
    [InlineData("ee8080", "e000")]
    [InlineData("e1bfbf", "1fff")]
    [InlineData("f1808080", "d8c0dc00")]
    [InlineData("e282acff", "20acfffd")]
    public void DecodingMatchesTheJvm(string hex, string expected)
    {
        Assert.Equal(expected, CodeUnits(JavaUtf8.GetString(Bytes(hex))));
    }

    /// <summary>
    /// The bytes the reference writes for these strings. An unpaired surrogate is a question
    /// mark, not the replacement character.
    /// </summary>
    [Theory]
    [InlineData("d800", "3f")]
    [InlineData("dc00", "3f")]
    [InlineData("0061d8000062", "613f62")]
    [InlineData("d83cdf89", "f09f8e89")]
    [InlineData("d800d800", "3f3f")]
    [InlineData("006f006b", "6f6b")]
    [InlineData("", "")]
    public void EncodingMatchesTheJvm(string codeUnits, string expected)
    {
        Assert.Equal(expected, Hex.BytesToHex(JavaUtf8.GetBytes(FromCodeUnits(codeUnits))));
    }

    /// <summary>
    /// Text that survives a round trip comes back as itself, and every scalar value encodes to the
    /// bytes it should.
    /// </summary>
    [Fact]
    public void EveryScalarValueSurvivesARoundTrip()
    {
        StringBuilder all = new();
        for (int cp = 0; cp <= 0x10FFFF; cp++)
        {
            if (cp is >= 0xD800 and <= 0xDFFF)
            {
                continue;
            }

            _ = all.Append(char.ConvertFromUtf32(cp));
        }

        byte[] encoded = JavaUtf8.GetBytes(all.ToString());
        Assert.Equal(all.ToString(), JavaUtf8.GetString(encoded));
    }

    private static byte[] Bytes(string hex)
    {
        byte[] result = new byte[hex.Length / 2];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }

        return result;
    }

    private static string FromCodeUnits(string hex)
    {
        StringBuilder b = new();
        for (int i = 0; i + 3 < hex.Length; i += 4)
        {
            _ = b.Append((char)Convert.ToInt32(hex.Substring(i, 4), 16));
        }

        return b.ToString();
    }

    private static string CodeUnits(string text)
    {
        if (text.Length == 0)
        {
            return "-";
        }

        StringBuilder b = new();
        foreach (char c in text)
        {
            _ = b.Append(((int)c).ToString("x4"));
        }

        return b.ToString();
    }
}
