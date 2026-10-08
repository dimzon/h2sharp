/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueTime.
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
/// Implementation of the TIME data type.
/// </summary>
/// <remarks>
/// A time is a count of nanoseconds from midnight, and the arithmetic here is on that count: two
/// times added is a count of nanoseconds that may be more than a day, and <see cref="FromNanos"/>
/// refuses it, which is the difference between a time and an interval of time and the reason this
/// class has no modulus.
/// </remarks>
public sealed class ValueTime : Value
{
    /// <summary>
    /// The default precision and display size of the textual representation of a time.
    /// Example: 10:00:00
    /// </summary>
    public const int DefaultPrecision = 8;

    /// <summary>
    /// The maximum precision and display size of the textual representation of a time.
    /// Example: 10:00:00.123456789
    /// </summary>
    public const int MaximumPrecision = 18;

    /// <summary>
    /// The default scale for time.
    /// </summary>
    public const int DefaultScale = 0;

    /// <summary>
    /// The maximum scale for time.
    /// </summary>
    public const int MaximumScale = 9;

    /// <summary>
    /// The twenty-four times of a whole hour, which are the times a query meets most.
    /// </summary>
    private static readonly ValueTime[] StaticCache = BuildStaticCache();

    /// <summary>
    /// The nanoseconds since midnight.
    /// </summary>
    private readonly long nanos;

    private ValueTime(long nanos)
    {
        this.nanos = nanos;
    }

    /// <summary>
    /// Get or create a time value.
    /// </summary>
    /// <param name="nanos">the nanoseconds since midnight</param>
    /// <returns>the value</returns>
    /// <exception cref="H2SharpException">if the time is not within a day</exception>
    public static ValueTime FromNanos(long nanos)
    {
        if (nanos < 0L || nanos >= DateTimeUtils.NanosPerDay)
        {
            throw DbException.Get(
                ErrorCode.InvalidDatetimeConstant2,
                "TIME",
                DateTimeUtils.AppendTime(new StringBuilder(), nanos).ToString());
        }

        if (nanos % DateTimeUtils.NanosPerHour == 0L)
        {
            return StaticCache[(int)(nanos / DateTimeUtils.NanosPerHour)];
        }

        return (ValueTime)Value.Cache(new ValueTime(nanos))!;
    }

    /// <summary>
    /// Parse a string to a time.
    /// </summary>
    /// <param name="s">the string to parse</param>
    /// <param name="provider">
    /// the cast information provider, which a time without a zone never needs and a time with one
    /// does; see <see cref="DateTimeUtils.ParseTime"/>
    /// </param>
    /// <returns>the time</returns>
    /// <exception cref="H2SharpException">if the text is not a time</exception>
    public static ValueTime Parse(string s, CastDataProvider? provider)
    {
        try
        {
            return (ValueTime)DateTimeUtils.ParseTime(s, provider, false);
        }
        catch (Exception e) when (e is ArgumentException or FormatException or ArithmeticException or NotSupportedException)
        {
            throw DbException.Get(ErrorCode.InvalidDatetimeConstant2, e, "TIME", s);
        }
    }

    /// <summary>
    /// The nanoseconds since midnight.
    /// </summary>
    /// <returns>the nanoseconds</returns>
    public long GetNanos() => nanos;

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => TypeInfo.TypeTime;

    /// <inheritdoc/>
    public override int GetValueType() => Value.Time;

    /// <inheritdoc/>
    public override string? GetString()
        => DateTimeUtils.AppendTime(new StringBuilder(MaximumPrecision), nanos).ToString();

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => DateTimeUtils.AppendTime(_ = builder.Append("TIME '"), nanos).Append('\'');

    /// <inheritdoc/>
    public override int CompareTypeSafe(Value o, CompareMode mode, CastDataProvider? provider)
        => nanos.CompareTo(((ValueTime)o).nanos);

    /// <inheritdoc/>
    public override bool Equals(object? other)
        => ReferenceEquals(this, other)
            || (other is ValueTime time && nanos == time.nanos);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(nanos);

    /// <inheritdoc/>
    public override Value Add(Value v) => FromNanos(nanos + ((ValueTime)v).GetNanos());

    /// <inheritdoc/>
    public override Value Subtract(Value v) => FromNanos(nanos - ((ValueTime)v).GetNanos());

    /// <inheritdoc/>
    public override Value Multiply(Value v) => FromNanos((long)(nanos * v.GetDouble()));

    /// <inheritdoc/>
    public override Value Divide(Value v, TypeInfo quotientType) => FromNanos((long)(nanos / v.GetDouble()));

    private static ValueTime[] BuildStaticCache()
    {
        ValueTime[] cache = new ValueTime[24];
        for (int hour = 0; hour < 24; hour++)
        {
            cache[hour] = new ValueTime(hour * DateTimeUtils.NanosPerHour);
        }

        return cache;
    }
}
