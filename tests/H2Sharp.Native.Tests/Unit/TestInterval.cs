// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.unit.TestInterval.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified: the reference's main() entry is the runner; xUnit runs Test() directly. The
// reference's IllegalArgumentException expectations are ArgumentException, the .NET spelling
// of the same argument error.
using System;
using H2Sharp.Api;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Native.Tests.Unit;

/// <summary>
/// Tests the Interval class.
/// </summary>
public sealed class TestInterval
{
    private static readonly long Max = 999_999_999_999_999_999L;

    private static readonly long Min = -999_999_999_999_999_999L;

    private const long NanosPerSecond = 1_000_000_000L;

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public void Test()
    {
        TestOfYears();
        TestOfMonths();
        TestOfDays();
        TestOfHours();
        TestOfMinutes();
        TestOfSeconds();
        TestOfSeconds2();
        TestOfNanos();
        TestOfYearsMonths();
        TestOfDaysHours();
        TestOfDaysHoursMinutes();
        TestOfDaysHoursMinutesSeconds();
        TestOfHoursMinutes();
        TestOfHoursMinutesSeconds();
        TestOfMinutesSeconds();
    }

    private static void TestOfYears()
    {
        TestOfYearsGood(0);
        TestOfYearsGood(100);
        TestOfYearsGood(-100);
        TestOfYearsGood(Max);
        TestOfYearsGood(Min);
        TestOfYearsBad(Max + 1);
        TestOfYearsBad(Min - 1);
        TestOfYearsBad(long.MaxValue);
        TestOfYearsBad(long.MinValue);
    }

    private static void TestOfYearsGood(long years)
    {
        Interval i = Interval.OfYears(years);
        Assert.Equal(years, i.GetYears());
        Assert.Equal("INTERVAL '" + years + "' YEAR", i.ToString());
    }

    private static void TestOfYearsBad(long years)
    {
        Assert.Throws<ArgumentException>(() => Interval.OfYears(years));
    }

    private static void TestOfMonths()
    {
        TestOfMonthsGood(0);
        TestOfMonthsGood(100);
        TestOfMonthsGood(-100);
        TestOfMonthsGood(Max);
        TestOfMonthsGood(Min);
        TestOfMonthsBad(Max + 1);
        TestOfMonthsBad(Min - 1);
        TestOfMonthsBad(long.MaxValue);
        TestOfMonthsBad(long.MinValue);
    }

    private static void TestOfMonthsGood(long months)
    {
        Interval i = Interval.OfMonths(months);
        Assert.Equal(months, i.GetMonths());
        Assert.Equal("INTERVAL '" + months + "' MONTH", i.ToString());
    }

    private static void TestOfMonthsBad(long months)
    {
        Assert.Throws<ArgumentException>(() => Interval.OfMonths(months));
    }

    private static void TestOfDays()
    {
        TestOfDaysGood(0);
        TestOfDaysGood(100);
        TestOfDaysGood(-100);
        TestOfDaysGood(Max);
        TestOfDaysGood(Min);
        TestOfDaysBad(Max + 1);
        TestOfDaysBad(Min - 1);
        TestOfDaysBad(long.MaxValue);
        TestOfDaysBad(long.MinValue);
    }

    private static void TestOfDaysGood(long days)
    {
        Interval i = Interval.OfDays(days);
        Assert.Equal(days, i.GetDays());
        Assert.Equal("INTERVAL '" + days + "' DAY", i.ToString());
    }

    private static void TestOfDaysBad(long days)
    {
        Assert.Throws<ArgumentException>(() => Interval.OfDays(days));
    }

    private static void TestOfHours()
    {
        TestOfHoursGood(0);
        TestOfHoursGood(100);
        TestOfHoursGood(-100);
        TestOfHoursGood(Max);
        TestOfHoursGood(Min);
        TestOfHoursBad(Max + 1);
        TestOfHoursBad(Min - 1);
        TestOfHoursBad(long.MaxValue);
        TestOfHoursBad(long.MinValue);
    }

    private static void TestOfHoursGood(long hours)
    {
        Interval i = Interval.OfHours(hours);
        Assert.Equal(hours, i.GetHours());
        Assert.Equal("INTERVAL '" + hours + "' HOUR", i.ToString());
    }

    private static void TestOfHoursBad(long hours)
    {
        Assert.Throws<ArgumentException>(() => Interval.OfHours(hours));
    }

    private static void TestOfMinutes()
    {
        TestOfMinutesGood(0);
        TestOfMinutesGood(100);
        TestOfMinutesGood(-100);
        TestOfMinutesGood(Max);
        TestOfMinutesGood(Min);
        TestOfMinutesBad(Max + 1);
        TestOfMinutesBad(Min - 1);
        TestOfMinutesBad(long.MaxValue);
        TestOfMinutesBad(long.MinValue);
    }

    private static void TestOfMinutesGood(long minutes)
    {
        Interval i = Interval.OfMinutes(minutes);
        Assert.Equal(minutes, i.GetMinutes());
        Assert.Equal("INTERVAL '" + minutes + "' MINUTE", i.ToString());
    }

    private static void TestOfMinutesBad(long minutes)
    {
        Assert.Throws<ArgumentException>(() => Interval.OfMinutes(minutes));
    }

    private static void TestOfSeconds()
    {
        TestOfSecondsGood(0);
        TestOfSecondsGood(100);
        TestOfSecondsGood(-100);
        TestOfSecondsGood(Max);
        TestOfSecondsGood(Min);
        TestOfSecondsBad(Max + 1);
        TestOfSecondsBad(Min - 1);
        TestOfSecondsBad(long.MaxValue);
        TestOfSecondsBad(long.MinValue);
    }

    private static void TestOfSecondsGood(long seconds)
    {
        Interval i = Interval.OfSeconds(seconds);
        Assert.Equal(seconds, i.GetSeconds());
        Assert.Equal("INTERVAL '" + seconds + "' SECOND", i.ToString());
    }

    private static void TestOfSecondsBad(long seconds)
    {
        Assert.Throws<ArgumentException>(() => Interval.OfSeconds(seconds));
    }

    private static void TestOfSeconds2()
    {
        TestOfSeconds2Good(0, 0);
        TestOfSeconds2Good(0, -2);
        TestOfSeconds2Good(100, 5);
        TestOfSeconds2Good(-100, -1);
        TestOfSeconds2Good(Max, 999_999_999);
        TestOfSeconds2Good(Min, -999_999_999);
        TestOfSeconds2Bad(0, 1_000_000_000);
        TestOfSeconds2Bad(0, -1_000_000_000);
        TestOfSeconds2Bad(Max + 1, 0);
        TestOfSeconds2Bad(Min - 1, 0);
        TestOfSeconds2Bad(long.MaxValue, 0);
        TestOfSeconds2Bad(long.MinValue, 0);
        TestOfSeconds2Bad(0, int.MaxValue);
        TestOfSeconds2Bad(0, int.MinValue);
    }

    private static void TestOfSeconds2Good(long seconds, int nanos)
    {
        Interval i = Interval.OfSeconds(seconds, nanos);
        Assert.Equal(seconds, i.GetSeconds());
        Assert.Equal(nanos, i.GetNanosOfSecond());
        if (Math.Abs(seconds) < 9_000_000_000L)
        {
            Assert.Equal(seconds * NanosPerSecond + nanos, i.GetSecondsAndNanos());
        }

        System.Text.StringBuilder b = new("INTERVAL '");
        if (seconds < 0 || nanos < 0)
        {
            _ = b.Append('-');
        }

        _ = b.Append(Math.Abs(seconds));
        if (nanos != 0)
        {
            _ = b.Append('.');
            _ = StringUtils.AppendZeroPadded(b, 9, Math.Abs(nanos));
            StripTrailingZeroes(b);
        }

        _ = b.Append("' SECOND");
        Assert.Equal(b.ToString(), i.ToString());
    }

    private static void TestOfSeconds2Bad(long seconds, int nanos)
    {
        Assert.Throws<ArgumentException>(() => Interval.OfSeconds(seconds, nanos));
    }

    private static void TestOfNanos()
    {
        TestOfNanosGood(0);
        TestOfNanosGood(100);
        TestOfNanosGood(-100);
        TestOfNanosGood(long.MaxValue);
        TestOfNanosGood(long.MinValue);
    }

    private static void TestOfNanosGood(long nanos)
    {
        Interval i = Interval.OfNanos(nanos);
        long seconds = nanos / NanosPerSecond;
        int nanosOfSecond = (int)(nanos % NanosPerSecond);
        Assert.Equal(seconds, i.GetSeconds());
        Assert.Equal(nanosOfSecond, i.GetNanosOfSecond());
        Assert.Equal(nanos, i.GetSecondsAndNanos());
        System.Text.StringBuilder b = new("INTERVAL '");
        if (nanos < 0)
        {
            _ = b.Append('-');
        }

        _ = b.Append(Math.Abs(seconds));
        if (nanosOfSecond != 0)
        {
            _ = b.Append('.');
            _ = StringUtils.AppendZeroPadded(b, 9, Math.Abs(nanosOfSecond));
            StripTrailingZeroes(b);
        }

        _ = b.Append("' SECOND");
        Assert.Equal(b.ToString(), i.ToString());
    }

    private static void TestOfYearsMonths()
    {
        TestOfYearsMonthsGood(0, 0);
        TestOfYearsMonthsGood(0, -2);
        TestOfYearsMonthsGood(100, 5);
        TestOfYearsMonthsGood(-100, -1);
        TestOfYearsMonthsGood(Max, 11);
        TestOfYearsMonthsGood(Min, -11);
        TestOfYearsMonthsBad(0, 12);
        TestOfYearsMonthsBad(0, -12);
        TestOfYearsMonthsBad(Max + 1, 0);
        TestOfYearsMonthsBad(Min - 1, 0);
        TestOfYearsMonthsBad(long.MaxValue, 0);
        TestOfYearsMonthsBad(long.MinValue, 0);
        TestOfYearsMonthsBad(0, int.MaxValue);
        TestOfYearsMonthsBad(0, int.MinValue);
    }

    private static void TestOfYearsMonthsGood(long years, int months)
    {
        Interval i = Interval.OfYearsMonths(years, months);
        Assert.Equal(years, i.GetYears());
        Assert.Equal(months, i.GetMonths());
        System.Text.StringBuilder b = new("INTERVAL '");
        if (years < 0 || months < 0)
        {
            _ = b.Append('-');
        }

        _ = b.Append(Math.Abs(years)).Append('-').Append(Math.Abs(months))
            .Append("' YEAR TO MONTH");
        Assert.Equal(b.ToString(), i.ToString());
    }

    private static void TestOfYearsMonthsBad(long years, int months)
    {
        Assert.Throws<ArgumentException>(() => Interval.OfYearsMonths(years, months));
    }

    private static void TestOfDaysHours()
    {
        TestOfDaysHoursGood(0, 0);
        TestOfDaysHoursGood(0, -2);
        TestOfDaysHoursGood(100, 5);
        TestOfDaysHoursGood(-100, -1);
        TestOfDaysHoursGood(Max, 23);
        TestOfDaysHoursGood(Min, -23);
        TestOfDaysHoursBad(0, 24);
        TestOfDaysHoursBad(0, -24);
        TestOfDaysHoursBad(Max + 1, 0);
        TestOfDaysHoursBad(Min - 1, 0);
        TestOfDaysHoursBad(long.MaxValue, 0);
        TestOfDaysHoursBad(long.MinValue, 0);
        TestOfDaysHoursBad(0, int.MaxValue);
        TestOfDaysHoursBad(0, int.MinValue);
    }

    private static void TestOfDaysHoursGood(long days, int hours)
    {
        Interval i = Interval.OfDaysHours(days, hours);
        Assert.Equal(days, i.GetDays());
        Assert.Equal(hours, i.GetHours());
        System.Text.StringBuilder b = new("INTERVAL '");
        if (days < 0 || hours < 0)
        {
            _ = b.Append('-');
        }

        _ = b.Append(Math.Abs(days)).Append(' ');
        _ = StringUtils.AppendTwoDigits(b, Math.Abs(hours));
        _ = b.Append("' DAY TO HOUR");
        Assert.Equal(b.ToString(), i.ToString());
    }

    private static void TestOfDaysHoursBad(long days, int hours)
    {
        Assert.Throws<ArgumentException>(() => Interval.OfDaysHours(days, hours));
    }

    private static void TestOfDaysHoursMinutes()
    {
        TestOfDaysHoursMinutesGood(0, 0, 0);
        TestOfDaysHoursMinutesGood(0, -2, 0);
        TestOfDaysHoursMinutesGood(0, 0, -2);
        TestOfDaysHoursMinutesGood(100, 5, 3);
        TestOfDaysHoursMinutesGood(-100, -1, -3);
        TestOfDaysHoursMinutesGood(Max, 23, 59);
        TestOfDaysHoursMinutesGood(Min, -23, -59);
        TestOfDaysHoursMinutesBad(0, 24, 0);
        TestOfDaysHoursMinutesBad(0, -24, 0);
        TestOfDaysHoursMinutesBad(0, 0, 60);
        TestOfDaysHoursMinutesBad(0, 0, -60);
        TestOfDaysHoursMinutesBad(Max + 1, 0, 0);
        TestOfDaysHoursMinutesBad(Min - 1, 0, 0);
        TestOfDaysHoursMinutesBad(long.MaxValue, 0, 0);
        TestOfDaysHoursMinutesBad(long.MinValue, 0, 0);
        TestOfDaysHoursMinutesBad(0, int.MaxValue, 0);
        TestOfDaysHoursMinutesBad(0, int.MinValue, 0);
        TestOfDaysHoursMinutesBad(0, 0, int.MaxValue);
        TestOfDaysHoursMinutesBad(0, 0, int.MinValue);
    }

    private static void TestOfDaysHoursMinutesGood(long days, int hours, int minutes)
    {
        Interval i = Interval.OfDaysHoursMinutes(days, hours, minutes);
        Assert.Equal(days, i.GetDays());
        Assert.Equal(hours, i.GetHours());
        Assert.Equal(minutes, i.GetMinutes());
        System.Text.StringBuilder b = new("INTERVAL '");
        if (days < 0 || hours < 0 || minutes < 0)
        {
            _ = b.Append('-');
        }

        _ = b.Append(Math.Abs(days)).Append(' ');
        _ = StringUtils.AppendTwoDigits(b, Math.Abs(hours));
        _ = b.Append(':');
        _ = StringUtils.AppendTwoDigits(b, Math.Abs(minutes));
        _ = b.Append("' DAY TO MINUTE");
        Assert.Equal(b.ToString(), i.ToString());
    }

    private static void TestOfDaysHoursMinutesBad(long days, int hours, int minutes)
    {
        Assert.Throws<ArgumentException>(
            () => Interval.OfDaysHoursMinutes(days, hours, minutes));
    }

    private static void TestOfDaysHoursMinutesSeconds()
    {
        TestOfDaysHoursMinutesSecondsGood(0, 0, 0, 0);
        TestOfDaysHoursMinutesSecondsGood(0, -2, 0, 0);
        TestOfDaysHoursMinutesSecondsGood(0, 0, -2, 0);
        TestOfDaysHoursMinutesSecondsGood(0, 0, 0, -2);
        TestOfDaysHoursMinutesSecondsGood(100, 5, 3, 4);
        TestOfDaysHoursMinutesSecondsGood(-100, -1, -3, -4);
        TestOfDaysHoursMinutesSecondsGood(Max, 23, 59, 59);
        TestOfDaysHoursMinutesSecondsGood(Min, -23, -59, -59);
        TestOfDaysHoursMinutesSecondsBad(0, 24, 0, 0);
        TestOfDaysHoursMinutesSecondsBad(0, -24, 0, 0);
        TestOfDaysHoursMinutesSecondsBad(0, 0, 60, 0);
        TestOfDaysHoursMinutesSecondsBad(0, 0, -60, 0);
        TestOfDaysHoursMinutesSecondsBad(0, 0, 0, 60);
        TestOfDaysHoursMinutesSecondsBad(0, 0, 0, -60);
        TestOfDaysHoursMinutesSecondsBad(Max + 1, 0, 0, 0);
        TestOfDaysHoursMinutesSecondsBad(Min - 1, 0, 0, 0);
        TestOfDaysHoursMinutesSecondsBad(long.MaxValue, 0, 0, 0);
        TestOfDaysHoursMinutesSecondsBad(long.MinValue, 0, 0, 0);
        TestOfDaysHoursMinutesSecondsBad(0, int.MaxValue, 0, 0);
        TestOfDaysHoursMinutesSecondsBad(0, int.MinValue, 0, 0);
        TestOfDaysHoursMinutesSecondsBad(0, 0, int.MaxValue, 0);
        TestOfDaysHoursMinutesSecondsBad(0, 0, int.MinValue, 0);
        TestOfDaysHoursMinutesSecondsBad(0, 0, 0, int.MaxValue);
        TestOfDaysHoursMinutesSecondsBad(0, 0, 0, int.MinValue);
    }

    private static void TestOfDaysHoursMinutesSecondsGood(long days, int hours, int minutes,
        int seconds)
    {
        Interval i = Interval.OfDaysHoursMinutesSeconds(days, hours, minutes, seconds);
        Assert.Equal(days, i.GetDays());
        Assert.Equal(hours, i.GetHours());
        Assert.Equal(minutes, i.GetMinutes());
        Assert.Equal(seconds, i.GetSeconds());
        Assert.Equal(0, i.GetNanosOfSecond());
        Assert.Equal(seconds * NanosPerSecond, i.GetSecondsAndNanos());
        System.Text.StringBuilder b = new("INTERVAL '");
        if (days < 0 || hours < 0 || minutes < 0 || seconds < 0)
        {
            _ = b.Append('-');
        }

        _ = b.Append(Math.Abs(days)).Append(' ');
        _ = StringUtils.AppendTwoDigits(b, Math.Abs(hours));
        _ = b.Append(':');
        _ = StringUtils.AppendTwoDigits(b, Math.Abs(minutes));
        _ = b.Append(':');
        _ = StringUtils.AppendTwoDigits(b, Math.Abs(seconds));
        _ = b.Append("' DAY TO SECOND");
        Assert.Equal(b.ToString(), i.ToString());
    }

    private static void TestOfDaysHoursMinutesSecondsBad(long days, int hours, int minutes,
        int seconds)
    {
        Assert.Throws<ArgumentException>(
            () => Interval.OfDaysHoursMinutesSeconds(days, hours, minutes, seconds));
    }

    private static void TestOfHoursMinutes()
    {
        TestOfHoursMinutesGood(0, 0);
        TestOfHoursMinutesGood(0, -2);
        TestOfHoursMinutesGood(100, 5);
        TestOfHoursMinutesGood(-100, -1);
        TestOfHoursMinutesGood(Max, 59);
        TestOfHoursMinutesGood(Min, -59);
        TestOfHoursMinutesBad(0, 60);
        TestOfHoursMinutesBad(0, -60);
        TestOfHoursMinutesBad(Max + 1, 0);
        TestOfHoursMinutesBad(Min - 1, 0);
        TestOfHoursMinutesBad(long.MaxValue, 0);
        TestOfHoursMinutesBad(long.MinValue, 0);
        TestOfHoursMinutesBad(0, int.MaxValue);
        TestOfHoursMinutesBad(0, int.MinValue);
    }

    private static void TestOfHoursMinutesGood(long hours, int minutes)
    {
        Interval i = Interval.OfHoursMinutes(hours, minutes);
        Assert.Equal(hours, i.GetHours());
        Assert.Equal(minutes, i.GetMinutes());
        System.Text.StringBuilder b = new("INTERVAL '");
        if (hours < 0 || minutes < 0)
        {
            _ = b.Append('-');
        }

        _ = b.Append(Math.Abs(hours)).Append(':');
        _ = StringUtils.AppendTwoDigits(b, Math.Abs(minutes));
        _ = b.Append("' HOUR TO MINUTE");
        Assert.Equal(b.ToString(), i.ToString());
    }

    private static void TestOfHoursMinutesBad(long hours, int minutes)
    {
        Assert.Throws<ArgumentException>(() => Interval.OfHoursMinutes(hours, minutes));
    }

    private static void TestOfHoursMinutesSeconds()
    {
        TestOfHoursMinutesSecondsGood(0, 0, 0);
        TestOfHoursMinutesSecondsGood(0, -2, 0);
        TestOfHoursMinutesSecondsGood(0, 0, -2);
        TestOfHoursMinutesSecondsGood(100, 5, 3);
        TestOfHoursMinutesSecondsGood(-100, -1, -3);
        TestOfHoursMinutesSecondsGood(Max, 59, 59);
        TestOfHoursMinutesSecondsGood(Min, -59, -59);
        TestOfHoursMinutesSecondsBad(0, 60, 0);
        TestOfHoursMinutesSecondsBad(0, -60, 0);
        TestOfHoursMinutesSecondsBad(0, 0, 60);
        TestOfHoursMinutesSecondsBad(0, 0, -60);
        TestOfHoursMinutesSecondsBad(Max + 1, 0, 0);
        TestOfHoursMinutesSecondsBad(Min - 1, 0, 0);
        TestOfHoursMinutesSecondsBad(long.MaxValue, 0, 0);
        TestOfHoursMinutesSecondsBad(long.MinValue, 0, 0);
        TestOfHoursMinutesSecondsBad(0, int.MaxValue, 0);
        TestOfHoursMinutesSecondsBad(0, int.MinValue, 0);
        TestOfHoursMinutesSecondsBad(0, 0, int.MaxValue);
        TestOfHoursMinutesSecondsBad(0, 0, int.MinValue);
    }

    private static void TestOfHoursMinutesSecondsGood(long hours, int minutes, int seconds)
    {
        Interval i = Interval.OfHoursMinutesSeconds(hours, minutes, seconds);
        Assert.Equal(hours, i.GetHours());
        Assert.Equal(minutes, i.GetMinutes());
        Assert.Equal(seconds, i.GetSeconds());
        Assert.Equal(0, i.GetNanosOfSecond());
        Assert.Equal(seconds * NanosPerSecond, i.GetSecondsAndNanos());
        System.Text.StringBuilder b = new("INTERVAL '");
        if (hours < 0 || minutes < 0 || seconds < 0)
        {
            _ = b.Append('-');
        }

        _ = b.Append(Math.Abs(hours)).Append(':');
        _ = StringUtils.AppendTwoDigits(b, Math.Abs(minutes));
        _ = b.Append(':');
        _ = StringUtils.AppendTwoDigits(b, Math.Abs(seconds));
        _ = b.Append("' HOUR TO SECOND");
        Assert.Equal(b.ToString(), i.ToString());
    }

    private static void TestOfHoursMinutesSecondsBad(long hours, int minutes, int seconds)
    {
        Assert.Throws<ArgumentException>(
            () => Interval.OfHoursMinutesSeconds(hours, minutes, seconds));
    }

    private static void TestOfMinutesSeconds()
    {
        TestOfMinutesSecondsGood(0, 0);
        TestOfMinutesSecondsGood(0, -2);
        TestOfMinutesSecondsGood(100, 5);
        TestOfMinutesSecondsGood(-100, -1);
        TestOfMinutesSecondsGood(Max, 59);
        TestOfMinutesSecondsGood(Min, -59);
        TestOfMinutesSecondsBad(0, 60);
        TestOfMinutesSecondsBad(0, -60);
        TestOfMinutesSecondsBad(Max + 1, 0);
        TestOfMinutesSecondsBad(Min - 1, 0);
        TestOfMinutesSecondsBad(long.MaxValue, 0);
        TestOfMinutesSecondsBad(long.MinValue, 0);
        TestOfMinutesSecondsBad(0, int.MaxValue);
        TestOfMinutesSecondsBad(0, int.MinValue);
    }

    private static void TestOfMinutesSecondsGood(long minutes, int seconds)
    {
        Interval i = Interval.OfMinutesSeconds(minutes, seconds);
        Assert.Equal(minutes, i.GetMinutes());
        Assert.Equal(seconds, i.GetSeconds());
        Assert.Equal(0, i.GetNanosOfSecond());
        Assert.Equal(seconds * NanosPerSecond, i.GetSecondsAndNanos());
        System.Text.StringBuilder b = new("INTERVAL '");
        if (minutes < 0 || seconds < 0)
        {
            _ = b.Append('-');
        }

        _ = b.Append(Math.Abs(minutes)).Append(':');
        _ = StringUtils.AppendTwoDigits(b, Math.Abs(seconds));
        _ = b.Append("' MINUTE TO SECOND");
        Assert.Equal(b.ToString(), i.ToString());
    }

    private static void TestOfMinutesSecondsBad(long minutes, int seconds)
    {
        Assert.Throws<ArgumentException>(() => Interval.OfMinutesSeconds(minutes, seconds));
    }

    private static void StripTrailingZeroes(System.Text.StringBuilder b)
    {
        int i = b.Length - 1;
        if (b[i] == '0')
        {
            while (b[--i] == '0')
            {
                // do nothing
            }

            b.Length = i + 1;
        }
    }
}
