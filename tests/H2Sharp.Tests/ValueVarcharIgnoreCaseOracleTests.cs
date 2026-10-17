/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for VARCHAR_IGNORECASE.
 *
 * This is the one string type whose identity depends on a case rule rather than on the text, so
 * the corpus is mostly pairs that differ only in case, plus the characters where a case rule is
 * not obvious: the Turkish dotless and dotted i, the German sharp s against SS, the long s, the
 * final sigma against its two other forms, and a cased letter of another script.
 *
 * Two comparisons are here on purpose. Equality is the value's own equalsIgnoreCase, which folds
 * case per code unit. Comparison goes through the collation with the ignore-case flag set, which is
 * a different code path in the reference and a different one here, and both are reported so a
 * divergence in one does not hide behind the other.
 *
 * The hash code is not compared, for the reason given in ValueStringBase: it is not a reproducible
 * number on either side.
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

public sealed class ValueVarcharIgnoreCaseOracleTests
{
    private static readonly string[] Strings =
    [
        "", "a", "A", "abc", "ABC", "AbC", "aBc", "abc ", " abc", "  spaced  ", "it's", "a\"b",
        "line\nbreak", "tab\there", "null", "true", "TRUE", "t", "Y", "yes", "false", "F", "n",
        // The pair that only differs in case, which is what the type is for.
        "Hello", "HELLO", "hello", "heLLo", "MiXeD", "mixed",
        // Where a case rule stops being obvious.
        "ı", "I", "İ", "i", "ss", "SS", "Ss", "ß", "ẞ", "ſ", "s", "S",
        "ς", "σ", "Σ", "ς ", "ΣΣ", "ǆ", "Ǆ", "ǅ", "Ǆǅ",
        "é", "É", "è", "Ё", "ё", "Ж", "ж", "Ä", "ä",
        "日本語", "🎉", "🎈", "日本語 ",
        // Long enough to be quoted in pieces, and long enough to be cached differently.
        new string('a', 100),
        new string('A', 100),
        "a" + new string('b', 50) + "A",
    ];

    [JavaOracleFact]
    public void VarcharIgnoreCase_MatchesReference()
    {
        string body = """
                      for (int i = 0; i < strings.length; i++) {
                        System.out.println(hexText.apply(describe.apply(strings[i])));
                      }
            """;

        List<string> java = OracleRunner.Run(
                [],
                Setup + Declare + body,
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
    /// The value keeps the case it was given, even when an equal value with a different case is
    /// already in the cache.
    /// </summary>
    [Fact]
    public void TheCaseOfTheStringIsKept()
    {
        Assert.Equal("ABC", ValueVarcharIgnoreCase.Get("ABC").GetString());

        // A value is only shared with one whose case matches exactly; an equal one with a
        // different case would render as the wrong text.
        _ = ValueVarcharIgnoreCase.Get("cached");
        Value again = ValueVarcharIgnoreCase.Get("cached");
        _ = ValueVarcharIgnoreCase.Get("CACHED");
        Assert.Equal("cached", again.GetString());
        Assert.Equal("CACHED", ValueVarcharIgnoreCase.Get("CACHED").GetString());
    }

    [Fact]
    public void TheEmptyStringHasItsOwnInstance()
    {
        Assert.Same(ValueVarcharIgnoreCase.Get(string.Empty), ValueVarcharIgnoreCase.Get(string.Empty));
    }

    private static string Describe(string s)
    {
        Value v = ValueVarcharIgnoreCase.Get(s);
        Value other = ValueVarcharIgnoreCase.Get("AbC");
        CompareMode off = CompareMode.GetInstance(null, 0);
        StringBuilder line = new();
        _ = line.Append(v.GetSql(new StringBuilder(), 0)).Append('|')
            .Append(v.GetSql(new StringBuilder(), HasSql.NoCasts)).Append('|')
            .Append(v.GetString()).Append('|')
            .Append(v.GetValueType().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(v.GetTypeInfo().GetSql(0)).Append('|')
            .Append(v.GetTypeInfo().GetPrecision().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(v.ContainsNull() ? "true" : "false").Append('|')
            .Append(v.Equals(v) ? "true" : "false").Append('|')
            .Append(v.Equals(other) ? "true" : "false").Append('|')
            .Append(v.Equals(ValueVarcharIgnoreCase.Get("ss")) ? "true" : "false").Append('|')
            .Append(v.CompareTo(other, null, off).ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(other.CompareTo(v, null, off).ToString(CultureInfo.InvariantCulture));
        return line.ToString();
    }

    private static readonly System.Text.RegularExpressions.Regex TraceSuffix =
        new(@"\s\[\d+-\d+\]", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private const string Declare = """
                      String[] strings = new String[] {
                          "", "a", "A", "abc", "ABC", "AbC", "aBc", "abc ", " abc", "  spaced  ",
                          "it's", "a\"b", "line\nbreak", "tab\there", "null",
                          "true", "TRUE", "t", "Y", "yes", "false", "F", "n",
                          "Hello", "HELLO", "hello", "heLLo", "MiXeD", "mixed",
                          "\u0131", "I", "\u0130", "i", "ss", "SS", "Ss", "\u00df", "\u1e9e",
                          "\u017f", "s", "S",
                          "\u03c2", "\u03c3", "\u03a3", "\u03c2 ", "\u03a3\u03a3",
                          "\u01c6", "\u01c4", "\u01c5", "\u01c4\u01c5",
                          "\u00e9", "\u00c9", "\u00e8", "\u0401", "\u0451", "\u0416", "\u0436",
                          "\u00c4", "\u00e4",
                          "\u65e5\u672c\u8a9e", "\ud83c\udf89", "\ud83c\udf88", "\u65e5\u672c\u8a9e ",
                          "a".repeat(100), "A".repeat(100), "a" + "b".repeat(50) + "A"
                      };

            """;

    private const string Setup = """
                      java.util.function.Function<String, String> hexText = s -> {
                        StringBuilder b = new StringBuilder();
                        for (int i = 0; i < s.length(); i++) {
                          b.append(String.format("%04x", (int) s.charAt(i)));
                        }
                        return b.length() == 0 ? "-" : b.toString();
                      };
                      java.util.function.Function<org.h2.value.Value, String> equality = v -> {
                        try {
                          return v.equals(v) + "|" + v.equals(
                              org.h2.value.ValueVarcharIgnoreCase.get("AbC")) + "|"
                              + v.equals(org.h2.value.ValueVarcharIgnoreCase.get("ss"));
                        } catch (org.h2.message.DbException ex) {
                          return "EX " + ex.getMessage();
                        }
                      };
                      java.util.function.Function<String, String> describe = s -> {
                        org.h2.value.Value v = org.h2.value.ValueVarcharIgnoreCase.get(s);
                        org.h2.value.Value other =
                            org.h2.value.ValueVarcharIgnoreCase.get("AbC");
                        org.h2.value.CompareMode off =
                            org.h2.value.CompareMode.getInstance(null, 0);
                        return v.getSQL(0) + "|"
                            + v.getSQL(org.h2.util.HasSQL.NO_CASTS) + "|"
                            + v.getString() + "|" + v.getValueType() + "|"
                            + v.getType().getSQL(0) + "|" + v.getType().getPrecision() + "|"
                            + v.containsNull() + "|"
                            + equality.apply(v) + "|"
                            + v.compareTo(other, null, off) + "|"
                            + other.compareTo(v, null, off);
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

    [Fact]
    public void HashCodeIsStableAndEqualForEqualValues()
    {
        ValueVarcharIgnoreCase lower = ValueVarcharIgnoreCase.Get("abc");
        ValueVarcharIgnoreCase upper = ValueVarcharIgnoreCase.Get("ABC");
        Assert.Equal(lower.GetHashCode(), upper.GetHashCode());

        // The fold is cached in the value, so a second probe answers without recomputing and
        // still agrees with the first.
        Assert.Equal(lower.GetHashCode(), lower.GetHashCode());
        Assert.Equal(upper.GetHashCode(), upper.GetHashCode());
    }
}
