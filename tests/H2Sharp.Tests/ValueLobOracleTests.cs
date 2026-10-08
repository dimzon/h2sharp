/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for BLOB and CLOB.
 *
 * The corpus is text and bytes that are not all ASCII, because a LOB is the one value where the
 * two lengths differ and where that difference is visible: a BLOB counts bytes and a CLOB counts
 * characters, and each has to answer the other's question by reading. Cases where the text is one
 * character wider than its bytes, or three times wider, are the ones that catch a count that is
 * really the other one.
 *
 * Not compared: the hash code, because the reference mixes in nothing here but for a large value
 * it hashes the length alone, and for a small one it hashes the bytes with a function of its own.
 * Equality is compared, and so are the two lengths.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]

public sealed class ValueLobOracleTests
{
    private static readonly string[] Texts =
    [
        "",
        "a",
        "abc",
        "hello world",
        "abcabcabcabcabcabc",
        new string('x', 300),
        "привет",
        "日本語のテキスト",
        "🎉🎈🎉",
        "line\nbreak\ttab",
        "it's \"quoted\"",
        new string('я', 200),
        "mixed ascii и кириллица 123",
        new string([(char)0, (char)1, (char)127]),
        "a" + new string('b', 500) + "c",
    ];


    [JavaOracleFact]
    public void BlobsMatchTheReference()
    {
        string body = """
                      for (int i = 0; i < inputs.length; i++) {
                        System.out.println(hexText.apply(blob.apply(inputs[i])));
                      }
            """;

        List<string> java = OracleRunner.Run(
                [],
                Setup + DeclareBlob + body,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .Select(Decode)
            .Select(line => TraceSuffix.Replace(line, string.Empty))
            .ToList();

        List<string> csharp = Texts.Select(s => DescribeBlob(s)).ToList();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [JavaOracleFact]
    public void ClobsMatchTheReference()
    {
        string body = """
                      for (int i = 0; i < inputs.length; i++) {
                        System.out.println(hexText.apply(clob.apply(inputs[i])));
                      }
            """;

        List<string> java = OracleRunner.Run(
                [],
                Setup + DeclareClob + body,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .Select(Decode)
            .Select(line => TraceSuffix.Replace(line, string.Empty))
            .ToList();

        List<string> csharp = Texts.Select(s => DescribeClob(s)).ToList();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [Fact]
    public void AClobIsItsOwnText()
    {
        ValueClob clob = ValueClob.CreateSmall("привет");
        Assert.Equal("привет", clob.GetString());
        Assert.Equal(6, clob.CharLength());
        Assert.Equal(12, clob.OctetLength());
        Assert.Equal(Encoding.UTF8.GetBytes("привет"), clob.GetBytes());
        // Comparing, on the other hand, is by text, so two of them differing only in case are the
        // same for a comparison. That is the reference's answer, and it is the surprise: the
        // equality above says they are not equal, because equality starts with a hash of the bytes.
        // The order is the ordinary one on code units, so the upper-case text comes first.
        Assert.Equal(
            1,
            clob.CompareTo(ValueClob.CreateSmall("ПРИВЕТ"), null, CompareMode.GetInstance(null, 0)));
        Assert.True(clob.Equals(ValueClob.CreateSmall("привет")));
    }

    [Fact]
    public void ABlobAnswersBothQuestions()
    {
        ValueBlob blob = ValueBlob.CreateSmall(Encoding.UTF8.GetBytes("привет"));

        // A BLOB counts bytes and has to read the text to answer in characters, and it remembers
        // the answer.
        Assert.Equal(12, blob.OctetLength());
        Assert.Equal(6, blob.CharLength());
        Assert.Equal(6, blob.CharLength());
        Assert.Equal("привет", blob.GetString());
    }

    [Fact]
    public void TwoSmallValuesAreEqualWhenTheySayTheSameThing()
    {
        Assert.True(ValueClob.CreateSmall("abc").Equals(ValueClob.CreateSmall("abc")));
        Assert.False(ValueClob.CreateSmall("abc").Equals(ValueClob.CreateSmall("abd")));
        Assert.False(ValueClob.CreateSmall("abc").Equals(ValueBlob.CreateSmall([1, 2, 3])));
        Assert.False(ValueClob.CreateSmall("abc").Equals(null));

        // Two CLOBs that differ only in case are not equal, even though comparing them says they
        // are the same text: equality here starts with the hash, and the hash is of the bytes.
        // That is the reference's answer, and it is worth pinning because it is surprising.
        Assert.False(ValueClob.CreateSmall("abc").Equals(ValueClob.CreateSmall("ABC")));

        Assert.True(ValueBlob.CreateSmall([1, 2, 3]).Equals(ValueBlob.CreateSmall([1, 2, 3])));
        Assert.False(ValueBlob.CreateSmall([1, 2, 3]).Equals(ValueBlob.CreateSmall([1, 2, 4])));
    }

    [Fact]
    public void TheBytesOfASmallValueAreNotTheValueItself()
    {
        ValueClob clob = ValueClob.CreateSmall("abc");

        // Asking with a copy hands out a copy, and changing it does not reach the value.
        byte[]? copy = clob.GetBytes();
        Assert.NotNull(copy);
        copy![0] = (byte)'z';
        Assert.Equal("abc", clob.GetString());

        // Asking without one hands out the value's own array, which is the whole point of the
        // name: nothing is copied, so a caller that writes to it writes to the value. The
        // reference answers the same, and that is why this is pinned rather than assumed.
        byte[]? direct = clob.GetBytesNoCopy();
        Assert.NotNull(direct);
        direct![0] = (byte)'y';
        Assert.Equal("ybc", clob.GetString());
    }

    [Fact]
    public void AWindowOfTheValueIsAWindowOfIt()
    {
        ValueBlob blob = ValueBlob.CreateSmall("0123456789"u8.ToArray());

        using Stream window = blob.GetInputStream(3, 4);

        // Offsets are one-based: three means the third byte, so the window starts at "2345".
        Assert.Equal("2345", ReadAll(window));

        // The window of characters, likewise one-based: two starts at the second character.
        using TextReader text = blob.GetReader(2, 3);
        Assert.Equal("123", text.ReadToEnd());
    }

    [Fact]
    public void AWindowOutsideTheValueIsRefused()
    {
        ValueBlob blob = ValueBlob.CreateSmall("0123456789"u8.ToArray());

        Assert.ThrowsAny<H2SharpException>(() => blob.GetInputStream(1, 100));
        Assert.ThrowsAny<H2SharpException>(() => blob.GetInputStream(0, 1));

        // The window of characters is a different matter: a BLOB does not know how many characters
        // its bytes are without reading them, so the reference does not check that window and we
        // do not either. Reading it answers as much as there is.
        using TextReader window = blob.GetReader(1, 100);
        Assert.Equal("0123456789", window.ReadToEnd());
    }

    [Fact]
    public void ABigValueIsHashedByItsLength()
    {
        // A LOB too long to read for a hash is hashed by how long it is, which is cheap and means
        // two big values of one length start out equal.
        ValueClob big = ValueClob.CreateSmall(new string('x', 5000));
        Assert.Equal(5000, big.CharLength());
        Assert.Equal(5000, ValueClob.CreateSmall(new string('x', 5000)).GetHashCode());
    }

    [Fact]
    public void TheMemoryOfASmallValueIsItsBytesAndTheOverhead()
    {
        ValueClob clob = ValueClob.CreateSmall("abc");
        Assert.Equal(3 + 127, clob.GetMemory());

        // And data that is not held in memory costs what the base says it costs: the bytes are not
        // in the value, so only the value is.
        Assert.Equal(140, new ValueClob(new Elsewhere(), 0, 0).GetMemory());
    }

    [Fact]
    public void ASmallValueDoesNotNeedTheStorageToBeCopied()
    {
        // Copying is a question for the storage, and a value that holds its own data has no answer
        // to give and no need to.
        ValueClob clob = ValueClob.CreateSmall("abc");
        Assert.Same(clob, clob.Copy(null!, 1));
        ValueBlob blob = ValueBlob.CreateSmall([1]);
        Assert.Same(blob, blob.Copy(null!, 1));
    }

    /// <summary>
    /// Data that is not in memory at all, to see what the base itself costs.
    /// </summary>
    private sealed class Elsewhere : Values.Lobs.LobData
    {
        public override Stream GetInputStream(long precision) => new MemoryStream();
    }

    /// <summary>
    /// The bytes as the oracle prints them: a dash when there are none, which is what the
    /// reference's own hex helper answers for an empty array.
    /// </summary>
    private static string HexBytes(byte[] bytes) => bytes.Length == 0 ? "-" : Hex.BytesToHex(bytes);

    private static string ReadAll(Stream stream)
    {
        using MemoryStream target = new();
        stream.CopyTo(target);
        return Encoding.UTF8.GetString(target.ToArray());
    }


    private static string DescribeBlob(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        ValueBlob blob = ValueBlob.CreateSmall(bytes);
        CompareMode off = CompareMode.GetInstance(null, 0);
        StringBuilder line = new();
        _ = line.Append(blob.GetString()).Append('|')
            .Append(HexBytes(blob.GetBytes() ?? [])).Append('|')
            .Append(blob.GetValueType().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(blob.CharLength().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(blob.GetTypeInfo().GetSql(0)).Append('|')
            .Append(blob.GetTypeInfo().GetPrecision().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(blob.ContainsNull() ? "true" : "false").Append('|')
            .Append(blob.Equals(blob) ? "true" : "false").Append('|')
            .Append(blob.Equals(ValueBlob.CreateSmall(bytes)) ? "true" : "false").Append('|')
            .Append(blob.CompareTo(ValueBlob.CreateSmall(bytes), null, off).ToString(CultureInfo.InvariantCulture))
            .Append('|')
            .Append(blob.CompareTo(ValueBlob.CreateSmall(Encoding.UTF8.GetBytes("different")), null, off)
                .ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(blob.GetMemory().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(blob.GetSql(new StringBuilder(), 0)).Append('|')
            .Append(blob.GetSql(new StringBuilder(), HasSql.NoCasts));
        return line.ToString();
    }

    private static string DescribeClob(string text)
    {
        ValueClob clob = ValueClob.CreateSmall(text);
        CompareMode off = CompareMode.GetInstance(null, 0);
        StringBuilder line = new();
        _ = line.Append(clob.GetString()).Append('|')
            .Append(HexBytes(clob.GetBytes() ?? [])).Append('|')
            .Append(clob.GetValueType().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(clob.CharLength().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(clob.OctetLength().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(clob.GetTypeInfo().GetSql(0)).Append('|')
            .Append(clob.GetTypeInfo().GetPrecision().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(clob.ContainsNull() ? "true" : "false").Append('|')
            .Append(clob.Equals(clob) ? "true" : "false").Append('|')
            .Append(clob.Equals(ValueClob.CreateSmall(text)) ? "true" : "false").Append('|')
            .Append(clob.CompareTo(ValueClob.CreateSmall(text), null, off).ToString(CultureInfo.InvariantCulture))
            .Append('|')
            .Append(clob.CompareTo(ValueClob.CreateSmall("different"), null, off).ToString(CultureInfo.InvariantCulture))
            .Append('|')
            .Append(clob.GetMemory().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(clob.GetSql(new StringBuilder(), 0)).Append('|')
            .Append(clob.GetSql(new StringBuilder(), HasSql.NoCasts));
        return line.ToString();
    }

    private static readonly System.Text.RegularExpressions.Regex TraceSuffix =
        new(@"\s\[\d+-\d+\]", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private const string Setup = """
                      java.util.function.Function<String, String> hexText = s -> {
                        StringBuilder b = new StringBuilder();
                        for (int i = 0; i < s.length(); i++) {
                          b.append(String.format("%04x", (int) s.charAt(i)));
                        }
                        return b.length() == 0 ? "-" : b.toString();
                      };
                      java.util.function.Function<byte[], String> hexBytes = bb -> {
                        StringBuilder x = new StringBuilder();
                        for (int i = 0; i < bb.length; i++) {
                          x.append(String.format("%02x", bb[i] & 0xff));
                        }
                        return x.length() == 0 ? "-" : x.toString();
                      };                      org.h2.value.CompareMode off =
                          org.h2.value.CompareMode.getInstance(null, 0);
                      java.util.function.Function<String, String> blob = s -> {
                        org.h2.value.ValueBlob v = org.h2.value.ValueBlob.createSmall(
                            s.getBytes(java.nio.charset.StandardCharsets.UTF_8));
                        org.h2.value.ValueBlob same = org.h2.value.ValueBlob.createSmall(
                            s.getBytes(java.nio.charset.StandardCharsets.UTF_8));
                        org.h2.value.ValueBlob other = org.h2.value.ValueBlob.createSmall(
                            "different".getBytes(java.nio.charset.StandardCharsets.UTF_8));
                        StringBuilder b = new StringBuilder();
                        b.append(v.getString()).append('|');
                        b.append(hexBytes.apply(v.getBytes())).append('|');
                        b.append(v.getValueType()).append('|');
                        b.append(v.charLength()).append('|');
                        b.append(v.getType().getSQL(0)).append('|');
                        b.append(v.getType().getPrecision()).append('|');
                        b.append(v.containsNull()).append('|');
                        b.append(v.equals(v)).append('|');
                        b.append(v.equals(same)).append('|');
                        b.append(v.compareTo(same, null, off)).append('|');
                        b.append(v.compareTo(other, null, off)).append('|');
                        b.append(v.getMemory()).append('|');
                        b.append(v.getSQL(0)).append('|');
                        b.append(v.getSQL(org.h2.util.HasSQL.NO_CASTS));
                        return b.toString();
                      };

                      java.util.function.Function<String, String> clob = s -> {
                        org.h2.value.ValueClob v = org.h2.value.ValueClob.createSmall(s);
                        org.h2.value.ValueClob same = org.h2.value.ValueClob.createSmall(s);
                        org.h2.value.ValueClob other = org.h2.value.ValueClob.createSmall("different");
                        StringBuilder b = new StringBuilder();
                        b.append(v.getString()).append('|');
                        b.append(hexBytes.apply(v.getBytes())).append('|');
                        b.append(v.getValueType()).append('|');
                        b.append(v.charLength()).append('|');
                        b.append(v.octetLength()).append('|');
                        b.append(v.getType().getSQL(0)).append('|');
                        b.append(v.getType().getPrecision()).append('|');
                        b.append(v.containsNull()).append('|');
                        b.append(v.equals(v)).append('|');
                        b.append(v.equals(same)).append('|');
                        b.append(v.compareTo(same, null, off)).append('|');
                        b.append(v.compareTo(other, null, off)).append('|');
                        b.append(v.getMemory()).append('|');
                        b.append(v.getSQL(0)).append('|');
                        b.append(v.getSQL(org.h2.util.HasSQL.NO_CASTS));
                        return b.toString();
                      };

            """;

    private const string DeclareBlob = """
                      String[] inputs = new String[] {
                          "", "a", "abc", "hello world", "abcabcabcabcabcabc",
                          "x".repeat(300),
                          "\u043f\u0440\u0438\u0432\u0435\u0442",
                          "\u65e5\u672c\u8a9e\u306e\u30c6\u30ad\u30b9\u30c8",
                          "\ud83c\udf89\ud83c\udf88\ud83c\udf89",
                          "line\nbreak\ttab", "it's \"quoted\"",
                          "\u044f".repeat(200),
                          "mixed ascii \u0438 \u043a\u0438\u0440\u0438\u043b\u043b\u0438\u0446\u0430 123",
                          "\u0000\u0001\u007f", "a" + "b".repeat(500) + "c"
                      };

            """;

    private const string DeclareClob = DeclareBlob;

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
            _ = sb.Append((char)Convert.ToInt32(only.Substring(i, 4), 16));
        }

        return sb.ToString();
    }
}
