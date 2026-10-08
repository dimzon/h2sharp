/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for CHARACTER.
 *
 * A fixed length string is the varying type with two differences, and both are about the length.
 * The SQL spelling carries a CAST, because a quoted string is a VARCHAR, and the length in that
 * CAST is at least 1, so the empty value renders as CHAR(1) while its own precision is 0. And the
 * comparison drops trailing spaces, because that is the padding of a fixed length type: the
 * reference converts both sides to CHAR before comparing, and that conversion trims, so 'a' and
 * 'a ' are the same CHAR. A corpus without trailing spaces would pass against a plain string
 * comparison and mean nothing.
 *
 * There is no empty value of its own here, unlike the varying type, and the corpus says so.
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

public sealed class ValueCharOracleTests
{
    private static readonly string[] Strings =
    [
        "", " ", "  ", "a", "a ", "a  ", " a", " a ", "ab", "ab ", " a b ",
        "A", "A ", "a", "abc", "abc ", "abc  ", " ", "ab ", "  ab",
        "it's", "a\"b", "tab\there", "юникод", "日本語", "🎉",
        "x", "x ", "xx ", " x", "x  y  ",
    ];

    [JavaOracleFact]
    public void Char_MatchesReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                Declare + Program,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .Select(Decode)
            .Select(line => TraceSuffix.Replace(line, string.Empty))
            .ToList();

        List<string> csharp = Strings.Select(Describe).ToList();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// The padding is not visible in the SQL or in the precision, only in the comparison, which is
    /// exactly where a port that compared the strings directly would be wrong.
    /// </summary>
    [Fact]
    public void TrailingSpacesDoNotMakeValuesDiffer()
    {
        CompareMode off = CompareMode.GetInstance(null, 0);

        Assert.Equal(0, Compare("a", "a ", off));
        Assert.Equal(0, Compare("a", "a   ", off));
        Assert.Equal(0, Compare("", "   ", off));
        Assert.Equal(0, Compare("ab", "ab  ", off));
        Assert.Equal(-1, Compare("a", "ab", off));

        // The answer is a difference and not a sign, because that is what the reference's string
        // comparison returns: 'a' is 97 and a space is 32, so comparing them gives 65 and not -1.
        Assert.Equal(65, Compare("a", " a", off));
        Assert.Equal(1, Math.Sign(Compare("a", " a", off)));

        // A longer string on the other side is compared as CHAR, so its padding goes too.
        Assert.Equal(0, Compare("a", "a  ", off));
        Assert.Equal(0, ValueChar.Get("a  ").CompareTo(ValueVarchar.Get("a"), null, off));

        static int Compare(string a, string b, CompareMode mode)
            => ValueChar.Get(a).CompareTo(ValueChar.Get(b), null, mode);
    }

    [Fact]
    public void TheEmptyValueIsNotShared()
    {
        Value first = ValueChar.Get("");
        Value second = ValueChar.Get("");

        Assert.Equal("CAST('' AS CHAR(1))", Sql(first, 0));
        Assert.Equal(0, first.GetTypeInfo().GetPrecision());
        Assert.Equal(3, ValueChar.Get("   ").GetTypeInfo().GetPrecision());

        // Equal after the padding is dropped, and a different type is never equal.
        Assert.True(first.Equals(ValueChar.Get("")));
        Assert.False(first.Equals(ValueChar.Get(" ")));
        Assert.False(first.Equals(ValueVarchar.Get("")));
        Assert.False(first.Equals(null));
    }

    private static string Sql(Value v, int flags) => v.GetSql(new StringBuilder(), flags).ToString();

    private static string Describe(string s)
    {
        Value v = ValueChar.Get(s);
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
            .Append(J(v.Equals(ValueChar.Get("a")))).Append('|')
            .Append(Compare(s, "a")).Append('|')
            .Append(Compare("a", s)).Append('|')
            .Append(v.CompareTo(ValueVarchar.Get(s), null, CompareMode.GetInstance(null, 0))
                .ToString(CultureInfo.InvariantCulture))
            .ToString();
    }

    private static string Compare(string a, string b)
    {
        CompareMode off = CompareMode.GetInstance(null, 0);
        return ValueChar.Get(a).CompareTo(ValueChar.Get(b), null, off)
            .ToString(CultureInfo.InvariantCulture);
    }

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
                      String[] strings = new String[] {
                          "", " ", "  ", "a", "a ", "a  ", " a", " a ", "ab", "ab ", " a b ",
                          "A", "A ", "a", "abc", "abc ", "abc  ", " ", "ab ", "  ab",
                          "it's", "a\"b", "tab\there", "\u044e\u043d\u0438\u043a\u043e\u0434",
                          "\u65e5\u672c\u8a9e", "\ud83c\udf89", "x", "x ", "xx ", " x", "x  y  "
                      };

            """;

    /// <remarks>
    /// One Java program, without lambdas: a snippet that carries its own functions is a snippet
    /// whose types have to be inferred, and javac answers an inference failure as a cascade of
    /// errors about symbols that are perfectly fine.
    /// </remarks>
    private const string Program = """
                      org.h2.value.CompareMode mode = org.h2.value.CompareMode.getInstance(null, 0);
                      for (int i = 0; i < strings.length; i++) {
                        org.h2.value.Value v = org.h2.value.ValueChar.get(strings[i]);
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
                        b.append(v.equals(org.h2.value.ValueChar.get("a"))).append('|');
                        b.append(v.compareTo(org.h2.value.ValueChar.get("a"), null, mode)).append('|');
                        b.append(org.h2.value.ValueChar.get("a").compareTo(v, null, mode)).append('|');
                        b.append(v.compareTo(
                            org.h2.value.ValueVarchar.get(strings[i]), null, mode));
                        System.out.println(hex(b.toString()));
                      }
            """;
}
