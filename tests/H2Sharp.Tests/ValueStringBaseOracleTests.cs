/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the conversions a VARCHAR value answers about itself.
 *
 * The trim is the part worth pinning from both ends. Java's String.trim strips every
 * character up to and including U+0020 and nothing else, while .NET's own Trim would also
 * strip the Unicode spaces above it - and would refuse to strip the control characters just
 * below it. Where Java's trim leaves a U+00A0 in the text, the reference parser refuses the
 * value; where it strips a U+001F, the reference parser accepts the rest. Either half of the
 * line, read in the wrong direction, turns a refused value into a parsed one or the reverse,
 * and the corpus carries both so that the oracle says which side is which.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using H2Sharp;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// The VARCHAR conversions against the reference: booleans, bytes, integers and decimals out
/// of padded, unpadded and refuse-worthy text.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class ValueStringBaseOracleTests
{
    private static readonly string[] Values =
    [
        "1.5", " 1.5", "1.5 ", " 1.5 ", "\t1.5\t", "\n 42 \r",
        "007", "+3", "-0.0", "1E+3", "1e-3", "-1.500",
        "abc", "", "   ",
        "\u000b1.5\u000b", "\u001f5\u001f", "\u00a01.5\u00a0", "\u3000123\u3000",
        "127", "128", "-128", "-129",
        "true", " TRUE ", "t", "y", "yes", "false", "F ", "no",
    ];

    [JavaOracleFact]
    public void ConversionsAgreeWithTheReference()
    {
        List<string> java = OracleRunner.Run(
                Values,
                Body(),
                extraJavaArgs: ["-Duser.language=en"])
            .Select(Decode)
            .ToList();

        List<string> csharp = Values.Select(Describe).ToList();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"case {i} ({Printable(Values[i])}):\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    private static string Body()
    {
        return """
        for (int i = 0; i < inputs.length; i++) {
          org.h2.value.Value v = org.h2.value.ValueVarchar.get(inputs[i]);
          StringBuilder b = new StringBuilder();
          try { b.append(v.getBoolean()); } catch (RuntimeException e) { b.append("THROWS"); }
          b.append('|');
          try { b.append(v.getByte()); } catch (RuntimeException e) { b.append("THROWS"); }
          b.append('|');
          try { b.append(v.getInt()); } catch (RuntimeException e) { b.append("THROWS"); }
          b.append('|');
          try { b.append(v.getBigDecimal().toString()); } catch (RuntimeException e) { b.append("THROWS"); }
          System.out.println(hex(b.toString()));
        }
        """;
    }

    private static string Describe(string text)
    {
        Value v = ValueVarchar.Get(text);
        StringBuilder b = new();
        try
        {
            _ = b.Append(J(v.GetBoolean()));
        }
        catch (H2SharpException)
        {
            _ = b.Append("THROWS");
        }

        _ = b.Append('|');
        try
        {
            _ = b.Append(v.GetByte().ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (H2SharpException)
        {
            _ = b.Append("THROWS");
        }

        _ = b.Append('|');
        try
        {
            _ = b.Append(v.GetInt().ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (H2SharpException)
        {
            _ = b.Append("THROWS");
        }

        _ = b.Append('|');
        try
        {
            _ = b.Append(v.GetBigDecimal().ToString());
        }
        catch (H2SharpException)
        {
            _ = b.Append("THROWS");
        }

        return b.ToString();
    }

    private static string Decode(string hex)
    {
        string only = hex.TrimStart('=');
        if (!only.All(char.IsAsciiHexDigit))
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

    private static string Printable(string s)
    {
        StringBuilder b = new();
        foreach (char c in s)
        {
            _ = b.Append(c < ' ' || c > '~' ? "\\u" + ((int)c).ToString("x4") : c);
        }

        return b.ToString();
    }

    private static string J(bool value) => value ? "true" : "false";
}
