/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for BINARY.
 *
 * A fixed length binary is the varying type with one thing added and one thing removed. Added:
 * the SQL spelling carries a CAST, because a hex literal is a VARBINARY and the type has to be
 * said out loud; the length in that CAST is at least 1, so the empty value renders as BINARY(1)
 * while its own precision is 0. Removed: the empty value is not a singleton here, because its
 * length is part of its type and an empty BINARY is not the same thing as no BINARY.
 *
 * The malformed UTF-8 cases are not repeated: they belong to the codec, which ValueVarbinaryOracleTests
 * covers and JavaUtf8Tests pins. This corpus is about the length and the CAST.
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

public sealed class ValueBinaryOracleTests
{
    private static readonly byte[][] Arrays =
    [
        [],
        [0x41],
        [0x41, 0x42],
        [0x41, 0x42, 0x43],
        [0x7F],
        [0x80],
        [0xFF],
        [0x00, 0x00],
        [0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4A, 0x4B, 0x4C],
        [],
    ];

    [JavaOracleFact]
    public void Binary_MatchesReference()
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

    /// <summary>
    /// The empty value is not a singleton here, and a BINARY never equals a VARBINARY with the
    /// same bytes, because the two are different types.
    /// </summary>
    [Fact]
    public void TheEmptyValueIsNotSharedAndTheTypesStayApart()
    {
        Value first = ValueBinary.Get([]);
        Value second = ValueBinary.Get([]);
        Assert.Equal("CAST(X'' AS BINARY(1))", Sql(first, 0));
        Assert.Equal(0, first.GetTypeInfo().GetPrecision());

        // The cache answers with the same instance for equal bytes, the varying type is a
        // different type even where the bytes are the same, and equality is the whole array.
        Assert.True(ValueBinary.Get([0x41]).Equals(ValueBinary.Get([0x41])));
        Assert.False(ValueBinary.Get([0x41]).Equals(ValueBinary.Get([0x41, 0x42])));
        Assert.False(ValueBinary.Get([0x41]).Equals(ValueVarbinary.Get([0x41])));
        Assert.False(first.Equals(ValueBinary.Get([0x41])));
        Assert.False(first.Equals(null));
    }

    /// <summary>
    /// NO_CASTS gives the bare literal, which is what a caller that already knows the type wants.
    /// </summary>
    [Fact]
    public void NoCastsDropsTheWrapper()
    {
        Value v = ValueBinary.Get([0x41, 0x42]);

        Assert.Equal("CAST(X'4142' AS BINARY(2))", Sql(v, 0));
        Assert.Equal("X'4142'", Sql(v, HasSql.NoCasts));
    }

    private static string Sql(Value v, int flags)
        => v.GetSql(new StringBuilder(), flags).ToString();

    private static string Describe(byte[] b)
    {
        Value v = ValueBinary.Get(b);
        return new StringBuilder(Sql(v, 0)).Append('|')
            .Append(Sql(v, HasSql.NoCasts)).Append('|')
            .Append(HexText(v.GetString())).Append('|')
            .Append(v.GetBytes() is { Length: > 0 } bytes ? Hex.BytesToHex(bytes) : "-").Append('|')
            .Append(v.GetMemory().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(v.GetValueType().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(v.GetTypeInfo().GetSql(0)).Append('|')
            .Append(v.GetTypeInfo().GetPrecision().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(J(v.ContainsNull())).Append('|')
            .Append(J(v.Equals(v))).Append('|')
            .Append(J(v.Equals(ValueBinary.Get([0x41, 0x42, 0x43])))).Append('|')
            .Append(Compare(b, [0x41, 0x42, 0x43])).Append('|')
            .Append(Compare([0x41, 0x42, 0x43], b))
            .ToString();
    }

    private static string Compare(byte[] a, byte[] b)
    {
        CompareMode off = CompareMode.GetInstance(null, 0);
        return ValueBinary.Get(a).CompareTo(ValueBinary.Get(b), null, off)
            .ToString(CultureInfo.InvariantCulture);
    }

    /// <remarks>
    /// Java concatenates a boolean as "true" and .NET as "True", so every boolean in this file
    /// goes through here rather than through an append.
    /// </remarks>
    private static string J(bool value) => value ? "true" : "false";

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

    private const string Declare = """
                      byte[][] arrays = new byte[][] {
                          {}, {0x41}, {0x41, 0x42}, {0x41, 0x42, 0x43}, {0x7F}, {(byte) 0x80},
                          {(byte) 0xFF}, {0x00, 0x00},
                          {0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
                           0x49, 0x4A, 0x4B, 0x4C},
                          {}
                      };

            """;

    /// <remarks>
    /// One Java program, without lambdas: a snippet that carries its own functions is a snippet
    /// whose types have to be inferred, and javac answers an inference failure as a cascade of
    /// errors about symbols that are perfectly fine.
    /// </remarks>
    private const string Program = """
                      org.h2.value.CompareMode mode = org.h2.value.CompareMode.getInstance(null, 0);
                      org.h2.value.Value probe =
                          org.h2.value.ValueBinary.get(new byte[] { 0x41, 0x42, 0x43 });
                      for (int i = 0; i < arrays.length; i++) {
                        org.h2.value.Value v = org.h2.value.ValueBinary.get(arrays[i]);
                        StringBuilder b = new StringBuilder();
                        b.append(v.getSQL(new StringBuilder(), 0)).append('|');
                        b.append(v.getSQL(new StringBuilder(), org.h2.util.HasSQL.NO_CASTS)).append('|');
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
}
