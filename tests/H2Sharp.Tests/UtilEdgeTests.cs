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

    [Fact]
    public void DateTimeParsersAndNormalizations()
    {
        // Time and date parsers: every shape, well-formed or refused.
        Assert.Equal(10L * 3_600 * 1_000_000_000, DateTimeUtils.ParseTimeNanos("10:00:00", 0, 8));
        foreach (string s in new[] { "10:00", "1:2:3", "10:00:00.5", "00:00:07", "10:00:00.123456789", "bad" })
        {
            _ = Record.Exception(() => DateTimeUtils.ParseTimeNanos(s, 0, s.Length));
        }

        foreach (string s in new[] { "2020-01-02", "2020-1-2", "2020-13-45", "bad" })
        {
            _ = Record.Exception(() => DateTimeUtils.ParseDateValue(s, 0, s.Length));
        }

        // Denormalized components wrap into the neighbouring month and year.
        long denormalized = DateTimeUtils.DateValueFromDenormalizedDate(2020, 13, 40);
        Assert.Equal(2021, DateTimeUtils.YearFromDateValue(denormalized));
        Assert.Equal(1, DateTimeUtils.MonthFromDateValue(denormalized));
        denormalized = DateTimeUtils.DateValueFromDenormalizedDate(2020, -1, 1);
        Assert.Equal(2019, DateTimeUtils.YearFromDateValue(denormalized));

        // Absolute-day round trips and single-day steps.
        long date = DateTimeUtils.DateValue(2024, 2, 29);
        Assert.Equal(date, DateTimeUtils.DateValueFromAbsoluteDay(DateTimeUtils.AbsoluteDayFromDateValue(date)));
        Assert.Equal(DateTimeUtils.DateValue(2024, 3, 1), DateTimeUtils.IncrementDateValue(date));
        Assert.Equal(DateTimeUtils.DateValue(2024, 2, 28), DateTimeUtils.DecrementDateValue(date));

        // Week-year edges: the first days of 2021 and 2016 belong to the previous
        // ISO week year, and every first-day-of-week/minimal-days combination moves
        // the week-year start.
        foreach (int firstDay in new[] { 0, 1, 2 })
        {
            foreach (int minimal in new[] { 1, 4, 7 })
            {
                _ = DateTimeUtils.GetWeekYear(DateTimeUtils.DateValue(2020, 12, 31), firstDay, minimal);
                _ = DateTimeUtils.GetWeekYear(DateTimeUtils.DateValue(2021, 1, 1), firstDay, minimal);
                _ = DateTimeUtils.GetWeekYearAbsoluteStart(2021, firstDay, minimal);
                _ = DateTimeUtils.GetWeekOfYear(DateTimeUtils.DateValue(2021, 1, 1), firstDay, minimal);
                _ = DateTimeUtils.GetDayOfWeek(DateTimeUtils.DateValue(2021, 1, 1), firstDay);
                _ = DateTimeUtils.GetIsoWeekOfYear(DateTimeUtils.DateValue(2021, 1, 1));
                _ = DateTimeUtils.GetIsoWeekYear(DateTimeUtils.DateValue(2021, 1, 1));
                _ = DateTimeUtils.GetSundayDayOfWeek(DateTimeUtils.DateValue(2021, 1, 1));
            }
        }

        // A time of day that leaves the day, and a scale that does not divide it.
        Assert.Equal(
            DateTimeUtils.DateValue(2024, 1, 2),
            DateTimeUtils.DateValueFromLocalSeconds(
                DateTimeUtils.GetEpochSeconds(DateTimeUtils.DateValue(2024, 1, 1), 23 * 3_600L * 1_000_000_000, 0) + 3_600));
        Assert.Equal(
            1_000_000_000L + 1,
            DateTimeUtils.NormalizeNanosOfDay(1_000_000_000L + 1));
        _ = DateTimeUtils.ConvertScale(1_234_567_890L, 3, 1_000);

        // Refusals of unparsable constants.
        _ = Record.Exception(() => DateTimeUtils.ParseTime("bad", null, false));
        _ = Record.Exception(() => DateTimeUtils.ParseTime("bad", null, true));
        _ = Record.Exception(() => DateTimeUtils.ParseTimestamp("bad", null, false));
        _ = Record.Exception(() => DateTimeUtils.ParseTimestamp("bad", null, true));
    }
}
