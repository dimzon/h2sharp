/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under MPL 2.0 or EPL 1.0.
 * Ported from org.h2.expression.IntervalOperation.
 * Copyright 2026 H2Sharp contributors.
 */
using System.Numerics;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions.Functions;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>A mathematical operation with intervals.</summary>
public class IntervalOperation : Operation2
{
    public enum IntervalOpType
    {
        INTERVAL_PLUS_INTERVAL,
        INTERVAL_MINUS_INTERVAL,
        INTERVAL_DIVIDE_INTERVAL,
        DATETIME_PLUS_INTERVAL,
        DATETIME_MINUS_INTERVAL,
        INTERVAL_MULTIPLY_NUMERIC,
        INTERVAL_DIVIDE_NUMERIC,
        DATETIME_MINUS_DATETIME
    }

    private const int IntervalYearDigits = 20;
    private const int IntervalDayDigits = 32;
    private static readonly TypeInfo IntervalDivideIntervalYearType =
        TypeInfo.GetTypeInfo(Value.Numeric, IntervalYearDigits * 3, IntervalYearDigits * 2, null);
    private static readonly TypeInfo IntervalDivideIntervalDayType =
        TypeInfo.GetTypeInfo(Value.Numeric, IntervalDayDigits * 3, IntervalDayDigits * 2, null);
    private readonly IntervalOpType opType;
    private TypeInfo? forcedType;

    private static BigInteger NanosFromValue(SessionLocal session, Value v)
    {
        long[] a = DateTimeUtils.DateAndTimeFromValue(v, session);
        return new BigInteger(DateTimeUtils.AbsoluteDayFromDateValue(a[0])) * IntervalUtils.NanosPerDayBi + a[1];
    }

    public IntervalOperation(IntervalOpType opType, Expression left, Expression right, TypeInfo? forcedType)
        : this(opType, left, right)
    {
        this.forcedType = forcedType;
    }

    public IntervalOperation(IntervalOpType opType, Expression left, Expression right) : base(left, right)
    {
        this.opType = opType;
        int l = left.GetTypeInfo().GetValueType(), r = right.GetTypeInfo().GetValueType();
        switch (opType)
        {
            case IntervalOpType.INTERVAL_PLUS_INTERVAL:
            case IntervalOpType.INTERVAL_MINUS_INTERVAL:
                type = TypeInfo.GetTypeInfo(Value.GetHigherOrder(l, r));
                break;
            case IntervalOpType.INTERVAL_DIVIDE_INTERVAL:
                type = DataType.IsYearMonthIntervalType(l) ? IntervalDivideIntervalYearType : IntervalDivideIntervalDayType;
                break;
            case IntervalOpType.DATETIME_PLUS_INTERVAL:
            case IntervalOpType.DATETIME_MINUS_INTERVAL:
            case IntervalOpType.INTERVAL_MULTIPLY_NUMERIC:
            case IntervalOpType.INTERVAL_DIVIDE_NUMERIC:
                type = left.GetTypeInfo();
                break;
            case IntervalOpType.DATETIME_MINUS_DATETIME:
                if (forcedType != null) type = forcedType;
                else if ((l == Value.Time || l == Value.TimeTz) && (r == Value.Time || r == Value.TimeTz))
                    type = TypeInfo.TypeIntervalHourToSecond;
                else if (l == Value.Date && r == Value.Date) type = TypeInfo.TypeIntervalDay;
                else type = TypeInfo.TypeIntervalDayToSecond;
                break;
        }
    }

    public override bool NeedParentheses() => forcedType == null;

    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        if (forcedType != null)
        {
            GetInnerSql2(builder.Append('('), sqlFlags);
            GetForcedTypeSql(builder.Append(") "), forcedType);
        }
        else GetInnerSql2(builder, sqlFlags);
        return builder;
    }

    private void GetInnerSql2(StringBuilder builder, int sqlFlags)
    {
        left.GetSql(builder, sqlFlags, WithParentheses).Append(' ').Append(GetOperationToken()).Append(' ');
        right.GetSql(builder, sqlFlags, WithParentheses);
    }

    internal static StringBuilder GetForcedTypeSql(StringBuilder builder, TypeInfo? forcedType)
    {
        int precision = (int)forcedType!.GetPrecision(), scale = forcedType.GetScale();
        return IntervalQualifiers.AppendTypeName(IntervalQualifiers.ValueOf(forcedType.GetValueType() - Value.IntervalYear),
            builder, precision == ValueInterval.DefaultPrecision ? -1 : precision,
            scale == ValueInterval.DefaultScale ? -1 : scale, true);
    }

    private char GetOperationToken() => opType switch
    {
        IntervalOpType.INTERVAL_PLUS_INTERVAL or IntervalOpType.DATETIME_PLUS_INTERVAL => '+',
        IntervalOpType.INTERVAL_MINUS_INTERVAL or IntervalOpType.DATETIME_MINUS_INTERVAL
            or IntervalOpType.DATETIME_MINUS_DATETIME => '-',
        IntervalOpType.INTERVAL_MULTIPLY_NUMERIC => '*',
        IntervalOpType.INTERVAL_DIVIDE_INTERVAL or IntervalOpType.INTERVAL_DIVIDE_NUMERIC => '/',
        _ => throw DbException.GetInternalError("opType=" + opType)
    };

    public override Value GetValue(SessionLocal session)
    {
        Value l = left.GetValue(session), r = right.GetValue(session);
        if (l == ValueNull.Instance || r == ValueNull.Instance) return ValueNull.Instance;
        int lType = l.GetValueType(), rType = r.GetValueType();
        switch (opType)
        {
            case IntervalOpType.INTERVAL_PLUS_INTERVAL:
            case IntervalOpType.INTERVAL_MINUS_INTERVAL:
                {
                    BigInteger a1 = IntervalUtils.IntervalToAbsolute((ValueInterval)l);
                    BigInteger a2 = IntervalUtils.IntervalToAbsolute((ValueInterval)r);
                    return IntervalUtils.IntervalFromAbsolute(
                        IntervalQualifiers.ValueOf(Value.GetHigherOrder(lType, rType) - Value.IntervalYear),
                        opType == IntervalOpType.INTERVAL_PLUS_INTERVAL ? a1 + a2 : a1 - a2);
                }
            case IntervalOpType.INTERVAL_DIVIDE_INTERVAL:
                return ValueNumeric.Get(IntervalUtils.IntervalToAbsolute((ValueInterval)l))
                    .Divide(ValueNumeric.Get(IntervalUtils.IntervalToAbsolute((ValueInterval)r)), type!);
            case IntervalOpType.DATETIME_PLUS_INTERVAL:
            case IntervalOpType.DATETIME_MINUS_INTERVAL:
                return GetDateTimeWithInterval(session, l, r, lType, rType);
            case IntervalOpType.INTERVAL_MULTIPLY_NUMERIC:
            case IntervalOpType.INTERVAL_DIVIDE_NUMERIC:
                {
                    BigDecimal a1 = BigDecimal.FromBigInteger(IntervalUtils.IntervalToAbsolute((ValueInterval)l));
                    BigDecimal a2 = r.GetBigDecimal();
                    return IntervalUtils.IntervalFromAbsolute(IntervalQualifiers.ValueOf(lType - Value.IntervalYear),
                        (opType == IntervalOpType.INTERVAL_MULTIPLY_NUMERIC ? a1.Multiply(a2) : a1.Divide(a2)).ToBigInteger());
                }
            case IntervalOpType.DATETIME_MINUS_DATETIME:
                {
                    Value result;
                    if ((lType == Value.Time || lType == Value.TimeTz) && (rType == Value.Time || rType == Value.TimeTz))
                    {
                        long diff;
                        if (lType == Value.Time && rType == Value.Time)
                            diff = ((ValueTime)l).GetNanos() - ((ValueTime)r).GetNanos();
                        else
                        {
                            ValueTimeTimeZone left = (ValueTimeTimeZone)l.ConvertToType(TypeInfo.TypeTimeTz, session);
                            ValueTimeTimeZone right = (ValueTimeTimeZone)r.ConvertToType(TypeInfo.TypeTimeTz, session);
                            diff = left.GetNanos() - right.GetNanos()
                                + (right.GetTimeZoneOffsetSeconds() - left.GetTimeZoneOffsetSeconds()) * DateTimeUtils.NanosPerSecond;
                        }
                        bool negative = diff < 0;
                        if (negative) diff = -diff;
                        result = ValueInterval.From(IntervalQualifier.HourToSecond, negative,
                            diff / DateTimeUtils.NanosPerHour, diff % DateTimeUtils.NanosPerHour);
                    }
                    else if (forcedType != null && DataType.IsYearMonthIntervalType(forcedType.GetValueType()))
                    {
                        long[] dt1 = DateTimeUtils.DateAndTimeFromValue(l, session), dt2 = DateTimeUtils.DateAndTimeFromValue(r, session);
                        long dateValue1 = lType == Value.Time || lType == Value.TimeTz ? session.CurrentTimestamp().GetDateValue() : dt1[0];
                        long dateValue2 = rType == Value.Time || rType == Value.TimeTz ? session.CurrentTimestamp().GetDateValue() : dt2[0];
                        long leading = 12L * (DateTimeUtils.YearFromDateValue(dateValue1) - DateTimeUtils.YearFromDateValue(dateValue2))
                            + DateTimeUtils.MonthFromDateValue(dateValue1) - DateTimeUtils.MonthFromDateValue(dateValue2);
                        int d1 = DateTimeUtils.DayFromDateValue(dateValue1), d2 = DateTimeUtils.DayFromDateValue(dateValue2);
                        if (leading >= 0)
                        {
                            if (d1 < d2 || d1 == d2 && dt1[1] < dt2[1]) leading--;
                        }
                        else if (d1 > d2 || d1 == d2 && dt1[1] > dt2[1]) leading++;
                        bool negative = leading < 0;
                        if (negative) leading = -leading;
                        result = ValueInterval.From(IntervalQualifier.Month, negative, leading, 0L);
                    }
                    else if (lType == Value.Date && rType == Value.Date)
                    {
                        long diff = DateTimeUtils.AbsoluteDayFromDateValue(((ValueDate)l).GetDateValue())
                            - DateTimeUtils.AbsoluteDayFromDateValue(((ValueDate)r).GetDateValue());
                        bool negative = diff < 0;
                        if (negative) diff = -diff;
                        result = ValueInterval.From(IntervalQualifier.Day, negative, diff, 0L);
                    }
                    else
                    {
                        BigInteger diff = NanosFromValue(session, l) - NanosFromValue(session, r);
                        if (lType == Value.TimestampTz || rType == Value.TimestampTz)
                        {
                            l = l.ConvertToType(TypeInfo.TypeTimestampTz, session);
                            r = r.ConvertToType(TypeInfo.TypeTimestampTz, session);
                            diff += new BigInteger((((ValueTimestampTimeZone)r).GetTimeZoneOffsetSeconds()
                                - ((ValueTimestampTimeZone)l).GetTimeZoneOffsetSeconds()) * DateTimeUtils.NanosPerSecond);
                        }
                        result = IntervalUtils.IntervalFromAbsolute(IntervalQualifier.DayToSecond, diff);
                    }
                    if (forcedType != null) result = result.CastToValue(forcedType, session);
                    return result;
                }
        }
        throw DbException.GetInternalError("type=" + opType);
    }

    private Value GetDateTimeWithInterval(SessionLocal session, Value l, Value r, int lType, int rType)
    {
        switch (lType)
        {
            case Value.Time:
                if (DataType.IsYearMonthIntervalType(rType)) throw DbException.GetInternalError("type=" + rType);
                return ValueTime.FromNanos(GetTimeWithInterval(r, ((ValueTime)l).GetNanos()));
            case Value.TimeTz:
                {
                    if (DataType.IsYearMonthIntervalType(rType)) throw DbException.GetInternalError("type=" + rType);
                    ValueTimeTimeZone t = (ValueTimeTimeZone)l;
                    return ValueTimeTimeZone.FromNanos(GetTimeWithInterval(r, t.GetNanos()), t.GetTimeZoneOffsetSeconds());
                }
            case Value.Date:
            case Value.Timestamp:
            case Value.TimestampTz:
                if (DataType.IsYearMonthIntervalType(rType))
                {
                    // java.math.BigInteger.longValue keeps the low 64 bits, unlike a checked CLR cast.
                    long m = unchecked((long)(ulong)(IntervalUtils.IntervalToAbsolute((ValueInterval)r) & ulong.MaxValue));
                    if (opType == IntervalOpType.DATETIME_MINUS_INTERVAL) m = -m;
                    return DateTimeFunction.DateaddValue(session, DateTimeFunction.Month, m, l);
                }
                else
                {
                    BigInteger a2 = IntervalUtils.IntervalToAbsolute((ValueInterval)r);
                    if (lType == Value.Date)
                    {
                        BigInteger a1 = new(DateTimeUtils.AbsoluteDayFromDateValue(((ValueDate)l).GetDateValue()));
                        a2 /= IntervalUtils.NanosPerDayBi;
                        BigInteger n = opType == IntervalOpType.DATETIME_PLUS_INTERVAL ? a1 + a2 : a1 - a2;
                        return ValueDate.FromDateValue(DateTimeUtils.DateValueFromAbsoluteDay(unchecked((long)(ulong)(n & ulong.MaxValue))));
                    }
                    else
                    {
                        long[] a = DateTimeUtils.DateAndTimeFromValue(l, session);
                        long absoluteDay = DateTimeUtils.AbsoluteDayFromDateValue(a[0]), timeNanos = a[1];
                        BigInteger days = BigInteger.DivRem(a2, IntervalUtils.NanosPerDayBi, out BigInteger nanos);
                        if (opType == IntervalOpType.DATETIME_PLUS_INTERVAL)
                        {
                            absoluteDay += unchecked((long)(ulong)(days & ulong.MaxValue));
                            timeNanos += (long)nanos;
                        }
                        else
                        {
                            absoluteDay -= unchecked((long)(ulong)(days & ulong.MaxValue));
                            timeNanos -= (long)nanos;
                        }
                        if (timeNanos >= DateTimeUtils.NanosPerDay)
                        {
                            timeNanos -= DateTimeUtils.NanosPerDay;
                            absoluteDay++;
                        }
                        else if (timeNanos < 0)
                        {
                            timeNanos += DateTimeUtils.NanosPerDay;
                            absoluteDay--;
                        }
                        return DateTimeUtils.DateTimeToValue(l, DateTimeUtils.DateValueFromAbsoluteDay(absoluteDay), timeNanos);
                    }
                }
        }
        throw DbException.GetInternalError("type=" + opType);
    }

    private long GetTimeWithInterval(Value r, long nanos)
    {
        BigInteger a1 = new(nanos), a2 = IntervalUtils.IntervalToAbsolute((ValueInterval)r);
        BigInteger n = opType == IntervalOpType.DATETIME_PLUS_INTERVAL ? a1 + a2 : a1 - a2;
        if (n.Sign < 0 || n >= IntervalUtils.NanosPerDayBi)
            throw DbException.Get(ErrorCode.NumericValueOutOfRange1, n.ToString());
        return (long)n;
    }

    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        right = right.Optimize(session);
        if (left.IsConstant() && right.IsConstant()) return ValueExpression.Get(GetValue(session));
        return this;
    }
}
