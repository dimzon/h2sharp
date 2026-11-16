/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.mode.ToDateTokenizer.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Knows the TO_DATE format conventions and how to parse the corresponding data. The regexes
 * are .NET (the same spelling, `^` anchors and character classes mean the same thing here).
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions.Functions;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Modes;

/// <summary>
/// Emulates Oracle's TO_DATE function: knows all about the TO_DATE format conventions.
/// </summary>
internal static class ToDateTokenizer
{
    /// <summary>The pattern for an inline literal.</summary>
    internal static readonly Regex PatternInline = new("(\"[^\"]*\")", RegexOptions.None);

    /// <summary>The pattern for a number.</summary>
    internal static readonly Regex PatternNumber = new("^([+-]?[0-9]+)", RegexOptions.None);

    /// <summary>The pattern for four digits (typically a year).</summary>
    internal static readonly Regex PatternFourDigits = new("^([+-]?[0-9]{4})", RegexOptions.None);

    /// <summary>The pattern 2-4 digits (e.g. for RRRR).</summary>
    internal static readonly Regex PatternTwoToFourDigits = new("^([+-]?[0-9]{2,4})", RegexOptions.None);

    /// <summary>The pattern for three digits.</summary>
    internal static readonly Regex PatternThreeDigits = new("^([+-]?[0-9]{3})", RegexOptions.None);

    /// <summary>The pattern for two digits.</summary>
    internal static readonly Regex PatternTwoDigits = new("^([+-]?[0-9]{2})", RegexOptions.None);

    /// <summary>The pattern for one or two digits.</summary>
    internal static readonly Regex PatternTwoDigitsOrLess = new("^([+-]?[0-9][0-9]?)", RegexOptions.None);

    /// <summary>The pattern for one digit.</summary>
    internal static readonly Regex PatternOneDigit = new("^([+-]?[0-9])", RegexOptions.None);

    /// <summary>The pattern for a fraction (of a second for example).</summary>
    internal static readonly Regex PatternFF = new("^(FF[0-9]?)", RegexOptions.IgnoreCase);

    /// <summary>The pattern for "am" or "pm".</summary>
    internal static readonly Regex PatternAmPm = new("^(AM|A\\.M\\.|PM|P\\.M\\.)", RegexOptions.IgnoreCase);

    /// <summary>The pattern for "bc" or "ad".</summary>
    internal static readonly Regex PatternBcAd = new("^(BC|B\\.C\\.|AD|A\\.D\\.)", RegexOptions.IgnoreCase);

    /// <summary>The parslet for a year.</summary>
    private static readonly YearParslet ParsletYear = new();

    /// <summary>The parslet for a month.</summary>
    private static readonly MonthParslet ParsletMonth = new();

    /// <summary>The parslet for a day.</summary>
    private static readonly DayParslet ParsletDay = new();

    /// <summary>The parslet for time.</summary>
    private static readonly TimeParslet ParsletTime = new();

    /// <summary>The inline parslet, e.g. for 'YYYY-MM-DD"T"HH24:MI:SS"Z"'.</summary>
    private static readonly InlineParslet ParsletInline = new();

    /// <summary>Throws a parse exception.</summary>
    /// <param name="parameters">the parameters with the input string</param>
    /// <param name="errorStr">the error string</param>
    internal static void ThrowException(ToDateParser parameters, string errorStr)
        => throw DbException.Get(ErrorCode.InvalidToDateFormat,
            parameters.GetFunctionName(), string.Create(CultureInfo.InvariantCulture,
                $" {errorStr}. Details: {parameters}"));

    /// <summary>Matches the pattern, or throws when it cannot.</summary>
    /// <param name="pattern">the pattern</param>
    /// <param name="parameters">the parameters with the input string</param>
    /// <param name="tokenName">the pattern name</param>
    /// <returns>the matched value</returns>
    internal static string MatchStringOrThrow(Regex pattern, ToDateParser parameters, string tokenName)
    {
        string s = parameters.GetInputStr();
        Match matcher = pattern.Match(s);
        if (!matcher.Success)
        {
            ThrowException(parameters, $"Issue happened when parsing token '{tokenName}'");
        }

        return matcher.Groups[1].Value;
    }

    /// <summary>Matches the pattern, or throws when it cannot.</summary>
    /// <param name="pattern">the pattern</param>
    /// <param name="parameters">the parameters with the input string</param>
    /// <param name="token">the format token</param>
    /// <returns>the matched value</returns>
    private static string MatchStringOrThrow(Regex pattern, ToDateParser parameters, FormatTokenEnum token)
        => MatchStringOrThrow(pattern, parameters, token.ToString());

    /// <summary>
    /// Matches one of the named month or weekday values in the input.
    /// </summary>
    /// <param name="parameters">the parameters with the input string</param>
    /// <param name="field">the field to set</param>
    /// <returns>the matched value</returns>
    private static string SetByName(ToDateParser parameters, int field)
    {
        string? inputFragmentStr = null;
        string s = parameters.GetInputStr();
        string[] values = ToCharFunction.GetDateNames(field);
        for (int i = 0; i < values.Length; i++)
        {
            string dayName = values[i];
            if (dayName.Length == 0)
            {
                continue;
            }

            int len = dayName.Length;
            if (string.Equals(dayName, s[..Math.Min(len, s.Length)], StringComparison.OrdinalIgnoreCase)
                && s.Length >= len)
            {
                switch (field)
                {
                    case ToCharFunction.Months:
                    case ToCharFunction.ShortMonths:
                        parameters.SetMonth(i + 1);
                        break;
                    case ToCharFunction.Weekdays:
                    case ToCharFunction.ShortWeekdays:
                        // TODO
                        break;
                    default:
                        throw new ArgumentException();
                }

                inputFragmentStr = dayName;
                break;
            }
        }

        if (string.IsNullOrEmpty(inputFragmentStr))
        {
            ThrowException(parameters,
                $"Tried to parse one of '[{string.Join(", ", values)}]' but failed "
                + "(may be an internal error?)");
        }

        return inputFragmentStr!;
    }

    /// <summary>Interface of the parslets that parse one bit of the TO_DATE format string.</summary>
    private interface ToDateParslet
    {
        /// <summary>Parses a date part.</summary>
        /// <param name="parameters">the parameters that contains the string</param>
        /// <param name="formatTokenEnum">the format</param>
        /// <param name="formatTokenStr">the format string</param>
        void Parse(ToDateParser parameters, FormatTokenEnum formatTokenEnum, string formatTokenStr);
    }

    /// <summary>Parslet responsible for parsing year parameters.</summary>
    private sealed class YearParslet : ToDateParslet
    {
        /// <inheritdoc/>
        public void Parse(ToDateParser parameters, FormatTokenEnum formatTokenEnum, string formatTokenStr)
        {
            string? inputFragmentStr = null;
            int dateNr;
            switch (formatTokenEnum)
            {
                case FormatTokenEnum.SYYYY:
                case FormatTokenEnum.YYYY:
                    inputFragmentStr = MatchStringOrThrow(PatternFourDigits, parameters, formatTokenEnum);
                    dateNr = int.Parse(inputFragmentStr, CultureInfo.InvariantCulture);
                    // Gregorian calendar does not have a year 0.
                    // 0 = 0001 BC, -1 = 0002 BC, ... so we adjust.
                    if (dateNr == 0)
                    {
                        ThrowException(parameters, "Year may not be zero");
                    }

                    parameters.SetYear(dateNr >= 0 ? dateNr : dateNr + 1);
                    break;
                case FormatTokenEnum.YYY:
                    inputFragmentStr = MatchStringOrThrow(PatternThreeDigits, parameters, formatTokenEnum);
                    dateNr = int.Parse(inputFragmentStr, CultureInfo.InvariantCulture);
                    if (dateNr > 999)
                    {
                        ThrowException(parameters,
                            "Year may have only three digits with specified format");
                    }

                    dateNr += (parameters.GetCurrentYear() / 1_000) * 1_000;
                    parameters.SetYear(dateNr >= 0 ? dateNr : dateNr + 1);
                    break;
                case FormatTokenEnum.RRRR:
                    inputFragmentStr = MatchStringOrThrow(PatternTwoToFourDigits, parameters, formatTokenEnum);
                    dateNr = int.Parse(inputFragmentStr, CultureInfo.InvariantCulture);
                    if (inputFragmentStr.Length < 4)
                    {
                        if (dateNr < 50)
                        {
                            dateNr += 2000;
                        }
                        else if (dateNr < 100)
                        {
                            dateNr += 1900;
                        }
                    }

                    if (dateNr == 0)
                    {
                        ThrowException(parameters, "Year may not be zero");
                    }

                    parameters.SetYear(dateNr);
                    break;
                case FormatTokenEnum.RR:
                    {
                        int cc = parameters.GetCurrentYear() / 100;
                        inputFragmentStr = MatchStringOrThrow(PatternTwoDigits, parameters, formatTokenEnum);
                        dateNr = int.Parse(inputFragmentStr, CultureInfo.InvariantCulture) + cc * 100;
                        parameters.SetYear(dateNr);
                        break;
                    }
                case FormatTokenEnum.EE:
                    ThrowException(parameters,
                        $"token '{formatTokenEnum}' not supported yet.");
                    break;
                case FormatTokenEnum.E:
                    ThrowException(parameters,
                        $"token '{formatTokenEnum}' not supported yet.");
                    break;
                case FormatTokenEnum.YY:
                    inputFragmentStr = MatchStringOrThrow(PatternTwoDigits, parameters, formatTokenEnum);
                    dateNr = int.Parse(inputFragmentStr, CultureInfo.InvariantCulture);
                    if (dateNr > 99)
                    {
                        ThrowException(parameters,
                            "Year may have only two digits with specified format");
                    }

                    dateNr += (parameters.GetCurrentYear() / 100) * 100;
                    parameters.SetYear(dateNr >= 0 ? dateNr : dateNr + 1);
                    break;
                case FormatTokenEnum.SCC:
                case FormatTokenEnum.CC:
                    inputFragmentStr = MatchStringOrThrow(PatternTwoDigits, parameters, formatTokenEnum);
                    dateNr = int.Parse(inputFragmentStr, CultureInfo.InvariantCulture) * 100;
                    parameters.SetYear(dateNr);
                    break;
                case FormatTokenEnum.Y:
                    inputFragmentStr = MatchStringOrThrow(PatternOneDigit, parameters, formatTokenEnum);
                    dateNr = int.Parse(inputFragmentStr, CultureInfo.InvariantCulture);
                    if (dateNr > 9)
                    {
                        ThrowException(parameters,
                            "Year may have only two digits with specified format");
                    }

                    dateNr += (parameters.GetCurrentYear() / 10) * 10;
                    parameters.SetYear(dateNr >= 0 ? dateNr : dateNr + 1);
                    break;
                case FormatTokenEnum.BC_AD:
                    inputFragmentStr = MatchStringOrThrow(PatternBcAd, parameters, formatTokenEnum);
                    parameters.SetBC(inputFragmentStr.ToUpperInvariant().StartsWith("B", StringComparison.Ordinal));
                    break;
                default:
                    throw new ArgumentException(
                        $"YearParslet: Internal Error. Unhandled case: {formatTokenEnum}");
            }

            parameters.Remove(inputFragmentStr, formatTokenStr);
        }
    }

    /// <summary>Parslet responsible for parsing month parameters.</summary>
    private sealed class MonthParslet : ToDateParslet
    {
        private static readonly string[] RomanMonth = ["I", "II", "III", "IV", "V", "VI",
            "VII", "VIII", "IX", "X", "XI", "XII"];

        /// <inheritdoc/>
        public void Parse(ToDateParser parameters, FormatTokenEnum formatTokenEnum, string formatTokenStr)
        {
            string s = parameters.GetInputStr();
            string? inputFragmentStr = null;
            int dateNr;
            switch (formatTokenEnum)
            {
                case FormatTokenEnum.MONTH:
                    inputFragmentStr = SetByName(parameters, ToCharFunction.Months);
                    break;
                case FormatTokenEnum.Q:
                    ThrowException(parameters, $"token '{formatTokenEnum}' not supported yet.");
                    break;
                case FormatTokenEnum.MON:
                    inputFragmentStr = SetByName(parameters, ToCharFunction.ShortMonths);
                    break;
                case FormatTokenEnum.MM:
                    inputFragmentStr = MatchStringOrThrow(PatternTwoDigitsOrLess, parameters, formatTokenEnum);
                    dateNr = int.Parse(inputFragmentStr, CultureInfo.InvariantCulture);
                    parameters.SetMonth(dateNr);
                    break;
                case FormatTokenEnum.RM:
                    {
                        dateNr = 0;
                        foreach (string monthName in RomanMonth)
                        {
                            dateNr++;
                            int len = monthName.Length;
                            if (s.Length >= len
                                && string.Equals(monthName, s[..len], StringComparison.OrdinalIgnoreCase))
                            {
                                parameters.SetMonth(dateNr + 1);
                                inputFragmentStr = monthName;
                                break;
                            }
                        }

                        if (string.IsNullOrEmpty(inputFragmentStr))
                        {
                            ThrowException(parameters,
                                $"Issue happened when parsing token '{formatTokenEnum}'. "
                                + $"Expected one of: [{string.Join(", ", RomanMonth)}]");
                        }

                        break;
                    }
                default:
                    throw new ArgumentException(
                        $"MonthParslet: Internal Error. Unhandled case: {formatTokenEnum}");
            }

            parameters.Remove(inputFragmentStr, formatTokenStr);
        }
    }

    /// <summary>Parslet responsible for parsing day parameters.</summary>
    private sealed class DayParslet : ToDateParslet
    {
        /// <inheritdoc/>
        public void Parse(ToDateParser parameters, FormatTokenEnum formatTokenEnum, string formatTokenStr)
        {
            string? inputFragmentStr = null;
            int dateNr;
            switch (formatTokenEnum)
            {
                case FormatTokenEnum.DDD:
                    inputFragmentStr = MatchStringOrThrow(PatternNumber, parameters, formatTokenEnum);
                    dateNr = int.Parse(inputFragmentStr, CultureInfo.InvariantCulture);
                    parameters.SetDayOfYear(dateNr);
                    break;
                case FormatTokenEnum.DD:
                    inputFragmentStr = MatchStringOrThrow(PatternTwoDigitsOrLess, parameters, formatTokenEnum);
                    dateNr = int.Parse(inputFragmentStr, CultureInfo.InvariantCulture);
                    parameters.SetDay(dateNr);
                    break;
                case FormatTokenEnum.D:
                    inputFragmentStr = MatchStringOrThrow(PatternOneDigit, parameters, formatTokenEnum);
                    dateNr = int.Parse(inputFragmentStr, CultureInfo.InvariantCulture);
                    parameters.SetDay(dateNr);
                    break;
                case FormatTokenEnum.DAY:
                    inputFragmentStr = SetByName(parameters, ToCharFunction.Weekdays);
                    break;
                case FormatTokenEnum.DY:
                    inputFragmentStr = SetByName(parameters, ToCharFunction.ShortWeekdays);
                    break;
                case FormatTokenEnum.J:
                    inputFragmentStr = MatchStringOrThrow(PatternNumber, parameters, formatTokenEnum);
                    dateNr = int.Parse(inputFragmentStr, CultureInfo.InvariantCulture);
                    parameters.SetAbsoluteDay(dateNr + ToCharFunction.JulianEpoch);
                    break;
                default:
                    throw new ArgumentException(
                        $"DayParslet: Internal Error. Unhandled case: {formatTokenEnum}");
            }

            parameters.Remove(inputFragmentStr, formatTokenStr);
        }
    }

    /// <summary>Parslet responsible for parsing time parameters.</summary>
    private sealed class TimeParslet : ToDateParslet
    {
        /// <inheritdoc/>
        public void Parse(ToDateParser parameters, FormatTokenEnum formatTokenEnum, string formatTokenStr)
        {
            string? inputFragmentStr = null;
            int dateNr;
            switch (formatTokenEnum)
            {
                case FormatTokenEnum.HH24:
                    inputFragmentStr = MatchStringOrThrow(PatternTwoDigitsOrLess, parameters, formatTokenEnum);
                    dateNr = int.Parse(inputFragmentStr, CultureInfo.InvariantCulture);
                    parameters.SetHour(dateNr);
                    break;
                case FormatTokenEnum.HH12:
                case FormatTokenEnum.HH:
                    inputFragmentStr = MatchStringOrThrow(PatternTwoDigitsOrLess, parameters, formatTokenEnum);
                    dateNr = int.Parse(inputFragmentStr, CultureInfo.InvariantCulture);
                    parameters.SetHour12(dateNr);
                    break;
                case FormatTokenEnum.MI:
                    inputFragmentStr = MatchStringOrThrow(PatternTwoDigitsOrLess, parameters, formatTokenEnum);
                    dateNr = int.Parse(inputFragmentStr, CultureInfo.InvariantCulture);
                    parameters.SetMinute(dateNr);
                    break;
                case FormatTokenEnum.SS:
                    inputFragmentStr = MatchStringOrThrow(PatternTwoDigitsOrLess, parameters, formatTokenEnum);
                    dateNr = int.Parse(inputFragmentStr, CultureInfo.InvariantCulture);
                    parameters.SetSecond(dateNr);
                    break;
                case FormatTokenEnum.SSSSS:
                    {
                        inputFragmentStr = MatchStringOrThrow(PatternNumber, parameters, formatTokenEnum);
                        dateNr = int.Parse(inputFragmentStr, CultureInfo.InvariantCulture);
                        int second = dateNr % 60;
                        dateNr /= 60;
                        int minute = dateNr % 60;
                        dateNr /= 60;
                        int hour = dateNr % 24;
                        parameters.SetHour(hour);
                        parameters.SetMinute(minute);
                        parameters.SetSecond(second);
                        break;
                    }
                case FormatTokenEnum.FF:
                    {
                        inputFragmentStr = MatchStringOrThrow(PatternNumber, parameters, formatTokenEnum);
                        string paddedRightNrStr = inputFragmentStr.PadRight(9, '0');
                        paddedRightNrStr = paddedRightNrStr[..Math.Min(9, paddedRightNrStr.Length)];
                        double nineDigits = double.Parse(paddedRightNrStr, CultureInfo.InvariantCulture);
                        parameters.SetNanos((int)nineDigits);
                        break;
                    }
                case FormatTokenEnum.AM_PM:
                    inputFragmentStr = MatchStringOrThrow(PatternAmPm, parameters, formatTokenEnum);
                    parameters.SetAmPm(inputFragmentStr.ToUpperInvariant()
                        .StartsWith("A", StringComparison.Ordinal));
                    break;
                case FormatTokenEnum.TZH:
                    inputFragmentStr = MatchStringOrThrow(PatternTwoDigitsOrLess, parameters, formatTokenEnum);
                    dateNr = int.Parse(inputFragmentStr, CultureInfo.InvariantCulture);
                    parameters.SetTimeZoneHour(dateNr);
                    break;
                case FormatTokenEnum.TZM:
                    inputFragmentStr = MatchStringOrThrow(PatternTwoDigitsOrLess, parameters, formatTokenEnum);
                    dateNr = int.Parse(inputFragmentStr, CultureInfo.InvariantCulture);
                    parameters.SetTimeZoneMinute(dateNr);
                    break;
                case FormatTokenEnum.TZR:
                case FormatTokenEnum.TZD:
                    {
                        string tzName = parameters.GetInputStr();
                        parameters.SetTimeZone(TimeZoneProvider.OfId(tzName));
                        inputFragmentStr = tzName;
                        break;
                    }
                default:
                    throw new ArgumentException(
                        $"TimeParslet: Internal Error. Unhandled case: {formatTokenEnum}");
            }

            parameters.Remove(inputFragmentStr, formatTokenStr);
        }
    }

    /// <summary>Parslet responsible for parsing inline literals.</summary>
    private sealed class InlineParslet : ToDateParslet
    {
        /// <inheritdoc/>
        public void Parse(ToDateParser parameters, FormatTokenEnum formatTokenEnum, string formatTokenStr)
        {
            string? inputFragmentStr;
            switch (formatTokenEnum)
            {
                case FormatTokenEnum.INLINE:
                    inputFragmentStr = formatTokenStr.Replace("\"", "", StringComparison.Ordinal);
                    break;
                default:
                    throw new ArgumentException(
                        $"InlineParslet: Internal Error. Unhandled case: {formatTokenEnum}");
            }

            parameters.Remove(inputFragmentStr, formatTokenStr);
        }
    }

    /// <summary>The format tokens.</summary>
    internal enum FormatTokenEnum
    {
        /// <summary>4-digit year.</summary>
        YYYY = 0,

        /// <summary>4-digit year with sign (- = B.C.).</summary>
        SYYYY,

        /// <summary>3-digit year.</summary>
        YYY,

        /// <summary>2-digit year.</summary>
        YY,

        /// <summary>Two-digit century with sign (- = B.C.).</summary>
        SCC,

        /// <summary>Two-digit century.</summary>
        CC,

        /// <summary>2-digit -> 4-digit year 0-49 -> 20xx, 50-99 -> 19xx.</summary>
        RRRR,

        /// <summary>Last 2 digits of the year using the "current" century value.</summary>
        RR,

        /// <summary>Meridian indicator.</summary>
        BC_AD,

        /// <summary>Full name of month.</summary>
        MONTH,

        /// <summary>Abbreviated name of month.</summary>
        MON,

        /// <summary>Month (01-12; JAN = 01).</summary>
        MM,

        /// <summary>Roman numeral month (I-XII; JAN = I).</summary>
        RM,

        /// <summary>Day of year (1-366).</summary>
        DDD,

        /// <summary>Name of day.</summary>
        DAY,

        /// <summary>Day of month (1-31).</summary>
        DD,

        /// <summary>Abbreviated name of day.</summary>
        DY,

        /// <summary>Hour of day (0-23).</summary>
        HH24,

        /// <summary>Hour of day (1-12).</summary>
        HH12,

        /// <summary>Hour of day (1-12).</summary>
        HH,

        /// <summary>Minutes.</summary>
        MI,

        /// <summary>Seconds past midnight (0-86399).</summary>
        SSSSS,

        /// <summary>Seconds.</summary>
        SS,

        /// <summary>Fractional seconds.</summary>
        FF,

        /// <summary>Time zone hour.</summary>
        TZH,

        /// <summary>Time zone minute.</summary>
        TZM,

        /// <summary>Time zone region ID.</summary>
        TZR,

        /// <summary>Daylight savings information.</summary>
        TZD,

        /// <summary>Meridian indicator.</summary>
        AM_PM,

        /// <summary>Not supported yet: full era name.</summary>
        EE,

        /// <summary>Not supported yet: abbreviated era name.</summary>
        E,

        /// <summary>One digit year.</summary>
        Y,

        /// <summary>Not supported yet: quarter of year.</summary>
        Q,

        /// <summary>Day of week (1-7).</summary>
        D,

        /// <summary>Not supported yet: Julian day; number of days since Jan 1, 4712 BC.</summary>
        J,

        /// <summary>Inline text, e.g. the "T" of 'YYYY-MM-DD"T"HH24:MI:SS"Z"'.</summary>
        INLINE,
    }

    /// <summary>
    /// The parsed pattern and parslet of one format token.
    /// </summary>
    private static class FormatTokenTable
    {
        /// <summary>The parslet each token asks.</summary>
        internal static readonly ToDateParslet?[] Parslets = BuildParslets();

        /// <summary>The named pattern a token carries, or null when its own name is the pattern.
        /// Declared before <see cref="Patterns"/>, which reads it while building.</summary>
        internal static readonly Dictionary<FormatTokenEnum, Regex> NamedPatterns = new()
        {
            [FormatTokenEnum.BC_AD] = PatternBcAd,
            [FormatTokenEnum.FF] = PatternFF,
            [FormatTokenEnum.AM_PM] = PatternAmPm,
            [FormatTokenEnum.INLINE] = PatternInline,
        };

        /// <summary>The pattern each token matches; null for the tokens the reference builds
        /// from their own name.</summary>
        internal static readonly Regex?[] Patterns = BuildPatterns();

        private static ToDateParslet?[] BuildParslets() => new ToDateParslet?[36]
        {
            ParsletYear,    // YYYY
            ParsletYear,    // SYYYY
            ParsletYear,    // YYY
            ParsletYear,    // YY
            ParsletYear,    // SCC
            ParsletYear,    // CC
            ParsletYear,    // RRRR
            ParsletYear,    // RR
            ParsletYear,    // BC_AD
            ParsletMonth,   // MONTH
            ParsletMonth,   // MON
            ParsletMonth,   // MM
            ParsletMonth,   // RM
            ParsletDay,     // DDD
            ParsletDay,     // DAY
            ParsletDay,     // DD
            ParsletDay,     // DY
            ParsletTime,    // HH24
            ParsletTime,    // HH12
            ParsletTime,    // HH
            ParsletTime,    // MI
            ParsletTime,    // SSSSS
            ParsletTime,    // SS
            ParsletTime,    // FF
            ParsletTime,    // TZH
            ParsletTime,    // TZM
            ParsletTime,    // TZR
            ParsletTime,    // TZD
            ParsletTime,    // AM_PM
            ParsletYear,    // EE
            ParsletYear,    // E
            ParsletYear,    // Y
            ParsletMonth,   // Q
            ParsletDay,     // D
            ParsletDay,     // J
            ParsletInline,  // INLINE
        };

        private static Regex?[] BuildPatterns()
        {
            Regex?[] patterns = new Regex?[36];
            foreach (FormatTokenEnum token in Enum.GetValues<FormatTokenEnum>())
            {
                patterns[(int)token] = NamedPatterns.TryGetValue(token, out Regex? named)
                    ? named
                    : new Regex($"^({token})", RegexOptions.IgnoreCase);
            }

            return patterns;
        }
    }

    private static class FormatTokenCache
    {
        /// <summary>The tokens that share a first letter, as the reference's cache does.</summary>
        internal static readonly List<FormatTokenEnum>?[] Tokens = BuildTokens();

        /// <summary>The single-token list the reference keeps for inline literals.</summary>
        internal static readonly List<FormatTokenEnum> InlineList = [FormatTokenEnum.INLINE];

        private static List<FormatTokenEnum>?[] BuildTokens()
        {
            List<FormatTokenEnum>?[] tokens = new List<FormatTokenEnum>?[25];
            foreach (FormatTokenEnum token in Enum.GetValues<FormatTokenEnum>())
            {
                string name = token.ToString();
                if (name.Contains('_', StringComparison.Ordinal))
                {
                    foreach (string tokenLets in name.Split('_', StringSplitOptions.RemoveEmptyEntries))
                    {
                        PutToCache(tokens, token, tokenLets);
                    }
                }
                else
                {
                    PutToCache(tokens, token, name);
                }
            }

            return tokens;
        }

        private static void PutToCache(List<FormatTokenEnum>?[] cache, FormatTokenEnum token, string name)
        {
            int idx = char.ToUpperInvariant(name[0]) - 'A';
            List<FormatTokenEnum> list = cache[idx] ??= [];
            list.Add(token);
        }
    }

    /// <summary>
    /// Returns the tokens that share the first character of the format string, or null.
    /// </summary>
    /// <param name="formatStr">the format string</param>
    /// <returns>the list of tokens, or null</returns>
    internal static List<FormatTokenEnum>? GetTokensInQuestion(string formatStr)
    {
        if (formatStr.Length > 0)
        {
            char key = char.ToUpperInvariant(formatStr[0]);
            if (key >= 'A' && key <= 'Y')
            {
                return FormatTokenCache.Tokens[key - 'A'];
            }

            if (key == '"')
            {
                return FormatTokenCache.InlineList;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses the format string with this token; answers whether it matched.
    /// </summary>
    /// <param name="token">the token</param>
    /// <param name="parameters">the parameters</param>
    /// <returns>true if it matched</returns>
    internal static bool ParseFormatStrWithToken(this FormatTokenEnum token, ToDateParser parameters)
    {
        Regex pattern = FormatTokenTable.Patterns[(int)token]!;
        Match matcher = pattern.Match(parameters.GetFormatStr());
        bool foundToken = matcher.Success;
        if (foundToken)
        {
            string formatTokenStr = matcher.Groups[1].Value;
            FormatTokenTable.Parslets[(int)token]!.Parse(parameters, token, formatTokenStr);
        }

        return foundToken;
    }
}
