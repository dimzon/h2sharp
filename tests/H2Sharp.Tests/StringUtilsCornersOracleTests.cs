/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The corners of StringUtils against the reference.
 */
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using H2Sharp.Message;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// The corners of <see cref="StringUtils"/> the first differential suite did not reach:
/// the quote helpers with their null roads, the XML node builders, the trim sets, the soft
/// string cache with its instance identity, the parse refusals, the hex-with-spaces decoder,
/// and the error roads of the unicode and octal decoders.
/// </summary>
/// <remarks>
/// Every group is a differential test against the reference JVM: the Java side calls the
/// reference, the C# side calls the port, and the outputs must be line-identical. The port
/// helper methods that have no reference counterpart are pinned by direct tests at the bottom.
/// </remarks>
[Collection(OracleCollection.Name)]
public sealed class StringUtilsCornersOracleTests
{
    // ------------------------------------------------------- quoting and arrays

    [JavaOracleFact]
    public void QuoteCorners_MatchReferenceJvm()
    {
        const string Body = """
        {
        String n = null;
        System.out.println("qs_null|" + (org.h2.util.StringUtils.quoteJavaString(n) == null ? "null"
            : hex(org.h2.util.StringUtils.quoteJavaString(n))));
        System.out.println("qs_empty|" + hex(org.h2.util.StringUtils.quoteJavaString("")));
        System.out.println("qs|" + hex(org.h2.util.StringUtils.quoteJavaString("a\"b\\c\n")));
        System.out.println("qsa_null|" + hex(org.h2.util.StringUtils.quoteJavaStringArray(null)));
        System.out.println("qsa_empty|" + hex(org.h2.util.StringUtils.quoteJavaStringArray(new String[0])));
        System.out.println("qsa|" + hex(org.h2.util.StringUtils.quoteJavaStringArray(
            new String[]{null, "a\"b\\c", "\n"})));
        System.out.println("qia_null|" + hex(org.h2.util.StringUtils.quoteJavaIntArray(null)));
        System.out.println("qia_empty|" + hex(org.h2.util.StringUtils.quoteJavaIntArray(new int[0])));
        System.out.println("qia|" + hex(org.h2.util.StringUtils.quoteJavaIntArray(new int[]{1, -2, 0, 2147483647})));
        }
        """;
        Compare(Body, QuoteCornersLines());
    }

    private static IEnumerable<string> QuoteCornersLines()
    {
        yield return "qs_null|" + OracleRunner.Hex(StringUtils.QuoteJavaString(null));
        yield return "qs_empty|" + OracleRunner.Hex(StringUtils.QuoteJavaString(""));
        yield return "qs|" + OracleRunner.Hex(StringUtils.QuoteJavaString("a\"b\\c\n"));
        yield return "qsa_null|" + OracleRunner.Hex(StringUtils.QuoteJavaStringArray(null));
        yield return "qsa_empty|" + OracleRunner.Hex(StringUtils.QuoteJavaStringArray([]));
        yield return "qsa|" + OracleRunner.Hex(StringUtils.QuoteJavaStringArray(
            [null, "a\"b\\c", "\n"]));
        yield return "qia_null|" + OracleRunner.Hex(StringUtils.QuoteJavaIntArray(null));
        yield return "qia_empty|" + OracleRunner.Hex(StringUtils.QuoteJavaIntArray([]));
        yield return "qia|" + OracleRunner.Hex(StringUtils.QuoteJavaIntArray([1, -2, 0, int.MaxValue]));
    }

    // ------------------------------------------------------- asterisk and array split

    [JavaOracleFact]
    public void AddAsterisk_ArraySplit_MatchReferenceJvm()
    {
        const string Body = """
        {
        System.out.println("ast_null|" + (org.h2.util.StringUtils.addAsterisk(null, 3) == null ? "null" : "notnull"));
        System.out.println("ast_1|" + hex(org.h2.util.StringUtils.addAsterisk("abcdef", 1)));
        safe(() -> System.out.println("ast_neg|" + hex(org.h2.util.StringUtils.addAsterisk("abcdef", -5))));
        System.out.println("ast_over|" + hex(org.h2.util.StringUtils.addAsterisk("abc", 99)));
        System.out.println("sp_null|" + (org.h2.util.StringUtils.arraySplit(null, ';', true) == null ? "null" : "notnull"));
        System.out.println("sp_empty|" + java.util.Arrays.toString(org.h2.util.StringUtils.arraySplit("", ';', true)));
        System.out.println("sp|" + java.util.Arrays.toString(org.h2.util.StringUtils.arraySplit("a\\;b; c ;d", ';', true)));
        System.out.println("sp_raw|" + java.util.Arrays.toString(org.h2.util.StringUtils.arraySplit("a\\;b; c", ';', false)));
        System.out.println("sp_one|" + java.util.Arrays.toString(org.h2.util.StringUtils.arraySplit("solo", ';', true)));
        }
        """;
        Compare(Body, AddAsteriskLines(), members: Members_Safe());
    }

    private static IEnumerable<string> AddAsteriskLines()
    {
        yield return "ast_null|" + (StringUtils.AddAsterisk(null, 3) is null ? "null" : "notnull");
        yield return "ast_1|" + OracleRunner.Hex(StringUtils.AddAsterisk("abcdef", 1)!);
        // A negative index is refused on both sides: the reference's append(s, 0, index)
        // rejects a negative end, the port's Append rejects a negative count. The JVM
        // answer arrives as the template's bare THROWS, so the whole line is inside the
        // guard here as well.
        yield return Safe(() => "ast_neg|" + OracleRunner.Hex(StringUtils.AddAsterisk("abcdef", -5)!));
        yield return "ast_over|" + OracleRunner.Hex(StringUtils.AddAsterisk("abc", 99)!);
        yield return "sp_null|" + (StringUtils.ArraySplit(null, ';', true) is null ? "null" : "notnull");
        yield return "sp_empty|" + JavaArrayToString(StringUtils.ArraySplit("", ';', true)!);
        yield return "sp|" + JavaArrayToString(StringUtils.ArraySplit("a\\;b; c ;d", ';', true)!);
        yield return "sp_raw|" + JavaArrayToString(StringUtils.ArraySplit("a\\;b; c", ';', false)!);
        yield return "sp_one|" + JavaArrayToString(StringUtils.ArraySplit("solo", ';', true)!);
    }

    /// <summary>
    /// An array the way <c>java.util.Arrays.toString</c> prints it, so a null element shows
    /// as null and the spacing matches.
    /// </summary>
    private static string JavaArrayToString(string?[] array)
        => "[" + string.Join(", ", array.Select(static e => e ?? "null")) + "]";

    // --------------------------------------------------------------------- xml

    [JavaOracleFact]
    public void XmlHelpers_MatchReferenceJvm()
    {
        const string Body = """
        {
        System.out.println("attr|" + hex(org.h2.util.StringUtils.xmlAttr("name", "v<>&'\"\u00e9")));
        System.out.println("node1|" + hex(org.h2.util.StringUtils.xmlNode("n", null, null)));
        System.out.println("node2|" + hex(org.h2.util.StringUtils.xmlNode("n", " a=\"b\"", "c")));
        System.out.println("node3|" + hex(org.h2.util.StringUtils.xmlNode("n", null, "line1\nline2")));
        System.out.println("node4|" + hex(org.h2.util.StringUtils.xmlNode("n", null, "line1\nline2", false)));
        System.out.println("node5|" + hex(org.h2.util.StringUtils.xmlNode("n", null, "line1\nline2", true)));
        System.out.println("doc|" + hex(org.h2.util.StringUtils.xmlStartDoc()));
        System.out.println("text1|" + hex(org.h2.util.StringUtils.xmlText("a<>&'\"\t\u00e9\n\r", true)));
        System.out.println("text2|" + hex(org.h2.util.StringUtils.xmlText("a<>&'\"\t\u00e9\n\r", false)));
        }
        """;
        Compare(Body, XmlLines());
    }

    private static IEnumerable<string> XmlLines()
    {
        yield return "attr|" + OracleRunner.Hex(StringUtils.XmlAttr("name", "v<>&'\"\u00e9"));
        yield return "node1|" + OracleRunner.Hex(StringUtils.XmlNode("n", null, null));
        yield return "node2|" + OracleRunner.Hex(StringUtils.XmlNode("n", " a=\"b\"", "c"));
        yield return "node3|" + OracleRunner.Hex(StringUtils.XmlNode("n", null, "line1\nline2"));
        yield return "node4|" + OracleRunner.Hex(StringUtils.XmlNode("n", null, "line1\nline2", false));
        yield return "node5|" + OracleRunner.Hex(StringUtils.XmlNode("n", null, "line1\nline2", true));
        yield return "doc|" + OracleRunner.Hex(StringUtils.XmlStartDoc());
        yield return "text1|" + OracleRunner.Hex(StringUtils.XmlText("a<>&'\"\t\u00e9\n\r", true));
        yield return "text2|" + OracleRunner.Hex(StringUtils.XmlText("a<>&'\"\t\u00e9\n\r", false));
    }

    // --------------------------------------------------------------------- trim

    [JavaOracleFact]
    public void TrimAndTrimSubstring_MatchReferenceJvm()
    {
        const string Body = """
        {
        String[] sets = { null, "", "x", "xy", "xx", "abc", "\uD83D\uDE00", "x\uD83D\uDE00z" };
        String[] subjects = { "xxytest", "  padded  ", "xxayybxx", "\uD83D\uDE00a\uD83D\uDE00b\uD83D\uDE00",
            "\uD83D\uDE00", "a", "zzz" };
        for (String s : subjects) {
          for (String c : sets) {
            System.out.println("trim|" + hex(org.h2.util.StringUtils.trim(s, true, true, c)));
          }
        }
        System.out.println("ts|" + org.h2.util.StringUtils.trimSubstring(new StringBuilder("["), "  a b  ", 0, 7).append("]").toString());
        System.out.println("ts_empty|" + org.h2.util.StringUtils.trimSubstring(new StringBuilder("["), "  ", 0, 2).append("]").toString());
        System.out.println("ts_mid|" + org.h2.util.StringUtils.trimSubstring(new StringBuilder("["), "a  b", 1, 3).append("]").toString());
        System.out.println("ts_none|" + org.h2.util.StringUtils.trimSubstring(new StringBuilder("["), "abc", 1, 2).append("]").toString());
        }
        """;
        Compare(Body, TrimLines());
    }

    private static IEnumerable<string> TrimLines()
    {
        string[] sets = [null!, "", "x", "xy", "xx", "abc", "\uD83D\uDE00", "x\uD83D\uDE00z"];
        string[] subjects = ["xxytest", "  padded  ", "xxayybxx", "\uD83D\uDE00a\uD83D\uDE00b\uD83D\uDE00",
            "\uD83D\uDE00", "a", "zzz"];
        foreach (string s in subjects)
        {
            foreach (string? c in sets)
            {
                yield return "trim|" + OracleRunner.Hex(StringUtils.Trim(s, true, true, c));
            }
        }

        yield return "ts|" + StringUtils.TrimSubstring(new StringBuilder("["), "  a b  ", 0, 7).Append(']');
        yield return "ts_empty|" + StringUtils.TrimSubstring(new StringBuilder("["), "  ", 0, 2).Append(']');
        yield return "ts_mid|" + StringUtils.TrimSubstring(new StringBuilder("["), "a  b", 1, 3).Append(']');
        yield return "ts_none|" + StringUtils.TrimSubstring(new StringBuilder("["), "abc", 1, 2).Append(']');
    }

    // --------------------------------------------------------------------- cache

    [JavaOracleFact]
    public void Cache_MatchesReferenceJvm()
    {
        const string Body = """
        {
        System.out.println("c_null|" + (org.h2.util.StringUtils.cache(null) == null ? "null" : "notnull"));
        System.out.println("c_empty|" + hex(org.h2.util.StringUtils.cache("")));
        String f1 = new String(new char[]{'a','b','c'});
        String r1 = org.h2.util.StringUtils.cache(f1);
        System.out.println("c_content1|" + hex(r1));
        String f2 = new String(new char[]{'a','b','c'});
        String r2 = org.h2.util.StringUtils.cache(f2);
        System.out.println("c_content2|" + hex(r2));
        org.h2.util.StringUtils.clearCache();
        String f3 = new String(new char[]{'a','b','c'});
        String r3 = org.h2.util.StringUtils.cache(f3);
        System.out.println("c_after|" + (r3 == f3) + "|" + (r3 == f1));
        String r4 = org.h2.util.StringUtils.cache(new String(new char[]{'a','b','c'}));
        System.out.println("c_fourth|" + (r4 == f1));
        }
        """;
        Compare(Body, CacheLines());
    }

    private static IEnumerable<string> CacheLines()
    {
        // Only the answers that do not depend on when this process last created the cache
        // are compared. The reference returns a stored instance on a hit and the argument
        // itself on a store, so instance identity against a fresh equal string is decided
        // by whether the slot happens to hold one — in a fresh JVM it always does, in this
        // shared process other threads fill the cache constantly, and the 5-second window
        // after clearCache decides the rest. Content and the two roads after clearCache
        // are stable everywhere: on both sides the argument comes back, and a different
        // fresh instance never is the one stored earlier.
        yield return "c_null|" + (StringUtils.Cache(null) is null ? "null" : "notnull");
        yield return "c_empty|" + OracleRunner.Hex(StringUtils.Cache(""));
        string f1 = new(new[] { 'a', 'b', 'c' });
        string r1 = StringUtils.Cache(f1);
        yield return "c_content1|" + OracleRunner.Hex(r1);
        string f2 = new(new[] { 'a', 'b', 'c' });
        string r2 = StringUtils.Cache(f2);
        yield return "c_content2|" + OracleRunner.Hex(r2);
        StringUtils.ClearCache();
        string f3 = new(new[] { 'a', 'b', 'c' });
        string r3 = StringUtils.Cache(f3);
        yield return "c_after|" + J(ReferenceEquals(r3, f3)) + "|" + J(ReferenceEquals(r3, f1));
        string r4 = StringUtils.Cache(new(new[] { 'a', 'b', 'c' }));
        yield return "c_fourth|" + J(ReferenceEquals(r4, f1));
    }

    // ------------------------------------------------------- parse and decode errors

    [JavaOracleFact]
    public void ParseUInt31_Refusals_MatchReferenceJvm()
    {
        const string Body = """
        {
        safe(() -> System.out.println(org.h2.util.StringUtils.parseUInt31("00123", 0, 5)));
        safe(() -> System.out.println(org.h2.util.StringUtils.parseUInt31("x2147483647y", 1, 11)));
        safe(() -> System.out.println(org.h2.util.StringUtils.parseUInt31("x2147483648y", 1, 11)));
        safe(() -> System.out.println(org.h2.util.StringUtils.parseUInt31("abc", 3, 4)));
        safe(() -> System.out.println(org.h2.util.StringUtils.parseUInt31("abc", 2, 1)));
        safe(() -> System.out.println(org.h2.util.StringUtils.parseUInt31("abc", -1, 1)));
        safe(() -> System.out.println(org.h2.util.StringUtils.parseUInt31("a12b", 1, 3)));
        }
        """;
        Compare(Body, ParseUInt31Lines(), members: Members_Safe());
    }

    private static IEnumerable<string> ParseUInt31Lines()
    {
        yield return Safe(() => StringUtils.ParseUInt31("00123", 0, 5)
            .ToString(CultureInfo.InvariantCulture));
        yield return Safe(() => StringUtils.ParseUInt31("x2147483647y", 1, 11)
            .ToString(CultureInfo.InvariantCulture));
        yield return Safe(() => StringUtils.ParseUInt31("x2147483648y", 1, 11)
            .ToString(CultureInfo.InvariantCulture));
        yield return Safe(() => StringUtils.ParseUInt31("abc", 3, 4)
            .ToString(CultureInfo.InvariantCulture));
        yield return Safe(() => StringUtils.ParseUInt31("abc", 2, 1)
            .ToString(CultureInfo.InvariantCulture));
        yield return Safe(() => StringUtils.ParseUInt31("abc", -1, 1)
            .ToString(CultureInfo.InvariantCulture));
        yield return Safe(() => StringUtils.ParseUInt31("a12b", 1, 3)
            .ToString(CultureInfo.InvariantCulture));
    }

    // ------------------------------------------------------- hex with spaces

    [JavaOracleFact]
    public void ConvertHexWithSpacesToBytes_MatchesReferenceJvm()
    {
        const string Body = """
        {
        String[] cases = { "41 42", "4142", " 4142 ", "41 4", "4 ", "4", "zz", "4g", "ff", "FF",
            "41 42 43", "  ", "4 1 4 2", "41" };
        for (String s : cases) {
          try {
            java.io.ByteArrayOutputStream out = new java.io.ByteArrayOutputStream();
            org.h2.util.StringUtils.convertHexWithSpacesToBytes(out, s, 0, s.length());
            StringBuilder b = new StringBuilder();
            for (byte v : out.toByteArray()) { b.append(b.length() > 0 ? " " : "").append(v & 255); }
            System.out.println("hex|" + b);
          } catch (RuntimeException e) {
            String m = e.getMessage();
            int nl = m.lastIndexOf('\n');
            if (nl >= 0) { m = m.substring(nl + 1); }
            System.out.println("hex|" + hex(m));
          }
        }
        }
        """;
        Compare(Body, HexWithSpacesLines(), members: Members_None());
    }

    private static IEnumerable<string> HexWithSpacesLines()
    {
        string[] cases =
        [
            "41 42", "4142", " 4142 ", "41 4", "4 ", "4", "zz", "4g", "ff", "FF",
            "41 42 43", "  ", "4 1 4 2", "41",
        ];
        foreach (string s in cases)
        {
            string result;
            try
            {
                ArrayBufferWriter<byte> writer = new();
                StringUtils.ConvertHexWithSpacesToBytes(writer, s, 0, s.Length);
                List<string> bytes = [.. writer.WrittenSpan.ToArray().Select(static b => b.ToString(CultureInfo.InvariantCulture))];
                result = string.Join(" ", bytes);
            }
        catch (H2SharpException e)
        {
            result = OracleRunner.Hex(StripTranslated(e.Message));
        }

            yield return "hex|" + result;
        }
    }

    /// <summary>
    /// The reference builds a DbException message as the translated line, a newline, and
    /// the original line with the error code; the port carries the original only. The
    /// comparison therefore reads the part after the last newline, which is the original
    /// on the JVM and the whole message on the port.
    /// </summary>
    private static string StripTranslated(string message)
    {
        int nl = message.LastIndexOf('\n');
        return nl >= 0 ? message[(nl + 1)..] : message;
    }

    // ------------------------------------------------------- encode/decode error roads

    [JavaOracleFact]
    public void EncodeForSql_And_DecodeErrors_MatchReferenceJvm()
    {
        const string Body = """
        {
        StringBuilder b = new StringBuilder();
        org.h2.util.StringUtils.javaEncode("a'b\"c\\d", b, true);
        System.out.println("enc_sql|" + hex(b.toString()));
        StringBuilder b2 = new StringBuilder();
        org.h2.util.StringUtils.javaEncode("plain", b2, true);
        System.out.println("enc_plain|" + hex(b2.toString()));
        System.out.println("dec_r|" + hex(org.h2.util.StringUtils.javaDecode("a\\rb")));
        System.out.println("dec_octal|" + hex(org.h2.util.StringUtils.javaDecode("\\777")));
        safe(() -> System.out.println(org.h2.util.StringUtils.javaDecode("\\999")));
        safe(() -> System.out.println(org.h2.util.StringUtils.javaDecode("\\789")));
        runMsg(() -> org.h2.util.StringUtils.decodeUnicodeStringSQL("\\+00ZZ00", '\\'));
        runMsg(() -> org.h2.util.StringUtils.decodeUnicodeStringSQL("\\ZZZZZ", '\\'));
        runMsg(() -> org.h2.util.StringUtils.decodeUnicodeStringSQL("u+0000Z1", 'u'));
        runMsg(() -> org.h2.util.StringUtils.decodeUnicodeStringSQL("\\ZZZZ", '\\'));
        }
        """;
        Compare(Body, EncodeDecodeLines(), members: Members_SafeMsg());
    }

    private static IEnumerable<string> EncodeDecodeLines()
    {
        StringBuilder b = new();
        StringUtils.JavaEncode("a'b\"c\\d", b, true);
        yield return "enc_sql|" + OracleRunner.Hex(b.ToString());
        StringBuilder b2 = new();
        StringUtils.JavaEncode("plain", b2, true);
        yield return "enc_plain|" + OracleRunner.Hex(b2.ToString());
        yield return "dec_r|" + OracleRunner.Hex(StringUtils.JavaDecode("a\\rb"));
        yield return "dec_octal|" + OracleRunner.Hex(StringUtils.JavaDecode("\\777"));
        yield return Safe(() => StringUtils.JavaDecode("\\999"), () => "THROWS");
        yield return Safe(() => StringUtils.JavaDecode("\\789"), () => "THROWS");
        yield return ExceptionMessage(() => StringUtils.DecodeUnicodeStringSql("\\+00ZZ00", '\\'));
        yield return ExceptionMessage(() => StringUtils.DecodeUnicodeStringSql("\\ZZZZZ", '\\'));
        yield return ExceptionMessage(() => StringUtils.DecodeUnicodeStringSql("u+0000Z1", 'u'));
        yield return ExceptionMessage(() => StringUtils.DecodeUnicodeStringSql("\\ZZZZ", '\\'));
    }

    /// <summary>
    /// A decoded error as both sides print it: the full exception message, which carries
    /// the asterisk that marks where the format broke.
    /// </summary>
    private static string ExceptionMessage(Func<string> action)
    {
        try
        {
            _ = action();
            return "msg|NO_THROW";
        }
        catch (H2SharpException e)
        {
            return "msg|" + OracleRunner.Hex(e.Message);
        }
    }

    private static string Safe(Func<string> action, Func<string>? onError = null)
    {
        try
        {
            return action();
        }
        catch (Exception)
        {
            return onError?.Invoke() ?? "THROWS";
        }
    }

    private static string J(bool value) => value ? "true" : "false";

    private static string Members_None() => string.Empty;

    private static string Members_Safe() =>
        """
        static void safe(Runnable r) {
          try { r.run(); } catch (RuntimeException e) { System.out.println("THROWS"); }
        }
        """;

    private static string Members_SafeMsg() =>
        """
        static void safe(Runnable r) {
          try { r.run(); } catch (RuntimeException e) { System.out.println("THROWS"); }
        }

        static void runMsg(Runnable r) {
          try { r.run(); } catch (RuntimeException e) {
            String m = e.getMessage();
            int nl = m.lastIndexOf('\n');
            if (nl >= 0) { m = m.substring(nl + 1); }
            System.out.println("msg|" + hex(m));
          }
        }
        """;

    /// <summary>
    /// Runs the Java body against the reference and compares the output line by line.
    /// </summary>
    private static void Compare(string body, IEnumerable<string> csharp, string? members = null)
    {
        List<string> java = OracleRunner.Run([], body, null, null, null, members).ToList();
        List<string> port = [.. csharp];
        int common = Math.Min(java.Count, port.Count);
        for (int i = 0; i < common; i++)
        {
            Assert.True(java[i] == port[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {port[i]}");
        }

        Assert.True(java.Count == port.Count,
            "строк: java=" + java.Count.ToString(CultureInfo.InvariantCulture)
            + " csharp=" + port.Count.ToString(CultureInfo.InvariantCulture)
            + "\n  последнее совпадение: " + (common - 1).ToString(CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// The port helpers that have no reference counterpart — the .NET road for formatting
/// numbers and lengths, the metadata pattern escape, and the code point appenders. They are
/// written directly against the reference behaviour they replace.
/// </summary>
public sealed class StringUtilsPortHelperTests
{
    [Fact]
    public void CompareJavaIgnoreCase_FoldsInThreeSteps()
    {
        // "i" and U+0130 differ, their uppercase differs, their lowercase agrees: equal.
        Assert.Equal(0, StringUtils.CompareJavaIgnoreCase("i", "\u0130"));
        Assert.Equal(0, StringUtils.CompareJavaIgnoreCase("\u0130", "i"));
        Assert.Equal('a' - 'b', StringUtils.CompareJavaIgnoreCase("a", "b"));
        Assert.True(StringUtils.CompareJavaIgnoreCase("B", "a") > 0);
        Assert.True(StringUtils.CompareJavaIgnoreCase("ABC", "ABD") < 0);
        Assert.True(StringUtils.CompareJavaIgnoreCase("abc", "ab") > 0);
        Assert.True(StringUtils.CompareJavaIgnoreCase("ab", "abc") < 0);
        Assert.Equal(0, StringUtils.CompareJavaIgnoreCase("", ""));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData(" ", true)]
    [InlineData("\t\n\r ", true)]
    [InlineData("a", false)]
    [InlineData(" a", false)]
    [InlineData("a ", false)]
    public void IsWhitespaceOrEmpty_AnswersLikeTheReference(string s, bool expected)
        => Assert.Equal(expected, StringUtils.IsWhitespaceOrEmpty(s));

    [Fact]
    public void AppendInvariant_WritesAsciiDigits()
    {
        StringBuilder b = new();
        _ = StringUtils.AppendInvariant(b, 0L);
        _ = StringUtils.AppendInvariant(b, -5L);
        _ = StringUtils.AppendInvariant(b, long.MinValue);
        _ = StringUtils.AppendInvariant(b, int.MaxValue);
        _ = StringUtils.AppendInvariant(b, 2147483648L);
        Assert.Equal("0-5-922337203685477580821474836472147483648", b.ToString());
    }

    [Theory]
    [InlineData(5, 42, "00042")]
    [InlineData(2, 12345, "12345")]
    [InlineData(4, -7, "00-7")]
    [InlineData(3, 0, "000")]
    [InlineData(1, 10, "10")]
    [InlineData(0, 7, "7")]
    [InlineData(6, 1000000, "1000000")]
    public void AppendZeroPadded_PadsToTheLength(int length, int value, string expected)
    {
        StringBuilder b = new();
        _ = StringUtils.AppendZeroPadded(b, length, value);
        Assert.Equal(expected, b.ToString());
    }

    [Fact]
    public void AppendToLength_FillsUpToTheLimit()
    {
        StringBuilder b = new("ab");
        _ = StringUtils.AppendToLength(b, "cdefghij", 8);
        Assert.Equal("abcdefgh", b.ToString());

        StringBuilder b2 = new("abcdefg");
        _ = StringUtils.AppendToLength(b2, "xy", 8);
        Assert.Equal("abcdefgx", b2.ToString());

        StringBuilder b3 = new("already long enough");
        _ = StringUtils.AppendToLength(b3, "s", 4);
        Assert.Equal("already long enough", b3.ToString());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("a_b", "a_b")]
    [InlineData("a\\b", "a\\\\b")]
    [InlineData("\\\\", "\\\\\\\\")]
    public void EscapeMetaDataPattern_DoublesBackslashes(string? pattern, string? expected)
        => Assert.Equal(expected, StringUtils.EscapeMetaDataPattern(pattern));

    [Theory]
    [InlineData("abc", "ab", true)]
    [InlineData("abc", "abc", true)]
    [InlineData("ABC", "ab", false)]
    [InlineData("ab", "abc", false)]
    public void StartsWith_IsCaseSensitiveOrdinal(string text, string prefix, bool expected)
        => Assert.Equal(expected, StringUtils.StartsWith(text, prefix));

    [Fact]
    public void AppendCodePoint_SurrogatePairsAndSingleChars()
    {
        StringBuilder b = new();
        StringUtils.AppendCodePoint(b, 'A');
        StringUtils.AppendCodePoint(b, 0x1F600);
        StringUtils.AppendCodePoint(b, 0xFFFF);
        StringUtils.AppendCodePoint(b, 0x10000);
        Assert.Equal("A\uD83D\uDE00\uFFFF\uD800\uDC00", b.ToString());
    }

    [Fact]
    public void ToUpperEnglish_KeepsNonAsciiMappingOnTheCacheRoad()
    {
        // U+00E9 changes under the invariant mapping, so the no-change fast road returns
        // the mapped form and the identity road keeps the original: both are the cache
        // entry the reference keeps as a pair.
        Assert.Equal("\u00c9", StringUtils.ToUpperEnglish("\u00e9"));
        Assert.Equal("ABC", StringUtils.ToUpperEnglish("abc"));
        Assert.Equal("ABC", StringUtils.ToUpperEnglish("ABC"));
    }
}
