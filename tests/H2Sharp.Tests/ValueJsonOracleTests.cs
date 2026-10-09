/*
 * Differential coverage for the JSON data type: the four ways in, and what the value looks like
 * afterwards.
 *
 * The interesting part is that a JSON column holds normalized bytes, so the question is never "did it
 * parse" but "what bytes did normalization leave behind". A document that differs only in whitespace
 * must come out as the same value, and one that differs in a digit must not.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Util.Json;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ValueJsonOracleTests
{
    /// <summary>
    /// Documents as text, plus the ways of making a value that do not go through text at all. The
    /// name is what the answer is about.
    /// </summary>
    private static readonly (string Name, string Document)[] Corpus =
    [
        ("null", "null"),
        ("true", "true"),
        ("false", "false"),
        ("zero", "0"),
        ("negative zero", "-0"),
        ("zero with scale", "0.00"),
        ("zero with scale three", "0.000"),
        ("integer", "42"),
        ("negative", "-42"),
        ("big long", "9223372036854775807"),
        ("decimal", "1.50"),
        ("exponent", "1E10"),
        ("exponent lower", "1e5"),
        ("exponent minus", "1E-10"),
        ("string", "\"text\""),
        ("string empty", "\"\""),
        ("string escapes", "\"a\\\"b\\\\c\""),
        ("string non ascii", "\"привет\""),
        ("string emoji", "\"🎉\""),
        ("array", "[1,2,3]"),
        ("empty array", "[]"),
        ("nested array", "[[1],[2]]"),
        ("object", "{\"a\":1}"),
        ("empty object", "{}"),
        ("nested object", "{\"a\":{\"b\":1}}"),
        ("repeated keys", "{\"a\":1,\"a\":2}"),
        ("whitespace around", "  { \"a\" : 1 }  "),
        ("whitespace inside", "[\n  1,\n  2\n]"),
        ("leading zeros", "{\"a\":01}"),
        ("raw control char", "[\"x\u0001y\"]"),
        ("bom then object", "\ufeff{\"a\":1}"),
        ("scalar string", "\"just a string\""),
        ("scalar number", "1.5"),
        ("scalar true", "true"),
        ("scalar null", "null"),
        ("bad trailing comma", "{\"a\":1,}"),
        ("bad missing value", "{\"a\":}"),
        ("bad garbage", "{\"a\":1} x"),
        ("empty", ""),
        ("bad lone surrogate", "[\"\\ud83c\"]"),
    ];

    /// <summary>
    /// Every document through <c>fromJson(String)</c>, and then the value itself: its bytes, its
    /// text, its item type, its SQL and its decomposition. Normalization shows up in the bytes, the
    /// SQL exercises the ASCII target, and the decomposition exercises the parser.
    /// </summary>
    [JavaOracleFact]
    public void ValuesAgreeWithTheReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                Program(),
                numbers: null,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
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

    /// <summary>
    /// The factories that do not take text: numbers of each width, strings, and the four shared
    /// instances. They have to agree on which documents are the same value, which is what the shared
    /// instances are for.
    /// </summary>
    [JavaOracleFact]
    public void FactoriesAgreeWithTheReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                FactoriesProgram(),
                numbers: null,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="],
                packageName: "org.h2.util.json")
            .ToList();

        List<string> csharp =
        [
            Bytes(ValueJson.Get(true)),
            Bytes(ValueJson.Get(false)),
            Bytes(ValueJson.Get(0)),
            Bytes(ValueJson.Get(42)),
            Bytes(ValueJson.Get(-42)),
            Bytes(ValueJson.Get(0L)),
            Bytes(ValueJson.Get(long.MaxValue)),
            Bytes(ValueJson.Get(BigDecimal.Zero)),
            Bytes(ValueJson.Get(BigDecimal.Parse("0.00"))),
            Bytes(ValueJson.Get(BigDecimal.Parse("1.50"))),
            Bytes(ValueJson.Get(BigDecimal.Parse("1E10"))),
            Bytes(ValueJson.Get(BigDecimal.Parse("1E-10"))),
            Bytes(ValueJson.Get(BigDecimal.Parse("-0.0"))),
            Bytes(ValueJson.Get("text")),
            Bytes(ValueJson.Get("")),
            Bytes(ValueJson.Get("a\"b")),
            Bytes(ValueJson.Get("привет")),
            Bytes(ValueJson.FromJson(JsonNull.Null)),
            Bytes(ValueJson.FromJson(JsonBoolean.True)),
            Bytes(ValueJson.FromJson(JsonBoolean.False)),
            Bytes(ValueJson.FromJson(new JsonNumber(BigDecimal.Zero))),
            Bytes(ValueJson.FromJson(new JsonNumber(BigDecimal.Parse("0.00")))),
            Bytes(ValueJson.FromJson(new JsonString("text"))),
            Bytes(ValueJson.FromJson(new JsonArray())),
            Bytes(ValueJson.GetInternal("0"u8.ToArray())),
            Bytes(ValueJson.GetInternal("true"u8.ToArray())),
            Bytes(ValueJson.GetInternal("null"u8.ToArray())),
            Bytes(ValueJson.GetInternal("false"u8.ToArray())),
            Bytes(ValueJson.GetInternal("0.0"u8.ToArray())),
        ];

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(
                java[i] == csharp[i],
                $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    private static string Describe((string Name, string Document) c)
    {
        try
        {
            ValueJson value = ValueJson.FromJson(c.Document);
            return Bytes(value)
                + " / " + value.GetItemType().ToString().ToUpperInvariant()
                + " / " + Hex(value.GetSql(new StringBuilder(), 0).ToString())
                + " / " + Hex(value.GetDecomposition().ToString())
                + " / " + (value.GetMemory());
        }
        catch (H2SharpException e)
        {
            // The error code and the message are both part of the contract here: a caller that
            // catches a conversion error needs to know it got one, and the message carries the
            // offending document so a user can find the row. The trailing "[code-build]" is how
            // the engine decorates an exception message when it is shown, and the reference shows
            // it here too, so it is built the same way rather than compared away.
            StringBuilder message = new(e.Message);
            _ = message.Append(" [").Append(e.H2ErrorCode.ToString(CultureInfo.InvariantCulture))
                .Append('-').Append(Constants.BuildId.ToString(CultureInfo.InvariantCulture)).Append(']');
            return e.H2ErrorCode + ":" + message;
        }
    }

    /// <summary>
    /// The stored document as UTF-16 units. The bytes *are* the text here, which is the point of
    /// this type: a JSON column holds normalized bytes and <c>GetString</c> decodes them.
    /// </summary>
    /// <param name="value">the value</param>
    /// <returns>the document as hex</returns>
    private static string Bytes(ValueJson value) => Hex(value.GetString()!);

    private static string Hex(string text)
    {
        StringBuilder hex = new((text.Length * 4) + 1);
        _ = hex.Append('=');
        foreach (char ch in text)
        {
            _ = hex.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
        }

        return hex.ToString();
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
{{rows}}};

class Runner {
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
String result;
try {
org.h2.value.ValueJson value = org.h2.value.ValueJson.fromJson(cases[i][1]);
result = Runner.hex(value.getString()) + " / " + value.getItemType()
+ " / " + Runner.hex(value.getSQL(new StringBuilder(), 0).toString())
+ " / " + Runner.hex(value.getDecomposition().toString())
+ " / " + value.getMemory();
} catch (org.h2.message.DbException e) {
result = e.getErrorCode() + ":" + e.getMessage();
} catch (RuntimeException e) {
result = "THROWS";
}
System.out.println(result);
}
}
""";
    }

    /// <remarks>
    /// A whole program rather than a body: the harness places a body inside its own <c>try</c>, and
    /// a static method may not be declared there. The helpers are methods, so this one keeps its own
    /// class.
    /// </remarks>
    private static string FactoriesProgram() => """
{
class Runner {
static void show(org.h2.value.ValueJson value) {
System.out.println(hex(value.getString()));
}

static String hex(String text) {
StringBuilder b = new StringBuilder();
b.append('=');
for (int i = 0; i < text.length(); i++) {
b.append(String.format("%04x", (int) text.charAt(i)));
}
return b.toString();
}
}

java.math.BigDecimal zero = java.math.BigDecimal.ZERO;
Runner.show(org.h2.value.ValueJson.get(true));
Runner.show(org.h2.value.ValueJson.get(false));
Runner.show(org.h2.value.ValueJson.get(0));
Runner.show(org.h2.value.ValueJson.get(42));
Runner.show(org.h2.value.ValueJson.get(-42));
Runner.show(org.h2.value.ValueJson.get(0L));
Runner.show(org.h2.value.ValueJson.get(Long.MAX_VALUE));
Runner.show(org.h2.value.ValueJson.get(zero));
Runner.show(org.h2.value.ValueJson.get(new java.math.BigDecimal("0.00")));
Runner.show(org.h2.value.ValueJson.get(new java.math.BigDecimal("1.50")));
Runner.show(org.h2.value.ValueJson.get(new java.math.BigDecimal("1E10")));
Runner.show(org.h2.value.ValueJson.get(new java.math.BigDecimal("1E-10")));
Runner.show(org.h2.value.ValueJson.get(new java.math.BigDecimal("-0.0")));
Runner.show(org.h2.value.ValueJson.get("text"));
Runner.show(org.h2.value.ValueJson.get(""));
Runner.show(org.h2.value.ValueJson.get("a\"b"));
Runner.show(org.h2.value.ValueJson.get("привет"));
Runner.show(org.h2.value.ValueJson.fromJson(org.h2.util.json.JSONNull.NULL));
Runner.show(org.h2.value.ValueJson.fromJson(org.h2.util.json.JSONBoolean.TRUE));
Runner.show(org.h2.value.ValueJson.fromJson(org.h2.util.json.JSONBoolean.FALSE));
Runner.show(org.h2.value.ValueJson.fromJson(
new org.h2.util.json.JSONNumber(zero)));
Runner.show(org.h2.value.ValueJson.fromJson(
new org.h2.util.json.JSONNumber(new java.math.BigDecimal("0.00"))));
Runner.show(org.h2.value.ValueJson.fromJson(
new org.h2.util.json.JSONString("text")));
Runner.show(org.h2.value.ValueJson.fromJson(new org.h2.util.json.JSONArray()));
Runner.show(org.h2.value.ValueJson.getInternal("0".getBytes()));
Runner.show(org.h2.value.ValueJson.getInternal("true".getBytes()));
Runner.show(org.h2.value.ValueJson.getInternal("null".getBytes()));
Runner.show(org.h2.value.ValueJson.getInternal("false".getBytes()));
Runner.show(org.h2.value.ValueJson.getInternal("0.0".getBytes()));
}
""";

    private static string JavaString(string value)
    {
        StringBuilder literal = new("\"");
        foreach (char ch in value)
        {
            literal.Append(ch switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\b' => "\\b",
                '\f' => "\\f",
                < ' ' => $"\\u{(int)ch:x4}",
                _ => ch.ToString(),
            });
        }

        return literal.Append('"').ToString();
    }
}
