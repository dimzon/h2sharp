/*
 * Differential coverage for the byte source: what an encoding guess does, and what it does not.
 *
 * The guess is a heuristic on the first bytes, so the cases that matter are the ones where it is
 * wrong: a document that begins with a zero byte and is not UTF-16, a byte order mark that is not
 * one, and UTF-8 that is malformed in each of the ways the reference bothers to check.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Util.Json;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class JsonBytesSourceOracleTests
{
    /// <summary>
    /// UTF-32 in either order, with and without a byte order mark. .NET has no named instances for
    /// the two orders. These are static because the corpus below is a static field as well, and a
    /// static initializer runs before a later static field is assigned.
    /// </summary>
    private static readonly Encoding Utf32BigEndian = new UnicodeEncoding(true, true);

    private static readonly Encoding Utf32LittleEndian = new UnicodeEncoding(false, true);

    private static readonly Encoding Utf32BigEndianNoBom = new UnicodeEncoding(true, false);

    private static readonly Encoding Utf32LittleEndianNoBom = new UnicodeEncoding(false, false);

    /// <summary>
    /// Documents as bytes. The name is what the answer is about; the bytes are what is fed in, and
    /// they are written as they are rather than built from text, because the point is what these
    /// particular bytes mean.
    /// </summary>
    private static readonly (string Name, byte[] Bytes)[] Corpus =
    [
        ("utf8 object", Utf8("{\"a\":1}")),
        ("utf8 array", Utf8("[1,2,3]")),
        ("utf8 empty", Utf8("")),
        ("utf8 whitespace", Utf8("  ")),
        ("utf8 with bom", Utf8Bom("{\"a\":1}")),
        ("utf8 double bom", Utf8("\ufeff{\"a\":1}")),
        ("utf8 non ascii", Utf8("{\"a\":\"привет\"}")),
        ("utf8 emoji", Utf8("[\"🎉\"]")),
        ("utf8 control char", Utf8("[\"x\u0001y\"]")),
        ("utf8 malformed continuation", [0x7b, 0x22, 0x61, 0x22, 0x3a, 0x22, 0xc3, 0x28, 0x22, 0x7d]),
        ("utf8 truncated two byte", [0x5b, 0x22, 0xc3]),
        ("utf8 overlong two byte", [0x5b, 0x22, 0xc0, 0xaf, 0x22, 0x5d]),
        ("utf8 truncated three byte", [0x5b, 0x22, 0xe2, 0x82]),
        ("utf8 overlong three byte", [0x5b, 0x22, 0xe0, 0x80, 0xaf, 0x22, 0x5d]),
        ("utf8 bad continuation three", [0x5b, 0x22, 0xe2, 0x28, 0xac, 0x22, 0x5d]),
        ("utf8 surrogate encoded", [0x5b, 0x22, 0xed, 0xa0, 0xbd, 0x22, 0x5d]),
        ("utf8 too large scalar", [0x5b, 0x22, 0xf7, 0xbf, 0xbf, 0xbf, 0x22, 0x5d]),
        ("utf8 number", Utf8("{\"a\":1.50}")),
        ("utf8 lone surrogate escape", Utf8("[\"\\ud83c\"]")),
        ("utf16be bom object", Utf16BeBom("{\"a\":1}")),
        ("utf16le bom object", Utf16LeBom("{\"a\":1}")),
        ("utf16be no bom object", Utf16Be("{\"a\":1}")),
        ("utf16le no bom object", Utf16Le("{\"a\":1}")),
        ("utf16be non ascii", Utf16BeBom("{\"a\":\"привет\"}")),
        ("utf16le emoji", Utf16LeBom("[\"🎉\"]")),
        ("utf16be bom only", [0xfe, 0xff]),
        ("utf16le bom only", [0xff, 0xfe]),
        ("utf32be bom object", Utf32BigEndian.GetBytes("{\"a\":1}")),
        ("utf32le bom object", Utf32LittleEndian.GetBytes("{\"a\":1}")),
        ("utf32be no bom", Utf32BigEndianNoBom.GetBytes("{\"a\":1}")),
        ("utf32le no bom", Utf32LittleEndianNoBom.GetBytes("{\"a\":1}")),
        ("single byte", [0x31]),
        ("two bytes null first", [0x00, 0x31]),
        ("two bytes non ascii", [0xd0, 0xb1]),
        ("three bytes null null", [0x00, 0x00, 0x31]),
        ("zero then nonzero", [0x00, 0x7b, 0x00, 0x7d]),
        ("utf16 bom odd length", [0xff, 0xfe, 0x7b]),
        ("utf16 bom alone", [0xff, 0xfe]),
        ("fe then non ff", [0xfe, 0x41, 0x00, 0x7d]),
    ];

    /// <summary>
    /// Bytes through the tree and through normalization. The tree notices a wrong string and a wrong
    /// number; normalization notices a wrong byte.
    /// </summary>
    [JavaOracleFact]
    public void TheByteSourceAgreesWithTheReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                Program(),
                numbers: null,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="],
                packageName: "org.h2.util.json")
            .ToList();

        List<string> csharp = [.. Corpus.Select(Describe)];

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(
                java[i] == csharp[i],
                $"line {i}, {Corpus[i].Name}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    private static string Describe((string Name, byte[] Bytes) c)
        => Guard(() => Tree(c.Bytes)) + " / " + Guard(() => HexOf(JsonBytesSource.Normalize(c.Bytes)));

    /// <summary>
    /// The tree, printed through the text target. A byte source that decoded wrongly shows up here
    /// as a replacement character, which is what the reference produces too.
    /// </summary>
    private static string Tree(byte[] bytes)
    {
        JsonValue value = JsonBytesSource.Parse(bytes, new JsonValueTarget());
        JsonStringTarget text = new();
        value.AddTo(text);
        return HexOf(text.Result!);
    }

    private static string HexOf(string text)
    {
        StringBuilder hex = new((text.Length * 4) + 1);
        _ = hex.Append('=');
        foreach (char ch in text)
        {
            _ = hex.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
        }

        return hex.ToString();
    }

    private static string HexOf(byte[] bytes)
    {
        StringBuilder hex = new((bytes.Length * 2) + 1);
        _ = hex.Append('=');
        foreach (byte b in bytes)
        {
            _ = hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return hex.ToString();
    }

    private static string Guard(Func<string> action)
    {
        try
        {
            return action();
        }
        catch (ArgumentException)
        {
            return "THROWS";
        }
        catch (InvalidOperationException)
        {
            return "THROWS";
        }
    }

    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    private static byte[] Utf8Bom(string text) => new UTF8Encoding(true).GetBytes(text);

    private static byte[] Utf16BeBom(string text) => new UnicodeEncoding(true, true).GetBytes(text);

    private static byte[] Utf16LeBom(string text) => new UnicodeEncoding(false, true).GetBytes(text);

    private static byte[] Utf16Be(string text) => new UnicodeEncoding(true, false).GetBytes(text);

    private static byte[] Utf16Le(string text) => new UnicodeEncoding(false, false).GetBytes(text);

    private static string Program()
    {
        StringBuilder rows = new();
        foreach ((string name, byte[] bytes) in Corpus)
        {
            rows.Append("      {").Append(JavaString(name))
                .Append(", \"").Append(Convert.ToHexString(bytes)).Append("\"},\n");
        }

        return $$"""
            {
              String[][] cases = new String[][] {
            {{rows}}              };

              class Runner {
              static String tree(byte[] bytes) {
                try {
                  return hex(org.h2.util.json.JSONBytesSource
                      .parse(bytes, new org.h2.util.json.JSONValueTarget()).toString());
                } catch (RuntimeException e) {
                  return "THROWS";
                }
              }

              static String normalized(byte[] bytes) {
                try {
                  byte[] b = org.h2.util.json.JSONBytesSource.normalize(bytes);
                  StringBuilder text = new StringBuilder();
                  text.append('=');
                  for (byte value : b) {
                    text.append(String.format("%02x", value & 0xff));
                  }
                  return text.toString();
                } catch (RuntimeException e) {
                  return "THROWS";
                }
              }

              static String hex(String text) {
                StringBuilder b = new StringBuilder();
                b.append('=');
                for (int i = 0; i < text.length(); i++) {
                  b.append(String.format("%04x", (int) text.charAt(i)));
                }
                return b.toString();
              }

              static byte[] unhex(String text) {
                byte[] out = new byte[text.length() / 2];
                for (int i = 0; i < out.length; i++) {
                  out[i] = (byte) Integer.parseInt(text.substring(i * 2, i * 2 + 2), 16);
                }
                return out;
              }
              }

              for (int i = 0; i < cases.length; i++) {
                byte[] bytes = Runner.unhex(cases[i][1]);
                System.out.println(Runner.tree(bytes) + " / " + Runner.normalized(bytes));
              }
            }
            """;
    }

    private static string JavaString(string value)
    {
        StringBuilder literal = new("\"");
        foreach (char ch in value)
        {
            literal.Append(ch switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                < ' ' => $"\\u{(int)ch:x4}",
                _ => ch.ToString(),
            });
        }

        return literal.Append('"').ToString();
    }
}
