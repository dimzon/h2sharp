// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.unit.TestStringUtils.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified: the reference's main() entry is the runner; xUnit runs Test() directly.
//
// Deferred (see MIGRATION_STATUS.md): testURL compares the port against
// java.net.URLEncoder/URLDecoder, which .NET does not have; testXML's RSS assembly uses
// DateTimeFormatFunction.parseDateTime/formatDateTime, which the port does not have —
// the xml primitives themselves are tested, the datetime-fueled assembly is not. The
// refusal types are mapped: NumberFormatException/NullPointerException paths answer any
// thrown exception, DbException is H2SharpException.
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Native.Tests.Unit;

/// <summary>
/// Tests string utility methods.
/// </summary>
public sealed class TestStringUtils
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public void Test()
    {
        TestParseUInt31();
        TestHex();
        TestXML();
        TestSplit();
        TestJavaString();
        // testURL: deferred, see the header.
        TestPad();
        TestReplaceAll();
        TestTrim();
        TestTrimSubstring();
        TestTruncateString();
    }

    private static void TestParseUInt31()
    {
        Assert.Equal(0, StringUtils.ParseUInt31("101", 1, 2));
        Assert.Equal(11, StringUtils.ParseUInt31("11", 0, 2));
        Assert.Equal(0, StringUtils.ParseUInt31("000", 0, 3));
        Assert.Equal(1, StringUtils.ParseUInt31("01", 0, 2));
        Assert.Equal(999999999, StringUtils.ParseUInt31("X999999999", 1, 10));
        Assert.Equal(2147483647, StringUtils.ParseUInt31("2147483647", 0, 10));
        TestParseUInt31Bad(null, 0, 1);
        TestParseUInt31Bad("1", -1, 1);
        TestParseUInt31Bad("1", 0, 0);
        TestParseUInt31Bad("12", 1, 0);
        TestParseUInt31Bad("-0", 0, 2);
        TestParseUInt31Bad("+0", 0, 2);
        TestParseUInt31Bad("2147483648", 0, 10);
        TestParseUInt31Bad("21474836470", 0, 11);
    }

    private static void TestParseUInt31Bad(string? s, int start, int end)
    {
        try
        {
            _ = StringUtils.ParseUInt31(s!, start, end);
        }
        catch (Exception)
        {
            return;
        }

        throw new Xunit.Sdk.XunitException("Expected an error");
    }

    private static void TestHex()
    {
        Assert.Equal("face", StringUtils.ConvertBytesToHex([(byte)0xfa, (byte)0xce]));
        Assert.Equal(new byte[] { (byte)0xfa, (byte)0xce },
            StringUtils.ConvertHexToBytes("face"));
        Assert.Equal(new byte[] { (byte)0xfa, (byte)0xce },
            StringUtils.ConvertHexToBytes("fAcE"));
        Assert.Equal(new byte[] { (byte)0xfa, (byte)0xce },
            StringUtils.ConvertHexToBytes("FaCe"));
        Assert.Throws<H2SharpException>(() => StringUtils.ConvertHexToBytes("120"));
        Assert.Throws<H2SharpException>(() => StringUtils.ConvertHexToBytes("fast"));
        Assert.Throws<H2SharpException>(() => StringUtils.ConvertHexToBytes("012=abcf"));
    }

    private static void TestPad()
    {
        Assert.Equal("large", StringUtils.Pad("larger text", 5, null, true));
        Assert.Equal("large", StringUtils.Pad("larger text", 5, null, false));
        Assert.Equal("short+++++", StringUtils.Pad("short", 10, "+", true));
        Assert.Equal("+++++short", StringUtils.Pad("short", 10, "+", false));
    }

    private static void TestXML()
    {
        Assert.Equal("<!-- - - - - - -abc- - - - - - -->\n",
            StringUtils.XmlComment("------abc------"));
        Assert.Equal("<test/>\n", StringUtils.XmlNode("test", null, null));
        Assert.Equal("<test>Gr&#xfc;bel</test>\n",
            StringUtils.XmlNode("test", null, StringUtils.XmlText("Gr\u00fcbel")));
        Assert.Equal("Rand&amp;Blue", StringUtils.XmlText("Rand&Blue"));
        Assert.Equal("&lt;&lt;[[[]]]&gt;&gt;", StringUtils.XmlCData("<<[[[]]]>>"));

        // The reference's RSS assembly uses DateTimeFormatFunction.parseDateTime and
        // formatDateTime, which the port does not have; the primitives above are the
        // rest of that check. See the header.
    }

    private static void TestJavaString()
    {
        Assert.Equal("a\"b", StringUtils.JavaDecode("a\"b"));
        Random random = new(1);
        for (int i = 0; i < 1000; i++)
        {
            int len = random.Next(10);
            System.Text.StringBuilder buff = new();
            for (int j = 0; j < len; j++)
            {
                if (random.Next(2) == 0)
                {
                    _ = buff.Append((char)random.Next(0x3000));
                }
                else
                {
                    _ = buff.Append((char)random.Next(255));
                }
            }

            string a = buff.ToString();
            string b = StringUtils.JavaEncode(a);
            string c = StringUtils.JavaDecode(b);
            Assert.Equal(a, c);
        }
    }

    private static void TestSplit()
    {
        Assert.Equal(3,
            StringUtils.ArraySplit("ABC,DEF,G\\,HI", ',', false)!.Length);
        Assert.Equal(", ,\\,",
            StringUtils.ArrayCombine(["", " ", ","], ','));
        Random random = new(1);
        for (int i = 0; i < 100; i++)
        {
            int len = random.Next(10);
            System.Text.StringBuilder buff = new();
            string select = "abcd,";
            for (int j = 0; j < len; j++)
            {
                char c = select[random.Next(select.Length)];
                if (c == 'a')
                {
                    _ = buff.Append("\\\\");
                }
                else if (c == 'b')
                {
                    _ = buff.Append("\\,");
                }
                else
                {
                    _ = buff.Append(c);
                }
            }

            string a = buff.ToString();
            string?[] b = StringUtils.ArraySplit(a, ',', false)!;
            string combined = StringUtils.ArrayCombine(b, ',');
            Assert.Equal(a, combined);
        }
    }

    private static void TestReplaceAll()
    {
        Assert.Equal("def", StringUtils.ReplaceAll("abc def", "abc ", ""));
        Assert.Equal("af", StringUtils.ReplaceAll("abc def", "bc de", ""));
        Assert.Equal("abc def", StringUtils.ReplaceAll("abc def", "bc ", "bc "));
        Assert.Equal("abc ", StringUtils.ReplaceAll("abc def", "def", ""));
        Assert.Equal(" ", StringUtils.ReplaceAll("abc abc", "abc", ""));
        Assert.Equal("xyz xyz", StringUtils.ReplaceAll("abc abc", "abc", "xyz"));
        Assert.Equal("abc def", StringUtils.ReplaceAll("abc def", "xyz", "abc"));
        Assert.Equal("", StringUtils.ReplaceAll("abcabcabc", "abc", ""));
        Assert.Equal("abcabcabc", StringUtils.ReplaceAll("abcabcabc", "aBc", ""));
        Assert.Equal("abcabcabc", StringUtils.ReplaceAll("abcabcabc", "", "abc"));
    }

    private static void TestTrim()
    {
        Assert.Equal("a a", StringUtils.Trim("a a", true, true, null));
        Assert.Equal("  a a  ", StringUtils.Trim("  a a  ", false, false, null));
        Assert.Equal("  a a", StringUtils.Trim("  a a  ", false, true, null));
        Assert.Equal("a a  ", StringUtils.Trim("  a a  ", true, false, null));
        Assert.Equal("a a", StringUtils.Trim("  a a  ", true, true, null));
        Assert.Equal("a a", StringUtils.Trim("  a a  ", true, true, ""));
        Assert.Equal("zzbbzz", StringUtils.Trim("zzbbzz", false, false, "z"));
        Assert.Equal("zzbb", StringUtils.Trim("zzbbzz", false, true, "z"));
        Assert.Equal("bbzz", StringUtils.Trim("zzbbzz", true, false, "z"));
        Assert.Equal("bb", StringUtils.Trim("zzbbzz", true, true, "z"));
    }

    private static void TestTrimSubstring()
    {
        TestTrimSubstringImpl("", "", 0, 0);
        TestTrimSubstringImpl("", "    ", 0, 0);
        TestTrimSubstringImpl("", "    ", 4, 4);
        TestTrimSubstringImpl("select", " select  from", 1, 7);
        TestTrimSubstringImpl("a b", " a b ", 1, 4);
        TestTrimSubstringImpl("a b", " a b ", 1, 5);
        TestTrimSubstringImpl("b", " a b ", 2, 5);
        Assert.ThrowsAny<Exception>(() => StringUtils.TrimSubstring(" with (", 1, 8));
    }

    private static void TestTrimSubstringImpl(string expected, string s, int startIndex,
        int endIndex)
    {
        Assert.Equal(expected, StringUtils.TrimSubstring(s, startIndex, endIndex));
        Assert.Equal(expected, StringUtils.TrimSubstring(
            new System.Text.StringBuilder(endIndex - startIndex), s, startIndex, endIndex).ToString());
    }

    private static void TestTruncateString()
    {
        Assert.Equal("", StringUtils.TruncateString("", 1));
        Assert.Equal("", StringUtils.TruncateString("a", 0));
        Assert.Equal("_\ud83d\ude00", StringUtils.TruncateString("_\ud83d\ude00", 3));
        Assert.Equal("_", StringUtils.TruncateString("_\ud83d\ude00", 2));
        Assert.Equal("_\ud83d", StringUtils.TruncateString("_\ud83d_", 2));
    }
}
