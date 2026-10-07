/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 */
using System;
using System.Text;

namespace H2Sharp.Api;

/// <summary>
/// Interval qualifier.
/// </summary>
/// <remarks>
/// The order of the members is the order of the reference enum, and the reference relies on
/// it: <c>TypeInfo.getSQL</c> reaches the qualifier with
/// <c>IntervalQualifier.valueOf(valueType - Value.INTERVAL_YEAR)</c>. So the members must
/// stay in the ordinal order the reference declares, and nothing may be inserted.
/// </remarks>
public enum IntervalQualifier
{
    /// <summary>
    /// <c>YEAR</c>
    /// </summary>
    Year,

    /// <summary>
    /// <c>MONTH</c>
    /// </summary>
    Month,

    /// <summary>
    /// <c>DAY</c>
    /// </summary>
    Day,

    /// <summary>
    /// <c>HOUR</c>
    /// </summary>
    Hour,

    /// <summary>
    /// <c>MINUTE</c>
    /// </summary>
    Minute,

    /// <summary>
    /// <c>SECOND</c>
    /// </summary>
    Second,

    /// <summary>
    /// <c>YEAR TO MONTH</c>
    /// </summary>
    YearToMonth,

    /// <summary>
    /// <c>DAY TO HOUR</c>
    /// </summary>
    DayToHour,

    /// <summary>
    /// <c>DAY TO MINUTE</c>
    /// </summary>
    DayToMinute,

    /// <summary>
    /// <c>DAY TO SECOND</c>
    /// </summary>
    DayToSecond,

    /// <summary>
    /// <c>HOUR TO MINUTE</c>
    /// </summary>
    HourToMinute,

    /// <summary>
    /// <c>HOUR TO SECOND</c>
    /// </summary>
    HourToSecond,

    /// <summary>
    /// <c>MINUTE TO SECOND</c>
    /// </summary>
    MinuteToSecond,
}

/// <summary>
/// The qualifier carried by a <see cref="IntervalQualifier"/>.
/// </summary>
public static class IntervalQualifiers
{
    /// <summary>
    /// Returns the interval qualifier with the specified ordinal value.
    /// </summary>
    /// <param name="ordinal">
    /// the 0-based value, which for the reference is
    /// <c>valueType - Value.INTERVAL_YEAR</c>
    /// </param>
    /// <returns>the interval qualifier with the specified ordinal value</returns>
    /// <exception cref="ArgumentOutOfRangeException">if there is no such qualifier</exception>
    public static IntervalQualifier ValueOf(int ordinal) => ordinal switch
    {
        0 => IntervalQualifier.Year,
        1 => IntervalQualifier.Month,
        2 => IntervalQualifier.Day,
        3 => IntervalQualifier.Hour,
        4 => IntervalQualifier.Minute,
        5 => IntervalQualifier.Second,
        6 => IntervalQualifier.YearToMonth,
        7 => IntervalQualifier.DayToHour,
        8 => IntervalQualifier.DayToMinute,
        9 => IntervalQualifier.DayToSecond,
        10 => IntervalQualifier.HourToMinute,
        11 => IntervalQualifier.HourToSecond,
        12 => IntervalQualifier.MinuteToSecond,
        _ => throw new ArgumentOutOfRangeException(nameof(ordinal)),
    };

    /// <summary>
    /// The qualifier as the reference spells it, that is the name with underscores turned
    /// into spaces. The reference builds it that way rather than listing the strings.
    /// </summary>
    /// <param name="qualifier">the qualifier</param>
    /// <returns>the qualifier name</returns>
    public static string Name(IntervalQualifier qualifier) => qualifier switch
    {
        IntervalQualifier.Year => "YEAR",
        IntervalQualifier.Month => "MONTH",
        IntervalQualifier.Day => "DAY",
        IntervalQualifier.Hour => "HOUR",
        IntervalQualifier.Minute => "MINUTE",
        IntervalQualifier.Second => "SECOND",
        IntervalQualifier.YearToMonth => "YEAR TO MONTH",
        IntervalQualifier.DayToHour => "DAY TO HOUR",
        IntervalQualifier.DayToMinute => "DAY TO MINUTE",
        IntervalQualifier.DayToSecond => "DAY TO SECOND",
        IntervalQualifier.HourToMinute => "HOUR TO MINUTE",
        IntervalQualifier.HourToSecond => "HOUR TO SECOND",
        IntervalQualifier.MinuteToSecond => "MINUTE TO SECOND",
        _ => throw new ArgumentOutOfRangeException(nameof(qualifier)),
    };

    /// <summary>
    /// Returns whether interval with this qualifier is a year-month interval.
    /// </summary>
    /// <param name="qualifier">the qualifier</param>
    /// <returns>whether interval with this qualifier is a year-month interval</returns>
    public static bool IsYearMonth(IntervalQualifier qualifier) => qualifier is
        IntervalQualifier.Year or IntervalQualifier.Month or IntervalQualifier.YearToMonth;

    /// <summary>
    /// Returns whether interval with this qualifier is a day-time interval.
    /// </summary>
    /// <param name="qualifier">the qualifier</param>
    /// <returns>whether interval with this qualifier is a day-time interval</returns>
    public static bool IsDayTime(IntervalQualifier qualifier) => !IsYearMonth(qualifier);

    /// <summary>
    /// Returns whether interval with this qualifier has years.
    /// </summary>
    /// <param name="qualifier">the qualifier</param>
    /// <returns>whether interval with this qualifier has years</returns>
    public static bool HasYears(IntervalQualifier qualifier) => qualifier is
        IntervalQualifier.Year or IntervalQualifier.YearToMonth;

    /// <summary>
    /// Returns whether interval with this qualifier has months.
    /// </summary>
    /// <param name="qualifier">the qualifier</param>
    /// <returns>whether interval with this qualifier has months</returns>
    public static bool HasMonths(IntervalQualifier qualifier) => qualifier is
        IntervalQualifier.Month or IntervalQualifier.YearToMonth;

    /// <summary>
    /// Returns whether interval with this qualifier has days.
    /// </summary>
    /// <param name="qualifier">the qualifier</param>
    /// <returns>whether interval with this qualifier has days</returns>
    public static bool HasDays(IntervalQualifier qualifier) => qualifier is
        IntervalQualifier.Day
        or IntervalQualifier.DayToHour
        or IntervalQualifier.DayToMinute
        or IntervalQualifier.DayToSecond;

    /// <summary>
    /// Returns whether interval with this qualifier has hours.
    /// </summary>
    /// <param name="qualifier">the qualifier</param>
    /// <returns>whether interval with this qualifier has hours</returns>
    public static bool HasHours(IntervalQualifier qualifier) => qualifier is
        IntervalQualifier.Hour
        or IntervalQualifier.DayToHour
        or IntervalQualifier.DayToMinute
        or IntervalQualifier.DayToSecond
        or IntervalQualifier.HourToMinute
        or IntervalQualifier.HourToSecond;

    /// <summary>
    /// Returns whether interval with this qualifier has minutes.
    /// </summary>
    /// <param name="qualifier">the qualifier</param>
    /// <returns>whether interval with this qualifier has minutes</returns>
    public static bool HasMinutes(IntervalQualifier qualifier) => qualifier is
        IntervalQualifier.Minute
        or IntervalQualifier.DayToMinute
        or IntervalQualifier.DayToSecond
        or IntervalQualifier.HourToMinute
        or IntervalQualifier.HourToSecond
        or IntervalQualifier.MinuteToSecond;

    /// <summary>
    /// Returns whether interval with this qualifier has seconds.
    /// </summary>
    /// <param name="qualifier">the qualifier</param>
    /// <returns>whether interval with this qualifier has seconds</returns>
    public static bool HasSeconds(IntervalQualifier qualifier) => qualifier is
        IntervalQualifier.Second
        or IntervalQualifier.DayToSecond
        or IntervalQualifier.HourToSecond
        or IntervalQualifier.MinuteToSecond;

    /// <summary>
    /// Returns whether interval with this qualifier has multiple fields.
    /// </summary>
    /// <param name="qualifier">the qualifier</param>
    /// <returns>whether interval with this qualifier has multiple fields</returns>
    public static bool HasMultipleFields(IntervalQualifier qualifier) =>
        (int)qualifier > (int)IntervalQualifier.Second;

    /// <summary>
    /// Returns full type name.
    /// </summary>
    /// <param name="qualifier">the qualifier</param>
    /// <param name="precision">precision, or <c>-1</c></param>
    /// <param name="scale">fractional seconds precision, or <c>-1</c></param>
    /// <returns>full type name</returns>
    public static string GetTypeName(IntervalQualifier qualifier, int precision, int scale)
        => AppendTypeName(qualifier, new StringBuilder(), precision, scale, false).ToString();

    /// <summary>
    /// Appends full type name to the specified string builder.
    /// </summary>
    /// <param name="qualifier">the qualifier</param>
    /// <param name="builder">string builder</param>
    /// <param name="precision">precision, or <c>-1</c></param>
    /// <param name="scale">fractional seconds precision, or <c>-1</c></param>
    /// <param name="qualifierOnly">if <c>true</c>, don't add the INTERVAL prefix</param>
    /// <returns>the specified string builder</returns>
    public static StringBuilder AppendTypeName(
        IntervalQualifier qualifier,
        StringBuilder builder,
        int precision,
        int scale,
        bool qualifierOnly)
    {
        if (!qualifierOnly)
        {
            _ = builder.Append("INTERVAL ");
        }

        switch (qualifier)
        {
            case IntervalQualifier.Year:
            case IntervalQualifier.Month:
            case IntervalQualifier.Day:
            case IntervalQualifier.Hour:
            case IntervalQualifier.Minute:
                _ = builder.Append(Name(qualifier));
                if (precision > 0)
                {
                    _ = builder.Append('(').Append(precision).Append(')');
                }

                break;

            case IntervalQualifier.Second:
                _ = builder.Append(Name(qualifier));
                if (precision > 0 || scale >= 0)
                {
                    _ = builder.Append('(').Append(precision > 0 ? precision : 2);
                    if (scale >= 0)
                    {
                        _ = builder.Append(", ").Append(scale);
                    }

                    _ = builder.Append(')');
                }

                break;

            case IntervalQualifier.YearToMonth:
                AppendQualifierWithPrecision(builder, "YEAR", "MONTH", precision);
                break;

            case IntervalQualifier.DayToHour:
                AppendQualifierWithPrecision(builder, "DAY", "HOUR", precision);
                break;

            case IntervalQualifier.DayToMinute:
                AppendQualifierWithPrecision(builder, "DAY", "MINUTE", precision);
                break;

            case IntervalQualifier.DayToSecond:
                AppendRangeWithScale(builder, "DAY", "SECOND", precision, scale);
                break;

            case IntervalQualifier.HourToMinute:
                AppendQualifierWithPrecision(builder, "HOUR", "MINUTE", precision);
                break;

            case IntervalQualifier.HourToSecond:
                AppendRangeWithScale(builder, "HOUR", "SECOND", precision, scale);
                break;

            case IntervalQualifier.MinuteToSecond:
                _ = AppendRangeWithScale(builder, "MINUTE", "SECOND", precision, scale);
                break;
        }

        return builder;
    }

    private static StringBuilder AppendQualifierWithPrecision(
        StringBuilder builder,
        string from,
        string to,
        int precision)
    {
        _ = builder.Append(from);
        if (precision > 0)
        {
            _ = builder.Append('(').Append(precision).Append(')');
        }

        return builder.Append(" TO ").Append(to);
    }

    private static StringBuilder AppendRangeWithScale(
        StringBuilder builder,
        string from,
        string to,
        int precision,
        int scale)
    {
        _ = AppendQualifierWithPrecision(builder, from, to, precision);
        if (scale >= 0)
        {
            _ = builder.Append('(').Append(scale).Append(')');
        }

        return builder;
    }
}