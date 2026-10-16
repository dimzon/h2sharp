using System;
using System.Collections.Generic;
using System.Numerics;

using H2Sharp.Api;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Aggregate;

/// <summary>
/// MEDIAN inverse distribution function.
///
/// Only the array path is ported: the index path (getFromIndex) needs
/// canFindNext, which no ported index provides yet, and the engine never asks
/// for it because MEDIAN is not optimizable to a quick query.
/// </summary>
internal static class Percentile
{
    ///
    /// BigDecimal value of 0.5.
    ///
    internal static readonly BigDecimal Half = BigDecimal.Parse("0.5");

    ///
    /// Gets the result from the array of values.
    ///
    /// <param name="session">the session</param>
    /// <param name="array">array with values</param>
    /// <param name="dataType">the data type</param>
    /// <param name="orderByList">ORDER BY list, null when the aggregate has none</param>
    /// <param name="percentile">argument of percentile function, or 0.5 for median</param>
    /// <param name="interpolate">whether value should be interpolated</param>
    /// <returns>the result</returns>
    internal static Value GetValue(
        SessionLocal session, Value[] array, int dataType, List<QueryOrderBy>? orderByList,
        BigDecimal percentile, bool interpolate)
    {
        CompareMode compareMode = session.GetCompareMode();
        Array.Sort(array, Comparer<Value>.Create((x, y) => session.Compare(x!, y!)));
        int count = array.Length;
        bool reverseIndex = orderByList is not null
            && (orderByList[0].sortType & SortOrder.Descending) != 0;
        BigDecimal fpRow = BigDecimal.FromLong(count - 1).Multiply(percentile);
        int rowIdx1 = (int)fpRow.LongValue();
        BigDecimal factor = fpRow.Subtract(BigDecimal.FromLong(rowIdx1));
        int rowIdx2;
        if (factor.Signum() == 0)
        {
            interpolate = false;
            rowIdx2 = rowIdx1;
        }
        else
        {
            rowIdx2 = rowIdx1 + 1;
            if (!interpolate)
            {
                if (factor.CompareTo(Half) > 0)
                {
                    rowIdx1 = rowIdx2;
                }
                else
                {
                    rowIdx2 = rowIdx1;
                }
            }
        }

        if (reverseIndex)
        {
            rowIdx1 = count - 1 - rowIdx1;
            rowIdx2 = count - 1 - rowIdx2;
        }

        Value v = array[rowIdx1];
        if (!interpolate)
        {
            return v;
        }

        return Interpolate(v, array[rowIdx2], factor, dataType, session, compareMode);
    }

    private static Value Interpolate(
        Value v0, Value v1, BigDecimal factor, int dataType, SessionLocal session, CompareMode compareMode)
    {
        if (v0.CompareTo(v1, session.CastDataProvider, compareMode) == 0)
        {
            return v0;
        }

        switch (dataType)
        {
            case Value.Tinyint:
            case Value.Smallint:
            case Value.Integer:
                return ValueNumeric.Get(
                    InterpolateDecimal(
                        BigDecimal.FromLong(v0.GetInt()), BigDecimal.FromLong(v1.GetInt()), factor));
            case Value.Bigint:
                return ValueNumeric.Get(
                    InterpolateDecimal(
                        BigDecimal.FromLong(v0.GetLong()), BigDecimal.FromLong(v1.GetLong()), factor));
            case Value.Numeric:
            case Value.Decfloat:
                return ValueNumeric.Get(InterpolateDecimal(v0.GetBigDecimal(), v1.GetBigDecimal(), factor));
            case Value.Real:
            case Value.Double:
                return ValueNumeric.Get(
                    InterpolateDecimal(
                        BigDecimal.Parse(JavaNumberFormat.DoubleToString(v0.GetDouble())),
                        BigDecimal.Parse(JavaNumberFormat.DoubleToString(v1.GetDouble())),
                        factor));
            case Value.Time:
                {
                    ValueTime t0 = (ValueTime)v0;
                    ValueTime t1 = (ValueTime)v1;
                    BigDecimal n0 = BigDecimal.FromLong(t0.GetNanos());
                    BigDecimal n1 = BigDecimal.FromLong(t1.GetNanos());
                    return ValueTime.FromNanos(InterpolateDecimal(n0, n1, factor).LongValue());
                }

            case Value.TimeTz:
                {
                    ValueTimeTimeZone t0 = (ValueTimeTimeZone)v0;
                    ValueTimeTimeZone t1 = (ValueTimeTimeZone)v1;
                    BigDecimal n0 = BigDecimal.FromLong(t0.GetNanos());
                    BigDecimal n1 = BigDecimal.FromLong(t1.GetNanos());
                    BigDecimal offset = BigDecimal.FromLong(t0.GetTimeZoneOffsetSeconds())
                        .Multiply(BigDecimal.One.Subtract(factor))
                        .Add(BigDecimal.FromLong(t1.GetTimeZoneOffsetSeconds()).Multiply(factor));
                    int intOffset = (int)offset.LongValue();
                    BigDecimal intOffsetBd = BigDecimal.FromLong(intOffset);
                    BigDecimal bd = InterpolateDecimal(n0, n1, factor);
                    if (offset.CompareTo(intOffsetBd) != 0)
                    {
                        bd = bd.Add(offset.Subtract(intOffsetBd)
                            .Multiply(BigDecimal.FromLong(DateTimeUtils.NanosPerSecond)));
                    }

                    long timeNanos = bd.LongValue();
                    if (timeNanos < 0L)
                    {
                        timeNanos += DateTimeUtils.NanosPerDay;
                        intOffset++;
                    }
                    else if (timeNanos >= DateTimeUtils.NanosPerDay)
                    {
                        timeNanos -= DateTimeUtils.NanosPerDay;
                        intOffset--;
                    }

                    return ValueTimeTimeZone.FromNanos(timeNanos, intOffset);
                }

            case Value.Date:
                {
                    ValueDate d0 = (ValueDate)v0;
                    ValueDate d1 = (ValueDate)v1;
                    BigDecimal a0 = BigDecimal.FromLong(DateTimeUtils.AbsoluteDayFromDateValue(d0.GetDateValue()));
                    BigDecimal a1 = BigDecimal.FromLong(DateTimeUtils.AbsoluteDayFromDateValue(d1.GetDateValue()));
                    return ValueDate.FromDateValue(
                        DateTimeUtils.DateValueFromAbsoluteDay(InterpolateDecimal(a0, a1, factor).LongValue()));
                }

            case Value.Timestamp:
                {
                    ValueTimestamp ts0 = (ValueTimestamp)v0;
                    ValueTimestamp ts1 = (ValueTimestamp)v1;
                    BigDecimal a0 = TimestampToDecimal(ts0.GetDateValue(), ts0.GetTimeNanos());
                    BigDecimal a1 = TimestampToDecimal(ts1.GetDateValue(), ts1.GetTimeNanos());
                    BigInteger[] dr = DivideAndRemainder(
                        TruncateToBigInteger(InterpolateDecimal(a0, a1, factor)), IntervalUtils.NanosPerDayBi);
                    long absoluteDay = (long)dr[0];
                    long timeNanos = (long)dr[1];
                    if (timeNanos < 0)
                    {
                        timeNanos += DateTimeUtils.NanosPerDay;
                        absoluteDay--;
                    }

                    return ValueTimestamp.FromDateValueAndNanos(
                        DateTimeUtils.DateValueFromAbsoluteDay(absoluteDay), timeNanos);
                }

            case Value.TimestampTz:
                {
                    ValueTimestampTimeZone ts0 = (ValueTimestampTimeZone)v0;
                    ValueTimestampTimeZone ts1 = (ValueTimestampTimeZone)v1;
                    BigDecimal a0 = TimestampToDecimal(ts0.GetDateValue(), ts0.GetTimeNanos());
                    BigDecimal a1 = TimestampToDecimal(ts1.GetDateValue(), ts1.GetTimeNanos());
                    BigDecimal offset = BigDecimal.FromLong(ts0.GetTimeZoneOffsetSeconds())
                        .Multiply(BigDecimal.One.Subtract(factor))
                        .Add(BigDecimal.FromLong(ts1.GetTimeZoneOffsetSeconds()).Multiply(factor));
                    int intOffset = (int)offset.LongValue();
                    BigDecimal intOffsetBd = BigDecimal.FromLong(intOffset);
                    BigDecimal bd = InterpolateDecimal(a0, a1, factor);
                    if (offset.CompareTo(intOffsetBd) != 0)
                    {
                        bd = bd.Add(offset.Subtract(intOffsetBd)
                            .Multiply(BigDecimal.FromLong(DateTimeUtils.NanosPerSecond)));
                    }

                    BigInteger[] dr = DivideAndRemainder(TruncateToBigInteger(bd), IntervalUtils.NanosPerDayBi);
                    long absoluteDay = (long)dr[0];
                    long timeNanos = (long)dr[1];
                    if (timeNanos < 0)
                    {
                        timeNanos += DateTimeUtils.NanosPerDay;
                        absoluteDay--;
                    }

                    return ValueTimestampTimeZone.FromDateValueAndNanos(
                        DateTimeUtils.DateValueFromAbsoluteDay(absoluteDay), timeNanos, intOffset);
                }

            case Value.IntervalYear:
            case Value.IntervalMonth:
            case Value.IntervalDay:
            case Value.IntervalHour:
            case Value.IntervalMinute:
            case Value.IntervalSecond:
            case Value.IntervalYearToMonth:
            case Value.IntervalDayToHour:
            case Value.IntervalDayToMinute:
            case Value.IntervalDayToSecond:
            case Value.IntervalHourToMinute:
            case Value.IntervalHourToSecond:
            case Value.IntervalMinuteToSecond:
                return IntervalUtils.IntervalFromAbsolute(
                    IntervalQualifiers.ValueOf(dataType - Value.IntervalYear),
                    TruncateToBigInteger(InterpolateDecimal(
                        new BigDecimal(IntervalUtils.IntervalToAbsolute((ValueInterval)v0), 0),
                        new BigDecimal(IntervalUtils.IntervalToAbsolute((ValueInterval)v1), 0),
                        factor)));
            default:
                // Use the same rules as PERCENTILE_DISC
                return factor.CompareTo(Half) > 0 ? v1 : v0;
        }
    }

    private static BigDecimal TimestampToDecimal(long dateValue, long timeNanos) =>
        new(
            BigInteger.Multiply(
                new BigInteger(DateTimeUtils.AbsoluteDayFromDateValue(dateValue)),
                IntervalUtils.NanosPerDayBi)
            + new BigInteger(timeNanos),
            0);

    private static BigDecimal InterpolateDecimal(BigDecimal d0, BigDecimal d1, BigDecimal factor) =>
        d0.Multiply(BigDecimal.One.Subtract(factor)).Add(d1.Multiply(factor));

    ///
    /// BigDecimal.toBigInteger: the integer part, truncated toward zero. The value
    /// layer does not carry it, and the only caller is the temporal interpolation
    /// above, so it lives here rather than on BigDecimal.
    ///
    private static BigInteger TruncateToBigInteger(BigDecimal value)
    {
        BigInteger unscaled = value.UnscaledValue;
        int scale = value.Scale;
        if (scale <= 0)
        {
            return scale == 0 ? unscaled : unscaled * BigInteger.Pow(10, -scale);
        }

        return unscaled / BigInteger.Pow(10, scale);
    }

    private static BigInteger[] DivideAndRemainder(BigInteger dividend, BigInteger divisor)
    {
        BigInteger remainder;
        BigInteger quotient = BigInteger.DivRem(dividend, divisor, out remainder);
        return [quotient, remainder];
    }
}
