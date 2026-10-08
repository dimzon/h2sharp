/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueTimestampTimeZone.
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
/// Implementation of the TIMESTAMP WITH TIME ZONE data type: an instant, written as a date and a
/// time and the offset it is written in.
/// </summary>
/// <remarks>
/// Two values of this type are compared by the instant, and the offset is subtracted from the time
/// before the comparison with a borrow from the date, because the offset is at most eighteen hours
/// and so can move the time out of its day but never out of its month. That is the whole reason the
/// comparison carries a date with it: subtracting the offset without borrowing would answer a day
/// wrong for every value whose local time is near midnight.
/// </remarks>
public sealed class ValueTimestampTimeZone : Value
{
    /// <summary>
    /// The default precision and display size of the textual representation.
    /// Example: 2001-01-01 23:59:59.123456+01
    /// </summary>
    public const int DefaultPrecision = 32;

    /// <summary>
    /// The maximum precision and display size of the textual representation.
    /// Example: 2001-01-01 23:59:59.123456789+01:30
    /// </summary>
    public const int MaximumPrecision = 35;

    /// <summary>
    /// The default scale for a timestamp with time zone.
    /// </summary>
    public const int DefaultScale = ValueTimestamp.DefaultScale;

    /// <summary>
    /// The maximum scale for a timestamp with time zone.
    /// </summary>
    public const int MaximumScale = ValueTimestamp.MaximumScale;

    private readonly long dateValue;

    private readonly long timeNanos;

    private readonly int timeZoneOffsetSeconds;

    private ValueTimestampTimeZone(long dateValue, long timeNanos, int timeZoneOffsetSeconds)
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

        if (timeZoneOffsetSeconds < -(18 * 60 * 60) || timeZoneOffsetSeconds > 18 * 60 * 60)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeZoneOffsetSeconds), "timeZoneOffsetSeconds " + timeZoneOffsetSeconds);
        }

        this.dateValue = dateValue;
        this.timeNanos = timeNanos;
        this.timeZoneOffsetSeconds = timeZoneOffsetSeconds;
    }

    /// <summary>
    /// Get or create a timestamp with time zone value.
    /// </summary>
    /// <param name="dateValue">the date value</param>
    /// <param name="timeNanos">the nanoseconds since midnight, in the zone the offset names</param>
    /// <param name="timeZoneOffsetSeconds">the offset from Greenwich in seconds</param>
    /// <returns>the value</returns>
    public static ValueTimestampTimeZone FromDateValueAndNanos(
        long dateValue, long timeNanos, int timeZoneOffsetSeconds)
        => (ValueTimestampTimeZone)Value.Cache(
            new ValueTimestampTimeZone(dateValue, timeNanos, timeZoneOffsetSeconds))!;

    /// <summary>
    /// Parse a string to a timestamp with time zone.
    /// </summary>
    /// <param name="s">the string to parse</param>
    /// <param name="provider">where the default zone comes from</param>
    /// <returns>the timestamp</returns>
    /// <exception cref="H2SharpException">if the text is not a timestamp with a zone</exception>
    public static ValueTimestampTimeZone Parse(string s, CastDataProvider? provider)
    {
        try
        {
            return (ValueTimestampTimeZone)DateTimeUtils.ParseTimestamp(s, provider, true);
        }
        catch (Exception e)
            when (e is ArgumentException or FormatException or ArithmeticException or NotSupportedException)
        {
            throw DbException.Get(ErrorCode.InvalidDatetimeConstant2, e, "TIMESTAMP WITH TIME ZONE", s);
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

    /// <summary>
    /// The offset from Greenwich in seconds.
    /// </summary>
    /// <returns>the offset</returns>
    public int GetTimeZoneOffsetSeconds() => timeZoneOffsetSeconds;

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => TypeInfo.TypeTimestampTz;

    /// <inheritdoc/>
    public override int GetValueType() => Value.TimestampTz;

    /// <inheritdoc/>
    public override int GetMemory() => 40;

    /// <inheritdoc/>
    public override string? GetString()
        => ToStringBuilder(new StringBuilder(MaximumPrecision), false).ToString();

    /// <summary>
    /// The value as text in the ISO shape, which puts a T between the date and the time.
    /// </summary>
    /// <returns>the value as text in ISO form</returns>
    public string GetIsoString() => ToStringBuilder(new StringBuilder(MaximumPrecision), true).ToString();

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => ToStringBuilder(_ = builder.Append("TIMESTAMP WITH TIME ZONE '"), false).Append('\'');

    private StringBuilder ToStringBuilder(StringBuilder builder, bool iso)
    {
        DateTimeUtils.AppendDate(builder, dateValue).Append(iso ? 'T' : ' ');
        DateTimeUtils.AppendTime(builder, timeNanos);
        return DateTimeUtils.AppendTimeZone(builder, timeZoneOffsetSeconds);
    }

    /// <inheritdoc/>
    public override int CompareTypeSafe(Value o, CompareMode mode, CastDataProvider? provider)
    {
        ValueTimestampTimeZone t = (ValueTimestampTimeZone)o;

        // The largest offset is eighteen hours in either direction, so the difference between the
        // local day and the day in Greenwich cannot be more than one day. That is what lets this be
        // a borrow of one day rather than a division.
        (long dateValueA, long timeA) = ToUtc(dateValue, timeNanos, timeZoneOffsetSeconds);
        (long dateValueB, long timeB) = ToUtc(t.dateValue, t.timeNanos, t.timeZoneOffsetSeconds);

        int cmp = dateValueA.CompareTo(dateValueB);
        return cmp != 0 ? cmp : timeA.CompareTo(timeB);
    }

    private static (long Date, long Time) ToUtc(long dateValue, long timeNanos, int offsetSeconds)
    {
        long date = dateValue;
        long time = timeNanos - (offsetSeconds * DateTimeUtils.NanosPerSecond);
        if (time < 0)
        {
            time += DateTimeUtils.NanosPerDay;
            date = DateTimeUtils.DecrementDateValue(date);
        }
        else if (time >= DateTimeUtils.NanosPerDay)
        {
            time -= DateTimeUtils.NanosPerDay;
            date = DateTimeUtils.IncrementDateValue(date);
        }

        return (date, time);
    }

    /// <inheritdoc/>
    public override bool Equals(object? other)
        => ReferenceEquals(this, other)
            || (other is ValueTimestampTimeZone t
                && dateValue == t.dateValue
                && timeNanos == t.timeNanos
                && timeZoneOffsetSeconds == t.timeZoneOffsetSeconds);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(dateValue, timeNanos, timeZoneOffsetSeconds);
}
