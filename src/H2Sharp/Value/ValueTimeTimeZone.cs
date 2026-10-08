/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueTimeTimeZone.
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
/// Implementation of the TIME WITH TIME ZONE data type: a time of day and the offset it is written
/// in.
/// </summary>
/// <remarks>
/// Two values of this type are compared by the instant they stand for, not by the time and the
/// offset: <c>10:00+01</c> and <c>09:00+00</c> are the same moment and must compare equal. That is
/// why <see cref="CompareTypeSafe"/> subtracts the offset before comparing and not after.
/// </remarks>
public sealed class ValueTimeTimeZone : Value
{
    /// <summary>
    /// The default precision and display size of the textual representation.
    /// Example: 10:00:00+01
    /// </summary>
    public const int DefaultPrecision = 14;

    /// <summary>
    /// The maximum precision and display size of the textual representation.
    /// Example: 10:00:00.123456789+01:30
    /// </summary>
    public const int MaximumPrecision = 24;

    /// <summary>
    /// The default scale for time with time zone.
    /// </summary>
    public const int DefaultScale = ValueTime.DefaultScale;

    /// <summary>
    /// The maximum scale for time with time zone.
    /// </summary>
    public const int MaximumScale = ValueTime.MaximumScale;

    /// <summary>
    /// The largest offset in either direction, which is eighteen hours: some zones have been further
    /// from Greenwich than that, and JSR-310 settles on eighteen in both directions, so this does
    /// too.
    /// </summary>
    private const int MaxOffsetSeconds = 18 * 60 * 60;

    private readonly long nanos;

    private readonly int timeZoneOffsetSeconds;

    private ValueTimeTimeZone(long nanos, int timeZoneOffsetSeconds)
    {
        this.nanos = nanos;
        this.timeZoneOffsetSeconds = timeZoneOffsetSeconds;
    }

    /// <summary>
    /// Get or create a time with time zone value.
    /// </summary>
    /// <param name="nanos">the nanoseconds since midnight, in the zone the offset names</param>
    /// <param name="timeZoneOffsetSeconds">the offset from Greenwich in seconds</param>
    /// <returns>the value</returns>
    /// <exception cref="H2SharpException">if the time or the offset is outside what a time may be</exception>
    public static ValueTimeTimeZone FromNanos(long nanos, int timeZoneOffsetSeconds)
    {
        if (nanos < 0L || nanos >= DateTimeUtils.NanosPerDay)
        {
            throw DbException.Get(
                ErrorCode.InvalidDatetimeConstant2,
                "TIME WITH TIME ZONE",
                DateTimeUtils.AppendTime(new StringBuilder(), nanos).ToString());
        }

        if (timeZoneOffsetSeconds < -MaxOffsetSeconds || timeZoneOffsetSeconds > MaxOffsetSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeZoneOffsetSeconds), "timeZoneOffsetSeconds " + timeZoneOffsetSeconds);
        }

        return (ValueTimeTimeZone)Value.Cache(new ValueTimeTimeZone(nanos, timeZoneOffsetSeconds))!;
    }

    /// <summary>
    /// Parse a string to a time with time zone.
    /// </summary>
    /// <param name="s">the string to parse</param>
    /// <param name="provider">where the default zone comes from</param>
    /// <returns>the time</returns>
    /// <exception cref="H2SharpException">if the text is not a time with a zone</exception>
    public static ValueTimeTimeZone Parse(string s, CastDataProvider? provider)
    {
        try
        {
            return (ValueTimeTimeZone)DateTimeUtils.ParseTime(s, provider, true);
        }
        catch (Exception e)
            when (e is ArgumentException or FormatException or ArithmeticException or NotSupportedException)
        {
            throw DbException.Get(ErrorCode.InvalidDatetimeConstant2, e, "TIME WITH TIME ZONE", s);
        }
    }

    /// <summary>
    /// The nanoseconds since midnight.
    /// </summary>
    /// <returns>the nanoseconds</returns>
    public long GetNanos() => nanos;

    /// <summary>
    /// The offset from Greenwich in seconds.
    /// </summary>
    /// <returns>the offset</returns>
    public int GetTimeZoneOffsetSeconds() => timeZoneOffsetSeconds;

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => TypeInfo.TypeTimeTz;

    /// <inheritdoc/>
    public override int GetValueType() => Value.TimeTz;

    /// <inheritdoc/>
    public override int GetMemory() => 32;

    /// <inheritdoc/>
    public override string? GetString()
        => ToStringBuilder(new StringBuilder(MaximumPrecision)).ToString();

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => ToStringBuilder(_ = builder.Append("TIME WITH TIME ZONE '")).Append('\'');

    private StringBuilder ToStringBuilder(StringBuilder builder)
        => DateTimeUtils.AppendTimeZone(DateTimeUtils.AppendTime(builder, nanos), timeZoneOffsetSeconds);

    /// <inheritdoc/>
    public override int CompareTypeSafe(Value o, CompareMode mode, CastDataProvider? provider)
    {
        ValueTimeTimeZone t = (ValueTimeTimeZone)o;
        return (nanos - (timeZoneOffsetSeconds * DateTimeUtils.NanosPerSecond))
            .CompareTo(t.nanos - (t.timeZoneOffsetSeconds * DateTimeUtils.NanosPerSecond));
    }

    /// <inheritdoc/>
    public override bool Equals(object? other)
        => ReferenceEquals(this, other)
            || (other is ValueTimeTimeZone t && nanos == t.nanos && timeZoneOffsetSeconds == t.timeZoneOffsetSeconds);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(nanos, timeZoneOffsetSeconds);
}
