/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the arithmetic of dates and times.
 *
 * A date is one shifted number here, and the arithmetic on it is the part of a database that is
 * easiest to get subtly wrong: a day of the week that is right for 2026 and wrong for 1900, a leap
 * year rule that treats 1900 as a leap year, a week year that disagrees with the year in the first
 * days of January. So the corpus is not a list of interesting dates but a walk: every year from 1
 * to 4000 with its first, last, and a day in the middle, every month of a leap year and a common
 * one, and the days around the epoch, where the remainder changes sign.
 *
 * Everything is asked of both sides at once for one date value: the year, month and day, the day of
 * the week counted four ways, the week and week year four ways, the day of the year, the next and
 * the previous day, and the absolute day both ways round.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class DateTimeUtilsOracleTests
{
    /// <summary>
    /// The date values the corpus walks: a whole calendar year by year, and the places where the
    /// arithmetic changes shape.
    /// </summary>
    private static List<long> DateValues()
    {
        List<long> values = [];

        // Every year from 1 to 4000: the first of January, the last day of February and a day in
        // the middle. Four thousand years is eight thousand dates per kind of question, and it is
        // the range where a leap year rule, a day-of-week rule and a week-year rule all have to be
        // right for four thousand consecutive years rather than for one remembered date.
        for (int year = 1; year <= 4000; year++)
        {
            values.Add(DateTimeUtils.DateValue(year, 1, 1));
            values.Add(DateTimeUtils.DateValue(year, 2, 28));
            values.Add(DateTimeUtils.DateValue(year, 7, 15));
            values.Add(DateTimeUtils.DateValue(year, 12, 31));
        }

        // Every month of a leap year and of a common one, the first and the last day of each.
        foreach (int year in new[] { 1900, 2000, 2004, 2023, 2024 })
        {
            for (int month = 1; month <= 12; month++)
            {
                values.Add(DateTimeUtils.DateValue(year, month, 1));
                values.Add(DateTimeUtils.DateValue(year, month, DateTimeUtils.GetDaysInMonth(year, month)));
            }
        }

        // The years around the epoch, where a remainder changes sign and a naive division gives
        // the wrong day.
        for (int day = -5; day <= 5; day++)
        {
            values.Add(DateTimeUtils.DateValueFromAbsoluteDay(day));
            values.Add(DateTimeUtils.DateValueFromAbsoluteDay(day * 365));
        }

        // The ends of what a year can be written as. The two ends of what may be *stored* are not
        // in this corpus: they are a million years away and do not fit in an int, and the file the
        // reference reads them from is an int[]. They are constants, so they are asserted on their
        // own below rather than smuggled through a transport that cannot carry them.
        values.Add(DateTimeUtils.EpochDateValue);
        values.Add(DateTimeUtils.DateValue(-999, 12, 31));
        values.Add(DateTimeUtils.DateValue(999, 1, 1));
        values.Add(DateTimeUtils.DateValue(1000, 1, 1));
        values.Add(DateTimeUtils.DateValue(100000, 6, 15));

        return values;
    }

    [JavaOracleFact]
    public void TheDateArithmeticMatchesTheReference()
    {
        List<long> values = DateValues();

        // The date values go over as numbers, in a file of their own, because a number is not text
        // and the text transport would turn a date into a string to be parsed back.
        // The numbers go over in a file of their own, as ints; a date value is a long and the
        // reference reads that file as an int[], so the corpus stays inside what both sides can
        // read without inventing a second transport for one test.
        List<long> wide = DateValues();
        List<int> narrowed = wide.Select(static v => (int)v).ToList();

        List<string> java = OracleRunner.Run(
                [],
                Program,
                numbers: [.. narrowed],
                extraJavaArgs: ["-Duser.language=en"])
            .Select(line => line.Trim())
            .ToList();

        List<string> csharp = values.Select(Describe).ToList();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i} ({values[i]}):\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// The times, asked of both sides: written, cut to each precision, and read back from every
    /// shape the parser accepts.
    /// </summary>
    [JavaOracleFact]
    public void TheTimeArithmeticMatchesTheReference()
    {
        List<string> texts =
        [
            "00:00", "00:00:00", "0:0", "00:00:00.000000001", "00:00:00.1", "00:00:00.12",
            "00:00:00.123", "00:00:00.1234", "00:00:00.12345", "00:00:00.123456",
            "00:00:00.1234567", "00:00:00.12345678", "00:00:00.123456789",
            "23:59:59.999999999", "12:34:56.789", "1:2:3", "0000", "000000", "000000.123",
            "00.00.00", "00.00", "24:00", "23:60", "23:59:60", "-01:00", "12", "123", "1234",
            "12:34:56.0", "12:34:56.00", "12:34:56.000", "12:34:56.000000000",
            ":", "::", ".", "..", "1.2.3", "12:34:56.", "12:34:", "1234567",
        ];

        List<string> java = OracleRunner.Run(
                texts,
                """
                for (int i = 0; i < inputs.length; i++) {
                  String s = inputs[i];
                  StringBuilder b = new StringBuilder();
                  long nanos;
                  try {
                    nanos = org.h2.util.DateTimeUtils.parseTimeNanos(s, 0, s.length());
                  } catch (RuntimeException e) {
                    System.out.println("THROWS");
                    continue;
                  }
                  b.append(nanos).append('|');
                  org.h2.util.DateTimeUtils.appendTime(new StringBuilder(), nanos);
                  b.append(org.h2.util.DateTimeUtils.appendTime(new StringBuilder(), nanos));
                  for (int scale = 0; scale <= 9; scale++) {
                    b.append('|').append(org.h2.util.DateTimeUtils.convertScale(nanos, scale, Long.MAX_VALUE));
                  }
                  System.out.println(b.toString());
                }
                """,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(line => line.TrimStart('='))
            .ToList();

        List<string> csharp = texts.Select(DescribeTime).ToList();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"{texts[i]}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// The dates, read back from text, because a date is read from a statement and a parser that
    /// reads one date and not another is a parser with a bug in it rather than a format difference.
    /// </summary>
    [JavaOracleFact]
    public void TheDateParserMatchesTheReference()
    {
        List<string> texts =
        [
            "2020-01-01", "2020-12-31", "1970-01-01", "0001-01-01", "-0001-12-31", "+2020-06-15",
            "20200101", "19000101", "99999999", "100000000", "2020-1-1", "2020-01-32",
            "2020-02-30", "2019-02-29", "2020-02-29", "2020-00-01", "2020-13-01", "2020-01-00",
            "2020-01", "2020", "20-01-01", "-2020-01-01", "2020-01-01-", "2020--01",
            "00010101", "100-01-01", "2020-01-1x", "x2020-01-01", "", " ", "202001", "2020-1",
        ];

        List<string> java = OracleRunner.Run(
                texts,
                """
                for (int i = 0; i < inputs.length; i++) {
                  String s = inputs[i];
                  try {
                    long v = org.h2.util.DateTimeUtils.parseDateValue(s, 0, s.length());
                    StringBuilder b = new StringBuilder();
                    b.append(v).append('|');
                    b.append(org.h2.util.DateTimeUtils.yearFromDateValue(v)).append('|');
                    b.append(org.h2.util.DateTimeUtils.monthFromDateValue(v)).append('|');
                    b.append(org.h2.util.DateTimeUtils.dayFromDateValue(v)).append('|');
                    org.h2.util.DateTimeUtils.appendDate(new StringBuilder(), v);
                    b.append(org.h2.util.DateTimeUtils.appendDate(new StringBuilder(), v));
                    System.out.println(b.toString());
                  } catch (RuntimeException e) {
                    System.out.println("THROWS");
                  }
                }
                """,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(line => line.TrimStart('='))
            .ToList();

        List<string> csharp = texts.Select(DescribeDate).ToList();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"{texts[i]}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// The two ends of what a date may be, which the arithmetic corpus cannot carry.
    /// </summary>
    [Fact]
    public void TheEndsOfWhatMayBeStored()
    {
        Assert.Equal(-512000000000L + (1 << DateTimeUtils.ShiftMonth) + 1, DateTimeUtils.MinDateValue);
        Assert.Equal(512000000000L + (12 << DateTimeUtils.ShiftMonth) + 31, DateTimeUtils.MaxDateValue);
        Assert.Equal(
            (1970L << DateTimeUtils.ShiftYear) + (1 << DateTimeUtils.ShiftMonth) + 1,
            DateTimeUtils.EpochDateValue);

        // Both ends are real dates, and the day of the week of each is what the arithmetic says.
        // Each end decodes back to the month and day it was built from, and to a year that is the
        // year shifted out of it, which is the check that the two halves of the layout agree.
        Assert.Equal(DateTimeUtils.MinDateValue >> DateTimeUtils.ShiftYear, DateTimeUtils.YearFromDateValue(DateTimeUtils.MinDateValue));
        Assert.Equal(1, DateTimeUtils.MonthFromDateValue(DateTimeUtils.MinDateValue));
        Assert.Equal(1, DateTimeUtils.DayFromDateValue(DateTimeUtils.MinDateValue));
        Assert.Equal(DateTimeUtils.MaxDateValue >> DateTimeUtils.ShiftYear, DateTimeUtils.YearFromDateValue(DateTimeUtils.MaxDateValue));
        Assert.Equal(12, DateTimeUtils.MonthFromDateValue(DateTimeUtils.MaxDateValue));
        Assert.Equal(31, DateTimeUtils.DayFromDateValue(DateTimeUtils.MaxDateValue));
    }

    private static string Describe(long dateValue) => string.Join(
        "|",
        DateTimeUtils.YearFromDateValue(dateValue),
        DateTimeUtils.MonthFromDateValue(dateValue),
        DateTimeUtils.DayFromDateValue(dateValue),
        DateTimeUtils.GetDayOfWeek(dateValue, 1),
        DateTimeUtils.GetDayOfWeek(dateValue, 0),
        DateTimeUtils.GetSundayDayOfWeek(dateValue),
        DateTimeUtils.GetIsoDayOfWeek(dateValue),
        DateTimeUtils.GetWeekOfYear(dateValue, 1, 4),
        DateTimeUtils.GetIsoWeekOfYear(dateValue),
        DateTimeUtils.GetWeekOfYear(dateValue, 0, 1),
        DateTimeUtils.GetWeekYear(dateValue, 1, 4),
        DateTimeUtils.GetIsoWeekYear(dateValue),
        DateTimeUtils.GetWeekYear(dateValue, 0, 1),
        DateTimeUtils.GetWeekYearAbsoluteStart(DateTimeUtils.YearFromDateValue(dateValue), 1, 4),
        DateTimeUtils.GetDayOfYear(dateValue),
        DateTimeUtils.AbsoluteDayFromDateValue(dateValue),
        DateTimeUtils.IncrementDateValue(dateValue),
        DateTimeUtils.DecrementDateValue(dateValue),
        DateTimeUtils.DateValueFromAbsoluteDay(DateTimeUtils.AbsoluteDayFromDateValue(dateValue)),
        DateTimeUtils.IsValidDate(
            DateTimeUtils.YearFromDateValue(dateValue),
            DateTimeUtils.MonthFromDateValue(dateValue),
            DateTimeUtils.DayFromDateValue(dateValue)) ? "true" : "false");

    private static string DescribeTime(string text)
    {
        long nanos;
        try
        {
            nanos = DateTimeUtils.ParseTimeNanos(text, 0, text.Length);
        }
        catch (Exception e) when (e is ArgumentException or H2SharpException)
        {
            return "THROWS";
        }
        catch (Exception)
        {
            return "THROWS";
        }

        StringBuilder b = new();
        _ = b.Append(nanos.ToString(CultureInfo.InvariantCulture)).Append('|');
        _ = DateTimeUtils.AppendTime(b, nanos);
        for (int scale = 0; scale <= 9; scale++)
        {
            _ = b.Append('|').Append(DateTimeUtils.ConvertScale(nanos, scale, long.MaxValue).ToString(CultureInfo.InvariantCulture));
        }

        return b.ToString();
    }

    private static string DescribeDate(string text)
    {
        long dateValue;
        try
        {
            dateValue = DateTimeUtils.ParseDateValue(text, 0, text.Length);
        }
        catch (Exception)
        {
            return "THROWS";
        }

        StringBuilder b = new();
        _ = b.Append(dateValue.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(DateTimeUtils.YearFromDateValue(dateValue).ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(DateTimeUtils.MonthFromDateValue(dateValue).ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(DateTimeUtils.DayFromDateValue(dateValue).ToString(CultureInfo.InvariantCulture)).Append('|');
        _ = DateTimeUtils.AppendDate(b, dateValue);
        return b.ToString();
    }

    /// <remarks>
    /// One Java program that asks the same twenty questions of every date, because a program that
    /// answers them one at a time is a program that is launched twenty thousand times.
    /// </remarks>
    private const string Program = """
              StringBuilder b = new StringBuilder();
              for (int i = 0; i < nums.length; i++) {
                long v = nums[i];
                b.setLength(0);
                b.append(org.h2.util.DateTimeUtils.yearFromDateValue(v)).append('|');
                b.append(org.h2.util.DateTimeUtils.monthFromDateValue(v)).append('|');
                b.append(org.h2.util.DateTimeUtils.dayFromDateValue(v)).append('|');
                b.append(org.h2.util.DateTimeUtils.getDayOfWeek(v, 1)).append('|');
                b.append(org.h2.util.DateTimeUtils.getDayOfWeek(v, 0)).append('|');
                b.append(org.h2.util.DateTimeUtils.getSundayDayOfWeek(v)).append('|');
                b.append(org.h2.util.DateTimeUtils.getIsoDayOfWeek(v)).append('|');
                b.append(org.h2.util.DateTimeUtils.getWeekOfYear(v, 1, 4)).append('|');
                b.append(org.h2.util.DateTimeUtils.getIsoWeekOfYear(v)).append('|');
                b.append(org.h2.util.DateTimeUtils.getWeekOfYear(v, 0, 1)).append('|');
                b.append(org.h2.util.DateTimeUtils.getWeekYear(v, 1, 4)).append('|');
                b.append(org.h2.util.DateTimeUtils.getIsoWeekYear(v)).append('|');
                b.append(org.h2.util.DateTimeUtils.getWeekYear(v, 0, 1)).append('|');
                b.append(org.h2.util.DateTimeUtils.getWeekYearAbsoluteStart(
                    org.h2.util.DateTimeUtils.yearFromDateValue(v), 1, 4)).append('|');
                b.append(org.h2.util.DateTimeUtils.getDayOfYear(v)).append('|');
                b.append(org.h2.util.DateTimeUtils.absoluteDayFromDateValue(v)).append('|');
                b.append(org.h2.util.DateTimeUtils.incrementDateValue(v)).append('|');
                b.append(org.h2.util.DateTimeUtils.decrementDateValue(v)).append('|');
                b.append(org.h2.util.DateTimeUtils.dateValueFromAbsoluteDay(
                    org.h2.util.DateTimeUtils.absoluteDayFromDateValue(v))).append('|');
                b.append(org.h2.util.DateTimeUtils.isValidDate(
                    org.h2.util.DateTimeUtils.yearFromDateValue(v),
                    org.h2.util.DateTimeUtils.monthFromDateValue(v),
                    org.h2.util.DateTimeUtils.dayFromDateValue(v)));
                System.out.println(b.toString());
              }
        """;
}
