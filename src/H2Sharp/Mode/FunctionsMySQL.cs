/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.mode.FunctionsMySQL.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Functions;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Modes;

/// <summary>
/// MySQL-specific compatibility functions.
/// </summary>
public sealed class FunctionsMySQL : ModeFunction
{
    // Java permits a field and a method with the same name; C# does not.
    private const int UnixTimestampFunction = 1001;
    private const int FromUnixtime = 1002;
    private const int Date = 1003;
    private const int LastInsertId = 1004;

    private static readonly Dictionary<string, FunctionInfo> Functions = new(StringComparer.Ordinal);

    static FunctionsMySQL()
    {
        Functions["UNIX_TIMESTAMP"] = new FunctionInfo(
            "UNIX_TIMESTAMP", UnixTimestampFunction, VarArgs, Value.Integer, true, false);
        Functions["FROM_UNIXTIME"] = new FunctionInfo(
            "FROM_UNIXTIME", FromUnixtime, VarArgs, Value.Varchar, true, true);
        Functions["DATE"] = new FunctionInfo("DATE", Date, 1, Value.Date, true, true);
        Functions["LAST_INSERT_ID"] = new FunctionInfo(
            "LAST_INSERT_ID", LastInsertId, VarArgs, Value.Bigint, false, false);
    }

    private static readonly string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";

    private static readonly string[] FormatReplace =
    [
        "%a", "EEE", "%b", "MMM", "%c", "MM", "%d", "dd", "%e", "d", "%H", "HH",
        "%h", "hh", "%I", "hh", "%i", "mm", "%j", "DDD", "%k", "H", "%l", "h",
        "%M", "MMMM", "%m", "MM", "%p", "a", "%r", "hh:mm:ss a", "%S", "ss",
        "%s", "ss", "%T", "HH:mm:ss", "%W", "EEEE", "%w", "F", "%Y", "yyyy",
        "%y", "yy", "%%", "%",
    ];

    /// <summary>
    /// Returns the seconds since 1970-01-01 00:00:00 UTC of the given timestamp.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="value">the timestamp</param>
    /// <returns>the seconds</returns>
    public static int UnixTimestamp(SessionLocal session, Value value)
    {
        long seconds;
        if (value is ValueTimestampTimeZone t)
        {
            seconds = (DateTimeUtils.AbsoluteDayFromDateValue(t.GetDateValue())
                    * DateTimeUtils.SecondsPerDay)
                + (t.GetTimeNanos() / DateTimeUtils.NanosPerSecond) - t.GetTimeZoneOffsetSeconds();
        }
        else
        {
            ValueTimestamp local = (ValueTimestamp)value.ConvertToType(TypeInfo.TypeTimestamp, session);
            seconds = session.CurrentTimeZone().GetEpochSecondsFromLocal(
                local.GetDateValue(), local.GetTimeNanos());
        }

        return (int)seconds;
    }

    /// <summary>Formats a Unix timestamp in the MySQL default format.</summary>
    /// <param name="seconds">the seconds since EPOCH</param>
    /// <returns>the formatted value</returns>
    public static string FromUnixTime(int seconds)
        => FromUnixTime(seconds, DateTimeFormat);

    /// <summary>Formats a Unix timestamp with a MySQL format pattern.</summary>
    /// <param name="seconds">the seconds since EPOCH</param>
    /// <param name="format">the pattern</param>
    /// <returns>the formatted value</returns>
    public static string FromUnixTime(int seconds, string format)
    {
        string converted = ConvertToSimpleDateFormat(format);
        return DateTimeOffset.FromUnixTimeSeconds(seconds)
            .ToString(converted, CultureInfo.InvariantCulture);
    }

    private static string ConvertToSimpleDateFormat(string format)
    {
        for (int i = 0; i < FormatReplace.Length; i += 2)
        {
            format = StringUtils.ReplaceAll(format, FormatReplace[i], FormatReplace[i + 1]);
        }

        return format;
    }

    /// <summary>Returns the mode-specific function of that name, or null.</summary>
    /// <param name="upperName">the uppercase name</param>
    /// <returns>the function, or null</returns>
    public static FunctionsMySQL? GetFunction(string upperName)
        => Functions.TryGetValue(upperName, out FunctionInfo? info) ? new FunctionsMySQL(info) : null;

    private FunctionsMySQL(FunctionInfo info) : base(info)
    {
    }

    /// <inheritdoc/>
    protected override void CheckParameterCount(int len)
    {
        int min, max;
        switch (Info.Type)
        {
            case UnixTimestampFunction: min = 0; max = 1; break;
            case FromUnixtime: min = 1; max = 2; break;
            case Date: min = 1; max = 1; break;
            case LastInsertId: min = 0; max = 1; break;
            default: throw DbException.GetInternalError("type=" + Info.Type);
        }

        if (len < min || len > max)
        {
            throw DbException.Get(
                Api.ErrorCode.InvalidParameterCount2, Info.Name, min + ".." + max);
        }
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        bool allConst = OptimizeArguments(session);
        type = TypeInfo.GetTypeInfo(Info.ReturnDataType);
        if (allConst)
        {
            return ValueExpression.Get(GetValue(session));
        }

        return this;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value?[]? values = GetArgumentsValues(session);
        if (values is null)
        {
            return ValueNull.Instance;
        }

        Value? v0 = GetNullOrValue(session, values, 0);
        Value? v1 = GetNullOrValue(session, values, 1);
        Value result;
        switch (Info.Type)
        {
            case UnixTimestampFunction:
                result = ValueInteger.Get(UnixTimestamp(
                    session, v0 ?? session.GetCurrentTimestamp()));
                break;
            case FromUnixtime:
                result = ValueVarchar.Get(v1 is null
                    ? FromUnixTime(v0!.GetInt())
                    : FromUnixTime(v0!.GetInt(), v1.GetString()!));
                break;
            case Date:
                if (v0!.GetValueType() == Value.Date)
                {
                    result = v0;
                }
                else
                {
                    Value converted;
                    try
                    {
                        converted = v0.ConvertToType(TypeInfo.TypeTimestamp, session);
                    }
                    catch (H2SharpException)
                    {
                        result = ValueNull.Instance;
                        break;
                    }

                    result = converted.ConvertToType(TypeInfo.TypeDate, session);
                }

                break;
            case LastInsertId:
                if (args.Length == 0)
                {
                    result = session.GetLastIdentity();
                    result = result == ValueNull.Instance
                        ? ValueBigint.Get(0L) : result.ConvertToType(TypeInfo.TypeBigint);
                }
                else
                {
                    result = v0!;
                    session.SetLastIdentity(result == ValueNull.Instance
                        ? ValueNull.Instance
                        : result.ConvertToType(TypeInfo.TypeBigint));
                }

                break;
            default:
                throw DbException.GetInternalError("type=" + Info.Type);
        }

        return result;
    }
}
