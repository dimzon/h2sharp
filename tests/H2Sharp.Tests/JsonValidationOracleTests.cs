/*
 * Differential coverage for the three targets that check a document instead of writing it, and
 * for the one that builds the value tree.
 *
 * These targets have almost nothing in them: no text, no bytes, no state beyond a stack. That is
 * exactly why they need a reference rather than a reading of the source. The interesting behaviour
 * is in which malformed document produces which error, and a stack-and-flags implementation is easy
 * to write so that it is right on the valid cases and wrong on the rest.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Util.Json;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class JsonValidationOracleTests
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
        ("repeated keys three times", "{\"a\":1,\"b\":2,\"a\":3}"),
        ("different keys", "{\"a\":1,\"b\":2}"),
        ("name without value", "{\"a\":}"),
        ("name then another name", "{\"a\" \"b\":1}"),
        ("missing colon", "{\"a\" 1}"),
        ("missing value", "{\"a\":,}"),
        ("trailing comma object", "{\"a\":1,}"),
        ("trailing comma array", "[1,2,]"),
        ("comma only object", "{,}"),
        ("two values one name", "{\"a\":1 2}"),
        ("unclosed object", "{\"a\":1"),
        ("unclosed array", "[1"),
        ("unclosed nested", "{\"a\":[1"),
        ("extra brace", "{\"a\":1}}"),
        ("extra bracket", "[1]]"),
        ("mismatched close", "{\"a\":[1}"),
        ("mismatched close other", "[{\"a\":1]"),
        ("empty input", ""),
        ("whitespace only", "   "),
        ("scalar string", "\"text\""),
        ("scalar number", "42"),
        ("scalar true", "true"),
        ("scalar null", "null"),
        ("leading zeros", "{\"a\":01}"),
        ("trailing dot", "{\"a\":1.}"),
        ("escaped quote", "{\"a\":\"say \\\"hi\\\"\"}"),
        ("raw control char", "{\"a\":\"x\u0001y\"}"),
        ("surrogate pair escape", "{\"a\":\"\\ud83c\\udf89\"}"),
        ("lone high surrogate", "{\"a\":\"\\ud83c\"}"),
        ("trailing garbage", "{\"a\":1} x"),
        ("two documents", "{\"a\":1}{\"b\":2}"),
        ("comment", "// c\n{\"a\":1}"),
        ("nan", "{\"a\":NaN}"),
        ("single quotes", "{'a':1}"),
        ("unquoted name", "{a:1}"),
        ("bom then object", "\ufeff{\"a\":1}"),
        ("array of objects", "[{\"a\":1},{\"a\":2}]"),
        ("object in array in object", "{\"a\":[{\"b\":1},{\"b\":2}]}"),
        ("repeated names across nesting", "{\"a\":{\"a\":1},\"b\":2}"),
    ];

    [JavaOracleFact]
    public void ValidatorsAgreeWithTheReference()
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
                $"line {i}, {Corpus[i].Name}, {Corpus[i].Document}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// A document through all three checking targets and the tree builder, as one comparable line.
    /// </summary>
    /// <param name="document">the document</param>
    /// <returns>what each target made of it</returns>
    private static string Describe((string Name, string Document) c)
    {
        return Guard(() => Validate(new JsonValidationTargetWithoutUniqueKeys(), c.Document))
            + " / " + Guard(() => Validate(new JsonValidationTargetWithUniqueKeys(), c.Document))
            + " / " + Guard(() => Tree(c.Document));
    }

    private static string Validate(JsonValidationTarget target, string document)
    {
        foreach (string token in Tokens(document))
        {
            Apply(target, token);
        }

        // Lower case: the enum names on the two sides differ only in case, and comparing them
        // exactly would be comparing Java's naming convention with C#'s rather than the behaviour.
        return ((JsonItemType)target.GetResult()!).ToString().ToLowerInvariant();
    }

    /// <summary>
    /// The tree, as its UTF-16 code units in hex. Not as text: a surrogate pair is two code units
    /// here and would be reassembled into one character by any console, so printing the tree would
    /// compare whatever the terminal made of it.
    /// </summary>
    private static string Tree(string document)
    {
        JsonValueTarget target = new();
        foreach (string token in Tokens(document))
        {
            Apply(target, token);
        }

        return Hex(((JsonValue)target.GetResult()!).ToString());
    }

    /// <summary>
    /// UTF-16 code units as hex, the same shape the harness uses for its own answers.
    /// </summary>
    /// <param name="value">the text</param>
    /// <returns>the units</returns>
    private static string Hex(string value)
    {
        StringBuilder hex = new(value.Length * 4 + 1);
        _ = hex.Append('=');
        foreach (char ch in value)
        {
            _ = hex.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
        }

        return hex.ToString();
    }

    /// <summary>
    /// A target answers THROWS for a document it rejects, which is the whole point of a validator.
    /// The distinction from an unexpected failure is the exception type: structural trouble is an
    /// <see cref="InvalidOperationException"/>, which is what the reference throws.
    /// </summary>
    private static string Guard(Func<string> action)
    {
        try
        {
            return action();
        }
        catch (InvalidOperationException)
        {
            return "THROWS";
        }
    }

    /// <summary>
    /// The events a document produces, in the order a parser would report them. Written out by hand
    /// rather than parsed, because there is no parser yet and inventing one inside its own test would
    /// test the invention rather than the targets.
    /// </summary>
    private static IEnumerable<string> Tokens(string document)
    {
        int i = 0;
        bool afterName = false;
        while (i < document.Length)
        {
            char c = document[i];
            if (c is ' ' or '\t' or '\n' or '\r')
            {
                i++;
                continue;
            }

            switch (c)
            {
                case '{':
                    yield return "startObject";
                    afterName = false;
                    i++;
                    continue;
                case '}':
                    yield return "endObject";
                    afterName = false;
                    i++;
                    continue;
                case '[':
                    yield return "startArray";
                    afterName = false;
                    i++;
                    continue;
                case ']':
                    yield return "endArray";
                    afterName = false;
                    i++;
                    continue;
                case ',':
                    yield return ",";
                    i++;
                    continue;
                case ':':
                    yield return ":";
                    i++;
                    continue;
                case '"':
                    {
                        string text = ReadString(document, ref i);
                        // The difference between a name and a value is a question the target answers,
                        // which is what isPropertyExpected is for.
                        yield return "isProperty:" + text;
                        afterName = true;
                        continue;
                    }

                default:
                    {
                        string literal = ReadLiteral(document, ref i);
                        yield return literal switch
                        {
                            "true" or "false" or "null" => literal,
                            _ => "number",
                        };
                        afterName = false;
                        continue;
                    }
            }
        }

        _ = afterName;
    }

    private static string ReadString(string document, ref int i)
    {
        StringBuilder text = new();
        i++;
        while (i < document.Length)
        {
            char c = document[i++];
            if (c == '"')
            {
                return text.ToString();
            }

            if (c != '\\')
            {
                _ = text.Append(c);
                continue;
            }

            char escape = document[i++];
            switch (escape)
            {
                case 'n': _ = text.Append('\n'); break;
                case 't': _ = text.Append('\t'); break;
                case 'r': _ = text.Append('\r'); break;
                case 'b': _ = text.Append('\b'); break;
                case 'f': _ = text.Append('\f'); break;
                case 'u':
                    {
                        string hex = document.Substring(i, 4);
                        i += 4;
                        _ = text.Append((char)ushort.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        break;
                    }

                default: _ = text.Append(escape); break;
            }
        }

        return text.ToString();
    }

    private static string ReadLiteral(string document, ref int i)
    {
        int start = i;
        while (i < document.Length && char.IsLetterOrDigit(document[i]))
        {
            i++;
        }

        // A character that is neither letter nor digit — the minus of a negative number, the dot of
        // "1." — still has to be consumed, or the loop never moves past it.
        if (i == start)
        {
            i++;
        }

        return document[start..i];
    }

    /// <summary>
    /// Applies one event to a target. The events are the parser's questions as well as its answers,
    /// so the separators and the colon go in here too.
    /// </summary>
    private static void Apply(JsonTarget target, string token)
    {
        switch (token)
        {
            case "startObject":
                // A target asks the parser whether a separator belongs here; the corpus says no.
                target.StartObject();
                break;
            case "endObject":
                target.EndObject();
                break;
            case "startArray":
                target.StartArray();
                break;
            case "endArray":
                target.EndArray();
                break;
            case ",":
                _ = target.IsValueSeparatorExpected();
                break;
            case ":":
                break;
            case "true":
                target.ValueTrue();
                break;
            case "false":
                target.ValueFalse();
                break;
            case "null":
                target.ValueNull();
                break;
            case "number":
                target.ValueNumber(BigDecimal.One);
                break;
            default:
                // "isProperty:<name>" — the target says whether this is a name, and then it is
                // either told so or told the value instead.
                string name = token["isProperty:".Length..];
                if (target.IsPropertyExpected())
                {
                    target.Member(name);
                }
                else
                {
                    target.ValueString(name);
                }

                break;
        }
    }

    private static string Program()
    {
        StringBuilder rows = new();
        foreach ((string name, string document) in Corpus)
        {
            rows.Append("      {").Append(JavaString(name))
                .Append(", ").Append(JavaString(document)).Append("},\n");
        }

        return RunnerFirst(rows);
    }

    /// <summary>
    /// The body the oracle runs: statements, not a whole program, because the harness builds the
    /// class, the package and the entry point itself.
    /// </summary>
    /// <remarks>
    /// Everything is inside one block, because the harness places the body inside its own
    /// <c>try</c>. A local class is legal in a block and not at class level, which is why the
    /// helpers live in one. The class comes before the loop on purpose: a local class is in scope
    /// only from its declaration onwards, so the order here is the order in Java.
    /// </remarks>
    /// <param name="rows">the corpus, already written as Java literals</param>
    /// <returns>the statements</returns>
    private static string RunnerFirst(StringBuilder rows) => $$"""
            {
              String[][] cases = new String[][] {
            {{rows}}              };

              class Runner {
              static String run(JSONTarget<JSONItemType> target, String document) {
                try {
                  feed(target, document);
                  return target.getResult().toString().toLowerCase(java.util.Locale.ROOT);
                } catch (RuntimeException e) {
                  return "THROWS";
                }
              }

              static String runTree(JSONTarget<JSONValue> target, String document) {
                try {
                  feed(target, document);
                  return hex(target.getResult().toString());
                } catch (RuntimeException e) {
                  return "THROWS";
                }
              }

              // UTF-16 code units as hex: a surrogate pair is two code units here, and any console
              // would reassemble it into one character and compare that instead.
              static String hex(String text) {
                StringBuilder b = new StringBuilder();
                b.append('=');
                for (int i = 0; i < text.length(); i++) {
                  b.append(String.format("%04x", (int) text.charAt(i)));
                }
                return b.toString();
              }

              static void feed(JSONTarget<?> target, String document) {
                int i = 0;
                int n = document.length();
                boolean afterName = false;
                while (i < n) {
                  char c = document.charAt(i);
                  if (c == ' ' || c == '\t' || c == '\n' || c == '\r') {
                    i++;
                    continue;
                  }
                  switch (c) {
                  case '{':
                    target.startObject();
                    afterName = false;
                    i++;
                    continue;
                  case '}':
                    target.endObject();
                    afterName = false;
                    i++;
                    continue;
                  case '[':
                    target.startArray();
                    afterName = false;
                    i++;
                    continue;
                  case ']':
                    target.endArray();
                    afterName = false;
                    i++;
                    continue;
                  case ',':
                    target.isValueSeparatorExpected();
                    i++;
                    continue;
                  case ':':
                    i++;
                    continue;
                  case '"': {
                    String[] read = readString(document, i);
                    i = Integer.parseInt(read[1]);
                    if (target.isPropertyExpected()) {
                      target.member(read[0]);
                    } else {
                      target.valueString(read[0]);
                    }
                    afterName = true;
                    continue;
                  }
                  default: {
                    String[] read = readLiteral(document, i);
                    i = Integer.parseInt(read[1]);
                    String literal = read[0];
                    if (literal.equals("true")) {
                      target.valueTrue();
                    } else if (literal.equals("false")) {
                      target.valueFalse();
                    } else if (literal.equals("null")) {
                      target.valueNull();
                    } else {
                      target.valueNumber(java.math.BigDecimal.ONE);
                    }
                    afterName = false;
                    continue;
                  }
                  }
                }
                // Nothing closes a truncated document, and that is the point: an unfinished
                // container is exactly what getResult() refuses. No attempt is made to tidy up
                // here, because a top-level scalar is finished with its own value and would be
                // broken by one.
              }

              static String[] readString(String document, int start) {
                StringBuilder b = new StringBuilder();
                int i = start + 1;
                while (i < document.length()) {
                  char c = document.charAt(i++);
                  if (c == '"') {
                    return new String[] { b.toString(), Integer.toString(i) };
                  }
                  if (c != '\\') {
                    b.append(c);
                    continue;
                  }
                  char e = document.charAt(i++);
                  switch (e) {
                  case 'n': b.append('\n'); break;
                  case 't': b.append('\t'); break;
                  case 'r': b.append('\r'); break;
                  case 'b': b.append('\b'); break;
                  case 'f': b.append('\f'); break;
                  case 'u':
                    b.append((char) Integer.parseInt(document.substring(i, i + 4), 16));
                    i += 4;
                    break;
                  default: b.append(e); break;
                  }
                }
                return new String[] { b.toString(), Integer.toString(i) };
              }

              static String[] readLiteral(String document, int start) {
                int i = start;
                while (i < document.length() && Character.isLetterOrDigit(document.charAt(i))) {
                  i++;
                }
                // A character that is neither letter nor digit — the minus of a negative number,
                // the dot of "1." — still has to be consumed, or the loop never moves past it.
                if (i == start) {
                  i++;
                }
                return new String[] { document.substring(start, i), Integer.toString(i) };
              }
              }

              for (int i = 0; i < cases.length; i++) {
                JSONTarget<JSONItemType> loose = new JSONValidationTargetWithoutUniqueKeys();
                JSONTarget<JSONItemType> strict = new JSONValidationTargetWithUniqueKeys();
                JSONTarget<JSONValue> tree = new JSONValueTarget();
                System.out.println(
                    Runner.run(loose, cases[i][1]) + " / " + Runner.run(strict, cases[i][1])
                        + " / " + Runner.runTree(tree, cases[i][1]));
              }
            }
            """;

    /// <summary>
    /// A Java string literal for a value, with the characters that cannot travel through a source
    /// file written as escapes.
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
                    literal.Append(ch < ' ' || ch > '\u007e'
                        ? "\\u" + ((int)ch).ToString("x4", CultureInfo.InvariantCulture)
                        : ch.ToString());
                    break;
            }
        }

        return literal.Append('"').ToString();
    }
}
