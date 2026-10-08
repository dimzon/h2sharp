/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.api.Interval.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Api;

/// <summary>
/// An interval as the API exposes it: the same two numbers and a qualifier as
/// <see cref="H2Sharp.Values.ValueInterval"/>, built from the fields a caller has rather than from
/// text.
/// </summary>
/// <remarks>
/// Every factory here takes the fields a person thinks in — years and months, days and hours,
/// hours and minutes and seconds and nanoseconds — and puts them into the two numbers the value is.
/// Each one insists that the fields it is given have the same sign, because an interval of one day
/// and minus one hour is not an interval: it is an interval of twenty-three hours with a different
/// qualifier, and writing it the other way round would give a value whose text and whose number
/// disagree.
/// </remarks>
public sealed class Interval
{
    private readonly IntervalQualifier qualifier;

    private readonly bool negative;

    private readonly long leading;

    private readonly long remaining;

    /// <summary>
    /// Creates a new interval. Prefer the factories: this one takes the two numbers the value is
    /// made of rather than the fields a caller has.
    /// </summary>
    /// <param name="qualifier">the qualifier</param>
    /// <param name="negative">whether the interval is negative</param>
    /// <param name="leading">the value of the leading field</param>
    /// <param name="remaining">the combined value of the remaining fields</param>
    /// <exception cref="ArgumentException">
    /// if the two numbers are not a valid interval of this qualifier, which the reference reports
    /// as a plain argument error rather than as a database error: a caller of the API has made a
    /// mistake in Java code, and no statement is involved
    /// </exception>
    public Interval(IntervalQualifier qualifier, bool negative, long leading, long remaining)
    {
        this.qualifier = qualifier;
        try
        {
            this.negative = IntervalUtils.ValidateInterval(qualifier, negative, leading, remaining);
        }
        catch (H2SharpException)
        {
            throw new ArgumentException();
        }

        this.leading = leading;
        this.remaining = remaining;
    }

    /// <summary>
    /// The qualifier of this interval.
    /// </summary>
    /// <returns>the qualifier</returns>
    public IntervalQualifier GetQualifier() => qualifier;

    /// <summary>
    /// Whether the interval is negative.
    /// </summary>
    /// <returns>true if it is negative</returns>
    public bool IsNegative() => negative;

    /// <summary>
    /// The value of the leading field, which for an interval of seconds is the whole seconds.
    /// </summary>
    /// <returns>the leading field</returns>
    public long GetLeading() => leading;

    /// <summary>
    /// The combined value of the remaining fields, which for an interval of seconds is the
    /// nanoseconds of that second.
    /// </summary>
    /// <returns>the remaining fields</returns>
    public long GetRemaining() => remaining;

    /// <summary>
    /// The years, if the qualifier has any.
    /// </summary>
    /// <returns>the years</returns>
    public long GetYears() => IntervalUtils.YearsFromInterval(qualifier, negative, leading, remaining);

    /// <summary>
    /// The months, if the qualifier has any.
    /// </summary>
    /// <returns>the months</returns>
    public long GetMonths() => IntervalUtils.MonthsFromInterval(qualifier, negative, leading, remaining);

    /// <summary>
    /// The days, if the qualifier has any.
    /// </summary>
    /// <returns>the days</returns>
    public long GetDays() => IntervalUtils.DaysFromInterval(qualifier, negative, leading, remaining);

    /// <summary>
    /// The hours, if the qualifier has any.
    /// </summary>
    /// <returns>the hours</returns>
    public long GetHours() => IntervalUtils.HoursFromInterval(qualifier, negative, leading, remaining);

    /// <summary>
    /// The minutes, if the qualifier has any.
    /// </summary>
    /// <returns>the minutes</returns>
    public long GetMinutes() => IntervalUtils.MinutesFromInterval(qualifier, negative, leading, remaining);

    /// <summary>
    /// The whole seconds, if the qualifier has any.
    /// </summary>
    /// <returns>the seconds</returns>
    public long GetSeconds()
        => qualifier == IntervalQualifier.Second
            ? (negative ? -leading : leading)
            : GetSecondsAndNanos() / DateTimeUtils.NanosPerSecond;

    /// <summary>
    /// The nanoseconds of the second, if the qualifier has any.
    /// </summary>
    /// <returns>the nanoseconds</returns>
    public long GetNanosOfSecond()
        => qualifier == IntervalQualifier.Second
            ? (negative ? -remaining : remaining)
            : GetSecondsAndNanos() % DateTimeUtils.NanosPerSecond;

    /// <summary>
    /// The seconds and the nanoseconds of the second in one number of nanoseconds.
    /// </summary>
    /// <remarks>
    /// This cannot hold every value an INTERVAL SECOND can be, so a very large one of those is to be
    /// asked with <see cref="GetSeconds"/> and <see cref="GetNanosOfSecond"/> instead. For the other
    /// qualifiers it always can.
    /// </remarks>
    /// <returns>the nanoseconds, seconds included</returns>
    public long GetSecondsAndNanos() => IntervalUtils.NanosFromInterval(qualifier, negative, leading, remaining);

    /// <summary>
    /// An INTERVAL YEAR.
    /// </summary>
    /// <param name="years">the years, |years| &lt; 10^18</param>
    /// <returns>the interval</returns>
    public static Interval OfYears(long years) => new(IntervalQualifier.Year, years < 0, Abs(years), 0);

    /// <summary>
    /// An INTERVAL MONTH.
    /// </summary>
    /// <param name="months">the months, |months| &lt; 10^18</param>
    /// <returns>the interval</returns>
    public static Interval OfMonths(long months) => new(IntervalQualifier.Month, months < 0, Abs(months), 0);

    /// <summary>
    /// An INTERVAL DAY.
    /// </summary>
    /// <param name="days">the days, |days| &lt; 10^18</param>
    /// <returns>the interval</returns>
    public static Interval OfDays(long days) => new(IntervalQualifier.Day, days < 0, Abs(days), 0);

    /// <summary>
    /// An INTERVAL HOUR.
    /// </summary>
    /// <param name="hours">the hours, |hours| &lt; 10^18</param>
    /// <returns>the interval</returns>
    public static Interval OfHours(long hours) => new(IntervalQualifier.Hour, hours < 0, Abs(hours), 0);

    /// <summary>
    /// An INTERVAL MINUTE.
    /// </summary>
    /// <param name="minutes">the minutes, |minutes| &lt; 10^18</param>
    /// <returns>the interval</returns>
    public static Interval OfMinutes(long minutes)
        => new(IntervalQualifier.Minute, minutes < 0, Abs(minutes), 0);

    /// <summary>
    /// An INTERVAL SECOND.
    /// </summary>
    /// <param name="seconds">the seconds, |seconds| &lt; 10^18</param>
    /// <param name="nanos">the nanoseconds of the second, |nanos| &lt; 10^9</param>
    /// <returns>the interval</returns>
    public static Interval OfSeconds(long seconds, int nanos)
    {
        // The two arguments must have the same sign, and the check is the same one the two-field
        // factories make: an interval of one second and minus a nanosecond is not an interval.
        bool negative = ((seconds) | (long)(nanos)) < 0;
        if (negative)
        {
            if (seconds > 0 || nanos > 0)
            {
                throw new ArgumentException();
            }

            seconds = -seconds;
            nanos = -nanos;
            if (nanos < 0)
            {
                throw new ArgumentException();
            }
        }

        return new Interval(IntervalQualifier.Second, negative, seconds, nanos);
    }

    /// <summary>
    /// An INTERVAL SECOND with no nanoseconds.
    /// </summary>
    /// <param name="seconds">the seconds</param>
    /// <returns>the interval</returns>
    public static Interval OfSeconds(long seconds) => OfSeconds(seconds, 0);

    /// <summary>
    /// An INTERVAL SECOND given in nanoseconds.
    /// </summary>
    /// <param name="nanos">the nanoseconds</param>
    /// <returns>the interval</returns>
    public static Interval OfNanos(long nanos) => OfSeconds(nanos / DateTimeUtils.NanosPerSecond,
        (int)(nanos % DateTimeUtils.NanosPerSecond));

    /// <summary>
    /// An INTERVAL YEAR TO MONTH.
    /// </summary>
    /// <param name="years">the years</param>
    /// <param name="months">the months, |months| &lt; 12</param>
    /// <returns>the interval</returns>
    public static Interval OfYearsMonths(long years, int months)
    {
        bool negative = ((years) | (long)(months)) < 0;
        if (negative)
        {
            if (years > 0 || months > 0)
            {
                throw new ArgumentException(nameof(months));
            }

            years = -years;
            months = -months;
        }

        return new Interval(IntervalQualifier.YearToMonth, negative, years, months);
    }

    /// <summary>
    /// An INTERVAL DAY TO HOUR.
    /// </summary>
    /// <param name="days">the days</param>
    /// <param name="hours">the hours, |hours| &lt; 24</param>
    /// <returns>the interval</returns>
    public static Interval OfDaysHours(long days, int hours)
    {
        bool negative = ((days) | (long)(hours)) < 0;
        if (negative)
        {
            if (days > 0 || hours > 0)
            {
                throw new ArgumentException();
            }

            days = -days;
            hours = -hours;
        }

        return new Interval(IntervalQualifier.DayToHour, negative, days, hours);
    }

    /// <summary>
    /// An INTERVAL DAY TO MINUTE.
    /// </summary>
    /// <param name="days">the days</param>
    /// <param name="hours">the hours, |hours| &lt; 24</param>
    /// <param name="minutes">the minutes, |minutes| &lt; 60</param>
    /// <returns>the interval</returns>
    public static Interval OfDaysHoursMinutes(long days, int hours, int minutes)
    {
        bool negative = ((days) | (long)(hours) | (long)(minutes)) < 0;
        if (negative)
        {
            if (days > 0 || hours > 0 || minutes > 0)
            {
                throw new ArgumentException();
            }

            days = -days;
            hours = -hours;
            minutes = -minutes;
            if (((long)hours | (long)minutes) < 0)
            {
                throw new ArgumentException();
            }
        }

        if (minutes >= 60)
        {
            throw new ArgumentException();
        }

        return new Interval(
            IntervalQualifier.DayToMinute, negative, days, (hours * 60L) + minutes);
    }

    /// <summary>
    /// An INTERVAL DAY TO SECOND.
    /// </summary>
    /// <param name="days">the days</param>
    /// <param name="hours">the hours</param>
    /// <param name="minutes">the minutes</param>
    /// <param name="seconds">the seconds, |seconds| &lt; 60</param>
    /// <returns>the interval</returns>
    public static Interval OfDaysHoursMinutesSeconds(long days, int hours, int minutes, int seconds)
        => OfDaysHoursMinutesNanos(days, hours, minutes, seconds * DateTimeUtils.NanosPerSecond);

    /// <summary>
    /// An INTERVAL DAY TO SECOND, given the last field in nanoseconds.
    /// </summary>
    /// <param name="days">the days</param>
    /// <param name="hours">the hours</param>
    /// <param name="minutes">the minutes</param>
    /// <param name="nanos">the nanoseconds of the second, |nanos| &lt; 60,000,000,000</param>
    /// <returns>the interval</returns>
    public static Interval OfDaysHoursMinutesNanos(long days, int hours, int minutes, long nanos)
    {
        bool negative = ((days) | (long)(hours) | (long)(minutes) | (long)(nanos)) < 0;
        if (negative)
        {
            if (days > 0 || hours > 0 || minutes > 0 || nanos > 0)
            {
                throw new ArgumentException();
            }

            days = -days;
            hours = -hours;
            minutes = -minutes;
            nanos = -nanos;
            if (((long)hours | (long)minutes | (long)nanos) < 0)
            {
                throw new ArgumentException();
            }
        }

        if (minutes >= 60 || nanos >= DateTimeUtils.NanosPerMinute)
        {
            throw new ArgumentException();
        }

        return new Interval(
            IntervalQualifier.DayToSecond,
            negative,
            days,
            ((hours * 60L) + minutes) * DateTimeUtils.NanosPerMinute + nanos);
    }

    /// <summary>
    /// An INTERVAL HOUR TO MINUTE.
    /// </summary>
    /// <param name="hours">the hours</param>
    /// <param name="minutes">the minutes, |minutes| &lt; 60</param>
    /// <returns>the interval</returns>
    public static Interval OfHoursMinutes(long hours, int minutes)
    {
        bool negative = ((hours) | (long)(minutes)) < 0;
        if (negative)
        {
            if (hours > 0 || minutes > 0)
            {
                throw new ArgumentException();
            }

            hours = -hours;
            minutes = -minutes;
        }

        return new Interval(IntervalQualifier.HourToMinute, negative, hours, minutes);
    }

    /// <summary>
    /// An INTERVAL HOUR TO SECOND.
    /// </summary>
    /// <param name="hours">the hours</param>
    /// <param name="minutes">the minutes</param>
    /// <param name="seconds">the seconds, |seconds| &lt; 60</param>
    /// <returns>the interval</returns>
    public static Interval OfHoursMinutesSeconds(long hours, int minutes, int seconds)
        => OfHoursMinutesNanos(hours, minutes, seconds * DateTimeUtils.NanosPerSecond);

    /// <summary>
    /// An INTERVAL HOUR TO SECOND, given the last field in nanoseconds.
    /// </summary>
    /// <param name="hours">the hours</param>
    /// <param name="minutes">the minutes</param>
    /// <param name="nanos">the nanoseconds of the second</param>
    /// <returns>the interval</returns>
    public static Interval OfHoursMinutesNanos(long hours, int minutes, long nanos)
    {
        bool negative = ((hours) | (long)(minutes) | (long)(nanos)) < 0;
        if (negative)
        {
            if (hours > 0 || minutes > 0 || nanos > 0)
            {
                throw new ArgumentException();
            }

            hours = -hours;
            minutes = -minutes;
            nanos = -nanos;
            if (((long)minutes | (long)nanos) < 0)
            {
                throw new ArgumentException();
            }
        }

        if (nanos >= DateTimeUtils.NanosPerMinute)
        {
            throw new ArgumentException();
        }

        return new Interval(
            IntervalQualifier.HourToSecond, negative, hours, (minutes * DateTimeUtils.NanosPerMinute) + nanos);
    }

    /// <summary>
    /// An INTERVAL MINUTE TO SECOND.
    /// </summary>
    /// <param name="minutes">the minutes</param>
    /// <param name="seconds">the seconds, |seconds| &lt; 60</param>
    /// <returns>the interval</returns>
    public static Interval OfMinutesSeconds(long minutes, int seconds)
        => OfMinutesNanos(minutes, seconds * DateTimeUtils.NanosPerSecond);

    /// <summary>
    /// An INTERVAL MINUTE TO SECOND, given the last field in nanoseconds.
    /// </summary>
    /// <param name="minutes">the minutes</param>
    /// <param name="nanos">the nanoseconds of the second</param>
    /// <returns>the interval</returns>
    public static Interval OfMinutesNanos(long minutes, long nanos)
    {
        bool negative = ((minutes) | (long)(nanos)) < 0;
        if (negative)
        {
            if (minutes > 0 || nanos > 0)
            {
                throw new ArgumentException();
            }

            minutes = -minutes;
            nanos = -nanos;
        }

        return new Interval(IntervalQualifier.MinuteToSecond, negative, minutes, nanos);
    }

    /// <remarks>
    /// The absolute value, which for the smallest long is that same value rather than an error:
    /// the reference takes it this way and the interval is refused by its validation instead.
    /// </remarks>
    private static long Abs(long value) => value < 0 ? -value : value;

    /// <inheritdoc/>
    public override bool Equals(object? other)
        => ReferenceEquals(this, other)
            || (other is Interval interval
                && qualifier == interval.qualifier
                && negative == interval.negative
                && leading == interval.leading
                && remaining == interval.remaining);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(qualifier, negative, leading, remaining);

    /// <inheritdoc/>
    public override string ToString()
        => IntervalUtils.AppendInterval(new StringBuilder(), qualifier, negative, leading, remaining)
            .ToString();
}
