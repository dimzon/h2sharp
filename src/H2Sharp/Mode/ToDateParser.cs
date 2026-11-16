/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.mode.ToDateParser.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Emulates Oracle's TO_DATE/TO_TIMESTAMP: holds the input data and the format, and drives the
 * format tokens of <see cref="ToDateTokenizer"/> until both are consumed.
 */
using System;
using System.Globalization;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Modes;

/// <summary>
/// Emulates Oracle's TO_DATE function: holds and handles the input data of the TO_DATE method.
/// </summary>
public sealed class ToDateParser
{
    /// <summary>The configuration of the date parser.</summary>
    private enum ConfigParam
    {
        /// <summary>TO_DATE.</summary>
        ToDate,

        /// <summary>TO_TIMESTAMP.</summary>
        ToTimestamp,

        /// <summary>TO_TIMESTAMP_TZ.</summary>
        ToTimestampTz,
    }

    /// <summary>The default Oracle format of a configuration parameter.</summary>
    /// <param name="param">the parameter</param>
    /// <returns>the default format</returns>
    private static string GetDefaultFormatStr(ConfigParam param) => param switch
    {
        ConfigParam.ToDate => "DD MON YYYY",
        ConfigParam.ToTimestamp => "DD MON YYYY HH:MI:SS",
        ConfigParam.ToTimestampTz => "DD MON YYYY HH:MI:SS TZR",
        _ => throw new ArgumentOutOfRangeException(nameof(param), param, null),
    };

    private readonly SessionLocal session;

    private readonly string unmodifiedInputStr;
    private readonly string unmodifiedFormatStr;
    private readonly ConfigParam functionName;
    private string inputStr;
    private string formatStr;

    private bool doyValid;
    private bool absoluteDayValid;
    private bool hour12Valid;
    private bool timeZoneHMValid;

    private bool bc;

    private long absoluteDay;

    private int year;
    private int month;
    private int day = 1;

    private int dayOfYear;

    private int hour;
    private int minute;
    private int second;
    private int nanos;

    private int hour12;

    private bool isAM = true;

    private TimeZoneProvider? timeZone;

    private int timeZoneHour;
    private int timeZoneMinute;

    private int currentYear;
    private int currentMonth;

    private ToDateParser(SessionLocal session, ConfigParam functionName, string input, string? format)
    {
        this.session = session;
        this.functionName = functionName;
        inputStr = input.Trim();
        // Keep a copy.
        unmodifiedInputStr = inputStr;
        formatStr = string.IsNullOrEmpty(format)
            ? GetDefaultFormatStr(functionName) // default Oracle format.
            : format.Trim();
        // Keep a copy.
        unmodifiedFormatStr = formatStr;
    }

    private static ToDateParser GetTimestampParser(
        SessionLocal session, ConfigParam param, string input, string? format)
    {
        ToDateParser result = new(session, param, input, format);
        Parse(result);
        return result;
    }

    private ValueTimestamp GetResultingValue()
    {
        long dateValue;
        if (absoluteDayValid)
        {
            dateValue = DateTimeUtils.DateValueFromAbsoluteDay(absoluteDay);
        }
        else
        {
            int resultYear = year == 0 ? GetCurrentYear() : year;
            if (bc)
            {
                resultYear = 1 - resultYear;
            }

            if (doyValid)
            {
                dateValue = DateTimeUtils.DateValueFromAbsoluteDay(
                    DateTimeUtils.AbsoluteDayFromYear(resultYear) + dayOfYear - 1);
            }
            else
            {
                int resultMonth = month == 0 ? GetCurrentMonth() : month;
                dateValue = DateTimeUtils.DateValue(resultYear, resultMonth, day);
            }
        }

        int resultHour;
        if (hour12Valid)
        {
            resultHour = hour12 % 12;
            if (!isAM)
            {
                resultHour += 12;
            }
        }
        else
        {
            resultHour = hour;
        }

        long timeNanos = (((resultHour * 60) + minute) * 60 + second) * 1_000_000_000L + nanos;
        return ValueTimestamp.FromDateValueAndNanos(dateValue, timeNanos);
    }

    private ValueTimestampTimeZone GetResultingValueWithTimeZone()
    {
        ValueTimestamp ts = GetResultingValue();
        long dateValue = ts.GetDateValue();
        long timeNanos = ts.GetTimeNanos();
        int offset;
        if (timeZoneHMValid)
        {
            offset = (timeZoneHour * 60
                + (timeZoneHour >= 0 ? timeZoneMinute : -timeZoneMinute)) * 60;
        }
        else
        {
            offset = (timeZone ?? session.CurrentTimeZone())
                .GetTimeZoneOffsetLocal(dateValue, timeNanos);
        }

        return ValueTimestampTimeZone.FromDateValueAndNanos(dateValue, ts.GetTimeNanos(), offset);
    }

    internal string GetInputStr() => inputStr;

    internal string GetFormatStr() => formatStr;

    internal string GetFunctionName() => functionName.ToString().ToUpperInvariant();

    private void QueryCurrentYearAndMonth()
    {
        long dateValue = session.CurrentTimestamp().GetDateValue();
        currentYear = DateTimeUtils.YearFromDateValue(dateValue);
        currentMonth = DateTimeUtils.MonthFromDateValue(dateValue);
    }

    internal int GetCurrentYear()
    {
        if (currentYear == 0)
        {
            QueryCurrentYearAndMonth();
        }

        return currentYear;
    }

    internal int GetCurrentMonth()
    {
        if (currentMonth == 0)
        {
            QueryCurrentYearAndMonth();
        }

        return currentMonth;
    }

    internal void SetAbsoluteDay(int absoluteDay)
    {
        doyValid = false;
        absoluteDayValid = true;
        this.absoluteDay = absoluteDay;
    }

    internal void SetBC(bool bc)
    {
        doyValid = false;
        absoluteDayValid = false;
        this.bc = bc;
    }

    internal void SetYear(int year)
    {
        doyValid = false;
        absoluteDayValid = false;
        this.year = year;
    }

    internal void SetMonth(int month)
    {
        doyValid = false;
        absoluteDayValid = false;
        this.month = month;
        if (year == 0)
        {
            year = 1970;
        }
    }

    internal void SetDay(int day)
    {
        doyValid = false;
        absoluteDayValid = false;
        this.day = day;
        if (year == 0)
        {
            year = 1970;
        }
    }

    internal void SetDayOfYear(int dayOfYear)
    {
        doyValid = true;
        absoluteDayValid = false;
        this.dayOfYear = dayOfYear;
    }

    internal void SetHour(int hour)
    {
        hour12Valid = false;
        this.hour = hour;
    }

    internal void SetMinute(int minute) => this.minute = minute;

    internal void SetSecond(int second) => this.second = second;

    internal void SetNanos(int nanos) => this.nanos = nanos;

    internal void SetAmPm(bool isAM)
    {
        hour12Valid = true;
        this.isAM = isAM;
    }

    internal void SetHour12(int hour12)
    {
        hour12Valid = true;
        this.hour12 = hour12;
    }

    internal void SetTimeZone(TimeZoneProvider timeZone)
    {
        timeZoneHMValid = false;
        this.timeZone = timeZone;
    }

    internal void SetTimeZoneHour(int timeZoneHour)
    {
        timeZoneHMValid = true;
        this.timeZoneHour = timeZoneHour;
    }

    internal void SetTimeZoneMinute(int timeZoneMinute)
    {
        timeZoneHMValid = true;
        this.timeZoneMinute = timeZoneMinute;
    }

    private bool HasToParseData() => formatStr.Length > 0;

    private void RemoveFirstChar()
    {
        if (formatStr.Length > 0)
        {
            formatStr = formatStr[1..];
        }

        if (inputStr.Length > 0)
        {
            inputStr = inputStr[1..];
        }
    }

    private static void Parse(ToDateParser parser)
    {
        while (parser.HasToParseData())
        {
            System.Collections.Generic.List<ToDateTokenizer.FormatTokenEnum>? tokenList =
                ToDateTokenizer.GetTokensInQuestion(parser.GetFormatStr());
            if (tokenList is null)
            {
                parser.RemoveFirstChar();
                continue;
            }

            bool foundAToken = false;
            foreach (ToDateTokenizer.FormatTokenEnum token in tokenList)
            {
                if (token.ParseFormatStrWithToken(parser))
                {
                    foundAToken = true;
                    break;
                }
            }

            if (!foundAToken)
            {
                parser.RemoveFirstChar();
            }
        }
    }

    /// <summary>Removes a token from a string.</summary>
    /// <param name="inputFragmentStr">the input fragment</param>
    /// <param name="formatFragment">the format fragment</param>
    internal void Remove(string? inputFragmentStr, string? formatFragment)
    {
        if (inputFragmentStr is not null && inputStr.Length >= inputFragmentStr.Length)
        {
            inputStr = inputStr[inputFragmentStr.Length..];
        }

        if (formatFragment is not null && formatStr.Length >= formatFragment.Length)
        {
            formatStr = formatStr[formatFragment.Length..];
        }
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        int inputStrLen = inputStr.Length;
        int orgInputLen = unmodifiedInputStr.Length;
        int currentInputPos = orgInputLen - inputStrLen;
        int restInputLen = inputStrLen <= 0 ? inputStrLen : inputStrLen - 1;

        int orgFormatLen = unmodifiedFormatStr.Length;
        int currentFormatPos = orgFormatLen - formatStr.Length;
        string functionNameStr = functionName.ToString().ToUpperInvariant();

        return string.Create(CultureInfo.InvariantCulture,
            $"\n    {functionNameStr}('{unmodifiedInputStr}', '{unmodifiedFormatStr}')")
            + string.Create(CultureInfo.InvariantCulture,
            $"\n      {Format(functionNameStr.Length + currentInputPos)}^" +
            $"{(restInputLen <= 0 ? "" : Format(restInputLen))} ,  " +
            $"{(currentFormatPos <= 0 ? "" : Format(currentFormatPos))}^ <-- Parsing failed at this point");
    }

    private static string Format(int width)
        => width > 0 ? new string(' ', width) : string.Empty;

    /// <summary>Parses a string as a timestamp with the given format.</summary>
    /// <param name="session">the database session</param>
    /// <param name="input">the input</param>
    /// <param name="format">the format</param>
    /// <returns>the timestamp</returns>
    public static ValueTimestamp ToTimestamp(SessionLocal session, string input, string? format)
    {
        ToDateParser parser = GetTimestampParser(session, ConfigParam.ToTimestamp, input, format);
        return parser.GetResultingValue();
    }

    /// <summary>Parses a string as a timestamp with time zone with the given format.</summary>
    /// <param name="session">the database session</param>
    /// <param name="input">the input</param>
    /// <param name="format">the format</param>
    /// <returns>the timestamp</returns>
    public static ValueTimestampTimeZone ToTimestampTz(SessionLocal session, string input, string? format)
    {
        ToDateParser parser = GetTimestampParser(session, ConfigParam.ToTimestampTz, input, format);
        return parser.GetResultingValueWithTimeZone();
    }

    /// <summary>Parses a string as a date with the given format.</summary>
    /// <param name="session">the database session</param>
    /// <param name="input">the input</param>
    /// <param name="format">the format</param>
    /// <returns>the date as a timestamp</returns>
    public static ValueTimestamp ToDate(SessionLocal session, string input, string? format)
    {
        ToDateParser parser = GetTimestampParser(session, ConfigParam.ToDate, input, format);
        return parser.GetResultingValue();
    }
}
