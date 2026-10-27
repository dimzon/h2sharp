/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.util.DateTimeUtils.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Globalization;
using System.Text;

namespace H2Sharp.Util;

/// <summary>
/// The arithmetic of dates and times, and the reading and writing of their text.
/// </summary>
/// <remarks>
/// A date is one number here and not three: the year, the month and the day are shifted into it,
/// nine bits for the year and five for the month, which is the reference's own layout and the one
/// that makes a date sortable by the number that holds it. A time is a count of nanoseconds from
/// midnight, and a timestamp is the pair.
/// <para>
/// The methods that need a time zone are here too: the two parsers and
/// <c>timestampTimeZoneAtOffset</c> all work with <see cref="TimeZoneProvider"/>, because a literal
/// in a statement is read by the parser and the parser must not have to wait for a time zone to
/// read a date.
/// </para>
/// </remarks>
public static class DateTimeUtils
{
    /// <summary>
    /// The milliseconds in a day.
    /// </summary>
    public const long MillisPerDay = 24 * 60 * 60 * 1000L;

    /// <summary>
    /// The seconds in a day.
    /// </summary>
    public const long SecondsPerDay = 24 * 60 * 60;

    /// <summary>
    /// The nanoseconds in a second.
    /// </summary>
    public const long NanosPerSecond = 1_000_000_000;

    /// <summary>
    /// The nanoseconds in a minute.
    /// </summary>
    public const long NanosPerMinute = 60 * NanosPerSecond;

    /// <summary>
    /// The nanoseconds in an hour.
    /// </summary>
    public const long NanosPerHour = 60 * NanosPerMinute;

    /// <summary>
    /// The nanoseconds in a day.
    /// </summary>
    public const long NanosPerDay = MillisPerDay * 1_000_000;

    /// <summary>
    /// How far the year is shifted in a date value.
    /// </summary>
    public const int ShiftYear = 9;

    /// <summary>
    /// How far the month is shifted in a date value.
    /// </summary>
    public const int ShiftMonth = 5;

    /// <summary>
    /// The date value of the epoch, which is what a timestamp counts from.
    /// </summary>
    public const long EpochDateValue = (1970L << ShiftYear) + (1 << ShiftMonth) + 1;

    /// <summary>
    /// The smallest date value that may be stored, which is a million years back.
    /// </summary>
    public const long MinDateValue = (-1_000_000_000L << ShiftYear) + (1 << ShiftMonth) + 1;

    /// <summary>
    /// The largest date value that may be stored, which is a million years forward.
    /// </summary>
    public const long MaxDateValue = (1_000_000_000L << ShiftYear) + (12 << ShiftMonth) + 31;

    /// <summary>
    /// The days in each month, February included as a month of twenty-eight.
    /// </summary>
    private static readonly int[] NormalDaysPerMonth =
        [0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

    /// <summary>
    /// The multiples of a second, from a whole second down to a single nanosecond, which is how a
    /// count of nanoseconds is written out with only the digits that mean something.
    /// </summary>
    private static readonly int[] FractionalSecondsTable =
        [1_000_000_000, 100_000_000, 10_000_000, 1_000_000, 100_000, 10_000, 1_000, 100, 10, 1];

    /// <summary>
    /// Reads a date written as year, month and day.
    /// </summary>
    /// <remarks>
    /// Two shapes are read: the usual <c>yyyy-mm-dd</c>, with a sign and any number of digits in
    /// the year, and the compact <c>yyyyMMdd</c> that is kept for compatibility. The compact shape
    /// is the reason the year is read from a position rather than assumed to be four digits, and the
    /// reason the month is read from the end: <c>20200101</c> is the first of January and not the
    /// first of the twentieth month.
    /// </remarks>
    /// <param name="s">the text</param>
    /// <param name="start">where the date begins</param>
    /// <param name="end">where the date ends</param>
    /// <returns>the date value</returns>
    /// <exception cref="ArgumentException">if the text is not a date</exception>
    public static long ParseDateValue(string s, int start, int end)
    {
        if (s[start] == '+')
        {
            start++;
        }

        // Starting at position 1 is what lets a year carry a minus sign.
        int yEnd = s.IndexOf('-', start + 1);
        int mStart, mEnd, dStart;
        if (yEnd > 0)
        {
            mStart = yEnd + 1;
            mEnd = s.IndexOf('-', mStart);
            if (mEnd <= mStart)
            {
                throw new ArgumentException(s, nameof(s));
            }

            dStart = mEnd + 1;
        }
        else
        {
            mEnd = dStart = end - 2;
            yEnd = mStart = mEnd - 2;
            if (yEnd < start + 3)
            {
                throw new ArgumentException(s, nameof(s));
            }
        }

        int year = ParseInt32(s, start, yEnd);
        int month = StringUtils.ParseUInt31(s, mStart, mEnd);
        int day = StringUtils.ParseUInt31(s, dStart, end);
        if (!IsValidDate(year, month, day))
        {
            throw new ArgumentException(
                year + "-" + month + "-" + day, nameof(s));
        }

        return DateValue(year, month, day);
    }

    /// <summary>
    /// Reads a time written in one of the four shapes the reference accepts.
    /// </summary>
    /// <remarks>
    /// The shapes are <c>hour:minute[:second[.nanos]]</c>, <c>hhmm[ss[.nanos]]</c>,
    /// <c>hhmmss.nanos</c> and the DB2 <c>hour.minute.second[.nanos]</c>. The shape is decided by
    /// what separators are present, and the two shapes with no separator are decided by how long the
    /// text is, which is why the length of the text is examined before any of it is read.
    /// </remarks>
    /// <param name="s">the text</param>
    /// <param name="start">where the time begins</param>
    /// <param name="end">where the time ends</param>
    /// <returns>the time in nanoseconds from midnight</returns>
    /// <exception cref="ArgumentException">if the text is not a time</exception>
    public static long ParseTimeNanos(string s, int start, int end)
    {
        int hEnd = s.IndexOf(':', start);
        int mStart, mEnd, sStart, sEnd;
        if (hEnd > 0)
        {
            mStart = hEnd + 1;
            mEnd = s.IndexOf(':', mStart);
            if (mEnd >= mStart)
            {
                sStart = mEnd + 1;
                sEnd = s.IndexOf('.', sStart);
            }
            else
            {
                mEnd = end;
                sStart = sEnd = -1;
            }
        }
        else
        {
            int t = s.IndexOf('.', start);
            if (t < 0)
            {
                hEnd = mStart = start + 2;
                mEnd = mStart + 2;
                int len = end - start;
                if (len == 6)
                {
                    sStart = mEnd;
                    sEnd = -1;
                }
                else if (len == 4)
                {
                    sStart = sEnd = -1;
                }
                else
                {
                    throw new ArgumentException(s, nameof(s));
                }
            }
            else if (t >= start + 6)
            {
                if (t - start != 6)
                {
                    throw new ArgumentException(s, nameof(s));
                }

                hEnd = mStart = start + 2;
                mEnd = sStart = mStart + 2;
                sEnd = t;
            }
            else
            {
                hEnd = t;
                mStart = hEnd + 1;
                mEnd = s.IndexOf('.', mStart);
                if (mEnd <= mStart)
                {
                    throw new ArgumentException(s, nameof(s));
                }

                sStart = mEnd + 1;
                sEnd = s.IndexOf('.', sStart);
            }
        }

        int hour = StringUtils.ParseUInt31(s, start, hEnd);
        if (hour >= 24)
        {
            throw new ArgumentException(s, nameof(s));
        }

        int minute = StringUtils.ParseUInt31(s, mStart, mEnd);
        int second, nanos;
        if (sStart > 0)
        {
            if (sEnd < 0)
            {
                second = StringUtils.ParseUInt31(s, sStart, end);
                nanos = 0;
            }
            else
            {
                second = StringUtils.ParseUInt31(s, sStart, sEnd);
                nanos = ParseNanos(s, sEnd + 1, end);
            }
        }
        else
        {
            second = nanos = 0;
        }

        // A minute or a second of sixty or more is not a time, and it is refused here rather than
        // being carried into a count of nanoseconds that is more than a day.
        if (minute >= 60 || second >= 60)
        {
            throw new ArgumentException(s, nameof(s));
        }

        return ((((long)hour * 60) + minute) * 60 + second) * NanosPerSecond + nanos;
    }

    /// <summary>
    /// Reads the digits after the point of a time.
    /// </summary>
    internal static int ParseNanos(string s, int start, int end)
    {
        if (start >= end)
        {
            throw new ArgumentException(s, nameof(s));
        }

        int nanos = 0, mul = 100_000_000;
        do
        {
            char c = s[start];
            if (c is < '0' or > '9')
            {
                throw new ArgumentException(s, nameof(s));
            }

            nanos += (c - '0') * mul;
            mul /= 10;
            start++;
        }
        while (start < end);

        return nanos;
    }

    /// <summary>
    /// Reads an int the way Integer.parseInt does, so that the same text is refused in the same
    /// place.
    /// </summary>
    private static int ParseInt32(string s, int start, int end)
    {
        bool negative = false;
        int i = start;
        if (s[i] == '-')
        {
            negative = true;
            i++;
        }
        else if (s[i] == '+')
        {
            i++;
        }

        if (negative)
        {
            return -StringUtils.ParseUInt31(s, i, end);
        }

        return StringUtils.ParseUInt31(s, i, end);
    }

    /// <summary>
    /// The number of days in a month, February being 29 in a leap year and 28 otherwise.
    /// </summary>
    /// <param name="year">the year</param>
    /// <param name="month">the month, January is 1</param>
    /// <returns>the number of days</returns>
    public static int GetDaysInMonth(int year, int month)
        => month == 2 ? (IsLeapYear(year) ? 29 : 28) : NormalDaysPerMonth[month];

    /// <summary>
    /// Whether the year is a leap year, which is every fourth year except the centuries that are
    /// not divisible by four hundred.
    /// </summary>
    /// <param name="year">the year</param>
    /// <returns>true if the year is a leap year</returns>
    public static bool IsLeapYear(int year)
        => (year & 3) == 0 && (year % 100 != 0 || year % 400 == 0);

    /// <summary>
    /// Whether the year, month and day are a date that exists.
    /// </summary>
    /// <param name="year">the year</param>
    /// <param name="month">the month, January is 1</param>
    /// <param name="day">the day, the first is 1</param>
    /// <returns>true if the date exists</returns>
    public static bool IsValidDate(int year, int month, int day)
        => month >= 1 && month <= 12 && day >= 1 && day <= GetDaysInMonth(year, month);

    /// <summary>
    /// The year of a date value.
    /// </summary>
    /// <param name="x">the date value</param>
    /// <returns>the year</returns>
    public static int YearFromDateValue(long x) => (int)(x >> ShiftYear);

    /// <summary>
    /// The month of a date value.
    /// </summary>
    /// <param name="x">the date value</param>
    /// <returns>the month, 1 to 12</returns>
    public static int MonthFromDateValue(long x) => (int)(x >> ShiftMonth) & 15;

    /// <summary>
    /// The day of a date value.
    /// </summary>
    /// <param name="x">the date value</param>
    /// <returns>the day, 1 to 31</returns>
    public static int DayFromDateValue(long x) => (int)x & 31;

    /// <summary>
    /// The date value of a year, month and day.
    /// </summary>
    /// <param name="year">the year</param>
    /// <param name="month">the month, January is 1</param>
    /// <param name="day">the day, the first is 1</param>
    /// <returns>the date value</returns>
    public static long DateValue(long year, int month, int day)
        // The day is sign-extended to a long and or-ed in, which is harmless because the five bits
        // it occupies are the low ones and the extension only fills bits above them. The compiler
        // warns about it and the warning is right in general and wrong here: the reference does the
        // same three operations with the same three widths.
        => (year << ShiftYear) | ((long)month << ShiftMonth) | (uint)day;

    /// <summary>
    /// The date value of a year, month and day that may be out of range, where the month carries
    /// into the year and the day is cut down to what the month has.
    /// </summary>
    /// <param name="year">the year</param>
    /// <param name="month">the month, which may be out of range</param>
    /// <param name="day">the day, which is cut down rather than refused</param>
    /// <returns>the date value</returns>
    public static long DateValueFromDenormalizedDate(long year, long month, int day)
    {
        long mm1 = month - 1;
        long yd = mm1 / 12;
        if (mm1 < 0 && yd * 12 != mm1)
        {
            yd--;
        }

        int y = (int)(year + yd);
        int m = (int)(month - (yd * 12));
        if (day < 1)
        {
            day = 1;
        }
        else
        {
            int max = GetDaysInMonth(y, m);
            if (day > max)
            {
                day = max;
            }
        }

        return DateValue(y, m, day);
    }

    /// <summary>
    /// The number of days since some day before the calendar started, for the first day of a year.
    /// </summary>
    /// <param name="year">the year</param>
    /// <returns>the absolute day</returns>
    public static long AbsoluteDayFromYear(long year)
    {
        long a = (365 * year) - 719_528;
        if (year >= 0)
        {
            a += ((year + 3) / 4) - ((year + 99) / 100) + ((year + 399) / 400);
        }
        else
        {
            a -= (year / -4) - (year / -100) + (year / -400);
        }

        return a;
    }

    /// <summary>
    /// The number of days since some day before the calendar started, for a date value.
    /// </summary>
    /// <param name="dateValue">the date value</param>
    /// <returns>the absolute day</returns>
    public static long AbsoluteDayFromDateValue(long dateValue)
        => AbsoluteDay(YearFromDateValue(dateValue), MonthFromDateValue(dateValue), DayFromDateValue(dateValue));

    /// <summary>
    /// The number of days since some day before the calendar started, for a year, month and day.
    /// </summary>
    private static long AbsoluteDay(long y, int m, int d)
    {
        long a = AbsoluteDayFromYear(y) + ((367 * m) - 362) / 12 + d - 1;
        if (m > 2)
        {
            a--;
            if ((y & 3) != 0 || (y % 100 == 0 && y % 400 != 0))
            {
                a--;
            }
        }

        return a;
    }

    /// <summary>
    /// The date value of a number of days since some day before the calendar started.
    /// </summary>
    /// <param name="absoluteDay">the absolute day</param>
    /// <returns>the date value</returns>
    public static long DateValueFromAbsoluteDay(long absoluteDay)
    {
        long d = absoluteDay + 719_468;
        long a = 0;
        if (d < 0)
        {
            a = ((d + 1) / 146_097) - 1;
            d -= a * 146_097;
            a *= 400;
        }

        long y = ((400 * d) + 591) / 146_097;
        int day = (int)(d - ((365 * y) + (y / 4) - (y / 100) + (y / 400)));
        if (day < 0)
        {
            y--;
            day = (int)(d - ((365 * y) + (y / 4) - (y / 100) + (y / 400)));
        }

        y += a;
        int m = ((day * 5) + 2) / 153;
        day -= ((m * 306) + 5) / 10 - 1;
        if (m >= 10)
        {
            y++;
            m -= 12;
        }

        return DateValue(y, m + 3, day);
    }

    /// <summary>
    /// The date value of the day after this one.
    /// </summary>
    /// <param name="dateValue">the date value</param>
    /// <returns>the next date value</returns>
    public static long IncrementDateValue(long dateValue)
    {
        int day = DayFromDateValue(dateValue);
        if (day < 28)
        {
            return dateValue + 1;
        }

        int year = YearFromDateValue(dateValue);
        int month = MonthFromDateValue(dateValue);
        if (day < GetDaysInMonth(year, month))
        {
            return dateValue + 1;
        }

        if (month < 12)
        {
            month++;
        }
        else
        {
            month = 1;
            year++;
        }

        return DateValue(year, month, 1);
    }

    /// <summary>
    /// The date value of the day before this one.
    /// </summary>
    /// <param name="dateValue">the date value</param>
    /// <returns>the previous date value</returns>
    public static long DecrementDateValue(long dateValue)
    {
        if (DayFromDateValue(dateValue) > 1)
        {
            return dateValue - 1;
        }

        int year = YearFromDateValue(dateValue);
        int month = MonthFromDateValue(dateValue);
        if (month > 1)
        {
            month--;
        }
        else
        {
            month = 12;
            year--;
        }

        return DateValue(year, month, GetDaysInMonth(year, month));
    }

    /// <summary>
    /// The day of the week, counted from the day the caller says the week starts on.
    /// </summary>
    /// <param name="dateValue">the date value</param>
    /// <param name="firstDayOfWeek">the first day of the week, Monday as 1 and Sunday as 0</param>
    /// <returns>the day of the week, 1 to 7</returns>
    public static int GetDayOfWeek(long dateValue, int firstDayOfWeek)
        => GetDayOfWeekFromAbsolute(AbsoluteDayFromDateValue(dateValue), firstDayOfWeek);

    /// <summary>
    /// The day of the week from an absolute day, which is what the callers that already have one
    /// ask for.
    /// </summary>
    /// <param name="absoluteValue">the absolute day</param>
    /// <param name="firstDayOfWeek">the first day of the week</param>
    /// <returns>the day of the week, 1 to 7</returns>
    /// <remarks>
    /// The two branches are the same answer and Java's remainder keeps the sign of what it divides,
    /// so a day before the epoch has to be taken apart differently. Writing it as one expression
    /// with a correction added at the end would be shorter and would be a different answer for the
    /// dates before 1970, which is a quarter of what this is asked about.
    /// </remarks>
    public static int GetDayOfWeekFromAbsolute(long absoluteValue, int firstDayOfWeek)
        => absoluteValue >= 0
            ? (int)((absoluteValue - firstDayOfWeek + 11) % 7) + 1
            : (int)((absoluteValue - firstDayOfWeek - 2) % 7) + 7;

    /// <summary>
    /// The number of the day within its year.
    /// </summary>
    /// <param name="dateValue">the date value</param>
    /// <returns>the number of the day in the year</returns>
    public static int GetDayOfYear(long dateValue)
    {
        int m = MonthFromDateValue(dateValue);
        int a = ((367 * m) - 362) / 12 + DayFromDateValue(dateValue);
        if (m > 2)
        {
            a--;
            long y = YearFromDateValue(dateValue);
            if ((y & 3) != 0 || (y % 100 == 0 && y % 400 != 0))
            {
                a--;
            }
        }

        return a;
    }

    /// <summary>
    /// The day of the week the ISO counts, Monday as one and Sunday as seven.
    /// </summary>
    /// <param name="dateValue">the date value</param>
    /// <returns>the ISO day of the week</returns>
    public static int GetIsoDayOfWeek(long dateValue) => GetDayOfWeek(dateValue, 1);

    /// <summary>
    /// The week of the year the ISO counts.
    /// </summary>
    /// <param name="dateValue">the date value</param>
    /// <returns>the number of the week in the year</returns>
    public static int GetIsoWeekOfYear(long dateValue) => GetWeekOfYear(dateValue, 1, 4);

    /// <summary>
    /// The year the ISO counts the weeks of, which is not the year for the first days of January
    /// and the last days of December.
    /// </summary>
    /// <param name="dateValue">the date value</param>
    /// <returns>the ISO week year</returns>
    public static int GetIsoWeekYear(long dateValue) => GetWeekYear(dateValue, 1, 4);

    /// <summary>
    /// The day of the week counted from Sunday, Sunday as one and Monday as seven.
    /// </summary>
    /// <param name="dateValue">the date value</param>
    /// <returns>the day of the week</returns>
    public static int GetSundayDayOfWeek(long dateValue) => GetDayOfWeek(dateValue, 0);

    /// <summary>
    /// The number of the week in the year, for a calendar that says which day the week starts on
    /// and how many days the first week must have.
    /// </summary>
    /// <param name="dateValue">the date value</param>
    /// <param name="firstDayOfWeek">the first day of the week</param>
    /// <param name="minimalDaysInFirstWeek">the days the first week of a year must have</param>
    /// <returns>the number of the week in the year</returns>
    public static int GetWeekOfYear(long dateValue, int firstDayOfWeek, int minimalDaysInFirstWeek)
    {
        long abs = AbsoluteDayFromDateValue(dateValue);
        int year = YearFromDateValue(dateValue);
        long weekStart = GetWeekYearAbsoluteStart(year, firstDayOfWeek, minimalDaysInFirstWeek);
        if (abs - weekStart < 0)
        {
            weekStart = GetWeekYearAbsoluteStart(year - 1, firstDayOfWeek, minimalDaysInFirstWeek);
        }
        else if (MonthFromDateValue(dateValue) == 12
            && 24 + minimalDaysInFirstWeek < DayFromDateValue(dateValue)
            && abs >= GetWeekYearAbsoluteStart(year + 1, firstDayOfWeek, minimalDaysInFirstWeek))
        {
            return 1;
        }

        return (int)((abs - weekStart) / 7) + 1;
    }

    /// <summary>
    /// The absolute day of the first day of a week year.
    /// </summary>
    /// <param name="weekYear">the week year</param>
    /// <param name="firstDayOfWeek">the first day of the week</param>
    /// <param name="minimalDaysInFirstWeek">the days the first week of a year must have</param>
    /// <returns>the absolute day of the first day of the week year</returns>
    public static long GetWeekYearAbsoluteStart(int weekYear, int firstDayOfWeek, int minimalDaysInFirstWeek)
    {
        long first = AbsoluteDayFromYear(weekYear);
        int daysInFirstWeek = 8 - GetDayOfWeekFromAbsolute(first, firstDayOfWeek);
        long weekStart = first + daysInFirstWeek;
        if (daysInFirstWeek >= minimalDaysInFirstWeek)
        {
            weekStart -= 7;
        }

        return weekStart;
    }

    /// <summary>
    /// The week year of a date, for a calendar that says which day the week starts on and how many
    /// days the first week must have.
    /// </summary>
    /// <param name="dateValue">the date value</param>
    /// <param name="firstDayOfWeek">the first day of the week</param>
    /// <param name="minimalDaysInFirstWeek">the days the first week of a year must have</param>
    /// <returns>the week year</returns>
    public static int GetWeekYear(long dateValue, int firstDayOfWeek, int minimalDaysInFirstWeek)
    {
        long abs = AbsoluteDayFromDateValue(dateValue);
        int year = YearFromDateValue(dateValue);
        long weekStart = GetWeekYearAbsoluteStart(year, firstDayOfWeek, minimalDaysInFirstWeek);
        if (abs < weekStart)
        {
            return year - 1;
        }

        if (MonthFromDateValue(dateValue) == 12
            && 24 + minimalDaysInFirstWeek < DayFromDateValue(dateValue)
            && abs >= GetWeekYearAbsoluteStart(year + 1, firstDayOfWeek, minimalDaysInFirstWeek))
        {
            return year + 1;
        }

        return year;
    }

    /// <summary>
    /// The date value of the day a number of seconds from the epoch falls on, in the time zone that
    /// counts no offset at all.
    /// </summary>
    /// <param name="localSeconds">the seconds from the epoch</param>
    /// <returns>the date value</returns>
    public static long DateValueFromLocalSeconds(long localSeconds)
    {
        long absoluteDay = localSeconds / SecondsPerDay;

        // Rounded towards negative infinity, which is what a day of a negative number of seconds
        // means.
        if (localSeconds < 0 && absoluteDay * SecondsPerDay != localSeconds)
        {
            absoluteDay--;
        }

        return DateValueFromAbsoluteDay(absoluteDay);
    }

    /// <summary>
    /// The nanoseconds from midnight that a number of seconds from the epoch falls on.
    /// </summary>
    /// <param name="localSeconds">the seconds from the epoch</param>
    /// <returns>the nanoseconds from midnight</returns>
    public static long NanosFromLocalSeconds(long localSeconds)
    {
        localSeconds %= SecondsPerDay;
        if (localSeconds < 0)
        {
            localSeconds += SecondsPerDay;
        }

        return localSeconds * NanosPerSecond;
    }

    /// <summary>
    /// The time of day wrapped into a day, for a time that was added to or subtracted from.
    /// </summary>
    /// <param name="nanos">the nanoseconds</param>
    /// <returns>the nanoseconds within a day</returns>
    public static long NormalizeNanosOfDay(long nanos)
    {
        nanos %= NanosPerDay;
        if (nanos < 0)
        {
            nanos += NanosPerDay;
        }

        return nanos;
    }

    /// <summary>
    /// Writes a date as year, month and day.
    /// </summary>
    /// <param name="builder">where to write it</param>
    /// <param name="dateValue">the date value</param>
    /// <returns>the builder</returns>
    /// <remarks>
    /// A year of fewer than four digits is written with the zeros in front, and a year before the
    /// calendar with a minus sign, because a date is read back by this parser and by the user's eye
    /// and both of them need to know how many digits a year has.
    /// </remarks>
    public static StringBuilder AppendDate(StringBuilder builder, long dateValue)
    {
        int y = YearFromDateValue(dateValue);
        if (y is > -1_000 and < 1_000)
        {
            if (y < 0)
            {
                _ = builder.Append('-');
                y = -y;
            }

            StringUtils.AppendZeroPadded(builder, 4, y);
        }
        else
        {
            _ = StringUtils.AppendInvariant(builder, y);
        }

        AppendTwoDigits(_ = builder.Append('-'), MonthFromDateValue(dateValue)).Append('-');
        return AppendTwoDigits(builder, DayFromDateValue(dateValue));
    }

    /// <summary>
    /// Writes a time as hours, minutes and seconds, and the nanoseconds of the second if there are
    /// any.
    /// </summary>
    /// <param name="builder">where to write it</param>
    /// <param name="nanos">the nanoseconds from midnight</param>
    /// <returns>the builder</returns>
    /// <remarks>
    /// The division is written as the negative of both halves because the time may be
    /// <see cref="long.MinValue"/>, and there is no other way to divide that by a billion without an
    /// overflow. The comment in the reference says so and the transliteration keeps the shape of it
    /// rather than the shape of the arithmetic, because the arithmetic is the trap.
    /// </remarks>
    public static StringBuilder AppendTime(StringBuilder builder, long nanos)
    {
        if (nanos < 0)
        {
            _ = builder.Append('-');
            nanos = -nanos;
        }

        long s = -nanos / -1_000_000_000;
        nanos -= s * 1_000_000_000;
        int m = (int)(s / 60);
        s -= m * 60;
        int h = m / 60;
        m -= h * 60;
        AppendTwoDigits(builder, h).Append(':');
        AppendTwoDigits(builder, m).Append(':');
        AppendTwoDigits(builder, (int)s);
        return AppendNanos(builder, (int)nanos);
    }

    /// <summary>
    /// Writes the nanoseconds of a second, with only the digits that mean something.
    /// </summary>
    /// <param name="builder">where to write it</param>
    /// <param name="nanos">the nanoseconds of a second</param>
    /// <returns>the builder</returns>
    /// <remarks>
    /// The trailing zeros are divided out in two steps of three and then one of one, which is what
    /// the reference does and is why 500 milliseconds are written <c>.5</c> and not <c>.500</c>.
    /// </remarks>
    public static StringBuilder AppendNanos(StringBuilder builder, int nanos)
    {
        if (nanos > 0)
        {
            _ = builder.Append('.');
            for (int i = 1; nanos < FractionalSecondsTable[i]; i++)
            {
                _ = builder.Append('0');
            }

            if (nanos % 1_000 == 0)
            {
                nanos /= 1_000;
                if (nanos % 1_000 == 0)
                {
                    nanos /= 1_000;
                }
            }

            if (nanos % 10 == 0)
            {
                nanos /= 10;
                if (nanos % 10 == 0)
                {
                    nanos /= 10;
                }
            }

            _ = StringUtils.AppendInvariant(builder, nanos);
        }

        return builder;
    }

    /// <summary>
    /// Cuts the precision of a time to a number of decimal places of a second, rounding to the
    /// nearest.
    /// </summary>
    /// <param name="nanosOfDay">the nanoseconds from midnight</param>
    /// <param name="scale">the decimal places of a second to keep, 0 to 9</param>
    /// <param name="range">the largest value that may be produced</param>
    /// <returns>the time at that precision</returns>
    public static long ConvertScale(long nanosOfDay, int scale, long range)
    {
        if (scale >= 9)
        {
            return nanosOfDay;
        }

        int m = FractionalSecondsTable[scale];
        long mod = nanosOfDay % m;
        if (mod >= m >>> 1)
        {
            nanosOfDay += m;
        }

        long r = nanosOfDay - mod;
        if (r >= range)
        {
            r = range - m;
        }

        return r;
    }

    /// <summary>
    /// Appends a number of two places, or of more if the number is bigger.
    /// </summary>
    /// <param name="builder">where to write it</param>
    /// <param name="i">the number</param>
    /// <returns>the builder</returns>
    internal static StringBuilder AppendTwoDigits(StringBuilder builder, int i)
        => i >= 0 && i < 100
            ? builder.Append((char)(i / 10 + '0')).Append((char)(i % 10 + '0'))
            : StringUtils.AppendInvariant(builder, i);

    /// <summary>
    /// Reads a timestamp written as a date and a time, and answers the value it stands for.
    /// </summary>
    /// <remarks>
    /// The date and the time are separated by a space or, for the ISO shape, by a T. A text with a
    /// time zone in it is read as one: an offset, Z, or a named <see cref="TimeZoneProvider"/>. The
    /// same is true of DB2's shape, where a dash separates them, because that is a flag of the
    /// compatibility mode and this class carries only the five flags the tokenizer reads.
    /// </remarks>
    /// <param name="s">the text</param>
    /// <param name="provider">where the mode comes from</param>
    /// <param name="withTimeZone">whether the result should carry a zone</param>
    /// <returns>the timestamp</returns>
    /// <exception cref="ArgumentException">if the text is not a timestamp</exception>
    /// <exception cref="NotSupportedException">if a zone is asked for or written</exception>
    public static Values.Value ParseTimestamp(string s, Engine.CastDataProvider? provider, bool withTimeZone)
    {
        int dateEnd = s.IndexOf(' ');
        if (dateEnd < 0)
        {
            dateEnd = s.IndexOf('T');
        }

        int timeStart;
        if (dateEnd < 0)
        {
            dateEnd = s.Length;
            timeStart = -1;
        }
        else
        {
            timeStart = dateEnd + 1;
        }

        long dateValue = ParseDateValue(s, 0, dateEnd);
        long nanos = 0;
        if (timeStart >= 0)
        {
            dateEnd++;
            int timeEnd = FindTimeZone(s, dateEnd, out bool hasTimeZone);
            if (hasTimeZone)
            {
                return ParseTimestampWithTimeZone(s, dateEnd, timeEnd, dateValue, provider);
            }

            nanos = ParseTimeNanos(s, dateEnd, timeEnd);
        }

        if (withTimeZone)
        {
            throw new NotSupportedException(
                "a timestamp without a time zone written as one that has it");
        }

        return Values.ValueTimestamp.FromDateValueAndNanos(dateValue, nanos);
    }

    /// <summary>
    /// Reads a time written with an optional time zone, and answers the value it stands for.
    /// </summary>
    /// <param name="s">the text</param>
    /// <param name="provider">where the mode comes from</param>
    /// <param name="withTimeZone">whether the result should carry a zone</param>
    /// <returns>the time</returns>
    /// <exception cref="ArgumentException">if the text is not a time</exception>
    /// <exception cref="NotSupportedException">if a zone is asked for or written</exception>
    public static Values.Value ParseTime(string s, Engine.CastDataProvider? provider, bool withTimeZone)
    {
        int timeEnd = FindTimeZone(s, 0, out bool hasTimeZone);
        if (hasTimeZone)
        {
            if (!withTimeZone)
            {
                throw new NotSupportedException(
                    "a time with a time zone written as one that has not got it");
            }

            long zoned = ParseTimeNanos(s, 0, timeEnd);
            return Values.ValueTimeTimeZone.FromNanos(zoned, ParseTimeZoneOffset(s, timeEnd));
        }

        if (withTimeZone)
        {
            throw new NotSupportedException(
                "a time without a time zone written as one that has it");
        }

        long nanos = ParseTimeNanos(s, 0, timeEnd);
        return Values.ValueTime.FromNanos(nanos);
    }

    /// <summary>
    /// Where the time ends and whether there is a time zone after it.
    /// </summary>
    /// <remarks>
    /// A zone is a Z at the end, an offset beginning with a plus or a minus, or a name after a
    /// space. The search starts at <paramref name="start"/> rather than at the beginning because a
    /// timestamp has a date in front of it, and a dash inside the date must not be read as the sign
    /// of an offset.
    /// </remarks>
    private static int FindTimeZone(string s, int start, out bool hasTimeZone)
    {
        hasTimeZone = false;
        if (s.EndsWith("Z", StringComparison.Ordinal))
        {
            hasTimeZone = true;
            return s.Length - 1;
        }

        int timeZoneStart = s.IndexOf('+', start);
        if (timeZoneStart < 0)
        {
            timeZoneStart = s.IndexOf('-', start);
        }

        if (timeZoneStart >= 0)
        {
            hasTimeZone = true;
            return timeZoneStart > 0 && s[timeZoneStart - 1] == ' ' ? timeZoneStart - 1 : timeZoneStart;
        }

        timeZoneStart = s.IndexOf(' ', start);
        if (timeZoneStart > 0)
        {
            hasTimeZone = true;
            return timeZoneStart;
        }

        return s.Length;
    }

    /// <summary>
    /// Writes a time zone offset after a time, as <c>+01</c>, <c>+01:30</c> or <c>+01:30:45</c>.
    /// </summary>
    /// <remarks>
    /// The minutes and the seconds are written only when they are not zero, so an offset of whole
    /// hours has two digits and not six. That is the reference's own rule and it is what a user
    /// expects to read back; an offset written with zeros in it would parse to the same value and
    /// print differently, which would make every round trip a difference.
    /// </remarks>
    /// <param name="builder">where to write it</param>
    /// <param name="tz">the offset in seconds</param>
    /// <returns>the builder</returns>
    public static StringBuilder AppendTimeZone(StringBuilder builder, int tz)
    {
        if (tz < 0)
        {
            _ = builder.Append('-');
            tz = -tz;
        }
        else
        {
            _ = builder.Append('+');
        }

        int rem = tz / 3_600;
        StringUtils.AppendTwoDigits(builder, rem);
        tz -= rem * 3_600;
        if (tz != 0)
        {
            rem = tz / 60;
            StringUtils.AppendTwoDigits(_ = builder.Append(':'), rem);
            tz -= rem * 60;
            if (tz != 0)
            {
                StringUtils.AppendTwoDigits(_ = builder.Append(':'), tz);
            }
        }

        return builder;
    }

    /// <summary>
    /// The name of a time zone that is nothing but this offset, as a zone would be written.
    /// </summary>
    /// <remarks>
    /// The seconds part is written only when it is not zero, which is what makes the name of a
    /// whole-minute offset the short one (<c>GMT+03:00</c>) and the name of an offset with
    /// seconds the long one. A reader parses both back to the same offset, so neither spelling
    /// is wrong; they are just not the same text, and the text is what a zone is named by.
    /// </remarks>
    /// <param name="offsetSeconds">the offset in seconds</param>
    /// <returns>the zone name</returns>
    public static string TimeZoneNameFromOffsetSeconds(int offsetSeconds)
    {
        if (offsetSeconds == 0)
        {
            return "UTC";
        }

        StringBuilder builder = new(12);
        _ = builder.Append("GMT");
        if (offsetSeconds < 0)
        {
            _ = builder.Append('-');
            offsetSeconds = -offsetSeconds;
        }
        else
        {
            _ = builder.Append('+');
        }

        StringUtils.AppendTwoDigits(builder, offsetSeconds / 3_600);
        offsetSeconds %= 3_600;
        _ = builder.Append(':');
        StringUtils.AppendTwoDigits(builder, offsetSeconds / 60);
        offsetSeconds %= 60;
        if (offsetSeconds != 0)
        {
            _ = builder.Append(':');
            StringUtils.AppendTwoDigits(builder, offsetSeconds);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Seconds since EPOCH of a moment that is written as a local date and time and a zone
    /// offset.
    /// </summary>
    /// <param name="dateValue">the date value</param>
    /// <param name="timeNanos">nanoseconds since midnight, in the local time</param>
    /// <param name="offsetSeconds">the zone offset in seconds</param>
    /// <returns>seconds since EPOCH, in UTC</returns>
    public static long GetEpochSeconds(long dateValue, long timeNanos, int offsetSeconds)
        => (AbsoluteDayFromDateValue(dateValue) * SecondsPerDay)
        + (timeNanos / NanosPerSecond)
        - offsetSeconds;

    /// <summary>
    /// Extracts date value and nanos of day from the specified value.
    /// </summary>
    /// <param name="value">value to extract fields from</param>
    /// <param name="provider">the cast information provider</param>
    /// <returns>array with date value and nanos of day</returns>
    public static long[] DateAndTimeFromValue(Values.Value value, Engine.CastDataProvider? provider)
    {
        long dateValue = EpochDateValue;
        long timeNanos = 0;
        if (value is Values.ValueTimestamp timestamp)
        {
            dateValue = timestamp.GetDateValue();
            timeNanos = timestamp.GetTimeNanos();
        }
        else if (value is Values.ValueDate date)
        {
            dateValue = date.GetDateValue();
        }
        else if (value is Values.ValueTime time)
        {
            timeNanos = time.GetNanos();
        }
        else if (value is Values.ValueTimestampTimeZone timestampTimeZone)
        {
            dateValue = timestampTimeZone.GetDateValue();
            timeNanos = timestampTimeZone.GetTimeNanos();
        }
        else if (value is Values.ValueTimeTimeZone timeTimeZone)
        {
            timeNanos = timeTimeZone.GetNanos();
        }
        else
        {
            Values.ValueTimestamp converted = (Values.ValueTimestamp)value.ConvertToForComparison(
                Values.Value.Timestamp, provider);
            dateValue = converted.GetDateValue();
            timeNanos = converted.GetTimeNanos();
        }

        return [dateValue, timeNanos];
    }

    /// <summary>
    /// Creates a new date-time value with the same type as original value. If original value is a
    /// ValueTimestampTimeZone or ValueTimeTimeZone, returned value will have the same time zone
    /// offset as original value.
    /// </summary>
    /// <param name="original">original value</param>
    /// <param name="dateValue">date value for the returned value</param>
    /// <param name="timeNanos">nanos of day for the returned value</param>
    /// <returns>new value with specified date value and nanos of day</returns>
    public static Values.Value DateTimeToValue(Values.Value original, long dateValue, long timeNanos)
    {
        switch (original.GetValueType())
        {
            case Values.Value.Date:
                return Values.ValueDate.FromDateValue(dateValue);
            case Values.Value.Time:
                return Values.ValueTime.FromNanos(timeNanos);
            case Values.Value.TimeTz:
                return Values.ValueTimeTimeZone.FromNanos(
                    timeNanos, ((Values.ValueTimeTimeZone)original).GetTimeZoneOffsetSeconds());
            case Values.Value.TimestampTz:
                return Values.ValueTimestampTimeZone.FromDateValueAndNanos(
                    dateValue, timeNanos,
                    ((Values.ValueTimestampTimeZone)original).GetTimeZoneOffsetSeconds());
            case Values.Value.Timestamp:
            default:
                return Values.ValueTimestamp.FromDateValueAndNanos(dateValue, timeNanos);
        }
    }

    /// <summary>
    /// Reads the offset written after a time, and answers the value it stands for.
    /// </summary>
    /// <summary>
    /// Moves a timestamp with time zone to a new time zone.
    /// </summary>
    /// <param name="dateValue">the date value</param>
    /// <param name="timeNanos">the nanoseconds since midnight</param>
    /// <param name="oldOffset">the old offset</param>
    /// <param name="newOffset">the new offset</param>
    /// <returns>the timestamp with time zone with the new offset</returns>
    public static Values.ValueTimestampTimeZone TimestampTimeZoneAtOffset(
        long dateValue, long timeNanos, int oldOffset, int newOffset)
    {
        timeNanos += (newOffset - oldOffset) * NanosPerSecond;
        if (timeNanos < 0)
        {
            timeNanos += NanosPerDay;
            dateValue = DecrementDateValue(dateValue);
            if (timeNanos < 0)
            {
                timeNanos += NanosPerDay;
                dateValue = DecrementDateValue(dateValue);
            }
        }
        else if (timeNanos >= NanosPerDay)
        {
            timeNanos -= NanosPerDay;
            dateValue = IncrementDateValue(dateValue);
            if (timeNanos >= NanosPerDay)
            {
                timeNanos -= NanosPerDay;
                dateValue = IncrementDateValue(dateValue);
            }
        }

        return Values.ValueTimestampTimeZone.FromDateValueAndNanos(dateValue, timeNanos, newOffset);
    }

    private static Values.Value ParseTimestampWithTimeZone(
        string s, int dateEnd, int timeEnd, long dateValue, Engine.CastDataProvider? provider)
    {
        long nanos = ParseTimeNanos(s, dateEnd, timeEnd);
        ReadOnlySpan<char> zone = s.AsSpan(timeEnd).Trim();
        int offset;
        if (zone.Length > 1 && zone[0] is not ('Z' or 'z' or '+' or '-'))
        {
            TimeZoneProvider timeZone = TimeZoneProvider.OfId(zone.ToString());
            offset = timeZone.GetTimeZoneOffsetUtc(
                timeZone.GetEpochSecondsFromLocal(dateValue, nanos));
        }
        else
        {
            offset = ParseTimeZoneOffset(s, timeEnd);
        }

        return Values.ValueTimestampTimeZone.FromDateValueAndNanos(dateValue, nanos, offset);
    }

    /// <summary>
    /// The offset in seconds that is written from <paramref name="start"/> to the end of the text.
    /// </summary>
    /// <remarks>
    /// Three shapes are written: a Z for Greenwich, a signed number of hours with minutes and
    /// seconds after it, and a name after a space. Only the first two are read here: a name is a
    /// zone of the operating system's database, which is not something a library should be deciding
    /// for a literal, and the reference answers a name with the zone the session is in.
    /// </remarks>
    private static int ParseTimeZoneOffset(string s, int start)
    {
        // No substring and no split: the zone is sliced where it stands, and at most two
        // colons are ever looked for. A third colon is more than three parts, which is the
        // shape the split refused.
        ReadOnlySpan<char> zone = s.AsSpan(start).Trim();
        if (zone.Length == 0 || (zone.Length == 1 && zone[0] is 'Z' or 'z'))
        {
            return 0;
        }

        if (zone[0] is not ('+' or '-'))
        {
            throw new ArgumentException(s, nameof(s));
        }

        bool negative = zone[0] == '-';
        ReadOnlySpan<char> digits = zone[1..];
        int first = digits.IndexOf(':');
        ReadOnlySpan<char> hoursPart = first < 0 ? digits : digits[..first];
        ReadOnlySpan<char> rest = first < 0 ? [] : digits[(first + 1)..];
        int second = rest.IndexOf(':');
        ReadOnlySpan<char> minutesPart = second < 0 ? rest : rest[..second];
        ReadOnlySpan<char> secondsPart = second < 0 ? [] : rest[(second + 1)..];
        if (secondsPart.IndexOf(':') >= 0)
        {
            throw new ArgumentException(s, nameof(s));
        }

        int hours = StringUtils.ParseUInt31(hoursPart);
        int minutes = first < 0 ? 0 : StringUtils.ParseUInt31(minutesPart);
        int seconds = second < 0 ? 0 : StringUtils.ParseUInt31(secondsPart);
        if (minutes > 59 || seconds > 59)
        {
            throw new ArgumentException(s, nameof(s));
        }

        int offset = (hours * 3_600) + (minutes * 60) + seconds;
        if (hours > 18 || (hours == 18 && (minutes > 0 || seconds > 0)))
        {
            throw new ArgumentException(s, nameof(s));
        }

        return negative ? -offset : offset;
    }
}
