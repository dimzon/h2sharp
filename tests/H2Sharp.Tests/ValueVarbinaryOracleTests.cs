/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for BINARY VARYING.
 *
 * A byte array is the first value whose bytes are not derived from a string, and that changes
 * three things: the ordering is unsigned, so X'80' sorts after X'7F' where a signed comparison
 * of two .NET byte arrays says the opposite; the SQL spelling is a hex literal and not a quoted
 * string; and the string form is a decode of bytes that need not be text at all.
 *
 * The hash code is not compared, because the reference mixes in getClass().hashCode(), which in
 * Java is an identity hash and differs on every run. Equality, which a hash has to agree with,
 * is compared.
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

[Collection(OracleCollection.Name)]

public sealed class ValueVarbinaryOracleTests
{
    private static readonly byte[][] Arrays =
    [
        [],
        [0x41],
        [0x61],
        [0x00],
        [0x7F],
        [0x80],
        [0xFF],
        [0x41, 0x42],
        [0x41, 0x42, 0x43],
        [0x41, 0x41],
        [0x42, 0x41],
        [0xFF, 0x00],
        [0x00, 0xFF],
        [0xC3, 0xA9],
        [0xE2, 0x82, 0xAC],
        [0xF0, 0x9F, 0x8E, 0x89],
        [0xC3],
        [0xE2, 0x82],
        [0xFF, 0xFE, 0xFD],
        [0xC0, 0x80],
        [0xED, 0xA0, 0x80],
        [0xF5, 0x80, 0x80, 0x80],
        [0x00, 0x41, 0x00],
        [0x41, 0x42, 0x00, 0x43],
        [0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F, 0x50],
        [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00],
        [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
        [],

        // Every way a sequence can go wrong, because that is where the two platforms disagree
        // about what text a byte array stands for.
        [0xC0],
        [0xC1],
        [0xC2],
        [0xE0],
        [0xE0, 0x80],
        [0xE0, 0x9F],
        [0xE1, 0x80, 0x41],
        [0xE2, 0x28, 0xA1],
        [0xED],
        [0xED, 0xA0],
        [0xED, 0xBF, 0xBF],
        [0xF0],
        [0xF0, 0x8F],
        [0xF4, 0x90, 0x80, 0x80],
        [0xF5, 0x80, 0x80, 0x80],
        [0xF8, 0x88, 0x80, 0x80, 0x80],
        [0xFE],
        [0xFF],
        [0xF0, 0x28, 0x8C, 0x28],
        [0x41, 0xC3, 0xA9, 0x42],
    ];

    [JavaOracleFact]
    public void Varbinary_MatchesReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                Declare + Program,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .Select(Decode)
            .Select(line => TraceSuffix.Replace(line, string.Empty))
            .ToList();

        List<string> csharp = Arrays.Select(Describe).ToList();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }
    private static string Describe(byte[] b)
    {
        Value v = ValueVarbinary.Get(b);
        return v.GetSql(new StringBuilder(), 0).Append('|')
            .Append(HexText(v.GetString())).Append('|')
            .Append(v.GetBytes() is { Length: > 0 } bytes ? Hex.BytesToHex(bytes) : "-").Append('|')
            .Append(v.GetMemory().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(v.GetValueType().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(v.GetTypeInfo().GetSql(0)).Append('|')
            .Append(v.GetTypeInfo().GetPrecision().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(J(v.ContainsNull())).Append('|')
            .Append(J(v.Equals(v))).Append('|')
            .Append(J(v.Equals(ValueVarbinary.Get([0x41, 0x42, 0x43])))).Append('|')
            .Append(Compare(b, [0x41, 0x42, 0x43])).Append('|')
            .Append(Compare([0x41, 0x42, 0x43], b))
            .ToString();
    }

    private static string Compare(byte[] a, byte[] b)
    {
        CompareMode off = CompareMode.GetInstance(null, 0);
        return ValueVarbinary.Get(a).CompareTo(ValueVarbinary.Get(b), null, off)
            .ToString(CultureInfo.InvariantCulture);
    }

    /// <remarks>
    /// Java concatenates a boolean as "true" and .NET as "True", so every boolean in this file
    /// goes through here rather than through an append.
    /// </remarks>
    private static string J(bool value) => value ? "true" : "false";

    /// <summary>
    /// Encodes a string as its UTF-16 code units, the way the oracle prints one.
    /// </summary>
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

    /// <remarks>
    /// The oracle prints one line per array, and the text is UTF-16, so a line is decoded back
    /// into code units before it is compared. A supplementary character arrives as a surrogate
    /// pair and is compared as the two code units Java held.
    /// </remarks>
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
    /// errors about symbols that are perfectly fine. Spelling the loop out costs nothing and
    /// compiles on the first try.
    /// </remarks>
    private const string Program = """
                      org.h2.value.CompareMode mode = org.h2.value.CompareMode.getInstance(null, 0);
                      org.h2.value.Value probe =
                          org.h2.value.ValueVarbinary.get(new byte[] { 0x41, 0x42, 0x43 });
                      for (int i = 0; i < arrays.length; i++) {
                        org.h2.value.Value v = org.h2.value.ValueVarbinary.get(arrays[i]);
                        StringBuilder b = new StringBuilder();
                        b.append(v.getSQL(new StringBuilder(), 0)).append('|');
                        String text = v.getString();
                        if (text.length() == 0) {
                          b.append('-');
                        } else {
                          for (int j = 0; j < text.length(); j++) {
                            b.append(String.format("%04x", (int) text.charAt(j)));
                          }
                        }
                        b.append('|');
                        byte[] bytes = v.getBytes();
                        if (bytes.length == 0) {
                          b.append('-');
                        } else {
                          for (int j = 0; j < bytes.length; j++) {
                            b.append(String.format("%02x", bytes[j] & 0xff));
                          }
                        }
                        b.append('|').append(v.getMemory()).append('|');
                        b.append(v.getValueType()).append('|');
                        b.append(v.getType().getSQL(0)).append('|');
                        b.append(v.getType().getPrecision()).append('|');
                        b.append(v.containsNull()).append('|');
                        b.append(v.equals(v)).append('|');
                        b.append(v.equals(probe)).append('|');
                        b.append(v.compareTo(probe, null, mode)).append('|');
                        b.append(probe.compareTo(v, null, mode));
                        System.out.println(hex(b.toString()));
                      }
            """;

    private const string Declare = """
                      byte[][] arrays = new byte[][] {
                          {}, {0x41}, {0x61}, {0x00}, {0x7F}, {(byte) 0x80}, {(byte) 0xFF},
                          {0x41, 0x42}, {0x41, 0x42, 0x43}, {0x41, 0x41}, {0x42, 0x41},
                          {(byte) 0xFF, 0x00}, {0x00, (byte) 0xFF},
                          {(byte) 0xC3, (byte) 0xA9}, {(byte) 0xE2, (byte) 0x82, (byte) 0xAC},
                          {(byte) 0xF0, (byte) 0x9F, (byte) 0x8E, (byte) 0x89},
                          {(byte) 0xC3}, {(byte) 0xE2, (byte) 0x82},
                          {(byte) 0xFF, (byte) 0xFE, (byte) 0xFD},
                          {(byte) 0xC0, (byte) 0x80},
                          {(byte) 0xED, (byte) 0xA0, (byte) 0x80},
                          {(byte) 0xF5, (byte) 0x80, (byte) 0x80, (byte) 0x80},
                          {0x00, 0x41, 0x00}, {0x41, 0x42, 0x00, 0x43},
                          {0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
                           0x49, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F, 0x50},
                          new byte[16],
                          {(byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF,
                           (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF,
                           (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF,
                           (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF},
                          {},
                          {(byte) 0xC0}, {(byte) 0xC1}, {(byte) 0xC2}, {(byte) 0xE0},
                          {(byte) 0xE0, (byte) 0x80}, {(byte) 0xE0, (byte) 0x9F},
                          {(byte) 0xE1, (byte) 0x80, 0x41},
                          {(byte) 0xE2, 0x28, (byte) 0xA1},
                          {(byte) 0xED}, {(byte) 0xED, (byte) 0xA0},
                          {(byte) 0xED, (byte) 0xBF, (byte) 0xBF},
                          {(byte) 0xF0}, {(byte) 0xF0, (byte) 0x8F},
                          {(byte) 0xF4, (byte) 0x90, (byte) 0x80, (byte) 0x80},
                          {(byte) 0xF5, (byte) 0x80, (byte) 0x80, (byte) 0x80},
                          {(byte) 0xF8, (byte) 0x88, (byte) 0x80, (byte) 0x80, (byte) 0x80},
                          {(byte) 0xFE}, {(byte) 0xFF},
                          {(byte) 0xF0, 0x28, (byte) 0x8C, 0x28},
                          {0x41, (byte) 0xC3, (byte) 0xA9, 0x42}
                      };

            """;
}
