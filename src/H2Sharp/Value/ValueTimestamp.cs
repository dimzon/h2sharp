/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueTimestamp.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Values;

/// <summary>
/// Implementation of the TIMESTAMP data type: a date and a time of day, with no time zone.
/// </summary>
/// <remarks>
/// The two halves are a shifted date and a count of nanoseconds from midnight, and adding two
/// timestamps adds both and then carries the nanoseconds into the day. That is the whole arithmetic
/// of this class, and the carry is where the dates before 1970 are interesting: subtracting two
/// timestamps gives a negative count of nanoseconds that has to borrow a day, and the borrow is
/// what the reference does in one place and what a naive subtraction would get wrong.
/// </remarks>
public sealed class ValueTimestamp : Value
{
    /// <summary>
    /// The default precision and display size of the textual representation of a timestamp.
    /// Example: 2001-01-01 23:59:59.123456
    /// </summary>
    public const int DefaultPrecision = 26;

    /// <summary>
    /// The maximum precision and display size of the textual representation of a timestamp.
    /// Example: 2001-01-01 23:59:59.123456789
    /// </summary>
    public const int MaximumPrecision = 29;

    /// <summary>
    /// The default scale for timestamps.
    /// </summary>
    public const int DefaultScale = 6;

    /// <summary>
    /// The maximum scale for timestamps.
    /// </summary>
    public const int MaximumScale = 9;

    /// <summary>
    /// The year, month and day shifted into one number.
    /// </summary>
    private readonly long dateValue;

    /// <summary>
    /// The nanoseconds since midnight.
    /// </summary>
    private readonly long timeNanos;

    private ValueTimestamp(long dateValue, long timeNanos)
    {
        if (dateValue < DateTimeUtils.MinDateValue || dateValue > DateTimeUtils.MaxDateValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dateValue), "dateValue out of range " + dateValue);
        }

        if (timeNanos < 0 || timeNanos >= DateTimeUtils.NanosPerDay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeNanos), "timeNanos out of range " + timeNanos);
        }

        this.dateValue = dateValue;
        this.timeNanos = timeNanos;
    }

    /// <summary>
    /// Get or create a timestamp value.
    /// </summary>
    /// <param name="dateValue">the date value, a bit field with bits for the year, month and day</param>
    /// <param name="timeNanos">the nanoseconds since midnight</param>
    /// <returns>the value</returns>
    public static ValueTimestamp FromDateValueAndNanos(long dateValue, long timeNanos)
        => (ValueTimestamp)Value.Cache(new ValueTimestamp(dateValue, timeNanos))!;

    /// <summary>
    /// Parse a string to a timestamp.
    /// </summary>
    /// <remarks>
    /// The format is <c>+/-year-month-day[ -]hour[:.]minute[:.]seconds.fractional</c>. A text with a
    /// time zone is refused, because the types that hold a zone are not ported; the reference reads
    /// one and answers with a different value type.
    /// </remarks>
    /// <param name="s">the string to parse</param>
    /// <param name="provider">the cast information provider</param>
    /// <returns>the timestamp</returns>
    /// <exception cref="H2SharpException">if the text is not a timestamp</exception>
    public static ValueTimestamp Parse(string s, CastDataProvider? provider)
    {
        try
        {
            return (ValueTimestamp)DateTimeUtils.ParseTimestamp(s, provider, false);
        }
        catch (Exception e)
            when (e is ArgumentException or FormatException or ArithmeticException or NotSupportedException)
        {
            throw DbException.Get(ErrorCode.InvalidDatetimeConstant2, e, "TIMESTAMP", s);
        }
    }

    /// <summary>
    /// The year, month and day shifted into one number.
    /// </summary>
    /// <returns>the date value</returns>
    public long GetDateValue() => dateValue;

    /// <summary>
    /// The nanoseconds since midnight.
    /// </summary>
    /// <returns>the nanoseconds</returns>
    public long GetTimeNanos() => timeNanos;

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => TypeInfo.TypeTimestamp;

    /// <inheritdoc/>
    public override int GetValueType() => Value.Timestamp;

    /// <inheritdoc/>
    public override int GetMemory() => 32;

    /// <inheritdoc/>
    public override string? GetString()
        => ToStringBuilder(new StringBuilder(MaximumPrecision), false).ToString();

    /// <summary>
    /// The value as text in the ISO shape, which puts a T between the date and the time where the
    /// SQL form puts a space.
    /// </summary>
    /// <returns>the value as text in ISO form</returns>
    public string GetIsoString() => ToStringBuilder(new StringBuilder(MaximumPrecision), true).ToString();

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => ToStringBuilder(_ = builder.Append("TIMESTAMP '"), false).Append('\'');

    private StringBuilder ToStringBuilder(StringBuilder builder, bool iso)
    {
        DateTimeUtils.AppendDate(builder, dateValue).Append(iso ? 'T' : ' ');
        return DateTimeUtils.AppendTime(builder, timeNanos);
    }

    /// <inheritdoc/>
    public override int CompareTypeSafe(Value o, CompareMode mode, CastDataProvider? provider)
    {
        ValueTimestamp t = (ValueTimestamp)o;
        int c = dateValue.CompareTo(t.dateValue);
        return c != 0 ? c : timeNanos.CompareTo(t.timeNanos);
    }

    /// <inheritdoc/>
    public override bool Equals(object? other)
        => ReferenceEquals(this, other)
            || (other is ValueTimestamp x && dateValue == x.dateValue && timeNanos == x.timeNanos);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(dateValue, timeNanos);

    /// <inheritdoc/>
    public override Value Add(Value v)
    {
        ValueTimestamp t = (ValueTimestamp)v;
        long absoluteDay = DateTimeUtils.AbsoluteDayFromDateValue(dateValue)
            + DateTimeUtils.AbsoluteDayFromDateValue(t.dateValue);
        long nanos = timeNanos + t.timeNanos;
        if (nanos >= DateTimeUtils.NanosPerDay)
        {
            nanos -= DateTimeUtils.NanosPerDay;
            absoluteDay++;
        }

        return FromDateValueAndNanos(DateTimeUtils.DateValueFromAbsoluteDay(absoluteDay), nanos);
    }

    /// <inheritdoc/>
    public override Value Subtract(Value v)
    {
        ValueTimestamp t = (ValueTimestamp)v;
        long absoluteDay = DateTimeUtils.AbsoluteDayFromDateValue(dateValue)
            - DateTimeUtils.AbsoluteDayFromDateValue(t.dateValue);
        long nanos = timeNanos - t.timeNanos;
        if (nanos < 0)
        {
            nanos += DateTimeUtils.NanosPerDay;
            absoluteDay--;
        }

        return FromDateValueAndNanos(DateTimeUtils.DateValueFromAbsoluteDay(absoluteDay), nanos);
    }
}
