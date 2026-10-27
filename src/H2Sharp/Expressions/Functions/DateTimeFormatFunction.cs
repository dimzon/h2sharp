/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.DateTimeFormatFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * .NET's DateTime format strings are used for the pattern. The common H2
 * patterns are the same; unsupported Java-only fields surface as a parse or
 * format error rather than being silently translated.
 */
using System;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>FORMATDATETIME and PARSEDATETIME.</summary>
public sealed class DateTimeFormatFunction : FunctionN
{
    /// <summary>FORMATDATETIME.</summary>
    public const int FormatDateTime = 0;

    /// <summary>PARSEDATETIME.</summary>
    public const int ParseDateTime = FormatDateTime + 1;

    private static readonly string[] Names = ["FORMATDATETIME", "PARSEDATETIME"];

    private readonly int function;

    /// <summary>Creates the function.</summary>
    public DateTimeFormatFunction(int function) : base([ValueExpression.Null, ValueExpression.Null, ValueExpression.Null, ValueExpression.Null])
        => this.function = function;

    /// <inheritdoc/>
    public override string GetName() => Names[function];

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value first = args[0].GetValue(session);
        if (first is ValueNull)
        {
            return ValueNull.Instance;
        }

        Value second = args[1].GetValue(session);
        if (second is ValueNull)
        {
            return ValueNull.Instance;
        }

        string format = second.GetString()!;
        string? locale = argsCount > 2 ? args[2].GetValue(session).GetString() : null;
        string? zone = argsCount > 3 ? args[3].GetValue(session).GetString() : null;
        return function == FormatDateTime
            ? ValueVarchar.Get(Format(first, format, locale, zone), session)
            : Parse(first.GetString()!, format, locale, zone);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        bool allConst = OptimizeArguments(session, true);
        type = function == FormatDateTime ? TypeInfo.TypeVarchar : TypeInfo.TypeTimestampTz;
        if (allConst && !session.IsParsingCreateView())
        {
            return TypedValueExpression.GetTypedIfNull(GetValue(session), type);
        }

        return this;
    }

    /// <summary>Formats a date-time value.</summary>
    public static string Format(Value value, string format, string? locale, string? timeZone)
    {
        CultureInfo culture = GetCulture(locale);
        switch (value.GetValueType())
        {
            case Value.TimestampTz:
                {
                    DateTimeOffset timestamp = (DateTimeOffset)H2ValueMapper.ToObject(
                        value.ConvertToType(TypeInfo.TypeTimestampTz))!;
                    if (timeZone is not null)
                    {
                        timestamp = TimeZoneInfo.ConvertTime(timestamp, GetTimeZone(timeZone));
                    }

                    return timestamp.ToString(format, culture);
                }

            case Value.Date:
            case Value.Timestamp:
                return ((DateTime)H2ValueMapper.ToObject(value.ConvertToType(TypeInfo.TypeTimestamp))!)
                    .ToString(format, culture);

            default:
                throw DbException.GetInvalidValueException("dateTime", value.GetTraceSql());
        }
    }

    /// <summary>Parses a date-time value.</summary>
    public static Value Parse(string text, string format, string? locale, string? timeZone)
    {
        CultureInfo culture = GetCulture(locale);
        try
        {
            DateTime parsed = DateTime.ParseExact(text, format, culture, DateTimeStyles.None);
            int offset = timeZone is null
                ? 0
                : (int)GetTimeZone(timeZone).GetUtcOffset(parsed).TotalSeconds;
            return ValueTimestampTimeZone.FromDateValueAndNanos(
                DateTimeUtils.DateValue(parsed.Year, parsed.Month, parsed.Day),
                parsed.TimeOfDay.Ticks * 100L,
                offset);
        }
        catch (FormatException e)
        {
            throw DbException.Get(Api.ErrorCode.ParseError1, e, text);
        }
    }

    private static CultureInfo GetCulture(string? locale)
        => locale is null ? CultureInfo.CurrentCulture : CultureInfo.GetCultureInfo(locale);

    private static TimeZoneInfo GetTimeZone(string timeZone)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        }
        catch (TimeZoneNotFoundException e)
        {
            throw DbException.GetInvalidValueException(e, "timeZone", timeZone);
        }
        catch (InvalidTimeZoneException e)
        {
            throw DbException.GetInvalidValueException(e, "timeZone", timeZone);
        }
    }
}
