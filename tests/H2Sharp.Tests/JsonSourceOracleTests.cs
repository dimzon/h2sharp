/*
 * Differential coverage for the parser: what a document is allowed to look like, and what a
 * target makes of it.
 *
 * The parser is where JSON is decided rather than rendered, and the decisions are not the ones a
 * reading of the specification would produce: leading zeros are accepted, an unescaped control
 * character inside a string is accepted, a form feed is not whitespace, and a document may be
 * several values in a row. Those are only knowable from the reference.
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
public sealed class JsonSourceOracleTests
{
    /// <summary>
    /// Documents written the way they are written, so the malformed ones stay readable. The name is
    /// what the answer is about, not part of the comparison.
    /// </summary>
    private static readonly (string Name, string Document)[] Corpus =
    [
        ("object", "{\"a\":1}"),
        ("empty object", "{}"),
        ("array", "[1,2,3]"),
        ("empty array", "[]"),
        ("nested", "{\"a\":[1,{\"b\":2}]}"),
        ("deeply nested", "[[[[1]]]]"),
        ("repeated keys", "{\"a\":1,\"a\":2}"),
        ("different keys", "{\"a\":1,\"b\":2}"),
        ("whitespace everywhere", " \t\r\n{ \"a\" \t: \r 1 } \n "),
        ("empty", ""),
        ("whitespace only", "   "),
        ("bom then object", "\ufeff{\"a\":1}"),
        ("two boms", "\ufeff\ufeff{\"a\":1}"),
        ("bom only", "\ufeff"),
        ("trailing whitespace", "{\"a\":1}  \n"),
        ("leading whitespace", "  {\"a\":1}"),
        ("leading comma", ",{\"a\":1}"),
        ("trailing comma object", "{\"a\":1,}"),
        ("trailing comma array", "[1,2,]"),
        ("double comma", "[1,,2]"),
        ("comma only", ","),
        ("two documents", "{\"a\":1}{\"b\":2}"),
        ("value after array", "[1] 2"),
        ("trailing garbage", "{\"a\":1} x"),
        ("missing colon", "{\"a\" 1}"),
        ("missing value", "{\"a\":}"),
        ("name without value", "{\"a\""),
        ("unquoted name", "{a:1}"),
        ("single quotes", "{'a':1}"),
        ("empty name", "{\"\":1}"),
        ("bare name", "{\"a\"}"),
        ("line comment", "// c\n{\"a\":1}"),
        ("block comment", "/* c */{\"a\":1}"),
        ("nan", "{\"a\":NaN}"),
        ("infinity", "{\"a\":Infinity}"),
        ("true", "true"),
        ("false", "false"),
        ("null", "null"),
        ("bare string", "\"text\""),
        ("bare number", "42"),
        ("number zero", "0"),
        ("number leading zero", "01"),
        ("number many zeros", "000123"),
        ("number negative", "-1.5"),
        ("number positive sign", "+1"),
        ("number trailing dot", "1."),
        ("number dot exponent", "1.e5"),
        ("number dot then digits", "1.5"),
        ("number exponent upper", "1E5"),
        ("number exponent lower", "1e5"),
        ("number exponent plus", "1E+10"),
        ("number exponent minus", "1E-10"),
        ("number exponent empty", "1E"),
        ("number exponent signs", "1E+-2"),
        ("number scale kept", "1.50"),
        ("number big", "123456789012345678901234567890"),
        ("number then letters", "1abc"),
        ("string escapes", "[\"\\b\\f\\n\\r\\t\\\\\\/\"]"),
        ("string quote escape", "[\"a\\\"b\"]"),
        ("string unicode escape", "[\"\\u0041\"]"),
        ("string unicode lowercase", "[\"\\u00e9\"]"),
        ("string unicode too short", "[\"\\u041\"]"),
        ("string unicode not hex", "[\"\\uZZZZ\"]"),
        ("string bad escape", "[\"\\q\"]"),
        ("string lone backslash", "[\"\\\"]"),
        ("string raw control char", "[\"x\u0001y\"]"),
        ("string raw tab", "[\"x\ty\"]"),
        ("string raw form feed", "[\"x\u000cy\"]"),
        ("string nbsp", "[\"x\u00a0y\"]"),
        ("string surrogate pair", "[\"\\ud83c\\udf89\"]"),
        // The two lone surrogates are built from their code units rather than written as \u escapes:
        // a lone surrogate in a C# literal is a thing the compiler is entitled to complain about, and
        // the point of the case is what the parser does with it, not how the source was spelled.
        ("string lone high surrogate", "[\"\\ud83c\"]"),
        ("string lone low surrogate", "[\"\\udf89\"]"),
        ("string raw emoji", "[\"🎉\"]"),
        ("string unterminated", "[\"abc"),
        ("string empty", "\"\""),
    ];

    /// <summary>
    /// Every document through every target that can produce something from one. The tree is the
    /// strictest check: it notices a number that came out different, where a validator would only
    /// say that there was a number.
    /// </summary>
    [JavaOracleFact]
    public void TheParserAgreesWithTheReference()
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
                $"line {i}, {Corpus[i].Name}, {Show(Corpus[i].Document)}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// A document through the value tree, the byte normalization and the validator.
    /// </summary>
    /// <param name="c">the document</param>
    /// <returns>what each of them made of it</returns>
    private static string Describe((string Name, string Document) c)
    {
        return Guard(() => Tree(c.Document))
            + " / " + Guard(() => HexOfBytes(JsonStringSource.Normalize(c.Document)))
            + " / " + Guard(() =>
                JsonStringSource.Parse(c.Document, new JsonValidationTargetWithoutUniqueKeys()).ToString().ToLowerInvariant());
    }

    private static string Tree(string document)
    {
        // The target, not the parsed value: JsonValueTarget holds a tree whose ToString is one
        // rendering of it, and the reference column is the tree printed through the text target.
        // Comparing the two would test the printer twice and the parser not at all.
        JsonValue value = JsonStringSource.Parse(document, new JsonValueTarget());
        JsonStringTarget text = new();
        value.AddTo(text);
        return HexOf(text.Result!);
    }

    /// <summary>
    /// UTF-16 code units as hex. A surrogate pair is two units here and would be reassembled by any
    /// console, which would compare the console's reading instead of the bytes.
    /// </summary>
    private static string HexOf(string text)
    {
        StringBuilder hex = new(text.Length * 4 + 1);
        _ = hex.Append('=');
        foreach (char ch in text)
        {
            _ = hex.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
        }

        return hex.ToString();
    }

    /// <summary>Bytes as hex, which is what the normalized form is.</summary>
    private static string HexOfBytes(byte[] bytes)
    {
        StringBuilder hex = new((bytes.Length * 2) + 1);
        _ = hex.Append('=');
        foreach (byte b in bytes)
        {
            _ = hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return hex.ToString();
    }

    /// <summary>
    /// A rejected document is reported as THROWS on both sides. Two exception types are caught and
    /// that is deliberate, not sloppiness: the source throws for a malformed document and the
    /// target throws for one that is malformed structurally, and the reference distinguishes them
    /// exactly this way. Catching one and not the other would make a document that only one of them
    /// rejects look like a difference in the port.
    /// </summary>
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

    /// <summary>The document, readable even when it is a control character.</summary>
    private static string Show(string document)
    {
        StringBuilder shown = new();
        foreach (char ch in document)
        {
            shown.Append(ch switch
            {
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' => $"\\u{(int)ch:x4}",
                _ => ch.ToString(),
            });
        }

        return shown.Length == 0 ? "(empty)" : shown.ToString();
    }

    private static string Program()
    {
        StringBuilder rows = new();
        foreach ((string name, string document) in Corpus)
        {
            rows.Append("      {").Append(JavaString(name))
                .Append(", ").Append(JavaString(document)).Append("},\n");
        }

        return $$"""
            {
              String[][] cases = new String[][] {
            {{rows}}              };

              // The harness places the body inside its own try, where a method cannot be declared,
              // so the helpers live in a local class. It is declared before the loop because a local
              // class is in scope only from its declaration onwards.
              class Runner {
              static String tree(String document) {
                try {
                  return hex(org.h2.util.json.JSONStringSource
                      .parse(document, new org.h2.util.json.JSONValueTarget()).toString());
                } catch (RuntimeException e) {
                  return "THROWS";
                }
              }

              static String bytes(String document) {
                try {
                  byte[] b = org.h2.util.json.JSONStringSource.normalize(document);
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

              static String kind(String document) {
                try {
                  return org.h2.util.json.JSONStringSource.parse(
                      document, new org.h2.util.json.JSONValidationTargetWithoutUniqueKeys())
                      .toString().toLowerCase(java.util.Locale.ROOT);
                } catch (RuntimeException e) {
                  return "THROWS";
                }
              }

              // UTF-16 code units as hex, so that a surrogate pair survives the trip: two units
              // here, and any console would reassemble it into one character.
              static String hex(String text) {
                StringBuilder b = new StringBuilder();
                b.append('=');
                for (int i = 0; i < text.length(); i++) {
                  b.append(String.format("%04x", (int) text.charAt(i)));
                }
                return b.toString();
              }
              }

              for (int i = 0; i < cases.length; i++) {
                System.out.println(Runner.tree(cases[i][1]) + " / " + Runner.bytes(cases[i][1])
                    + " / " + Runner.kind(cases[i][1]));
              }
            }
            """;
    }

    private static string JavaString(string value)
    {
        StringBuilder literal = new("\"");
        foreach (char ch in value)
        {
            switch (ch)
            {
                case '\\': literal.Append("\\\\"); break;
                case '"': literal.Append("\\\""); break;
                case '\n': literal.Append("\\n"); break;
                case '\r': literal.Append("\\r"); break;
                case '\t': literal.Append("\\t"); break;
                case '\b': literal.Append("\\b"); break;
                case '\f': literal.Append("\\f"); break;
                default:
                    literal.Append(ch < ' ' || ch > '\u007e'
                        ? "\\u" + ((int)ch).ToString("x4", CultureInfo.InvariantCulture)
                        : ch.ToString());
                    break;
            }
        }

        return literal.Append('"').ToString();
    }
}
