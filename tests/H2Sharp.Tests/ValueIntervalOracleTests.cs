/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for INTERVAL.
 *
 * An interval is a leading field, the rest of the fields, and a qualifier that says what those two
 * numbers are, and the qualifier decides everything: which separators may be read, what a literal of
 * it looks like, and what the bounds are. So the corpus is walked by qualifier rather than by value,
 * and there are thirteen of them: six single fields and seven with a "TO" in their name.
 *
 * The text is the interesting half. An interval is read from a statement and written back into one,
 * and the shapes are not the ones an integer would suggest: INTERVAL '1 02' DAY TO HOUR keeps the
 * two digits of the hour, INTERVAL '1-2' YEAR TO MONTH uses a dash where a time uses a colon, and a
 * sign written outside the quotes and a sign on the leading field inside have to be brought into
 * agreement. A negative zero is written and read here too, which is not a number and is why the sign
 * is a separate field in the value.
 *
 * The arithmetic is compared through the absolute value, which is months for a year-month interval
 * and nanoseconds for a day-time one, so the conversion and the arithmetic are one thing and are
 * compared together.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]

public sealed class ValueIntervalOracleTests
{
    /// <summary>
    /// The number of qualifiers the reference has: six single fields and seven with a TO.
    /// </summary>
    private const int QualifierCount = 13;

    /// <summary>
    /// The texts, which is where the reading and the writing meet.
    /// </summary>
    private static readonly string[] Texts =
    [
        "INTERVAL '1' YEAR", "INTERVAL '-1' YEAR", "INTERVAL '0' YEAR", "INTERVAL '-0' YEAR",
        "INTERVAL '999' YEAR", "INTERVAL '1000000000000' YEAR",
        "INTERVAL '1-2' YEAR TO MONTH", "INTERVAL '-1-2' YEAR TO MONTH", "INTERVAL '0-11' YEAR TO MONTH",
        "INTERVAL '1' MONTH", "INTERVAL '-1' MONTH",
        "INTERVAL '1' DAY", "INTERVAL '-1' DAY",
        "INTERVAL '1 02' DAY TO HOUR", "INTERVAL '1 23' DAY TO HOUR", "INTERVAL '1 0' DAY TO HOUR",
        "INTERVAL '1 02:03' DAY TO MINUTE", "INTERVAL '1 23:59' DAY TO MINUTE",
        "INTERVAL '1 02:03:04' DAY TO SECOND", "INTERVAL '1 02:03:04.5' DAY TO SECOND",
        "INTERVAL '1 00:00:00.000000001' DAY TO SECOND", "INTERVAL '1 00' DAY TO SECOND",
        "INTERVAL '1' HOUR", "INTERVAL '02:03' HOUR TO MINUTE", "INTERVAL '02:03:04' HOUR TO SECOND",
        "INTERVAL '02:03:04.5' HOUR TO SECOND", "INTERVAL '1' MINUTE",
        "INTERVAL '02:03' MINUTE TO SECOND", "INTERVAL '02:03.5' MINUTE TO SECOND",
        "INTERVAL '1.5' SECOND", "INTERVAL '-1.5' SECOND", "INTERVAL '1' SECOND",
        "INTERVAL '0.000000001' SECOND", "INTERVAL '59.999999999' SECOND",

        // The shapes that must be refused, and the shapes that look refused and are not.
        "INTERVAL '1 24' DAY TO HOUR", "INTERVAL '1 12' MONTH", "INTERVAL '1 60' MINUTE TO SECOND",
        "INTERVAL '1.5' YEAR", "INTERVAL '1-2-3' YEAR TO MONTH", "INTERVAL '' YEAR",
        "INTERVAL 'x' YEAR", "INTERVAL '1' FORTNIGHT", "INTERVAL 1 YEAR", "INTERVAL '1'",
    ];

    [JavaOracleFact]
    public void IntervalsMatchTheReference()
    {
        List<string> java = OracleRunner.Run(
                Texts,
                Program,
                extraJavaArgs: ["-Duser.language=en", "-Dfile.encoding=UTF-8"])
            .Select(line => Decode(line))
            .ToList();

        List<string> csharp = Texts.Select(Describe).ToList();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"{Texts[i]}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// The arithmetic of an interval, which goes through its absolute value and comes back.
    /// </summary>
    [JavaOracleFact]
    public void TheArithmeticOfAnIntervalMatchesTheReference()
    {
        // Every qualifier, two values each, asked of both sides: the absolute value, the sum, the
        // difference, the negation and the comparison.
        List<int> numbers =
            [.. Enumerable.Range(0, QualifierCount).SelectMany(q => new[] { q * 1000, (q * 1000) + 1 })];

        List<string> java = OracleRunner.Run(
                [],
                Arithmetic,
                numbers: numbers,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(line => Decode(line))
            .ToList();

        List<string> csharp = numbers.Select(DescribeArithmetic).ToList();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(
                java[i] == csharp[i],
                $"{numbers[i]}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// Every qualifier is one value type here, and a value read back from its own printed text is
    /// the same value.
    /// </summary>
    /// <remarks>
    /// The count is asserted because a qualifier that is not in the corpus is a qualifier whose text
    /// and arithmetic have never been asked about, and the round trip is of the *printed* text rather
    /// than of the text that went in: the printer writes two digits where one was given, so reading
    /// back the input would ask for an identity the standard does not promise.
    /// </remarks>
    [Fact]
    public void EveryQualifierHasItsOwnValueType()
    {
        HashSet<int> types = [];
        foreach (string text in new[]
        {
            "INTERVAL '1' YEAR", "INTERVAL '1' MONTH", "INTERVAL '1' DAY", "INTERVAL '1' HOUR",
            "INTERVAL '1' MINUTE", "INTERVAL '1' SECOND", "INTERVAL '1-1' YEAR TO MONTH",
            "INTERVAL '1 1' DAY TO HOUR", "INTERVAL '1 1:1' DAY TO MINUTE",
            "INTERVAL '1 1:1:1' DAY TO SECOND", "INTERVAL '1:1' HOUR TO MINUTE",
            "INTERVAL '1:1:1' HOUR TO SECOND", "INTERVAL '1:1' MINUTE TO SECOND",
        })
        {
            ValueInterval v = IntervalUtils.ParseFormattedInterval(IntervalQualifier.Year, text);
            types.Add(v.GetValueType());

            string printed = v.GetString()!;
            ValueInterval again = IntervalUtils.ParseFormattedInterval(IntervalQualifier.Year, printed);
            Assert.Equal(v, again);
            Assert.Equal(printed, again.GetString());
            Assert.Equal(v, v.Negate().Negate());
        }

        Assert.Equal(QualifierCount, types.Count);
        Assert.Equal(QualifierCount, types.Distinct().Count());
        Assert.Equal(Value.IntervalYear, types.Min());
        Assert.Equal(Value.IntervalMinuteToSecond, types.Max());
        for (int type = Value.IntervalYear; type <= Value.IntervalMinuteToSecond; type++)
        {
            Assert.Contains(type, types);
        }
    }

    /// <summary>
    /// The bounds are the qualifier's, and a leading field that does not fit is refused with the
    /// code the reference gives it.
    /// </summary>
    [Fact]
    public void TheBoundsAreTheQualifiersOwn()
    {
        // Twelve months in a YEAR TO MONTH, twenty-three hours in a DAY TO HOUR, and a leading
        // field of nothing at all is not negative.
        Assert.Equal(0, ValueInterval.From(IntervalQualifier.Year, true, 0, 0).GetSignum());
        Assert.False(ValueInterval.From(IntervalQualifier.YearToMonth, true, 0, 0).IsNegative());

        Assert.Throws<H2SharpException>(
            () => ValueInterval.From(IntervalQualifier.YearToMonth, false, 1, 12));
        Assert.Throws<H2SharpException>(
            () => ValueInterval.From(IntervalQualifier.DayToHour, false, 1, 24));
        Assert.Throws<H2SharpException>(
            () => ValueInterval.From(IntervalQualifier.MinuteToSecond, false, 1, 60 * 1_000_000_000L));

        // And these are inside them.
        Assert.Equal(11, ValueInterval.From(IntervalQualifier.YearToMonth, false, 1, 11).GetRemaining());
        Assert.Equal(23, ValueInterval.From(IntervalQualifier.DayToHour, false, 1, 23).GetRemaining());
    }

    private static string Describe(string text)
    {
        try
        {
            ValueInterval v = IntervalUtils.ParseFormattedInterval(IntervalQualifier.Year, text);
            StringBuilder b = new();
            _ = b.Append(v.GetValueType()).Append('|')
                .Append(v.GetLeading()).Append('|')
                .Append(v.GetRemaining()).Append('|')
                .Append(v.IsNegative() ? "true" : "false").Append('|')
                .Append(v.GetString()).Append('|')
                .Append(v.GetSql(new StringBuilder(), 0)).Append('|')
                .Append(v.GetTypeInfo().GetSql(0)).Append('|')
                .Append(v.GetMemory()).Append('|')
                .Append(v.GetSignum()).Append('|')
                .Append(v.GetLong()).Append('|')
                .Append(v.GetBigInteger()).Append('|')
                .Append(v.GetBigDecimal()).Append('|')
                .Append(((ValueInterval)v.Negate()).GetString());
            return b.ToString();
        }
        catch (H2SharpException e)
        {
            return "ERR " + e.H2ErrorCode.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            return "REFUSED";
        }
    }

    private static string DescribeArithmetic(int packed)
    {
        IntervalQualifier qualifier = (IntervalQualifier)(packed / 1000);
        int which = packed % 1000;

        try
        {
            return ArithmeticOf(qualifier, Sample(qualifier, which, 1), Sample(qualifier, which, 2));
        }
        catch (H2SharpException e)
        {
            // An interval whose absolute value no longer fits its leading field is refused, and the
            // refusal is compared: the reference refuses it too, with the same code.
            return "ERR " + e.H2ErrorCode.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static string ArithmeticOf(IntervalQualifier qualifier, string first, string second)
    {
        ValueInterval a = IntervalUtils.ParseFormattedInterval(qualifier, first);
        ValueInterval b = IntervalUtils.ParseFormattedInterval(qualifier, second);

        StringBuilder r = new();
        _ = r.Append(IntervalUtils.IntervalToAbsolute(a)).Append('|');
        _ = r.Append(a.Add(b).GetString()).Append('|');
        _ = r.Append(a.Subtract(b).GetString()).Append('|');
        _ = r.Append(((ValueInterval)a.Negate()).GetString()).Append('|');
        _ = r.Append(a.CompareTypeSafe(b, CompareMode.GetInstance(null, 0), null));
        return r.ToString();
    }

    /// <summary>
    /// A value of the given qualifier, written the way that qualifier is written.
    /// </summary>
    private static string Sample(IntervalQualifier qualifier, int which, int amount) => qualifier switch
    {
        IntervalQualifier.Year => $"INTERVAL '{amount * 2}' YEAR",
        IntervalQualifier.Month => $"INTERVAL '{amount * 3}' MONTH",
        IntervalQualifier.Day => $"INTERVAL '{amount * 4}' DAY",
        IntervalQualifier.Hour => $"INTERVAL '{amount * 5}' HOUR",
        IntervalQualifier.Minute => $"INTERVAL '{amount * 6}' MINUTE",
        IntervalQualifier.Second => $"INTERVAL '{amount}.{amount}000000' SECOND",
        IntervalQualifier.YearToMonth => $"INTERVAL '{amount}-{amount}' YEAR TO MONTH",
        IntervalQualifier.DayToHour => $"INTERVAL '{amount} {amount:00}' DAY TO HOUR",
        IntervalQualifier.DayToMinute => $"INTERVAL '{amount} {amount:00}:{amount:00}' DAY TO MINUTE",
        IntervalQualifier.DayToSecond =>
            $"INTERVAL '{amount} {amount:00}:{amount:00}:{amount:00}' DAY TO SECOND",
        IntervalQualifier.HourToMinute => $"INTERVAL '{amount:00}:{amount:00}' HOUR TO MINUTE",
        IntervalQualifier.HourToSecond => $"INTERVAL '{amount:00}:{amount:00}:{amount:00}' HOUR TO SECOND",
        IntervalQualifier.MinuteToSecond => $"INTERVAL '{amount:00}:{amount:00}' MINUTE TO SECOND",
        _ => throw new ArgumentOutOfRangeException(nameof(qualifier)),
    };

    private const string Program = """
              for (int i = 0; i < inputs.length; i++) {
                String s = inputs[i];
                try {
                  org.h2.value.ValueInterval v = org.h2.util.IntervalUtils.parseFormattedInterval(
                      org.h2.api.IntervalQualifier.YEAR, s);
                  StringBuilder b = new StringBuilder();
                  b.append(v.getValueType()).append('|');
                  b.append(v.getLeading()).append('|');
                  b.append(v.getRemaining()).append('|');
                  b.append(v.isNegative()).append('|');
                  b.append(v.getString()).append('|');
                  b.append(v.getSQL(new StringBuilder(), 0)).append('|');
                  b.append(v.getType().getSQL(0)).append('|');
                  b.append(v.getMemory()).append('|');
                  b.append(v.getSignum()).append('|');
                  b.append(v.getLong()).append('|');
                  b.append(v.getBigInteger()).append('|');
                  b.append(v.getBigDecimal()).append('|');
                  b.append(v.negate().getString());
                  System.out.println(hex(b.toString()));
                } catch (org.h2.message.DbException e) {
                  System.out.println(hex("ERR " + e.getErrorCode()));
                } catch (RuntimeException e) {
                  System.out.println("REFUSED");
                }
              }
        """;

    private const string Arithmetic = """
              org.h2.value.CompareMode mode = org.h2.value.CompareMode.getInstance(null, 0);
              String[] samples = {
                  "INTERVAL '2' YEAR", "INTERVAL '4' YEAR",
                  "INTERVAL '3' MONTH", "INTERVAL '6' MONTH",
                  "INTERVAL '4' DAY", "INTERVAL '8' DAY",
                  "INTERVAL '5' HOUR", "INTERVAL '10' HOUR",
                  "INTERVAL '6' MINUTE", "INTERVAL '12' MINUTE",
                  "INTERVAL '1.1000000' SECOND", "INTERVAL '2.2000000' SECOND",
                  "INTERVAL '1-1' YEAR TO MONTH", "INTERVAL '2-2' YEAR TO MONTH",
                  "INTERVAL '1 01' DAY TO HOUR", "INTERVAL '2 02' DAY TO HOUR",
                  "INTERVAL '1 01:01' DAY TO MINUTE", "INTERVAL '2 02:02' DAY TO MINUTE",
                  "INTERVAL '1 01:01:01' DAY TO SECOND", "INTERVAL '2 02:02:02' DAY TO SECOND",
                  "INTERVAL '01:01' HOUR TO MINUTE", "INTERVAL '02:02' HOUR TO MINUTE",
                  "INTERVAL '01:01:01' HOUR TO SECOND", "INTERVAL '02:02:02' HOUR TO SECOND",
                  "INTERVAL '01:01' MINUTE TO SECOND", "INTERVAL '02:02' MINUTE TO SECOND"};
              for (int i = 0; i < nums.length; i++) {
                try {
                  org.h2.api.IntervalQualifier q = org.h2.api.IntervalQualifier.valueOf(nums[i] / 1000);
                  org.h2.value.ValueInterval a = org.h2.util.IntervalUtils.parseFormattedInterval(
                      q, samples[nums[i] / 1000 * 2]);
                  org.h2.value.ValueInterval b = org.h2.util.IntervalUtils.parseFormattedInterval(
                      q, samples[nums[i] / 1000 * 2 + 1]);
                  StringBuilder r = new StringBuilder();
                  r.append(org.h2.util.IntervalUtils.intervalToAbsolute(a)).append('|');
                  r.append(a.add(b).getString()).append('|');
                  r.append(a.subtract(b).getString()).append('|');
                  r.append(a.negate().getString()).append('|');
                  r.append(a.compareTo(b, null, mode));
                  System.out.println(hex(r.toString()));
                } catch (org.h2.message.DbException e) {
                  System.out.println(hex("ERR " + e.getErrorCode()));
                }
              }
        """;

    private static string Decode(string hex)
    {
        string only = hex.TrimStart('=');
        if (only.Length == 0)
        {
            return string.Empty;
        }

        if (!only.All(static c => char.IsAsciiHexDigit(c)))
        {
            return only;
        }

        StringBuilder b = new(only.Length / 4);
        for (int i = 0; i + 3 < only.Length; i += 4)
        {
            _ = b.Append((char)Convert.ToInt32(only.Substring(i, 4), 16));
        }

        return b.ToString();
    }
}
