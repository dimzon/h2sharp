/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the JSON value model and the text that comes out of it.
 *
 * The model is the easy half of JSON and the half where a port most easily changes the text: a
 * JSON value knows only how to write itself into a target, so the printer decides everything
 * about spacing, separators, and escaping. The cases that matter are the strings — an apostrophe
 * is legal JSON but is escaped in the ASCII-only mode, a control character is written as \u00xx,
 * and a non-ASCII character is written as itself in one mode and as \uxxxx in the other — and the
 * numbers, where the reference writes a Java decimal and the grammar of JSON does not allow the
 * plus sign that comes with its exponent.
 *
 * The numbers are compared through their text, because that is where the two renderings differ;
 * the type names are not compared, since a value knows no type number.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Util.Json;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class JsonValueOracleTests
{
    /// <summary>
    /// A value to write out, described the same way on both sides.
    /// </summary>
    private sealed record Case(string Kind, string Text);

    private static readonly Case[] Corpus =
    [
        // The scalars.
        new("null", ""),
        new("true", ""),
        new("false", ""),
        new("string", "abc"),
        new("string", ""),
        new("string", "a\"b"),
        new("string", "a'b"),
        new("string", "a\\b"),
new("string", "line\nbreak\ttab"),
        new("string", "\u0001"),
        new("string", "\u001f"),
        new("string", "\u007f"),
        new("string", "\u0085"),
        new("string", "\u00a0"),
        new("string", "\u2028\u2029"),
        new("string", "привет"),
        new("string", "日本語"),
new("string", "🎉"),
        new("string", "a🎉b\u0001c"),
        new("string", "a<b>&c+d`e"),
        new("string", "\u00a0"),

        // Numbers: the exponent is where the two renderings differ.
        new("number", "0"),
        new("number", "-1"),
        new("number", "1.5"),
        new("number", "-1.5"),
        new("number", "0.1"),
        new("number", "100"),
        new("number", "1E10"),
        new("number", "1e5"),
        new("number", "1E-10"),
        new("number", "-0"),
        new("number", "1.50"),
        new("number", "1.5E-10"),
        new("number", "123456789012345678901234567890"),
        new("number", "1234567890123456789012345678901234567890"),

        // Arrays, including the empty one and nesting.
        new("array", ""),
        new("array", "1"),
        new("array", "1;2;3"),
        new("array", "null;true;x"),
        new("array", "1;2"),
        new("array", "[]"),

        // Objects, including repeated names, which JSON allows and this model keeps in order.
        new("object", ""),
        new("object", "a:1"),
        new("object", "a:1;b:2"),
        new("object", "a:1;a:2"),
        new("object", "a:null"),
        new("object", "a:[]"),
        new("object", "a:{b:1}"),
    ];

    [JavaOracleFact]
    public void PrintedValuesMatchTheReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                Declare() + Body,
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
                $"line {i}, {Corpus[i].Kind} {Corpus[i].Text}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// The same corpus through the writer that goes to <c>Utf8JsonWriter</c>, compared both with
    /// the reference and with the target that writes every character itself.
    /// </summary>
    /// <remarks>
    /// The second comparison is the one that matters for maintenance. The reference column says
    /// the text is right today; the <see cref="JsonStringTarget"/> column says the two ways of
    /// producing it still agree with each other, so a future change to the encoder fails here
    /// rather than in whatever reads a JSON column.
    /// </remarks>
    [JavaOracleFact]
    public void TheStandardWriterProducesTheSameText()
    {
        List<string> java = OracleRunner.Run(
                [],
                Declare() + Body,
                numbers: null,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="],
                packageName: "org.h2.util.json")
            .ToList();

        List<string> viaStj = [.. Corpus.Select(c => Render(new JsonStjTarget(), c))];
        List<string> viaOwn = [.. Corpus.Select(c => Render(new JsonStringTarget(), c))];

        Assert.Equal(java.Count, viaStj.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(
                java[i] == viaStj[i],
                $"reference, line {i}, {Corpus[i].Kind} {Corpus[i].Text}:\n  java:   {java[i]}\n  stj:    {viaStj[i]}");
            Assert.True(
                viaOwn[i] == viaStj[i],
                $"targets differ, line {i}, {Corpus[i].Kind} {Corpus[i].Text}:\n  own:    {viaOwn[i]}\n  stj:    {viaStj[i]}");
        }
    }

    /// <summary>
    /// A case, written into a target, in both renderings.
    /// </summary>
    /// <param name="plain">the target for ordinary output</param>
    /// <param name="c">the case</param>
    /// <returns>the plain and ASCII renderings</returns>
    private static string Render(JsonTarget plain, Case c)
    {
        string text;
        Build(c.Kind, c.Text).AddTo(plain);
        text = (string)plain.GetResult()!;

        JsonTarget ascii = plain is JsonStjTarget ? new JsonStjTarget(true) : new JsonStringTarget(true);
        Build(c.Kind, c.Text).AddTo(ascii);
        return text + " / " + ascii.GetResult();
    }

    private static string Describe(Case c)
    {
        string text = Build(c.Kind, c.Text).ToString();

        // The ASCII rendering cannot come from ToString: that always uses the plain target,
        // which is the point of ToString. It has to be written into a target built for it.
        JsonStringTarget ascii = new(true);
        Build(c.Kind, c.Text).AddTo(ascii);
        return text + " / " + ascii.GetResult();
    }

    /// <summary>
    /// The value a case names.
    /// </summary>
    /// <param name="kind">what to build</param>
    /// <param name="text">the text it is built from</param>
    /// <returns>the value</returns>
    private static JsonValue Build(string kind, string text)
    {
        return kind switch
        {
            "null" => JsonNull.Null,
            "true" => JsonBoolean.True,
            "false" => JsonBoolean.False,
            "string" => new JsonString(text),
            "number" => new JsonNumber(BigDecimal.Parse(text)),
            "array" => Array(text),
            _ => Object(text),
        };
    }

    /// <summary>
    /// An array whose elements are separated by semicolons, so the cases can be written as
    /// "1;2;3" and a semicolon inside a string stays unambiguous.
    /// </summary>
    /// <param name="text">the elements</param>
    /// <returns>the array</returns>
    private static JsonArray Array(string text)
    {
        JsonArray array = new();
        if (text.Length > 0)
        {
            foreach (string part in text.Split(';'))
            {
                array.AddElement(Element(part));
            }
        }

        return array;
    }

    private static JsonValue Element(string part) => part switch
    {
        "null" => JsonNull.Null,
        "true" => JsonBoolean.True,
        "false" => JsonBoolean.False,
        _ => part.StartsWith('[') ? Array(part[1..^1]) : new JsonString(part),
    };

    /// <summary>
    /// An object whose members are separated by semicolons and written "name:value".
    /// </summary>
    /// <param name="text">the members</param>
    /// <returns>the object</returns>
    private static JsonObject Object(string text)
    {
        JsonObject obj = new();
        if (text.Length > 0)
        {
            foreach (string part in text.Split(';'))
            {
                int colon = part.IndexOf(':', StringComparison.Ordinal);
                string name = part[..colon];
                string member = part[(colon + 1)..];
                obj.AddMember(name, member switch
                {
                    "null" => JsonNull.Null,
                    "true" => JsonBoolean.True,
                    "false" => JsonBoolean.False,
                    _ when member.StartsWith('[') => Array(member[1..^1]),
                    _ when member.StartsWith('{') => Object(member[1..^1]),
                    _ => new JsonString(member),
                });
            }
        }

        return obj;
    }

    /// <summary>
    /// The case table in the form the oracle reads it. It is generated from the same corpus the
    /// managed side walks, so the two halves cannot drift apart: a hand-written copy of this table
    /// is a list of cases that silently stops being compared.
    /// </summary>
    /// <returns>the Java statements that build the table</returns>
    private static string Declare()
    {
        StringBuilder rows = new();
        foreach (Case c in Corpus)
        {
            rows.Append("  {").Append(JavaString(c.Kind))
                .Append(", ").Append(JavaString(c.Text)).Append("},").Append('\n');
        }

        return "String[][] cases = new String[][] {\n" + rows + "};\n";
    }

    /// <summary>
    /// A Java string literal for a value. The characters that JSON itself has to escape are the
    /// ones that cannot travel through a source file unescaped, so they are written the way the
    /// reference writes them.
    /// </summary>
    /// <param name="value">the value</param>
    /// <returns>the literal, quotes included</returns>
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
                    literal.Append(ch < ' ' || ch == '\u007f' ? "\\u" + ((int)ch).ToString("x4", CultureInfo.InvariantCulture) : ch.ToString());
                    break;
            }
        }

        return literal.Append('"').ToString();
    }

    private const string Body = """
                        // The three builders do not call each other, because a Java method may not
                        // be declared inside another one and the oracle can only add statements.
                        // That bounds the corpus to one level of nesting, which is all it needs:
                        // the deeper cases would be testing recursion, not printing.
                        java.util.function.Function<String, org.h2.util.json.JSONValue> scalar =
                            part -> {
                              if (part.equals("null")) {
                                return org.h2.util.json.JSONNull.NULL;
                              }
                              if (part.equals("true")) {
                                return org.h2.util.json.JSONBoolean.TRUE;
                              }
                              return org.h2.util.json.JSONString.class.cast(
                                  new org.h2.util.json.JSONString(part));
                            };

                        // A lambda may not name itself, so each recursive builder is held in
                        // an array of one and reads itself back out of the array.
                        @SuppressWarnings("unchecked")
                        java.util.function.Function<String, org.h2.util.json.JSONValue>[] arrayOf =
                            new java.util.function.Function[1];
                        @SuppressWarnings("unchecked")
                        java.util.function.Function<String, org.h2.util.json.JSONValue>[] objectOf =
                            new java.util.function.Function[1];

                        arrayOf[0] = text -> {
                          org.h2.util.json.JSONArray a = new org.h2.util.json.JSONArray();
                          if (!text.isEmpty()) {
                            for (String part : text.split(";", -1)) {
                              a.addElement(
                                  part.startsWith("[")
                                      ? arrayOf[0].apply(part.substring(1, part.length() - 1))
                                      : scalar.apply(part));
                            }
                          }
                          return a;
                        };

                        objectOf[0] = text -> {
                          org.h2.util.json.JSONObject o = new org.h2.util.json.JSONObject();
                          if (!text.isEmpty()) {
                            for (String part : text.split(";", -1)) {
                              int colon = part.indexOf(':');
                              String member = part.substring(colon + 1);
                              o.addMember(part.substring(0, colon),
                                  member.startsWith("[")
                                      ? arrayOf[0].apply(member.substring(1, member.length() - 1))
                                      : member.startsWith("{")
                                          ? objectOf[0].apply(member.substring(
                                              1, member.length() - 1))
                                          : scalar.apply(member));
                            }
                          }
                          return o;
                        };

                        java.util.function.BiFunction<String, String, org.h2.util.json.JSONValue>
                            build = (kind, text) -> {
                              if (kind.equals("null")) {
                                return org.h2.util.json.JSONNull.NULL;
                              }
                              if (kind.equals("true")) {
                                return org.h2.util.json.JSONBoolean.TRUE;
                              }
                              if (kind.equals("false")) {
                                return org.h2.util.json.JSONBoolean.FALSE;
                              }
                              if (kind.equals("string")) {
                                return new org.h2.util.json.JSONString(text);
                              }
                              if (kind.equals("number")) {
                                return new org.h2.util.json.JSONNumber(
                                    new java.math.BigDecimal(text));
                              }
                              return kind.equals("array")
                                  ? arrayOf[0].apply(text) : objectOf[0].apply(text);
                            };

                        for (int i = 0; i < cases.length; i++) {
                          org.h2.util.json.JSONValue v =
                              build.apply(cases[i][0], cases[i][1]);
                          org.h2.util.json.JSONStringTarget asciiTarget =
                              new org.h2.util.json.JSONStringTarget(true);
                          build.apply(cases[i][0], cases[i][1]).addTo(asciiTarget);
                          System.out.println(
                              v.toString() + " / " + asciiTarget.getResult());
                        }
              """;
}
