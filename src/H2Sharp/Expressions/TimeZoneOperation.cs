/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.TimeZoneOperation.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>A time zone specification (AT { TIME ZONE | LOCAL }).</summary>
public sealed class TimeZoneOperation : Operation1_2
{
    /// <summary>Creates the operation.</summary>
    public TimeZoneOperation(Expression left, Expression? right) : base(left, right)
    {
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        _ = left.GetSql(builder, sqlFlags, AutoParentheses).Append(" AT ");
        if (right is not null)
        {
            _ = right.GetSql(builder.Append("TIME ZONE "), sqlFlags, AutoParentheses);
        }
        else
        {
            _ = builder.Append("LOCAL");
        }

        return builder;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value l = left.GetValue(session);
        Value a = l.ConvertToType(type!, session);
        if (a is ValueNull)
        {
            return ValueNull.Instance;
        }

        Value? b;
        if (right is null)
        {
            int t = l.GetValueType();
            if (t == Value.Time || t == Value.Timestamp)
            {
                return a;
            }

            b = null;
        }
        else
        {
            b = right.GetValue(session);
            if (b is ValueNull)
            {
                return ValueNull.Instance;
            }
        }

        if (a is ValueTimestampTimeZone timestampTimeZone)
        {
            long dateValue = timestampTimeZone.GetDateValue();
            long timeNanos = timestampTimeZone.GetTimeNanos();
            int offsetSeconds = timestampTimeZone.GetTimeZoneOffsetSeconds();
            int newOffset = b is not null
                ? ParseTimeZone(b, dateValue, timeNanos, offsetSeconds, true)
                : session.CurrentTimeZone().GetTimeZoneOffsetUtc(
                    DateTimeUtils.GetEpochSeconds(dateValue, timeNanos, offsetSeconds));
            if (offsetSeconds != newOffset)
            {
                a = DateTimeUtils.TimestampTimeZoneAtOffset(
                    dateValue, timeNanos, offsetSeconds, newOffset);
            }
        }
        else
        {
            ValueTimeTimeZone timeTimeZone = (ValueTimeTimeZone)a;
            long timeNanos = timeTimeZone.GetNanos();
            int offsetSeconds = timeTimeZone.GetTimeZoneOffsetSeconds();
            int newOffset = b is not null
                ? ParseTimeZone(b, DateTimeUtils.EpochDateValue, timeNanos, offsetSeconds, false)
                : session.CurrentTimeZone().GetTimeZoneOffsetUtc(DateTimeUtils.GetEpochSeconds(
                    session.CurrentTimestamp().GetDateValue(), timeNanos, offsetSeconds));
            if (offsetSeconds != newOffset)
            {
                timeNanos += (newOffset - offsetSeconds) * DateTimeUtils.NanosPerSecond;
                a = ValueTimeTimeZone.FromNanos(
                    DateTimeUtils.NormalizeNanosOfDay(timeNanos), newOffset);
            }
        }

        return a;
    }

    private static int ParseTimeZone(
        Value b, long dateValue, long timeNanos, int offsetSeconds, bool allowTimeZoneName)
    {
        if (DataType.IsCharacterStringType(b.GetValueType()))
        {
            TimeZoneProvider timeZone;
            try
            {
                timeZone = TimeZoneProvider.OfId(b.GetString()!);
            }
            catch (Exception)
            {
                throw DbException.GetInvalidValueException("time zone", b.GetTraceSql());
            }

            if (!allowTimeZoneName && !timeZone.HasFixedOffset())
            {
                throw DbException.GetInvalidValueException("time zone", b.GetTraceSql());
            }

            return timeZone.GetTimeZoneOffsetUtc(
                DateTimeUtils.GetEpochSeconds(dateValue, timeNanos, offsetSeconds));
        }

        return ParseInterval(b);
    }

    /// <summary>Parses a daytime interval as a time zone offset.</summary>
    /// <param name="interval">the interval</param>
    /// <returns>the time zone offset in seconds</returns>
    public static int ParseInterval(Value interval)
    {
        ValueInterval i = (ValueInterval)interval.ConvertToType(
            TypeInfo.TypeIntervalHourToSecond, null);
        long h = i.GetLeading();
        long seconds = i.GetRemaining();
        if (h > 18 || (h == 18 && seconds != 0)
            || seconds % DateTimeUtils.NanosPerSecond != 0)
        {
            throw DbException.GetInvalidValueException("time zone", i.GetTraceSql());
        }

        int newOffset = (int)((h * 3_600) + (seconds / DateTimeUtils.NanosPerSecond));
        if (i.IsNegative())
        {
            newOffset = -newOffset;
        }

        return newOffset;
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        if (right is not null)
        {
            right = right.Optimize(session);
        }

        TypeInfo leftType = left.GetTypeInfo();
        int valueType = Value.TimestampTz;
        int scale = ValueTimestamp.MaximumScale;
        int leftValueType = leftType.GetValueType();
        switch (leftValueType)
        {
            case Value.Timestamp:
            case Value.TimestampTz:
                scale = leftType.GetScale();
                break;
            case Value.Time:
            case Value.TimeTz:
                valueType = Value.TimeTz;
                scale = leftType.GetScale();
                break;
            default:
                {
                    StringBuilder builder = left.GetSql(
                        new StringBuilder(), HasSql.TraceSqlFlags, AutoParentheses);
                    int offset = builder.Length;
                    _ = builder.Append(" AT ");
                    if (right is not null)
                    {
                        _ = right.GetSql(
                            builder.Append("TIME ZONE "), HasSql.TraceSqlFlags, AutoParentheses);
                    }
                    else
                    {
                        _ = builder.Append("LOCAL");
                    }

                    throw DbException.GetSyntaxError(builder.ToString(), offset, "time, timestamp");
                }
        }

        type = TypeInfo.GetTypeInfo(valueType, -1L, scale, null);
        if (left.IsConstant()
            && (leftValueType == Value.TimeTz || leftValueType == Value.TimestampTz)
            && right is not null && right.IsConstant())
        {
            return ValueExpression.Get(GetValue(session));
        }

        return this;
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        if (visitor.GetVisitorType() == ExpressionVisitor.Deterministic)
        {
            if (right is null)
            {
                return false;
            }

            int leftValueType = left.GetTypeInfo().GetValueType();
            if (leftValueType == Value.Time || leftValueType == Value.Timestamp)
            {
                return false;
            }
        }

        return left.IsEverything(visitor) && (right is null || right.IsEverything(visitor));
    }
}
