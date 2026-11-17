/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.mode.CompatibilityDateTimeValueFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The current date-time of the session, converted from the session zone to the default zone:
 * SYSDATE answers the local date and time without a zone, SYSTIMESTAMP keeps the zone.
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Functions;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Modes;

/// <summary>
/// The current date-time value function of the compatibility modes.
/// </summary>
public sealed class CompatibilityDateTimeValueFunction : Expression, INamedExpression
{
    /// <summary>The function SYSDATE.</summary>
    public const int Sysdate = 0;

    /// <summary>The function SYSTIMESTAMP.</summary>
    public const int Systimestamp = Sysdate + 1;

    private static readonly string[] Names = ["SYSDATE", "SYSTIMESTAMP"];

    private readonly int function;
    private readonly int scale;
    private readonly TypeInfo type;

    /// <summary>
    /// Creates the function.
    /// </summary>
    /// <param name="function">one of the constants of this class</param>
    /// <param name="scale">the scale, or a negative number for the default of the type</param>
    public CompatibilityDateTimeValueFunction(int function, int scale)
    {
        this.function = function;
        this.scale = scale;
        type = function == Sysdate
            ? TypeInfo.GetTypeInfo(Value.Timestamp, 0L, 0, null)
            : TypeInfo.GetTypeInfo(Value.TimestampTz, 0L, scale, null);
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        ValueTimestampTimeZone v = session.CurrentTimestamp();
        long dateValue = v.GetDateValue();
        long timeNanos = v.GetTimeNanos();
        int offsetSeconds = v.GetTimeZoneOffsetSeconds();
        int newOffset = TimeZoneProvider.GetDefault().GetTimeZoneOffsetUtc(
            DateTimeUtils.GetEpochSeconds(dateValue, timeNanos, offsetSeconds));
        if (offsetSeconds != newOffset)
        {
            v = DateTimeUtils.TimestampTimeZoneAtOffset(dateValue, timeNanos, offsetSeconds, newOffset);
        }

        if (function == Sysdate)
        {
            return ValueTimestamp.FromDateValueAndNanos(
                v.GetDateValue(), v.GetTimeNanos() / DateTimeUtils.NanosPerSecond * DateTimeUtils.NanosPerSecond);
        }

        return v.CastToValue(type, session);
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        _ = builder.Append(GetName());
        if (scale >= 0)
        {
            _ = builder.Append('(').Append(scale).Append(')');
        }

        return builder;
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
        => visitor.GetVisitorType() != ExpressionVisitor.Deterministic;

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => type;

    /// <inheritdoc/>
    public override int GetCost() => 1;

    /// <inheritdoc/>
    public string GetName() => Names[function];
}
