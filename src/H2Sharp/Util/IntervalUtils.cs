/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.util.IntervalUtils.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Util;

/// <summary>
/// The reading, the writing and the arithmetic of intervals.
/// </summary>
/// <remarks>
/// An interval here is not a number and not a duration: it is a *leading* field and the *rest* of
/// the fields put together, and which fields those are is what the qualifier says. INTERVAL '1' YEAR
/// has a leading field of years and nothing else; INTERVAL '1 2:3' DAY TO MINUTE has a leading field
/// of days and the rest as minutes since midnight; INTERVAL '1.5' SECOND has a leading field of
/// seconds and the rest as the nanoseconds of that second. The two halves are kept apart because the
/// SQL standard keeps them apart, and because a value of DAY TO SECOND cannot be a number of
/// nanoseconds without losing the day.
/// <para>
/// The absolute value of an interval is what the arithmetic is done on: months for a year-month
/// interval, nanoseconds for a day-time one. Converting back and forth is where the bounds live, and
/// they are the bounds of the leading field, which is why an interval of a thousand years and one of
/// a thousand months are not the same value.
/// </para>
/// </remarks>
public static class IntervalUtils
{
    /// <summary>
    /// The months in a year.
    /// </summary>
    public const int MonthsPerYear = 12;

    /// <summary>
    /// The hours in a day.
    /// </summary>
    public const int HoursPerDay = 24;

    /// <summary>
    /// The minutes in an hour.
    /// </summary>
    public const int MinutesPerHour = 60;

    /// <summary>
    /// The minutes in a day.
    /// </summary>
    public const int MinutesPerDay = HoursPerDay * MinutesPerHour;

    /// <summary>
    /// The nanoseconds in a day, as a big integer, for the conversions that must not overflow.
    /// </summary>
    public static readonly BigInteger NanosPerDayBi = new(DateTimeUtils.NanosPerDay);

    /// <summary>
    /// The nanoseconds in an hour, as a big integer.
    /// </summary>
    public static readonly BigInteger NanosPerHourBi = new(DateTimeUtils.NanosPerHour);

    /// <summary>
    /// The nanoseconds in a minute, as a big integer.
    /// </summary>
    public static readonly BigInteger NanosPerMinuteBi = new(DateTimeUtils.NanosPerMinute);

    /// <summary>
    /// The nanoseconds in a second, as a big integer.
    /// </summary>
    public static readonly BigInteger NanosPerSecondBi = new(DateTimeUtils.NanosPerSecond);

    /// <summary>
    /// The months in a year, as a big integer.
    /// </summary>
    public static readonly BigInteger MonthsPerYearBi = new(MonthsPerYear);

    /// <summary>
    /// The hours in a day, as a big integer.
    /// </summary>
    public static readonly BigInteger HoursPerDayBi = new(HoursPerDay);

    /// <summary>
    /// The minutes in an hour, as a big integer.
    /// </summary>
    public static readonly BigInteger MinutesPerHourBi = new(MinutesPerHour);

    /// <summary>
    /// The minutes in a day, as a big integer.
    /// </summary>
    public static readonly BigInteger MinutesPerDayBi = new(MinutesPerDay);

    /// <summary>
    /// The largest the leading field may be, exclusive, and the largest it may be in an absolute
    /// value before it is refused.
    /// </summary>
    /// <remarks>
    /// The two are not the same number: a value may not have a leading field of a quintillion, and an
    /// absolute value outside nine hundred and ninety-nine quadrillion is refused before it is turned
    /// into a leading field at all. The second bound is the one the conversion from an absolute value
    /// checks, and it is smaller by one so that a leading field of exactly the bound still fits in a
    /// long after its sign has been taken away.
    /// </remarks>
    private const long LeadingBound = 1_000_000_000_000_000_000L;

    private static readonly BigInteger LeadingMax = new(999_999_999_999_999_999L);

    private static readonly BigInteger LeadingMin = new(-999_999_999_999_999_999L);

    /// <summary>
    /// Reads an interval written with the word INTERVAL and a qualifier in it.
    /// </summary>
    /// <remarks>
    /// The qualifier may be written after the value, as in <c>INTERVAL '1-2' YEAR TO MONTH</c>, and
    /// the word INTERVAL itself may be left out, in which case the qualifier the caller has already
    /// decided is used. Every word is matched without regard to case, and whitespace is allowed
    /// around each of them, because a formatter is allowed to write it that way and a parser that
    /// insists is a parser that refuses a statement the reference accepts.
    /// </remarks>
    /// <param name="qualifier">the qualifier the caller has, for a text without one</param>
    /// <param name="s">the text</param>
    /// <returns>the interval</returns>
    /// <exception cref="ArgumentException">if the text is not an interval</exception>
    public static ValueInterval ParseFormattedInterval(IntervalQualifier qualifier, string s)
    {
        int i = SkipWhitespace(s, 0);
        if (!RegionMatchesIgnoringCase(s, i, "INTERVAL"))
        {
            return ParseInterval(qualifier, false, s);
        }

        i = SkipWhitespace(s, i + 8);
        bool negative = false;
        char ch = s[i];
        if (ch == '-')
        {
            negative = true;
            i = SkipWhitespace(s, i + 1);
            ch = s[i];
        }
        else if (ch == '+')
        {
            i = SkipWhitespace(s, i + 1);
            ch = s[i];
        }

        if (ch != '\'')
        {
            throw new ArgumentException(s, nameof(s));
        }

        int start = ++i;
        int l = s.Length;
        for (; ; )
        {
            if (i == l)
            {
                throw new ArgumentException(s, nameof(s));
            }

            if (s[i] == '\'')
            {
                break;
            }

            i++;
        }

        string v = s[start..i];
        i = SkipWhitespace(s, i + 1);

        // The qualifiers are tried in one sequence and each one asks the same two questions: is this
        // the word, and is there nothing else after it. They are separate "if"s and not a chain of
        // "else if"s because a word may begin two of them, and which one it is depends on what
        // follows: MINUTE alone is a qualifier and MINUTE TO SECOND is another, and the text
        // "MINUTE TO SECOND" also begins with the word MINUTE.
        if (RegionMatchesIgnoringCase(s, i, "YEAR"))
        {
            i += 4;
            int j = SkipWhitespaceEnd(s, i);
            if (j == l)
            {
                return ParseInterval(IntervalQualifier.Year, negative, v);
            }

            if (j > i && RegionMatchesIgnoringCase(s, j, "TO"))
            {
                j += 2;
                i = SkipWhitespace(s, j);
                if (i > j && RegionMatchesIgnoringCase(s, i, "MONTH") && SkipWhitespaceEnd(s, i + 5) == l)
                {
                    return ParseInterval(IntervalQualifier.YearToMonth, negative, v);
                }
            }
        }
        else if (RegionMatchesIgnoringCase(s, i, "MONTH") && SkipWhitespaceEnd(s, i + 5) == l)
        {
            return ParseInterval(IntervalQualifier.Month, negative, v);
        }

        if (RegionMatchesIgnoringCase(s, i, "DAY"))
        {
            i += 3;
            int j = SkipWhitespaceEnd(s, i);
            if (j == l)
            {
                return ParseInterval(IntervalQualifier.Day, negative, v);
            }

            if (j > i && RegionMatchesIgnoringCase(s, j, "TO"))
            {
                j += 2;
                i = SkipWhitespace(s, j);
                if (i > j)
                {
                    if (RegionMatchesIgnoringCase(s, i, "HOUR") && SkipWhitespaceEnd(s, i + 4) == l)
                    {
                        return ParseInterval(IntervalQualifier.DayToHour, negative, v);
                    }

                    if (RegionMatchesIgnoringCase(s, i, "MINUTE") && SkipWhitespaceEnd(s, i + 6) == l)
                    {
                        return ParseInterval(IntervalQualifier.DayToMinute, negative, v);
                    }

                    if (RegionMatchesIgnoringCase(s, i, "SECOND") && SkipWhitespaceEnd(s, i + 6) == l)
                    {
                        return ParseInterval(IntervalQualifier.DayToSecond, negative, v);
                    }
                }
            }
        }

        if (RegionMatchesIgnoringCase(s, i, "HOUR"))
        {
            i += 4;
            int j = SkipWhitespaceEnd(s, i);
            if (j == l)
            {
                return ParseInterval(IntervalQualifier.Hour, negative, v);
            }

            if (j > i && RegionMatchesIgnoringCase(s, j, "TO"))
            {
                j += 2;
                i = SkipWhitespace(s, j);
                if (i > j)
                {
                    if (RegionMatchesIgnoringCase(s, i, "MINUTE") && SkipWhitespaceEnd(s, i + 6) == l)
                    {
                        return ParseInterval(IntervalQualifier.HourToMinute, negative, v);
                    }

                    if (RegionMatchesIgnoringCase(s, i, "SECOND") && SkipWhitespaceEnd(s, i + 6) == l)
                    {
                        return ParseInterval(IntervalQualifier.HourToSecond, negative, v);
                    }
                }
            }
        }

        if (RegionMatchesIgnoringCase(s, i, "MINUTE"))
        {
            i += 6;
            int j = SkipWhitespaceEnd(s, i);
            if (j == l)
            {
                return ParseInterval(IntervalQualifier.Minute, negative, v);
            }

            if (j > i && RegionMatchesIgnoringCase(s, j, "TO"))
            {
                j += 2;
                i = SkipWhitespace(s, j);
                if (i > j && RegionMatchesIgnoringCase(s, i, "SECOND")
                    && SkipWhitespaceEnd(s, i + 6) == l)
                {
                    return ParseInterval(IntervalQualifier.MinuteToSecond, negative, v);
                }
            }
        }

        if (RegionMatchesIgnoringCase(s, i, "SECOND") && SkipWhitespaceEnd(s, i + 6) == l)
        {
            return ParseInterval(IntervalQualifier.Second, negative, v);
        }
        throw new ArgumentException(s, nameof(s));
    }

    /// <summary>
    /// Reads the value of an interval, without the word INTERVAL and without the qualifier.
    /// </summary>
    /// <remarks>
    /// Which separators may appear and where is decided by the qualifier and by nothing else, and
    /// each of them is optional except the leading field: INTERVAL '1' DAY TO MONTH is not a shape
    /// anything has, and INTERVAL '1' DAY TO HOUR with no hour is a day.
    /// </remarks>
    /// <param name="qualifier">the qualifier</param>
    /// <param name="negative">whether the text was written with a minus in front of it</param>
    /// <param name="s">the text</param>
    /// <returns>the interval</returns>
    /// <exception cref="ArgumentException">if the text is not a value of this qualifier</exception>
    public static ValueInterval ParseInterval(IntervalQualifier qualifier, bool negative, string s)
    {
        long leading, remaining;
        switch (qualifier)
        {
            case IntervalQualifier.Year:
            case IntervalQualifier.Month:
            case IntervalQualifier.Day:
            case IntervalQualifier.Hour:
            case IntervalQualifier.Minute:
                leading = ParseIntervalLeading(s, 0, s.Length, negative);
                remaining = 0;
                break;

            case IntervalQualifier.Second:
                int dot = s.IndexOf('.');
                if (dot < 0)
                {
                    leading = ParseIntervalLeading(s, 0, s.Length, negative);
                    remaining = 0;
                }
                else
                {
                    leading = ParseIntervalLeading(s, 0, dot, negative);
                    remaining = DateTimeUtils.ParseNanos(s, dot + 1, s.Length);
                }

                break;

            case IntervalQualifier.YearToMonth:
                return ParseInterval2(qualifier, s, '-', 11, negative);

            case IntervalQualifier.DayToHour:
                return ParseInterval2(qualifier, s, ' ', 23, negative);

            case IntervalQualifier.DayToMinute:
                int space = s.IndexOf(' ');
                if (space < 0)
                {
                    leading = ParseIntervalLeading(s, 0, s.Length, negative);
                    remaining = 0;
                }
                else
                {
                    leading = ParseIntervalLeading(s, 0, space, negative);
                    int colon = s.IndexOf(':', space + 1);
                    remaining = colon < 0
                        ? ParseIntervalRemaining(s, space + 1, s.Length, 23) * 60
                        : (ParseIntervalRemaining(s, space + 1, colon, 23) * 60)
                            + ParseIntervalRemaining(s, colon + 1, s.Length, 59);
                }

                break;

            case IntervalQualifier.DayToSecond:
                int space2 = s.IndexOf(' ');
                if (space2 < 0)
                {
                    leading = ParseIntervalLeading(s, 0, s.Length, negative);
                    remaining = 0;
                }
                else
                {
                    leading = ParseIntervalLeading(s, 0, space2, negative);
                    int colon = s.IndexOf(':', space2 + 1);
                    if (colon < 0)
                    {
                        remaining = ParseIntervalRemaining(s, space2 + 1, s.Length, 23) * DateTimeUtils.NanosPerHour;
                    }
                    else
                    {
                        int secondColon = s.IndexOf(':', colon + 1);
                        remaining = secondColon < 0
                            ? (ParseIntervalRemaining(s, space2 + 1, colon, 23) * DateTimeUtils.NanosPerHour)
                                + (ParseIntervalRemaining(s, colon + 1, s.Length, 59) * DateTimeUtils.NanosPerMinute)
                            : (ParseIntervalRemaining(s, space2 + 1, colon, 23) * DateTimeUtils.NanosPerHour)
                                + (ParseIntervalRemaining(s, colon + 1, secondColon, 59) * DateTimeUtils.NanosPerMinute)
                                + ParseIntervalRemainingSeconds(s, secondColon + 1);
                    }
                }

                break;

            case IntervalQualifier.HourToMinute:
                return ParseInterval2(qualifier, s, ':', 59, negative);

            case IntervalQualifier.HourToSecond:
                int colon2 = s.IndexOf(':');
                if (colon2 < 0)
                {
                    leading = ParseIntervalLeading(s, 0, s.Length, negative);
                    remaining = 0;
                }
                else
                {
                    leading = ParseIntervalLeading(s, 0, colon2, negative);
                    int colon3 = s.IndexOf(':', colon2 + 1);
                    remaining = colon3 < 0
                        ? ParseIntervalRemaining(s, colon2 + 1, s.Length, 59) * DateTimeUtils.NanosPerMinute
                        : (ParseIntervalRemaining(s, colon2 + 1, colon3, 59) * DateTimeUtils.NanosPerMinute)
                            + ParseIntervalRemainingSeconds(s, colon3 + 1);
                }

                break;

            case IntervalQualifier.MinuteToSecond:
                // The remaining half of a MINUTE TO SECOND is seconds *and* nanoseconds, so it has
                // a point in it where the others do not, and it cannot go through the two-field
                // reader the YEAR TO MONTH and the HOUR TO MINUTE share.
                int minuteColon = s.IndexOf(':');
                if (minuteColon < 0)
                {
                    leading = ParseIntervalLeading(s, 0, s.Length, negative);
                    remaining = 0;
                }
                else
                {
                    leading = ParseIntervalLeading(s, 0, minuteColon, negative);
                    remaining = ParseIntervalRemainingSeconds(s, minuteColon + 1);
                }

                break;

            default:
                throw new ArgumentException(nameof(qualifier));
        }

        // A leading field written with its own sign is the same interval as one written with the
        // sign outside the quotes, and this is where the two are brought into agreement: the value
        // of the leading field is never negative in a value, and the sign is. A leading field of
        // zero with a sign is written as long.MinValue, and it becomes zero here, so that an
        // interval of nothing is not negative.
        negative = leading < 0;
        if (negative)
        {
            leading = leading != long.MinValue ? -leading : 0;
        }

        return ValueInterval.From(qualifier, negative, leading, remaining);
    }

    /// <summary>
    /// Writes an interval as the text of a literal, with the word INTERVAL and the qualifier in it.
    /// </summary>
    /// <param name="builder">where to write it</param>
    /// <param name="qualifier">the qualifier</param>
    /// <param name="negative">whether the interval is negative</param>
    /// <param name="leading">the value of the leading field</param>
    /// <param name="remaining">the value of the remaining fields</param>
    /// <returns>the builder</returns>
    /// <remarks>
    /// The separators are the ones the standard writes and not the ones that would be shortest:
    /// INTERVAL '1 02' DAY TO HOUR keeps the two digits of the hour, because an hour written as 2
    /// reads as a different shape and a formatter that writes the shortest thing writes a statement
    /// that is not the statement it was asked about.
    /// </remarks>
    public static StringBuilder AppendInterval(
        StringBuilder builder, IntervalQualifier qualifier, bool negative, long leading, long remaining)
    {
        _ = builder.Append("INTERVAL '");
        if (negative)
        {
            _ = builder.Append('-');
        }

        switch (qualifier)
        {
            case IntervalQualifier.Year:
            case IntervalQualifier.Month:
            case IntervalQualifier.Day:
            case IntervalQualifier.Hour:
            case IntervalQualifier.Minute:
                _ = builder.Append(leading.ToString(CultureInfo.InvariantCulture));
                break;

            case IntervalQualifier.Second:
                DateTimeUtils.AppendNanos(
                    _ = builder.Append(leading.ToString(CultureInfo.InvariantCulture)), (int)remaining);
                break;

            case IntervalQualifier.YearToMonth:
                _ = builder.Append(leading.ToString(CultureInfo.InvariantCulture)).Append('-')
                    .Append(remaining.ToString(CultureInfo.InvariantCulture));
                break;

            case IntervalQualifier.DayToHour:
                _ = builder.Append(leading.ToString(CultureInfo.InvariantCulture)).Append(' ');
                StringUtils.AppendTwoDigits(builder, (int)remaining);
                break;

            case IntervalQualifier.DayToMinute:
                _ = builder.Append(leading.ToString(CultureInfo.InvariantCulture)).Append(' ');
                int r = (int)remaining;
                StringUtils.AppendTwoDigits(builder, r / 60).Append(':');
                StringUtils.AppendTwoDigits(builder, r % 60);
                break;

            case IntervalQualifier.DayToSecond:
                long nanos = remaining % DateTimeUtils.NanosPerMinute;
                int r2 = (int)(remaining / DateTimeUtils.NanosPerMinute);
                _ = builder.Append(leading.ToString(CultureInfo.InvariantCulture)).Append(' ');
                StringUtils.AppendTwoDigits(builder, r2 / 60).Append(':');
                StringUtils.AppendTwoDigits(builder, r2 % 60).Append(':');
                StringUtils.AppendTwoDigits(builder, (int)(nanos / DateTimeUtils.NanosPerSecond));
                DateTimeUtils.AppendNanos(builder, (int)(nanos % DateTimeUtils.NanosPerSecond));
                break;

            case IntervalQualifier.HourToMinute:
                _ = builder.Append(leading.ToString(CultureInfo.InvariantCulture)).Append(':');
                StringUtils.AppendTwoDigits(builder, (int)remaining);
                break;

            case IntervalQualifier.HourToSecond:
                _ = builder.Append(leading.ToString(CultureInfo.InvariantCulture)).Append(':');
                StringUtils.AppendTwoDigits(builder, (int)(remaining / DateTimeUtils.NanosPerMinute)).Append(':');
                long s = remaining % DateTimeUtils.NanosPerMinute;
                StringUtils.AppendTwoDigits(builder, (int)(s / DateTimeUtils.NanosPerSecond));
                DateTimeUtils.AppendNanos(builder, (int)(s % DateTimeUtils.NanosPerSecond));
                break;

            case IntervalQualifier.MinuteToSecond:
                _ = builder.Append(leading.ToString(CultureInfo.InvariantCulture)).Append(':');
                StringUtils.AppendTwoDigits(builder, (int)(remaining / DateTimeUtils.NanosPerSecond));
                DateTimeUtils.AppendNanos(builder, (int)(remaining % DateTimeUtils.NanosPerSecond));
                break;

            default:
                throw new ArgumentException(nameof(qualifier));
        }

        return builder.Append("' ").Append(IntervalQualifiers.Name(qualifier));
    }

    /// <summary>
    /// The absolute value of an interval: months for a year-month interval, nanoseconds otherwise.
    /// </summary>
    /// <param name="interval">the interval</param>
    /// <returns>the absolute value, signed</returns>
    public static BigInteger IntervalToAbsolute(ValueInterval interval)
    {
        BigInteger r;
        switch (interval.GetQualifier())
        {
            case IntervalQualifier.Year:
                r = new BigInteger(interval.GetLeading()) * MonthsPerYearBi;
                break;

            case IntervalQualifier.Month:
                r = new BigInteger(interval.GetLeading());
                break;

            case IntervalQualifier.Day:
                r = new BigInteger(interval.GetLeading()) * NanosPerDayBi;
                break;

            case IntervalQualifier.Hour:
                r = new BigInteger(interval.GetLeading()) * NanosPerHourBi;
                break;

            case IntervalQualifier.Minute:
                r = new BigInteger(interval.GetLeading()) * NanosPerMinuteBi;
                break;

            case IntervalQualifier.Second:
                r = IntervalToAbsolute(interval, NanosPerSecondBi);
                break;

            case IntervalQualifier.YearToMonth:
                r = IntervalToAbsolute(interval, MonthsPerYearBi);
                break;

            case IntervalQualifier.DayToHour:
                r = IntervalToAbsolute(interval, NanosPerHourBi, HoursPerDayBi);
                break;

            case IntervalQualifier.DayToMinute:
                r = IntervalToAbsolute(interval, NanosPerMinuteBi, MinutesPerDayBi);
                break;

            case IntervalQualifier.DayToSecond:
                r = IntervalToAbsolute(interval, NanosPerDayBi);
                break;

            case IntervalQualifier.HourToMinute:
                r = IntervalToAbsolute(interval, NanosPerMinuteBi, MinutesPerHourBi);
                break;

            case IntervalQualifier.HourToSecond:
                r = IntervalToAbsolute(interval, NanosPerHourBi);
                break;

            case IntervalQualifier.MinuteToSecond:
                r = IntervalToAbsolute(interval, NanosPerMinuteBi);
                break;

            default:
                throw new ArgumentException(nameof(interval));
        }

        return interval.IsNegative() ? -r : r;
    }

    private static BigInteger IntervalToAbsolute(ValueInterval interval, BigInteger multiplier)
        => (new BigInteger(interval.GetLeading()) * multiplier) + new BigInteger(interval.GetRemaining());

    /// <remarks>
    /// The leading field and the remaining half are in different units, so the leading field is
    /// first turned into the unit of the remaining half by multiplying it by the number of them in a
    /// day or an hour, and the whole is then multiplied by what one of those is worth. Multiplying
    /// the sum instead is the mistake this shape invites: it multiplies the remaining half too, and
    /// an interval of one day and one hour comes out as one day and twenty-four nanoseconds.
    /// </remarks>
    private static BigInteger IntervalToAbsolute(
        ValueInterval interval, BigInteger multiplier, BigInteger totalMultiplier)
        => ((new BigInteger(interval.GetLeading()) * totalMultiplier) + new BigInteger(interval.GetRemaining()))
            * multiplier;

    /// <summary>
    /// The interval of an absolute value.
    /// </summary>
    /// <param name="qualifier">the qualifier of the interval wanted</param>
    /// <param name="absolute">the absolute value, in months or in nanoseconds</param>
    /// <returns>the interval</returns>
    public static ValueInterval IntervalFromAbsolute(IntervalQualifier qualifier, BigInteger absolute)
    {
        switch (qualifier)
        {
            case IntervalQualifier.Year:
                return ValueInterval.From(
                    qualifier, absolute.Sign < 0, LeadingExact(BigInteger.Divide(absolute, MonthsPerYearBi)), 0);

            case IntervalQualifier.Month:
                return ValueInterval.From(qualifier, absolute.Sign < 0, LeadingExact(absolute), 0);

            case IntervalQualifier.Day:
                return ValueInterval.From(
                    qualifier, absolute.Sign < 0, LeadingExact(BigInteger.Divide(absolute, NanosPerDayBi)), 0);

            case IntervalQualifier.Hour:
                return ValueInterval.From(
                    qualifier, absolute.Sign < 0, LeadingExact(BigInteger.Divide(absolute, NanosPerHourBi)), 0);

            case IntervalQualifier.Minute:
                return ValueInterval.From(
                    qualifier, absolute.Sign < 0, LeadingExact(BigInteger.Divide(absolute, NanosPerMinuteBi)), 0);

            case IntervalQualifier.Second:
                return IntervalFromAbsolute(qualifier, absolute, NanosPerSecondBi);

            case IntervalQualifier.YearToMonth:
                return IntervalFromAbsolute(qualifier, absolute, MonthsPerYearBi);

            case IntervalQualifier.DayToHour:
                return IntervalFromAbsolute(
                    qualifier, BigInteger.Divide(absolute, NanosPerHourBi), HoursPerDayBi);

            case IntervalQualifier.DayToMinute:
                return IntervalFromAbsolute(
                    qualifier, BigInteger.Divide(absolute, NanosPerMinuteBi), MinutesPerDayBi);

            case IntervalQualifier.DayToSecond:
                return IntervalFromAbsolute(qualifier, absolute, NanosPerDayBi);

            case IntervalQualifier.HourToMinute:
                return IntervalFromAbsolute(
                    qualifier, BigInteger.Divide(absolute, NanosPerMinuteBi), MinutesPerHourBi);

            case IntervalQualifier.HourToSecond:
                return IntervalFromAbsolute(qualifier, absolute, NanosPerHourBi);

            case IntervalQualifier.MinuteToSecond:
                return IntervalFromAbsolute(qualifier, absolute, NanosPerMinuteBi);

            default:
                throw new ArgumentException(nameof(qualifier));
        }
    }

    private static ValueInterval IntervalFromAbsolute(
        IntervalQualifier qualifier, BigInteger absolute, BigInteger divisor)
    {
        BigInteger quotient = BigInteger.DivRem(absolute, divisor, out BigInteger remainder);
        return ValueInterval.From(
            qualifier, absolute.Sign < 0, LeadingExact(quotient), (long)BigInteger.Abs(remainder));
    }

    /// <summary>
    /// The leading field of an absolute value, refusing one too wide to be a leading field.
    /// </summary>
    private static long LeadingExact(BigInteger absolute)
    {
        if (absolute > LeadingMax || absolute < LeadingMin)
        {
            throw DbException.Get(
                ErrorCode.NumericValueOutOfRange1,
                absolute.ToString(CultureInfo.InvariantCulture));
        }

        // The leading field of a value is never negative; the sign of the interval is a separate
        // thing, and this is the one place where the two are joined. Returning the magnitude here is
        // what makes the subtraction of two intervals answer a negative *interval* rather than a
        // negative leading field that the validation would refuse.
        return (long)BigInteger.Abs(absolute);
    }

    /// <summary>
    /// Checks the two halves of an interval and answers whether it is negative.
    /// </summary>
    /// <remarks>
    /// An interval of nothing at all is not negative: INTERVAL '0' DAY is written without a sign,
    /// and the reference decides that here rather than leaving it to the caller. The bound on the
    /// remaining half is the qualifier's: the months of a YEAR TO MONTH, the nanoseconds of a SECOND,
    /// the minutes of a MINUTE TO SECOND.
    /// </remarks>
    /// <param name="qualifier">the qualifier</param>
    /// <param name="negative">whether the text carried a sign</param>
    /// <param name="leading">the leading field</param>
    /// <param name="remaining">the remaining fields</param>
    /// <returns>whether the interval is negative</returns>
    public static bool ValidateInterval(
        IntervalQualifier qualifier, bool negative, long leading, long remaining)
    {
        if (leading == 0L && remaining == 0L)
        {
            return false;
        }

        long bound;
        switch (qualifier)
        {
            case IntervalQualifier.Year:
            case IntervalQualifier.Month:
            case IntervalQualifier.Day:
            case IntervalQualifier.Hour:
            case IntervalQualifier.Minute:
                bound = 1;
                break;

            case IntervalQualifier.Second:
                bound = DateTimeUtils.NanosPerSecond;
                break;

            case IntervalQualifier.YearToMonth:
                bound = 12;
                break;

            case IntervalQualifier.DayToHour:
                bound = 24;
                break;

            case IntervalQualifier.DayToMinute:
                bound = MinutesPerDay;
                break;

            case IntervalQualifier.DayToSecond:
                bound = DateTimeUtils.NanosPerDay;
                break;

            case IntervalQualifier.HourToMinute:
                bound = 60;
                break;

            case IntervalQualifier.HourToSecond:
                bound = DateTimeUtils.NanosPerHour;
                break;

            case IntervalQualifier.MinuteToSecond:
                bound = DateTimeUtils.NanosPerMinute;
                break;

            default:
                throw DbException.GetInvalidValueException("interval", nameof(qualifier));
        }

        if (leading < 0L || leading >= LeadingBound)
        {
            throw DbException.GetInvalidValueException(
                "interval", leading.ToString(CultureInfo.InvariantCulture));
        }

        if (remaining < 0L || remaining >= bound)
        {
            throw DbException.GetInvalidValueException(
                "interval", remaining.ToString(CultureInfo.InvariantCulture));
        }

        return negative;
    }

    /// <summary>
    /// The years of an interval, which is its leading field when the qualifier has years.
    /// </summary>
    /// <param name="qualifier">the qualifier</param>
    /// <param name="negative">whether the interval is negative</param>
    /// <param name="leading">the leading field</param>
    /// <param name="remaining">the remaining fields</param>
    /// <returns>the years</returns>
    public static long YearsFromInterval(
        IntervalQualifier qualifier, bool negative, long leading, long remaining)
    {
        if (qualifier != IntervalQualifier.Year && qualifier != IntervalQualifier.YearToMonth)
        {
            throw DbException.GetInvalidValueException("interval", nameof(qualifier));
        }

        long months = MonthsFromInterval(qualifier, negative, leading, remaining);
        return months / MonthsPerYear;
    }

    /// <summary>
    /// The months of an interval.
    /// </summary>
    /// <param name="qualifier">the qualifier</param>
    /// <param name="negative">whether the interval is negative</param>
    /// <param name="leading">the leading field</param>
    /// <param name="remaining">the remaining fields</param>
    /// <returns>the months</returns>
    public static long MonthsFromInterval(
        IntervalQualifier qualifier, bool negative, long leading, long remaining)
    {
        if (qualifier != IntervalQualifier.Month && qualifier != IntervalQualifier.YearToMonth)
        {
            throw DbException.GetInvalidValueException("interval", nameof(qualifier));
        }

        long months;
        if (qualifier == IntervalQualifier.YearToMonth)
        {
            months = (leading * MonthsPerYear) + remaining;
        }
        else
        {
            months = leading;
        }

        return negative ? -months : months;
    }

    /// <summary>
    /// The days of an interval.
    /// </summary>
    /// <param name="qualifier">the qualifier</param>
    /// <param name="negative">whether the interval is negative</param>
    /// <param name="leading">the leading field</param>
    /// <param name="remaining">the remaining fields</param>
    /// <returns>the days</returns>
    public static long DaysFromInterval(
        IntervalQualifier qualifier, bool negative, long leading, long remaining)
    {
        if (!IntervalQualifiers.HasDays(qualifier))
        {
            throw DbException.GetInvalidValueException("interval", nameof(qualifier));
        }

        long days;
        if (qualifier == IntervalQualifier.Day)
        {
            days = leading;
        }
        else
        {
            long rest;
            switch (qualifier)
            {
                case IntervalQualifier.DayToHour:
                    rest = remaining;
                    break;

                case IntervalQualifier.DayToMinute:
                    rest = remaining;
                    break;

                default:
                    rest = remaining / DateTimeUtils.NanosPerDay;
                    break;
            }

            days = (leading * HoursPerDay) + (rest / DateTimeUtils.NanosPerHour);
        }

        return negative ? -days : days;
    }

    /// <summary>
    /// The hours of an interval.
    /// </summary>
    /// <param name="qualifier">the qualifier</param>
    /// <param name="negative">whether the interval is negative</param>
    /// <param name="leading">the leading field</param>
    /// <param name="remaining">the remaining fields</param>
    /// <returns>the hours</returns>
    public static long HoursFromInterval(
        IntervalQualifier qualifier, bool negative, long leading, long remaining)
    {
        if (!IntervalQualifiers.HasHours(qualifier))
        {
            throw DbException.GetInvalidValueException("interval", nameof(qualifier));
        }

        long hours = qualifier == IntervalQualifier.Hour
            ? leading
            : (leading * HoursPerDay) + (remaining / DateTimeUtils.NanosPerHour);

        return negative ? -hours : hours;
    }

    /// <summary>
    /// The minutes of an interval.
    /// </summary>
    /// <param name="qualifier">the qualifier</param>
    /// <param name="negative">whether the interval is negative</param>
    /// <param name="leading">the leading field</param>
    /// <param name="remaining">the remaining fields</param>
    /// <returns>the minutes</returns>
    public static long MinutesFromInterval(
        IntervalQualifier qualifier, bool negative, long leading, long remaining)
    {
        if (!IntervalQualifiers.HasMinutes(qualifier))
        {
            throw DbException.GetInvalidValueException("interval", nameof(qualifier));
        }

        long minutes = qualifier switch
        {
            IntervalQualifier.Minute => leading,
            IntervalQualifier.HourToMinute => (leading * MinutesPerHour) + remaining,
            IntervalQualifier.DayToMinute => (leading * MinutesPerDay) + remaining,
            IntervalQualifier.MinuteToSecond =>
                (leading * MinutesPerHour) + (remaining / DateTimeUtils.NanosPerMinute),
            IntervalQualifier.HourToSecond =>
                ((leading * MinutesPerHour) + (remaining / DateTimeUtils.NanosPerMinute)),
            IntervalQualifier.DayToSecond =>
                ((leading * MinutesPerDay) + (remaining / DateTimeUtils.NanosPerMinute)),
            _ => throw DbException.GetInvalidValueException("interval", nameof(qualifier)),
        };

        return negative ? -minutes : minutes;
    }

    /// <summary>
    /// The nanoseconds of an interval.
    /// </summary>
    /// <param name="qualifier">the qualifier</param>
    /// <param name="negative">whether the interval is negative</param>
    /// <param name="leading">the leading field</param>
    /// <param name="remaining">the remaining fields</param>
    /// <returns>the nanoseconds</returns>
    public static long NanosFromInterval(
        IntervalQualifier qualifier, bool negative, long leading, long remaining)
    {
        if (!IntervalQualifiers.HasSeconds(qualifier))
        {
            throw DbException.GetInvalidValueException("interval", nameof(qualifier));
        }

        long nanos = qualifier switch
        {
            IntervalQualifier.Second => (leading * DateTimeUtils.NanosPerSecond) + remaining,
            IntervalQualifier.MinuteToSecond => (leading * DateTimeUtils.NanosPerMinute) + remaining,
            IntervalQualifier.HourToSecond => (leading * DateTimeUtils.NanosPerHour) + remaining,
            IntervalQualifier.DayToSecond => (leading * DateTimeUtils.NanosPerDay) + remaining,
            _ => throw DbException.GetInvalidValueException("interval", nameof(qualifier)),
        };

        return negative ? -nanos : nanos;
    }

    /// <summary>
    /// Skips whitespace, and refuses to run off the end of the text.
    /// </summary>
    private static int SkipWhitespace(string s, int i)
    {
        for (int l = s.Length; ; i++)
        {
            if (i == l)
            {
                throw new ArgumentException(s, nameof(s));
            }

            if (!JavaCharacter.IsWhitespace(s[i]))
            {
                return i;
            }
        }
    }

    /// <summary>
    /// Skips whitespace, and stops at the end of the text instead of refusing to pass it.
    /// </summary>
    private static int SkipWhitespaceEnd(string s, int i)
    {
        for (int l = s.Length; ; i++)
        {
            if (i == l)
            {
                return i;
            }

            if (!JavaCharacter.IsWhitespace(s[i]))
            {
                return i;
            }
        }
    }

    private static bool RegionMatchesIgnoringCase(string s, int start, string expected)
    {
        if (start < 0 || start + expected.Length > s.Length)
        {
            return false;
        }

        for (int i = 0; i < expected.Length; i++)
        {
            if ((expected[i] & 0xFFDF) != (s[start + i] & 0xFFDF))
            {
                return false;
            }
        }

        return true;
    }

    /// <remarks>
    /// Two fields with one separator between them, which is three of the twelve qualifiers: the
    /// months of a YEAR TO MONTH, the hours of a DAY TO HOUR and the minutes of an HOUR TO MINUTE.
    /// The separator is searched from the second character, because the first character has been
    /// read already as the sign or as the first digit.
    /// </remarks>
    private static ValueInterval ParseInterval2(
        IntervalQualifier qualifier, string s, char ch, int max, bool negative)
    {
        long leading;
        long remaining;
        int dash = s.IndexOf(ch, 1);
        if (dash < 0)
        {
            leading = ParseIntervalLeading(s, 0, s.Length, negative);
            remaining = 0;
        }
        else
        {
            leading = ParseIntervalLeading(s, 0, dash, negative);
            remaining = ParseIntervalRemaining(s, dash + 1, s.Length, max);
        }

        negative = leading < 0;
        if (negative)
        {
            // Only the leading field carries the sign. The remaining half is read as digits and
            // keeps its sign from the leading field, which is why INTERVAL '-1-2' YEAR TO MONTH is
            // a leading field of one, a remaining half of two and a sign, and not a negative
            // remaining half that the validation would then refuse.
            leading = leading != long.MinValue ? -leading : 0;
        }

        return ValueInterval.From(qualifier, negative, leading, remaining);
    }

    /// <remarks>
    /// The leading field is written with its own sign when it is negative, so the text of an
    /// interval is the sign outside the quotes and the sign of the leading field inside, and the two
    /// have to be brought into agreement. A leading field of zero with a sign is
    /// <see cref="long.MinValue"/>, which is how "negative zero" is written down without being zero.
    /// </remarks>
    private static long ParseIntervalLeading(string s, int start, int end, bool negative)
    {
        long leading = long.Parse(s[start..end], CultureInfo.InvariantCulture);
        if (leading == 0)
        {
            return negative ^ s[start] == '-' ? long.MinValue : 0;
        }

        return negative ? -leading : leading;
    }

    private static long ParseIntervalRemaining(string s, int start, int end, int max)
    {
        int v = StringUtils.ParseUInt31(s, start, end);
        if (v > max)
        {
            throw new ArgumentException(s, nameof(s));
        }

        return v;
    }

    private static long ParseIntervalRemainingSeconds(string s, int start)
    {
        int seconds, nanos;
        int dot = s.IndexOf('.', start + 1);
        if (dot < 0)
        {
            seconds = StringUtils.ParseUInt31(s, start, s.Length);
            nanos = 0;
        }
        else
        {
            seconds = StringUtils.ParseUInt31(s, start, dot);
            nanos = DateTimeUtils.ParseNanos(s, dot + 1, s.Length);
        }

        if (seconds > 59)
        {
            throw new ArgumentException(s, nameof(s));
        }

        return (seconds * DateTimeUtils.NanosPerSecond) + nanos;
    }
}
