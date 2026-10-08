/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for CHARACTER VARYING.
 *
 * A string value is where the reference's type is derived from the value rather than the other
 * way round: the precision of a VARCHAR is its own length, so the type is built once and kept.
 * The corpus is therefore mostly strings, and it is chosen for what they exercise: quoting in
 * SQL, the byte encoding, the memory estimate, and every converting accessor with both a value
 * it accepts and text it must refuse.
 *
 * Two things are deliberately not compared. The hash code, because the reference mixes in
 * getClass().hashCode(), which in Java is an identity hash and differs on every run, so it is
 * not a reproducible number on either side. And the Java-only spellings of a double, the
 * trailing type suffix and the hexadecimal literal, which .NET's parser refuses; that gap is
 * written down in the accessor rather than hidden here.
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

public sealed class ValueVarcharOracleTests
{
    private static readonly string[] Strings =
    [
        "", "a", "A", "abc", "ABC", "abc ", " abc", "  spaced  ", "it's", "a\"b",
        "line\nbreak", "tab\there", "юникод", "日本語", "🎉", "null",
        "true", "TRUE", "t", "Y", "yes", "false", "F", "n", "NO",
        "0", "1", "-1", "0.0", "007", "1e5", "1E-5", "+5", " 42 ",
        "1.5e400", "99999999999999999999", "127", "128", "-128", "-129",
        "1.0", "-1.0", "0.1", "NaN", "Infinity", "abc def", "", "0x10", "1d",
        // A surrogate without its partner cannot be encoded at all, and the two platforms put
        // different bytes in its place: the reference writes '?', .NET the replacement character.
        "\uD800", "\uDC00", "a\uD800b", "\uD800\uD800", "\uDBFF\uDFFF",
    ];

    [JavaOracleFact]
    public void Varchar_MatchesReference()
    {
        string body = """
                      for (int i = 0; i < strings.length; i++) {
                        System.out.println(hexText.apply(describe.apply(strings[i])));
                        System.out.println(hexText.apply(accessors.apply(strings[i])));
                      }
            """;

        List<string> java = OracleRunner.Run(
                [],
                Setup + Declare + body,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .Select(Decode)
            .Select(line => TraceSuffix.Replace(line, string.Empty))
            .ToList();

        List<string> csharp = [];
        foreach (string s in Strings)
        {
            csharp.Add(Describe(s));
            csharp.Add(Accessors(s));
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// Equal strings are the same object once they are in the shared cache, and the empty string
    /// has its own instance.
    /// </summary>
    [Fact]
    public void EmptyStringHasItsOwnInstance()
    {
        Assert.Same(ValueVarchar.Empty, ValueVarchar.Get(string.Empty));

        Value first = ValueVarchar.Get("repeated literal");
        Value second = ValueVarchar.Get("repeated literal");
        Assert.True(ReferenceEquals(first, second));
        Assert.True(first.Equals(second));

        // A different string is not equal, and equality is not a substring relation.
        Value other = ValueVarchar.Get("repeated");
        Assert.False(first.Equals(other));
        Assert.False(first.Equals(null));
    }

    private static string Describe(string s)
    {
        Value v = ValueVarchar.Get(s);
        StringBuilder line = new();
        _ = line.Append(v.GetSql(new StringBuilder(), 0)).Append('|')
            .Append(v.GetString()).Append('|')
            .Append(v.GetBytes() is { Length: > 0 } b ? Convert.ToHexString(b).ToLowerInvariant() : "-").Append('|')
            .Append(v.GetMemory().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(v.GetValueType().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(v.GetTypeInfo().GetSql(0)).Append('|')
            .Append(v.GetTypeInfo().GetPrecision().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(J(v.ContainsNull())).Append('|')
            .Append(J(v.Equals(v))).Append('|')
            .Append(J(v.Equals(ValueVarchar.Get("a")))).Append('|')
            .Append(Compare(s));
        return line.ToString();
    }

    /// <remarks>
    /// Java concatenates a boolean as "true" and .NET as "True", so every boolean in this file
    /// goes through here rather than through an append.
    /// </remarks>
    private static string J(bool value) => value ? "true" : "false";

    private static string Compare(string s)
    {
        CompareMode off = CompareMode.GetInstance(null, 0);
        Value a = ValueVarchar.Get(s);
        Value b = ValueVarchar.Get("abc");
        return a.CompareTo(b, null, off).ToString(CultureInfo.InvariantCulture)
            + "|" + b.CompareTo(a, null, off).ToString(CultureInfo.InvariantCulture);
    }

    private static string Accessors(string s)
    {
        Value v = ValueVarchar.Get(s);
        return string.Join(
            "|",
            Get(() => J(v.GetBoolean())),
            Get(() => v.GetByte().ToString(CultureInfo.InvariantCulture)),
            Get(() => v.GetShort().ToString(CultureInfo.InvariantCulture)),
            Get(() => v.GetInt().ToString(CultureInfo.InvariantCulture)),
            Get(() => v.GetLong().ToString(CultureInfo.InvariantCulture)),
            Get(() => JavaNumberFormat.FloatToString(v.GetFloat())),
            Get(() => JavaNumberFormat.DoubleToString(v.GetDouble())));
    }

    private static string Get(Func<string> f)
    {
        try
        {
            return f();
        }
        catch (H2SharpException e)
        {
            return "EX " + e.Message;
        }
    }

    private static readonly System.Text.RegularExpressions.Regex TraceSuffix =
        new(@"\s\[\d+-\d+\]", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private const string Declare = """
                      String[] strings = new String[] {
                          "", "a", "A", "abc", "ABC", "abc ", " abc", "  spaced  ", "it's",
                          "a\"b", "line\nbreak", "tab\there", "\u044e\u043d\u0438\u043a\u043e\u0434",
                          "\u65e5\u672c\u8a9e", "\ud83c\udf89", "null",
                          "true", "TRUE", "t", "Y", "yes", "false", "F", "n", "NO",
                          "0", "1", "-1", "0.0", "007", "1e5", "1E-5", "+5", " 42 ",
                          "1.5e400", "99999999999999999999", "127", "128", "-128", "-129",
                          "1.0", "-1.0", "0.1", "NaN", "Infinity", "abc def", "", "0x10", "1d",
                          "\uD800", "\uDC00", "a\uD800b", "\uD800\uD800", "\uDBFF\uDFFF"
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
                      java.util.function.Function<byte[], String> hexBytes = b -> {
                        StringBuilder x = new StringBuilder();
                        for (int i = 0; i < b.length; i++) {
                          x.append(String.format("%02x", b[i] & 0xff));
                        }
                        return x.length() == 0 ? "-" : x.toString();
                      };
                      java.util.function.Function<org.h2.value.Value, String> get =
                          v -> {
                            try {
                              return v.getBoolean() ? "true" : "false";
                            } catch (org.h2.message.DbException ex) {
                              return "EX " + ex.getMessage();
                            }
                          };
                      java.util.function.Function<org.h2.value.Value, String> getByte =
                          v -> {
                            try {
                              return String.valueOf(v.getByte());
                            } catch (org.h2.message.DbException ex) {
                              return "EX " + ex.getMessage();
                            }
                          };
                      java.util.function.Function<org.h2.value.Value, String> getShort =
                          v -> {
                            try {
                              return String.valueOf(v.getShort());
                            } catch (org.h2.message.DbException ex) {
                              return "EX " + ex.getMessage();
                            }
                          };
                      java.util.function.Function<org.h2.value.Value, String> getInt =
                          v -> {
                            try {
                              return String.valueOf(v.getInt());
                            } catch (org.h2.message.DbException ex) {
                              return "EX " + ex.getMessage();
                            }
                          };
                      java.util.function.Function<org.h2.value.Value, String> getLong =
                          v -> {
                            try {
                              return String.valueOf(v.getLong());
                            } catch (org.h2.message.DbException ex) {
                              return "EX " + ex.getMessage();
                            }
                          };
                      java.util.function.Function<org.h2.value.Value, String> getFloat =
                          v -> {
                            try {
                              return Float.toString(v.getFloat());
                            } catch (org.h2.message.DbException ex) {
                              return "EX " + ex.getMessage();
                            }
                          };
                      java.util.function.Function<org.h2.value.Value, String> getDouble =
                          v -> {
                            try {
                              return Double.toString(v.getDouble());
                            } catch (org.h2.message.DbException ex) {
                              return "EX " + ex.getMessage();
                            }
                          };
                      java.util.function.Function<String, String> describe = s -> {
                        org.h2.value.Value v = org.h2.value.ValueVarchar.get(s);
                        org.h2.value.CompareMode off =
                            org.h2.value.CompareMode.getInstance(null, 0);
                        org.h2.value.Value other = org.h2.value.ValueVarchar.get("abc");
                        return v.getSQL(0) + "|" + v.getString() + "|"
                            + hexBytes.apply(v.getBytes()) + "|" + v.getMemory() + "|"
                            + v.getValueType() + "|" + v.getType().getSQL(0) + "|"
                            + v.getType().getPrecision() + "|" + v.containsNull() + "|"
                            + v.equals(v) + "|" + v.equals(org.h2.value.ValueVarchar.get("a")) + "|"
                            + v.compareTo(other, null, off) + "|" + other.compareTo(v, null, off);
                      };
                      java.util.function.Function<String, String> accessors = s -> {
                        org.h2.value.Value v = org.h2.value.ValueVarchar.get(s);
                        return get.apply(v) + "|" + getByte.apply(v) + "|" + getShort.apply(v) + "|"
                            + getInt.apply(v) + "|" + getLong.apply(v) + "|" + getFloat.apply(v) + "|"
                            + getDouble.apply(v);
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
