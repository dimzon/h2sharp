/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueInterval.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Values;

/// <summary>
/// Implementation of the INTERVAL data types, of which there are twelve: one per qualifier.
/// </summary>
/// <remarks>
/// The value is a leading field, the rest of the fields as one number, and a sign; the qualifier
/// says which of those the two numbers are. That is the whole shape of it, and everything else in
/// the class is a consequence: printing depends on the qualifier, adding goes through the absolute
/// value of the interval, and the sign is a separate thing because "negative zero" is not a number
/// but an interval is allowed to have one.
/// </remarks>
public sealed class ValueInterval : Value
{
    /// <summary>
    /// The default leading field precision for intervals.
    /// </summary>
    public const int DefaultPrecision = 2;

    /// <summary>
    /// The maximum leading field precision for intervals.
    /// </summary>
    public const int MaximumPrecision = 18;

    /// <summary>
    /// The default scale for intervals with seconds.
    /// </summary>
    public const int DefaultScale = 6;

    /// <summary>
    /// The maximum scale for intervals with seconds.
    /// </summary>
    public const int MaximumScale = 9;

    /// <summary>
    /// What the remaining half of an interval counts, one entry per qualifier from SECOND on.
    /// </summary>
    /// <remarks>
    /// The entry is the number of the remaining half's unit in the unit of the absolute value of
    /// that qualifier: the remaining half of a SECOND is nanoseconds, of a DAY TO SECOND is
    /// nanoseconds of a day, and of a YEAR TO MONTH is months of a year.
    /// </remarks>
    private static readonly long[] Multipliers =
    [
        DateTimeUtils.NanosPerSecond,
        12,
        24,
        24 * 60,
        DateTimeUtils.NanosPerDay,
        60,
        DateTimeUtils.NanosPerHour,
        DateTimeUtils.NanosPerMinute,
    ];

    private readonly int valueType;

    private readonly bool negative;

    private readonly long leading;

    private readonly long remaining;

    private ValueInterval(int type, bool negative, long leading, long remaining)
    {
        this.valueType = type;
        this.negative = negative;
        this.leading = leading;
        this.remaining = remaining;
    }

    /// <summary>
    /// Create an interval value.
    /// </summary>
    /// <param name="qualifier">the qualifier, which is what decides the type</param>
    /// <param name="negative">whether the interval is negative</param>
    /// <param name="leading">the value of the leading field</param>
    /// <param name="remaining">the values of all the remaining fields</param>
    /// <returns>the value</returns>
    public static ValueInterval From(
        IntervalQualifier qualifier, bool negative, long leading, long remaining)
    {
        negative = IntervalUtils.ValidateInterval(qualifier, negative, leading, remaining);
        return (ValueInterval)Value.Cache(
            new ValueInterval(Value.IntervalYear + (int)qualifier, negative, leading, remaining))!;
    }

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => Dialect.AppendInterval(builder, this, sqlFlags);

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => TypeInfo.GetTypeInfo(valueType);

    /// <inheritdoc/>
    public override int GetValueType() => valueType;

    /// <inheritdoc/>
    public override int GetMemory() => 48;

    /// <summary>
    /// Whether the leading field is wider than the width asked for.
    /// </summary>
    /// <param name="prec">the width asked for</param>
    /// <returns>true if the value fits in that width</returns>
    internal bool CheckPrecision(long prec)
    {
        if (prec < MaximumPrecision)
        {
            for (long l = leading, p = 1, precision = 0; l >= p; p *= 10)
            {
                if (++precision > prec)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Converts this value to the type a target asks for, truncating or refusing by the target's
    /// scale and precision.
    /// </summary>
    /// <remarks>
    /// This is the scale half of a conversion to a narrower INTERVAL: the remaining fields are
    /// rounded to the declared scale, and if rounding them up carries into the leading field —
    /// 59.9996 seconds at scale 2 is a minute, not 59.99 — the leading field takes the carry and
    /// the rest starts again at zero. An interval whose type has no fractional field at all is
    /// not converted but refused, which is the <c>convertScale</c> label in the reference: there
    /// is nothing to scale.
    /// </remarks>
    /// <param name="targetType">the type to convert to</param>
    /// <param name="column">the column, for the error message</param>
    /// <returns>the converted value</returns>
    /// <exception cref="H2SharpException">if the leading field is wider than the target allows</exception>
    internal ValueInterval SetPrecisionAndScale(TypeInfo targetType, object? column)
    {
        int targetScale = targetType.GetScale();
        ValueInterval v = this;

        if (targetScale < MaximumScale)
        {
            long range;
            switch (valueType)
            {
                case Value.IntervalSecond:
                    range = DateTimeUtils.NanosPerSecond;
                    break;

                case Value.IntervalDayToSecond:
                    range = DateTimeUtils.NanosPerDay;
                    break;

                case Value.IntervalHourToSecond:
                    range = DateTimeUtils.NanosPerHour;
                    break;

                case Value.IntervalMinuteToSecond:
                    range = DateTimeUtils.NanosPerMinute;
                    break;

                default:
                    range = 0;
                    break;
            }

            // Zero means the type has no fractional field, and the reference leaves the label
            // rather than scaling by nothing. Written as a range of zero rather than as an
            // early return so the carry below is not duplicated.
            if (range != 0)
            {
                long l = leading;
                long r = DateTimeUtils.ConvertScale(
                    remaining,
                    targetScale,
                    l == 999_999_999_999_999_999L ? range : long.MaxValue);
                if (r != remaining)
                {
                    if (r >= range)
                    {
                        l++;
                        r -= range;
                    }

                    v = From(GetQualifier(), IsNegative(), l, r);
                }
            }
        }

        if (!v.CheckPrecision(targetType.GetPrecision()))
        {
            throw v.GetValueTooLongException(targetType, column);
        }

        return v;
    }

    /// <inheritdoc/>
    public override string? GetString()
        => IntervalUtils
            .AppendInterval(new StringBuilder(), GetQualifier(), negative, leading, remaining)
            .ToString();

    /// <inheritdoc/>
    public override long GetLong()
    {
        long l = leading;
        if (valueType >= Value.IntervalSecond && remaining != 0L
            && remaining >= Multipliers[valueType - Value.IntervalSecond] >> 1)
        {
            l++;
        }

        return negative ? -l : l;
    }

    /// <inheritdoc/>
    public override BigInteger GetBigInteger() => new(GetLong());

    /// <inheritdoc/>
    public override H2Sharp.Util.BigDecimal GetBigDecimal()
    {
        if (valueType < Value.IntervalSecond || remaining == 0L)
        {
            return H2Sharp.Util.BigDecimal.FromLong(negative ? -leading : leading);
        }

        H2Sharp.Util.BigDecimal m =
            H2Sharp.Util.BigDecimal.FromLong(Multipliers[valueType - Value.IntervalSecond]);
        H2Sharp.Util.BigDecimal bd = H2Sharp.Util.BigDecimal
            .FromLong(leading)
            .Add(H2Sharp.Util.BigDecimal.FromLong(remaining)
                .Divide(m, m.Precision(), RoundingMode.HalfDown))
            .StripTrailingZeros();
        return negative ? bd.Negate() : bd;
    }

    /// <inheritdoc/>
    public override float GetFloat()
    {
        if (valueType < Value.IntervalSecond || remaining == 0L)
        {
            return negative ? -leading : leading;
        }

        return (float)GetBigDecimal().ToDouble();
    }

    /// <inheritdoc/>
    public override double GetDouble()
    {
        if (valueType < Value.IntervalSecond || remaining == 0L)
        {
            return negative ? -leading : leading;
        }

        return GetBigDecimal().ToDouble();
    }

    /// <summary>
    /// The interval qualifier, which is what says how to read the two numbers.
    /// </summary>
    /// <returns>the qualifier</returns>
    public IntervalQualifier GetQualifier() => (IntervalQualifier)(valueType - Value.IntervalYear);

    /// <summary>
    /// Whether the interval is negative.
    /// </summary>
    /// <returns>true if it is negative</returns>
    public bool IsNegative() => negative;

    /// <summary>
    /// The value of the leading field, which for an interval of seconds is the whole seconds and
    /// for a YEAR TO MONTH is the years.
    /// </summary>
    /// <returns>the leading field</returns>
    public long GetLeading() => leading;

    /// <summary>
    /// The value of the remaining fields put together, which for an interval of seconds is the
    /// nanoseconds of that second.
    /// </summary>
    /// <returns>the remaining fields</returns>
    public long GetRemaining() => remaining;

    /// <inheritdoc/>
    public override bool Equals(object? other)
        => ReferenceEquals(this, other)
            || (other is ValueInterval interval
                && valueType == interval.valueType
                && negative == interval.negative
                && leading == interval.leading
                && remaining == interval.remaining);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(valueType, negative, leading, remaining);

    /// <inheritdoc/>
    public override int CompareTypeSafe(Value v, CompareMode mode, CastDataProvider? provider)
    {
        ValueInterval other = (ValueInterval)v;
        if (negative != other.negative)
        {
            return negative ? -1 : 1;
        }

        int cmp = leading.CompareTo(other.leading);
        if (cmp == 0)
        {
            cmp = remaining.CompareTo(other.remaining);
        }

        return negative ? -cmp : cmp;
    }

    /// <inheritdoc/>
    public override int GetSignum() => negative ? -1 : leading == 0L && remaining == 0L ? 0 : 1;

    /// <inheritdoc/>
    public override Value Add(Value v)
        => IntervalUtils.IntervalFromAbsolute(
            GetQualifier(),
            IntervalUtils.IntervalToAbsolute(this) + IntervalUtils.IntervalToAbsolute((ValueInterval)v));

    /// <inheritdoc/>
    public override Value Subtract(Value v)
        => IntervalUtils.IntervalFromAbsolute(
            GetQualifier(),
            IntervalUtils.IntervalToAbsolute(this) - IntervalUtils.IntervalToAbsolute((ValueInterval)v));

    /// <inheritdoc/>
    public override Value Negate()
    {
        if (leading == 0L && remaining == 0L)
        {
            return this;
        }

        return (ValueInterval)Value.Cache(new ValueInterval(valueType, !negative, leading, remaining))!;
    }
}
