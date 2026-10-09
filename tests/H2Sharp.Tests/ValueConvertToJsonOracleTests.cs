/*
 * Differential coverage for the conversion into JSON: what each source type becomes.
 *
 * The answers are not interchangeable, and the interesting ones are the ones a reader would not
 * guess. Bytes are parsed rather than encoded. A string becomes a JSON string, so a value that looks
 * like a number comes out quoted. A timestamp becomes a string in ISO form, not a number. An array
 * becomes an array of its elements, each converted in turn.
 *
 * Each case carries the C# factory and the Java expression that build the same value. Both sides
 * build, neither serialises across the wire, and a mismatch means the conversion differs rather than
 * the transport.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Util.Json;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ValueConvertToJsonOracleTests
{
    private static readonly (string Name, Func<Value> CSharp, string Java)[] Corpus =
    [
        ("boolean true", () => ValueBoolean.True, "org.h2.value.ValueBoolean.TRUE"),
        ("boolean false", () => ValueBoolean.False, "org.h2.value.ValueBoolean.FALSE"),
        ("tinyint", () => ValueTinyint.Get(42), "org.h2.value.ValueTinyint.get((byte) 42)"),
        ("tinyint zero", () => ValueTinyint.Get(0), "org.h2.value.ValueTinyint.get((byte) 0)"),
        ("tinyint negative", () => ValueTinyint.Get(-8), "org.h2.value.ValueTinyint.get((byte) -8)"),
        ("smallint", () => ValueSmallint.Get(1234), "org.h2.value.ValueSmallint.get((short) 1234)"),
        ("integer", () => ValueInteger.Get(123456), "org.h2.value.ValueInteger.get(123456)"),
        ("integer negative", () => ValueInteger.Get(-123456), "org.h2.value.ValueInteger.get(-123456)"),
        ("bigint", () => ValueBigint.Get(9007199254740993L), "org.h2.value.ValueBigint.get(9007199254740993L)"),
        ("bigint zero", () => ValueBigint.Get(0L), "org.h2.value.ValueBigint.get(0L)"),
        ("real", () => ValueReal.Get(1.5f), "org.h2.value.ValueReal.get(1.5f)"),
        ("double", () => ValueDouble.Get(2.25d), "org.h2.value.ValueDouble.get(2.25d)"),
        ("double exponent", () => ValueDouble.Get(1e300d), "org.h2.value.ValueDouble.get(1e300d)"),
        ("numeric", () => ValueNumeric.Get(BigDecimal.Parse("1.50")), "org.h2.value.ValueNumeric.get(new java.math.BigDecimal(\"1.50\"))"),
        ("numeric zero", () => ValueNumeric.Get(BigDecimal.Parse("0.00")), "org.h2.value.ValueNumeric.get(new java.math.BigDecimal(\"0.00\"))"),
        ("decfloat", () => ValueDecfloat.Get(BigDecimal.Parse("3.14")), "org.h2.value.ValueDecfloat.get(new java.math.BigDecimal(\"3.14\"))"),
        ("char", () => ValueChar.Get("x"), "org.h2.value.ValueChar.get(\"x\")"),
        ("varchar", () => ValueVarchar.Get("hello"), "org.h2.value.ValueVarchar.get(\"hello\")"),
        ("varchar numeric looking", () => ValueVarchar.Get("42"), "org.h2.value.ValueVarchar.get(\"42\")"),
        ("varchar with quote", () => ValueVarchar.Get("a\"b"), "org.h2.value.ValueVarchar.get(\"a\\\"b\")"),
        ("varchar with backslash", () => ValueVarchar.Get("a\\b"), "org.h2.value.ValueVarchar.get(\"a\\\\b\")"),
        ("varchar with apostrophe", () => ValueVarchar.Get("it's"), "org.h2.value.ValueVarchar.get(\"it's\")"),
        ("varchar html", () => ValueVarchar.Get("<b>&c</b>"), "org.h2.value.ValueVarchar.get(\"<b>&c</b>\")"),
        ("varchar non ascii", () => ValueVarchar.Get("привет"), "org.h2.value.ValueVarchar.get(\"привет\")"),
        ("varchar emoji", () => ValueVarchar.Get("🎉"), "org.h2.value.ValueVarchar.get(\"🎉\")"),
        ("varchar control char", () => ValueVarchar.Get("a\u0001b"), "org.h2.value.ValueVarchar.get(\"a\\u0001b\")"),
        ("varchar empty", () => ValueVarchar.Get(""), "org.h2.value.ValueVarchar.get(\"\")"),
        ("varchar json text", () => ValueVarchar.Get("{\"a\":1}"), "org.h2.value.ValueVarchar.get(\"{\\\"a\\\":1}\")"),
        ("varchar json array", () => ValueVarchar.Get("[1,2]"), "org.h2.value.ValueVarchar.get(\"[1,2]\")"),
        ("varchar invalid json", () => ValueVarchar.Get("{oops"), "org.h2.value.ValueVarchar.get(\"{oops\")"),
        ("varchar ignoring case", () => ValueVarcharIgnoreCase.Get("AbC"), "org.h2.value.ValueVarcharIgnoreCase.get(\"AbC\")"),
        ("date", () => ValueDate.Parse("2026-09-09"), "org.h2.value.ValueDate.parse(\"2026-09-09\")"),
        ("time", () => ValueTime.Parse("12:34:56", null), "org.h2.value.ValueTime.parse(\"12:34:56\", null)"),
        ("time tz", () => ValueTimeTimeZone.Parse("12:34:56+01:30", null), "org.h2.value.ValueTimeTimeZone.parse(\"12:34:56+01:30\", null)"),
        ("timestamp", () => ValueTimestamp.Parse("2026-09-09 12:34:56", null), "org.h2.value.ValueTimestamp.parse(\"2026-09-09 12:34:56\", null)"),
        ("timestamp tz", () => ValueTimestampTimeZone.Parse("2026-09-09 12:34:56+01:30", null), "org.h2.value.ValueTimestampTimeZone.parse(\"2026-09-09 12:34:56+01:30\", null)"),
        ("uuid", () => ValueUuid.Get(0L, 1L), "org.h2.value.ValueUuid.get(new java.util.UUID(0L, 1L))"),
        ("binary json", () => ValueBinary.Get(Utf8("{\"a\":1}")), "org.h2.value.ValueBinary.get(\"{\\\"a\\\":1}\".getBytes(java.nio.charset.StandardCharsets.UTF_8))"),
        ("varbinary json", () => ValueVarbinary.Get(Utf8("[1,2]")), "org.h2.value.ValueVarbinary.get(\"[1,2]\".getBytes(java.nio.charset.StandardCharsets.UTF_8))"),
        ("varbinary not json", () => ValueVarbinary.Get(Utf8("nonsense")), "org.h2.value.ValueVarbinary.get(\"nonsense\".getBytes(java.nio.charset.StandardCharsets.UTF_8))"),
        ("varbinary empty", () => ValueVarbinary.Get([]), "org.h2.value.ValueVarbinary.get(new byte[0])"),
        ("blob json", () => ValueBlob.CreateSmall(Utf8("{\"a\":1}")), "org.h2.value.ValueBlob.createSmall(\"{\\\"a\\\":1}\".getBytes(java.nio.charset.StandardCharsets.UTF_8))"),
        ("json itself", () => ValueJson.FromJson("{\"a\":1}"), "org.h2.value.ValueJson.fromJson(\"{\\\"a\\\":1}\")"),
        ("json number", () => ValueJson.FromJson("42"), "org.h2.value.ValueJson.fromJson(\"42\")"),
        ("json string", () => ValueJson.FromJson("\"text\""), "org.h2.value.ValueJson.fromJson(\"\\\"text\\\"\")"),
        ("array of numbers", () => Arr(ValueInteger.Get(1), ValueInteger.Get(2)), "org.h2.value.ValueArray.get(new org.h2.value.Value[] { org.h2.value.ValueInteger.get(1), org.h2.value.ValueInteger.get(2) }, null)"),
        ("array empty", () => Arr(), "org.h2.value.ValueArray.get(new org.h2.value.Value[0], null)"),
        ("array with string", () => Arr(ValueVarchar.Get("x"), ValueBoolean.True), "org.h2.value.ValueArray.get(new org.h2.value.Value[] { org.h2.value.ValueVarchar.get(\"x\"), org.h2.value.ValueBoolean.TRUE }, null)"),
        ("array with json", () => Arr(ValueJson.FromJson("{\"a\":1}")), "org.h2.value.ValueArray.get(new org.h2.value.Value[] { org.h2.value.ValueJson.fromJson(\"{\\\"a\\\":1}\") }, null)"),
        ("array nested", () => Arr(Arr(ValueInteger.Get(1))), "org.h2.value.ValueArray.get(new org.h2.value.Value[] { org.h2.value.ValueArray.get(new org.h2.value.Value[] { org.h2.value.ValueInteger.get(1) }, null) }, null)"),
    ];

    [JavaOracleFact]
    public void ConversionsAgreeWithTheReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                Program(),
                numbers: null,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .ToList();

        List<string> csharp = [.. Corpus.Select(Describe)];

        if (java.Count != csharp.Count)
        {
            Assert.Fail(
                "java=" + java.Count + " csharp=" + csharp.Count + "\n"
                + "JAVA>>" + string.Join(" ;; ", java) + "\n"
                + "CSHARP>>" + string.Join(" ;; ", csharp));
        }
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(
                java[i] == csharp[i],
                $"line {i}, {Corpus[i].Name}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>A value converted to JSON, and what a caller would read back.</summary>
    private static string Describe((string Name, Func<Value> CSharp, string Java) c)
    {
        try
        {
            ValueJson json = c.CSharp().ConvertToJson(TypeInfo.TypeJson, Value.ConvertTo, null);
            return Hex(json.GetString()!) + " / " + json.GetItemType().ToString().ToUpperInvariant();
        }
        catch (H2SharpException e)
        {
            // The trailing "[code-build]" is how the engine decorates a message when it is shown,
            // and the reference shows it here too.
            StringBuilder message = new(e.Message);
            _ = message.Append(" [").Append(e.H2ErrorCode.ToString(CultureInfo.InvariantCulture))
                .Append('-').Append(Constants.BuildId.ToString(CultureInfo.InvariantCulture)).Append(']');
            return e.H2ErrorCode + ":" + message;
        }
    }

    /// <summary>An array, whose factory takes the component type it cannot work out from an empty list.</summary>
    private static ValueArray Arr(params Value[] list) => ValueArray.Get(list, null);

    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    /// <summary>The text as UTF-16 units in hex: one unit is one column of the answer.</summary>
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
        StringBuilder calls = new();
        foreach ((string name, Func<Value> cSharp, string java) in Corpus)
        {
            // JavaString already supplies the quotes, so nothing is wrapped around it here, and the
            // call is qualified: the method lives in the local class, not in the block.
            _ = calls.Append("      Runner.out(").Append(JavaString(name))
                .Append(", () -> ").Append(java).Append(");\n");
        }

        return $$"""
{
class Runner {
static String hex(String text) {
StringBuilder b = new StringBuilder();
b.append('=');
for (int i = 0; i < text.length(); i++) {
b.append(String.format("%04x", (int) text.charAt(i)));
}
return b.toString();
}

static void out(String name, java.util.function.Supplier<org.h2.value.Value> factory) {
String result;
try {
org.h2.value.Value value = factory.get();
org.h2.value.ValueJson json =
value.convertToJson(org.h2.value.TypeInfo.TYPE_JSON, org.h2.value.Value.CONVERT_TO, null);
result = hex(json.getString()) + " / " + json.getItemType();
} catch (org.h2.message.DbException e) {
result = e.getErrorCode() + ":" + e.getMessage();
} catch (RuntimeException e) {
result = "THROWS";
}
System.out.println(result);
}
}

{{calls}}
}
""";
    }

    private static string JavaString(string text)
    {
        StringBuilder literal = new("\"");
        foreach (char ch in text)
        {
            literal.Append(ch switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' => $"\\u{(int)ch:x4}",
                _ => ch.ToString(),
            });
        }

        return literal.Append('"').ToString();
    }
}
