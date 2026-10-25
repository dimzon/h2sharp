// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.unit.TestJsonUtils.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): org.h2.util.json.JSON* classes are H2Sharp.Util.Json.Json*; Java's
// JSONTarget<?> has no wildcard equivalent, so the untyped H2Sharp.Util.Json.JsonTarget
// base is used and the typed Parse calls go through a small switch; java.nio charsets
// are .NET encodings without a byte order mark; StringBuilder.appendCodePoint is
// char.ConvertFromUtf32; expected runtime failures are ArgumentException/
// InvalidOperationException (the reference's IllegalArgumentException/
// IllegalStateException) and encode failures are H2SharpException.
using H2Sharp.Util;
using H2Sharp.Util.Json;
using Xunit;

namespace H2Sharp.Native.Tests.Unit;

/// <summary>
/// Tests the classes from the JSON package.
/// </summary>
public sealed class TestJsonUtils : TestBase
{
    private static readonly Encoding[] Charsets =
    {
        new UTF8Encoding(false),
        new UnicodeEncoding(bigEndian: true, byteOrderMark: false),
        new UnicodeEncoding(bigEndian: false, byteOrderMark: false),
        new UTF32Encoding(bigEndian: true, byteOrderMark: false),
        new UTF32Encoding(bigEndian: false, byteOrderMark: false),
    };

    private static readonly Func<JsonTarget> StringTarget = () => new JsonStringTarget();

    private static readonly Func<JsonTarget> BytesTarget = () => new JsonByteArrayTarget();

    private static readonly Func<JsonTarget> ValueTarget = () => new JsonValueTarget();

    private static readonly Func<JsonTarget> ValidationTargetWithoutUniqueKeys =
        () => new JsonValidationTargetWithoutUniqueKeys();

    private static readonly Func<JsonTarget> ValidationTargetWithUniqueKeys =
        () => new JsonValidationTargetWithUniqueKeys();

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestTargetErrorDetection();
        TestSourcesAndTargetsAll();
        TestUtfErrorAll();
        TestLongNesting();
        TestEncodeStringAll();
    }

    private void TestTargetErrorDetection()
    {
        TestTargetErrorDetection(StringTarget);
        TestTargetErrorDetection(BytesTarget);
        TestTargetErrorDetection(ValueTarget);
        TestTargetErrorDetection(ValidationTargetWithoutUniqueKeys);
        TestTargetErrorDetection(ValidationTargetWithUniqueKeys);
    }

    private void TestTargetErrorDetection(Func<JsonTarget> constructor)
    {
        AssertThrows(typeof(Exception), () => constructor().EndObject());
        AssertThrows(typeof(Exception), () => constructor().EndArray());
        // Unexpected member without object
        AssertThrows(typeof(Exception), () => constructor().Member("1"));
        // Unexpected member inside array
        JsonTarget target1 = constructor();
        target1.StartArray();
        AssertThrows(typeof(Exception), () => target1.Member("1"));
        // Unexpected member without value
        JsonTarget target2 = constructor();
        target2.StartObject();
        target2.Member("1");
        AssertThrows(typeof(Exception), () => target2.Member("2"));
        JsonTarget target3 = constructor();
        target3.StartObject();
        target3.Member("1");
        AssertThrows(typeof(Exception), () => target3.EndObject());
        // Unexpected value without member name
        TestJsonStringTargetErrorDetectionAllValues(() =>
        {
            JsonTarget target = constructor();
            target.StartObject();
            return target;
        });
        // Unexpected second value
        TestJsonStringTargetErrorDetectionAllValues(() =>
        {
            JsonTarget target = constructor();
            target.ValueNull();
            return target;
        });
        // No value
        AssertIncomplete(constructor());
        // Unclosed object
        JsonTarget target4 = constructor();
        target4.StartObject();
        AssertIncomplete(target4);
        // Unclosed array
        JsonTarget target5 = constructor();
        target5.StartObject();
        AssertIncomplete(target5);
        // End of array after start of object or vice versa
        JsonTarget target6 = constructor();
        target6.StartObject();
        AssertThrows(typeof(Exception), () => target6.EndArray());
        JsonTarget target7 = constructor();
        target7.StartArray();
        AssertThrows(typeof(Exception), () => target7.EndObject());
    }

    private void AssertIncomplete(JsonTarget target)
    {
        AssertThrows(typeof(Exception), () => target.GetResult());
    }

    private void TestJsonStringTargetErrorDetectionAllValues(Func<JsonTarget> initializer)
    {
        AssertThrows(typeof(Exception), () => initializer().ValueNull());
        AssertThrows(typeof(Exception), () => initializer().ValueFalse());
        AssertThrows(typeof(Exception), () => initializer().ValueTrue());
        AssertThrows(typeof(Exception), () => initializer().ValueNumber(BigDecimal.One));
        AssertThrows(typeof(Exception), () => initializer().ValueString("string"));
    }

    private void TestSourcesAndTargetsAll()
    {
        TestSourcesAndTargets("1", "1");
        TestSourcesAndTargets("\uFEFF0", "0");
        TestSourcesAndTargets("\uFEFF-1", "-1");
        TestSourcesAndTargets("null", "null");
        TestSourcesAndTargets("true", "true");
        TestSourcesAndTargets("false", "false");
        TestSourcesAndTargets("1.2", "1.2");
        TestSourcesAndTargets("1.2e+1", "12");
        TestSourcesAndTargets("10000.0", "10000.0");
        TestSourcesAndTargets("\t\r\n 1.2E-1 ", "0.12");
        TestSourcesAndTargets("9.99e99", "9.99E99");
        TestSourcesAndTargets("\"\"", "\"\"");
        TestSourcesAndTargets("\"\\b\\f\\t\\r\\n\\\"\\/\\\\\\u0019\\u0020\"", "\"\\b\\f\\t\\r\\n\\\"/\\\\\\u0019 \"");
        TestSourcesAndTargets("{ }", "{}");
        TestSourcesAndTargets("{\"a\" : 1}", "{\"a\":1}");
        TestSourcesAndTargets("{\"a\" : 1, \"b\":[], \"c\":{}}", "{\"a\":1,\"b\":[],\"c\":{}}");
        TestSourcesAndTargets("{\"a\" : 1, \"b\":[1,null, true,false,{}]}", "{\"a\":1,\"b\":[1,null,true,false,{}]}");
        TestSourcesAndTargets("{\"1\" : [[[[[[[[[[11.1e-100]]]], null]]], {\n\r}]]]}",
            "{\"1\":[[[[[[[[[[1.11E-99]]]],null]]],{}]]]}");
        TestSourcesAndTargets("{\"b\":false,\"a\":1,\"a\":null}", "{\"b\":false,\"a\":1,\"a\":null}", true);
        TestSourcesAndTargets("[[{\"b\":false,\"a\":1,\"a\":null}]]", "[[{\"b\":false,\"a\":1,\"a\":null}]]", true);
        TestSourcesAndTargets("\"\uD800\uDFFF\"", "\"\uD800\uDFFF\"");
        TestSourcesAndTargets("\"\\uD800\\uDFFF\"", "\"\uD800\uDFFF\"");
        TestSourcesAndTargets("\"\u0700\"", "\"\u0700\"");
        TestSourcesAndTargets("\"\\u0700\"", "\"\u0700\"");
        StringBuilder builder = new();
        _ = builder.Append('"');
        for (int cp = 0x80; cp < 0xD800; cp++)
        {
            _ = builder.Append(char.ConvertFromUtf32(cp));
        }

        for (int cp = 0xE000; cp < 0xfffe; cp++)
        {
            _ = builder.Append(char.ConvertFromUtf32(cp));
        }

        for (int cp = 0xffff; cp <= 0x10FFFF; cp++)
        {
            _ = builder.Append(char.ConvertFromUtf32(cp));
        }

        string s = builder.Append('"').ToString();
        TestSourcesAndTargets(s, s);
        TestSourcesAndTargetsError("", true);
        TestSourcesAndTargetsError("\"", true);
        TestSourcesAndTargetsError("\"\\u", true);
        TestSourcesAndTargetsError("\u0080", true);
        TestSourcesAndTargetsError(".1", true);
        TestSourcesAndTargetsError("1.", true);
        TestSourcesAndTargetsError("1.1e", true);
        TestSourcesAndTargetsError("1.1e+", true);
        TestSourcesAndTargetsError("1.1e-", true);
        TestSourcesAndTargetsError("\b1", true);
        TestSourcesAndTargetsError("\"\\u", true);
        TestSourcesAndTargetsError("\"\\u0", true);
        TestSourcesAndTargetsError("\"\\u00", true);
        TestSourcesAndTargetsError("\"\\u000", true);
        TestSourcesAndTargetsError("\"\\u0000", true);
        TestSourcesAndTargetsError("{,}", true);
        TestSourcesAndTargetsError("{,,}", true);
        TestSourcesAndTargetsError("{}}", true);
        TestSourcesAndTargetsError("{\"a\":\"\":\"\"}", true);
        TestSourcesAndTargetsError("[]]", true);
        TestSourcesAndTargetsError("\"\\uZZZZ\"", true);
        TestSourcesAndTargetsError("\"\\x\"", true);
        TestSourcesAndTargetsError("\"\\", true);
        TestSourcesAndTargetsError("[1,", true);
        TestSourcesAndTargetsError("[1,,2]", true);
        TestSourcesAndTargetsError("[1,]", true);
        TestSourcesAndTargetsError("{\"a\":1,]", true);
        TestSourcesAndTargetsError("[1 2]", true);
        TestSourcesAndTargetsError("{\"a\"-1}", true);
        TestSourcesAndTargetsError("[1;2]", true);
        TestSourcesAndTargetsError("{\"a\":1,b:2}", true);
        TestSourcesAndTargetsError("{\"a\":1;\"b\":2}", true);
        TestSourcesAndTargetsError("fals", true);
        TestSourcesAndTargetsError("falsE", true);
        TestSourcesAndTargetsError("False", true);
        TestSourcesAndTargetsError("nul", true);
        TestSourcesAndTargetsError("nulL", true);
        TestSourcesAndTargetsError("Null", true);
        TestSourcesAndTargetsError("tru", true);
        TestSourcesAndTargetsError("truE", true);
        TestSourcesAndTargetsError("True", true);
        TestSourcesAndTargetsError("\"\uD800\"", false);
        TestSourcesAndTargetsError("\"\\uD800\"", true);
        TestSourcesAndTargetsError("\"\uDC00\"", false);
        TestSourcesAndTargetsError("\"\\uDC00\"", true);
        TestSourcesAndTargetsError("\"\uDBFF \"", false);
        TestSourcesAndTargetsError("\"\\uDBFF \"", true);
        TestSourcesAndTargetsError("\"\uDBFF\\\"", true);
        TestSourcesAndTargetsError("\"\\uDBFF\\\"", true);
        TestSourcesAndTargetsError("\"\uDFFF\uD800\"", false);
        TestSourcesAndTargetsError("\"\\uDFFF\\uD800\"", true);
    }

    private void TestSourcesAndTargets(string src, string expected)
    {
        TestSourcesAndTargets(src, expected, false);
    }

    private void TestSourcesAndTargets(string src, string expected, bool hasNonUniqueKeys)
    {
        JsonItemType itemType = expected[0] switch
        {
            '[' => JsonItemType.Array,
            '{' => JsonItemType.Object,
            _ => JsonItemType.Scalar,
        };
        AssertEquals(expected, (string)ParseString(src, new JsonStringTarget())!);
        AssertEquals(Encoding.UTF8.GetBytes(expected),
            (byte[])ParseString(src, new JsonByteArrayTarget())!);
        AssertEquals(expected, ParseString(src, new JsonValueTarget())!.ToString()!);
        AssertEquals(itemType, ParseString(src, new JsonValidationTargetWithoutUniqueKeys()));
        if (hasNonUniqueKeys)
        {
            TestSourcesAndTargetsError(src, ValidationTargetWithUniqueKeys, true);
        }
        else
        {
            AssertEquals(itemType, ParseString(src, new JsonValidationTargetWithUniqueKeys()));
        }

        foreach (Encoding charset in Charsets)
        {
            AssertEquals(expected,
                (string)ParseBytes(charset.GetBytes(src), new JsonStringTarget())!);
        }
    }

    private void TestSourcesAndTargetsError(string src, bool testBytes)
    {
        TestSourcesAndTargetsError(src, StringTarget, testBytes);
        TestSourcesAndTargetsError(src, BytesTarget, testBytes);
        TestSourcesAndTargetsError(src, ValueTarget, testBytes);
        TestSourcesAndTargetsError(src, ValidationTargetWithoutUniqueKeys, testBytes);
        TestSourcesAndTargetsError(src, ValidationTargetWithUniqueKeys, testBytes);
    }

    private void TestSourcesAndTargetsError(string src, Func<JsonTarget> constructor,
        bool testBytes)
    {
        JsonTarget target = constructor();
        if (!ThrowsParse(() => ParseString(src, target)))
        {
            Fail("Expected a parse failure for: " + src);
        }

        /*
         * String.getBytes() replaces invalid characters, so some tests are disabled.
         */
        if (testBytes)
        {
            target = constructor();
            if (!ThrowsParse(() => ParseBytes(Encoding.UTF8.GetBytes(src), target)))
            {
                Fail("Expected a byte parse failure for: " + src);
            }
        }
    }

    private static bool ThrowsParse(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private void TestUtfErrorAll()
    {
        // 2 bytes
        TestUtfError(new byte[] { (byte)'"', 0xc2, 0xc0, (byte)'"' });
        TestUtfError(new byte[] { (byte)'"', 0xc1, 0xbf, (byte)'"' });
        TestUtfError(new byte[] { (byte)'"', 0xc2 });
        // 3 bytes
        TestUtfError(new byte[] { (byte)'"', 0xe1, 0xc0, 0x80, (byte)'"' });
        TestUtfError(new byte[] { (byte)'"', 0xe1, 0x80, 0xc0, (byte)'"' });
        TestUtfError(new byte[] { (byte)'"', 0xe0, 0x9f, 0xbf, (byte)'"' });
        TestUtfError(new byte[] { (byte)'"', 0xe1, 0x80 });
        // 4 bytes
        TestUtfError(new byte[] { (byte)'"', 0xf1, 0xc0, 0x80, 0x80, (byte)'"' });
        TestUtfError(new byte[] { (byte)'"', 0xf1, 0x80, 0xc0, 0x80, (byte)'"' });
        TestUtfError(new byte[] { (byte)'"', 0xf1, 0x80, 0x80, 0xc0, (byte)'"' });
        TestUtfError(new byte[] { (byte)'"', 0xf0, 0x8f, 0xbf, 0xbf, (byte)'"' });
        TestUtfError(new byte[] { (byte)'"', 0xf4, 0x90, 0x80, 0x80, (byte)'"' });
        TestUtfError(new byte[] { (byte)'"', 0xf1, 0x80, 0x80 });
    }

    private void TestUtfError(byte[] bytes)
    {
        AssertThrows(typeof(ArgumentException),
            () => ParseBytes(bytes, new JsonValidationTargetWithoutUniqueKeys()));
    }

    private void TestLongNesting()
    {
        const int halfLevel = 2048;
        StringBuilder builder = new(halfLevel * 8);
        for (int i = 0; i < halfLevel; i++)
        {
            _ = builder.Append("{\"a\":[");
        }

        for (int i = 0; i < halfLevel; i++)
        {
            _ = builder.Append("]}");
        }

        string s = builder.ToString();
        AssertEquals(s, (string)ParseString(s, new JsonStringTarget())!);
        byte[] bytes = Encoding.Latin1.GetBytes(s);
        AssertEquals(bytes, JsonBytesSource.Normalize(bytes));
    }

    private void TestEncodeStringAll()
    {
        TestEncodeString("abc \"\u0001\u007f\u0080\u1000\uabcd\n'\t",
            "\"abc \\\"\\u0001\u007f\u0080\u1000\uabcd\\n'\\t\"",
            "\"abc \\\"\\u0001\\u007f\\u0080\\u1000\\uabcd\\n\\u0027\\t\"");
        // A string with a surrogate pair can be encoded
        using (MemoryStream stream = new())
        {
            _ = JsonByteArrayTarget.EncodeString(stream, "\uD83D\uDE00");
            AssertEquals("\"\uD83D\uDE00\"", Encoding.UTF8.GetString(stream.ToArray()));
        }

        // Strings with unpaired surrogates cannot be encoded
        TestEncodeStringError("x\uD800y");
        TestEncodeStringError("\uDC00");
        TestEncodeStringError("\uD800\uD800");
        TestEncodeStringError("\uDC00\uD800");
    }

    private void TestEncodeStringError(string s)
    {
        AssertThrows(typeof(H2SharpException),
            () => JsonByteArrayTarget.EncodeString(new MemoryStream(), s));
    }

    private void TestEncodeString(string source, string expected, string expectedPrintable)
    {
        AssertEquals(expected,
            JsonStringTarget.EncodeString(new StringBuilder(), source, false).ToString());
        AssertEquals(expectedPrintable,
            JsonStringTarget.EncodeString(new StringBuilder(), source, true).ToString());
    }

    /// <summary>
    /// Parses a string into the target, choosing the typed Parse overload by the target's
    /// runtime type (Java's <c>JSONTarget&lt;?&gt;</c> wildcard has no C# counterpart).
    /// </summary>
    /// <param name="text">the source</param>
    /// <param name="target">the target</param>
    /// <returns>the target's result</returns>
    private static object? ParseString(string text, JsonTarget target)
    {
        return target switch
        {
            JsonStringTarget t => JsonStringSource.Parse(text, t),
            JsonByteArrayTarget t => JsonStringSource.Parse(text, t),
            JsonValueTarget t => JsonStringSource.Parse(text, t),
            JsonValidationTargetWithoutUniqueKeys t => JsonStringSource.Parse(text, t),
            JsonValidationTargetWithUniqueKeys t => JsonStringSource.Parse(text, t),
            _ => throw new InvalidOperationException(target.GetType().Name),
        };
    }

    /// <summary>
    /// Parses bytes into the target, choosing the typed Parse overload by the target's
    /// runtime type.
    /// </summary>
    /// <param name="bytes">the source</param>
    /// <param name="target">the target</param>
    /// <returns>the target's result</returns>
    private static object? ParseBytes(byte[] bytes, JsonTarget target)
    {
        return target switch
        {
            JsonStringTarget t => JsonBytesSource.Parse(bytes, t),
            JsonByteArrayTarget t => JsonBytesSource.Parse(bytes, t),
            JsonValueTarget t => JsonBytesSource.Parse(bytes, t),
            JsonValidationTargetWithoutUniqueKeys t => JsonBytesSource.Parse(bytes, t),
            JsonValidationTargetWithUniqueKeys t => JsonBytesSource.Parse(bytes, t),
            _ => throw new InvalidOperationException(target.GetType().Name),
        };
    }
}
