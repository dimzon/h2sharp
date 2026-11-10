/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 113: the utility edges SQL does not reach — URL decode/encode, the char-array
 * clone, the hex conversions with their refusals, Java encode/decode, the XML and
 * quoting helpers, split/combine, indent, replace, and the secure random seed.
 */
using System;
using System.Text;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class UtilEdgeTests
{
    [Fact]
    public void UrlFormsRoundTrip()
    {
        Assert.Equal("AB  x", StringUtils.UrlDecode("%41%42+%20x"));
        Assert.Equal(new string('a', 300), StringUtils.UrlDecode(new string('a', 300)));
        Assert.Equal("abc", StringUtils.UrlDecode("abc"));
        Assert.Equal("AB+%2B", StringUtils.UrlEncode("AB +"));
    }

    [Fact]
    public void CharArrayCloneAnswersNullEmptyAndCopy()
    {
        Assert.Null(StringUtils.CloneCharArray(null!));
        char[] empty = [];
        Assert.Same(empty, StringUtils.CloneCharArray(empty));
        char[] source = ['a', 'b'];
        char[]? copy = StringUtils.CloneCharArray(source);
        Assert.NotNull(copy);
        Assert.Equal(source, copy!);
        Assert.NotSame(source, copy);
    }

    [Fact]
    public void HexConversionsAndRefusals()
    {
        Assert.Equal([0xCA, 0xFE], StringUtils.ConvertHexToBytes("cafe"));
        Assert.Equal([0x0A], StringUtils.ConvertHexToBytes("0a"));
        Assert.Throws<H2SharpException>(() => StringUtils.ConvertHexToBytes("c"));
        Assert.Throws<H2SharpException>(() => StringUtils.ConvertHexToBytes("cg"));
        Assert.Throws<H2SharpException>(() => StringUtils.ConvertHexToBytes((ReadOnlySpan<char>)"c"));
        Assert.Throws<H2SharpException>(() => StringUtils.ConvertHexToBytes((ReadOnlySpan<char>)"cg"));
        Assert.Throws<H2SharpException>(() => StringUtils.ConvertHexToBytes("0 "));
    }

    [Fact]
    public void JavaAndSqlQuotingHelpers()
    {
        Assert.Equal("a\\u03a9b", StringUtils.JavaEncode("aΩb"));
        Assert.Equal("aΩb", StringUtils.JavaDecode("a\\u03a9b"));
        Assert.Equal("'O''Brien'", StringUtils.QuoteStringSql("O'Brien"));
        Assert.Equal("\"O\\\"B\"", StringUtils.QuoteJavaString("O\"B"));
        Assert.Equal("""new String[]{"a", "b"}""", StringUtils.QuoteJavaStringArray(["a", "b"]));
        Assert.Equal("""new int[]{1, 2}""", StringUtils.QuoteJavaIntArray([1, 2]));
        Assert.Equal("a_b", StringUtils.ReplaceAll("a-b", "-", "_"));
        Assert.Equal("x[*]y", StringUtils.AddAsterisk("xy", 1));
        Assert.Equal("    a", StringUtils.Indent(new StringBuilder(), "a", 4, false).ToString());
        Assert.Equal("<!--  c  -->" + "\n", StringUtils.XmlComment(" c "));
        Assert.Equal("<ab=\"c\">d</a>" + "\n", StringUtils.XmlNode("a", "b=\"c\"", "d"));
        Assert.Equal("<?xml version=\"1.0\"?>\n", StringUtils.XmlStartDoc());
        Assert.Equal("a&#xa;b", StringUtils.XmlText("a\nb", escapeNewline: true));
        Assert.Equal(
            ["a", "b"],
            StringUtils.ArraySplit("a,b", ',', trim: true));
        Assert.Equal("a,b", StringUtils.ArrayCombine(["a", "b"], ','));
        Assert.Equal("AB", StringUtils.ToUpperEnglish("ab"));
        Assert.Equal("ab", StringUtils.ToLowerEnglish("AB"));
        Assert.Equal("AB", StringUtils.ToUpperEnglish("ab".AsSpan()).ToString());
        Assert.Equal("ab", StringUtils.ToLowerEnglish("AB".AsSpan()).ToString());
    }

    [Fact]
    public void TheAlternativeSeedIsGenerated()
    {
        byte[] first = MathUtils.GenerateAlternativeSeed();
        byte[] second = MathUtils.GenerateAlternativeSeed();
        Assert.NotEmpty(first);
        Assert.NotEqual(first, second);
    }
}
