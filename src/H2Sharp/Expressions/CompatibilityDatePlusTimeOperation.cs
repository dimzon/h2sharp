/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under MPL 2.0 or EPL 1.0.
 * Ported from org.h2.expression.CompatibilityDatePlusTimeOperation.
 * Copyright 2026 H2Sharp contributors.
 */
using System;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>A compatibility mathematical operation with datetime values.</summary>
public class CompatibilityDatePlusTimeOperation : Operation2
{
    public CompatibilityDatePlusTimeOperation(Expression left, Expression right) : base(left, right)
    {
        TypeInfo l = left.GetTypeInfo(), r = right.GetTypeInfo();
        int t;
        switch (l.GetValueType())
        {
            case Value.TimestampTz:
                if (r.GetValueType() == Value.TimeTz)
                    throw DbException.GetUnsupportedException("TIMESTAMP WITH TIME ZONE + TIME WITH TIME ZONE");
                goto case Value.Time;
            case Value.Time:
                t = r.GetValueType() == Value.Date ? Value.Timestamp : l.GetValueType();
                break;
            case Value.TimeTz:
                if (r.GetValueType() == Value.TimeTz)
                    throw DbException.GetUnsupportedException("TIME WITH TIME ZONE + TIME WITH TIME ZONE");
                t = r.GetValueType() == Value.Date ? Value.TimestampTz : l.GetValueType();
                break;
            case Value.Timestamp:
                t = r.GetValueType() == Value.TimeTz ? Value.TimestampTz : Value.Timestamp;
                break;
            default:
                throw DbException.GetUnsupportedException(Value.GetTypeName(l.GetValueType()) + " + " + Value.GetTypeName(r.GetValueType()));
        }
        type = TypeInfo.GetTypeInfo(t, 0L, Math.Max(l.GetScale(), r.GetScale()), null);
    }

    public override bool NeedParentheses() => true;

    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        left.GetSql(builder, sqlFlags, WithParentheses).Append(" + ");
        return right.GetSql(builder, sqlFlags, WithParentheses);
    }

    public override Value GetValue(SessionLocal session)
    {
        Value l = left.GetValue(session), r = right.GetValue(session);
        if (l == ValueNull.Instance || r == ValueNull.Instance) return ValueNull.Instance;
        switch (l.GetValueType())
        {
            case Value.Time:
                if (r.GetValueType() == Value.Date)
                    return ValueTimestamp.FromDateValueAndNanos(((ValueDate)r).GetDateValue(), ((ValueTime)l).GetNanos());
                break;
            case Value.TimeTz:
                if (r.GetValueType() == Value.Date)
                {
                    ValueTimeTimeZone t = (ValueTimeTimeZone)l;
                    return ValueTimestampTimeZone.FromDateValueAndNanos(((ValueDate)r).GetDateValue(), t.GetNanos(), t.GetTimeZoneOffsetSeconds());
                }
                break;
            case Value.Timestamp:
                if (r.GetValueType() == Value.TimeTz)
                {
                    ValueTimestamp ts = (ValueTimestamp)l;
                    l = ValueTimestampTimeZone.FromDateValueAndNanos(ts.GetDateValue(), ts.GetTimeNanos(), ((ValueTimeTimeZone)r).GetTimeZoneOffsetSeconds());
                }
                break;
        }
        long[] a = DateTimeUtils.DateAndTimeFromValue(l, session);
        long dateValue = a[0], timeNanos = a[1] + (r is ValueTime time ? time.GetNanos() : ((ValueTimeTimeZone)r).GetNanos());
        if (timeNanos >= DateTimeUtils.NanosPerDay)
        {
            timeNanos -= DateTimeUtils.NanosPerDay;
            dateValue = DateTimeUtils.IncrementDateValue(dateValue);
        }
        return DateTimeUtils.DateTimeToValue(l, dateValue, timeNanos);
    }

    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        right = right.Optimize(session);
        if (left.IsConstant() && right.IsConstant()) return ValueExpression.Get(GetValue(session));
        return this;
    }
}
