/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.DateTimeFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Globalization;
using System.Numerics;

using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// A date-time function.
/// </summary>
/// <remarks>
/// The week fields the reference takes from <c>WeekFields.of(Locale.getDefault())</c> are taken
/// here from <see cref="CultureInfo.CurrentCulture"/>: its first day of the week, and its week
/// rule mapped to the day count <c>WeekFields</c> would carry — first day of the year 1, first
/// four-day week 4, first full week 7. .NET has no <c>WeekFields</c>, so the two numbers are read
/// off the culture's own calendar settings.
/// </remarks>
public sealed class DateTimeFunction : Function1_2
{
    /// <summary>
    /// EXTRACT().
    /// </summary>
    public const int Extract = 0;

    /// <summary>
    /// DATE_TRUNC() (non-standard).
    /// </summary>
    public const int DateTrunc = Extract + 1;

    /// <summary>
    /// DATEADD() (non-standard).
    /// </summary>
    /// <remarks>
    /// The C# name of the method that adds to a date-time value is <see cref="DateaddValue"/>
    /// rather than <c>Dateadd</c>: C# will not hold a constant and a method under one name, and
    /// the constant keeps the reference's name.
    /// </remarks>
    public const int Dateadd = DateTrunc + 1;

    /// <summary>
    /// DATEDIFF() (non-standard).
    /// </summary>
    public const int Datediff = Dateadd + 1;

    /// <summary>
    /// LAST_DAY() (non-standard);
    /// </summary>
    public const int LastDay = Datediff + 1;

    private static readonly string[] Names = { //
        "EXTRACT", "DATE_TRUNC", "DATEADD", "DATEDIFF", "LAST_DAY" //
    };

    // Standard fields

    /// <summary>
    /// Year.
    /// </summary>
    public const int Year = 0;

    /// <summary>
    /// Month.
    /// </summary>
    public const int Month = Year + 1;

    /// <summary>
    /// Day of month.
    /// </summary>
    public const int Day = Month + 1;

    /// <summary>
    /// Hour.
    /// </summary>
    public const int Hour = Day + 1;

    /// <summary>
    /// Minute.
    /// </summary>
    public const int Minute = Hour + 1;

    /// <summary>
    /// Second.
    /// </summary>
    public const int Second = Minute + 1;

    /// <summary>
    /// Time zone hour.
    /// </summary>
    public const int TimezoneHour = Second + 1;

    /// <summary>
    /// Time zone minute.
    /// </summary>
    public const int TimezoneMinute = TimezoneHour + 1;

    // Additional fields

    /// <summary>
    /// Time zone second.
    /// </summary>
    public const int TimezoneSecond = TimezoneMinute + 1;

    /// <summary>
    /// Millennium.
    /// </summary>
    public const int Millennium = TimezoneSecond + 1;

    /// <summary>
    /// Century.
    /// </summary>
    public const int Century = Millennium + 1;

    /// <summary>
    /// Decade.
    /// </summary>
    public const int Decade = Century + 1;

    /// <summary>
    /// Quarter.
    /// </summary>
    public const int Quarter = Decade + 1;

    /// <summary>
    /// Millisecond.
    /// </summary>
    public const int Millisecond = Quarter + 1;

    /// <summary>
    /// Microsecond.
    /// </summary>
    public const int Microsecond = Millisecond + 1;

    /// <summary>
    /// Nanosecond.
    /// </summary>
    public const int Nanosecond = Microsecond + 1;

    /// <summary>
    /// Day of year.
    /// </summary>
    public const int DayOfYear = Nanosecond + 1;

    /// <summary>
    /// ISO day of week.
    /// </summary>
    public const int IsoDayOfWeek = DayOfYear + 1;

    /// <summary>
    /// ISO week.
    /// </summary>
    public const int IsoWeek = IsoDayOfWeek + 1;

    /// <summary>
    /// ISO week-based year.
    /// </summary>
    public const int IsoWeekYear = IsoWeek + 1;

    /// <summary>
    /// Day of week (locale-specific).
    /// </summary>
    public const int DayOfWeek = IsoWeekYear + 1;

    /// <summary>
    /// Week (locale-specific).
    /// </summary>
    public const int Week = DayOfWeek + 1;

    /// <summary>
    /// Week-based year (locale-specific).
    /// </summary>
    public const int WeekYear = Week + 1;

    /// <summary>
    /// Epoch.
    /// </summary>
    public const int Epoch = WeekYear + 1;

    /// <summary>
    /// Day of week (locale-specific) for PostgreSQL compatibility.
    /// </summary>
    public const int Dow = Epoch + 1;

    private const int FieldsCount = Dow + 1;

    private static readonly string[] FieldNames = { //
        "YEAR", "MONTH", "DAY", //
        "HOUR", "MINUTE", "SECOND", //
        "TIMEZONE_HOUR", "TIMEZONE_MINUTE", "TIMEZONE_SECOND", //
        "MILLENNIUM", "CENTURY", "DECADE", //
        "QUARTER", //
        "MILLISECOND", "MICROSECOND", "NANOSECOND", //
        "DAY_OF_YEAR", //
        "ISO_DAY_OF_WEEK", "ISO_WEEK", "ISO_WEEK_YEAR", //
        "DAY_OF_WEEK", "WEEK", "WEEK_YEAR", //
        "EPOCH", "DOW", //
    };

    private static readonly BigDecimal BdSecondsPerDay = BigDecimal.FromLong(DateTimeUtils.SecondsPerDay);

    private static readonly BigInteger BiSecondsPerDay = new(DateTimeUtils.SecondsPerDay);

    private static readonly BigDecimal BdNanosPerSecond = BigDecimal.FromLong(DateTimeUtils.NanosPerSecond);

    /// <summary>
    /// Local definitions of day-of-week, week-of-month, and week-of-year.
    /// </summary>
    private static volatile WeekFields? weekFields;

    /// <summary>
    /// Get date-time field for the specified name.
    /// </summary>
    /// <param name="name">the name</param>
    /// <returns>the date-time field</returns>
    /// <exception cref="H2SharpException">on unknown field name</exception>
    public static int GetField(string name)
    {
        switch (StringUtils.ToUpperEnglish(name))
        {
            case "YEAR":
            case "YY":
            case "YYYY":
            case "SQL_TSI_YEAR":
                return Year;
            case "MONTH":
            case "M":
            case "MM":
            case "SQL_TSI_MONTH":
                return Month;
            case "DAY":
            case "D":
            case "DD":
            case "SQL_TSI_DAY":
                return Day;
            case "HOUR":
            case "HH":
            case "SQL_TSI_HOUR":
                return Hour;
            case "MINUTE":
            case "MI":
            case "N":
            case "SQL_TSI_MINUTE":
                return Minute;
            case "SECOND":
            case "S":
            case "SS":
            case "SQL_TSI_SECOND":
                return Second;
            case "TIMEZONE_HOUR":
                return TimezoneHour;
            case "TIMEZONE_MINUTE":
                return TimezoneMinute;
            case "TIMEZONE_SECOND":
                return TimezoneSecond;
            case "MILLENNIUM":
                return Millennium;
            case "CENTURY":
                return Century;
            case "DECADE":
                return Decade;
            case "QUARTER":
                return Quarter;
            case "MILLISECOND":
            case "MILLISECONDS":
            case "MS":
                return Millisecond;
            case "MICROSECOND":
            case "MICROSECONDS":
            case "MCS":
                return Microsecond;
            case "NANOSECOND":
            case "NS":
                return Nanosecond;
            case "DAY_OF_YEAR":
            case "DAYOFYEAR":
            case "DY":
            case "DOY":
                return DayOfYear;
            case "ISO_DAY_OF_WEEK":
            case "ISODOW":
                return IsoDayOfWeek;
            case "ISO_WEEK":
                return IsoWeek;
            case "ISO_WEEK_YEAR":
            case "ISO_YEAR":
            case "ISOYEAR":
                return IsoWeekYear;
            case "DAY_OF_WEEK":
            case "DAYOFWEEK":
                return DayOfWeek;
            case "WEEK":
            case "WK":
            case "WW":
            case "SQL_TSI_WEEK":
                return Week;
            case "WEEK_YEAR":
                return WeekYear;
            case "EPOCH":
                return Epoch;
            case "DOW":
                return Dow;
            default:
                throw DbException.GetInvalidValueException("date-time field", name);
        }
    }

    /// <summary>
    /// Get the name of the specified date-time field.
    /// </summary>
    /// <param name="field">the date-time field</param>
    /// <returns>the name of the specified field</returns>
    public static string GetFieldName(int field)
    {
        if (field < 0 || field >= FieldsCount)
        {
            throw DbException.GetUnsupportedException("datetime field " + field);
        }

        return FieldNames[field];
    }

    private readonly int function, field;

    /// <summary>
    /// Creates a new date-time function.
    /// </summary>
    /// <param name="function">one of the function constants of this class</param>
    /// <param name="field">one of the field constants of this class</param>
    /// <param name="arg1">the first argument</param>
    /// <param name="arg2">the second argument, or null</param>
    public DateTimeFunction(int function, int field, Expression arg1, Expression? arg2)
        : base(arg1, arg2)
    {
        this.function = function;
        this.field = field;
    }

    /// <inheritdoc/>
    protected override Value GetValue(SessionLocal session, Value v1, Value? v2)
    {
        switch (function)
        {
            case Extract:
                v1 = field == Epoch
                    ? ExtractEpoch(session, v1)
                    : ValueInteger.Get(ExtractInteger(session, v1, field));
                break;
            case DateTrunc:
                v1 = TruncateDate(session, field, v1);
                break;
            case Dateadd:
                v1 = DateaddValue(session, field, v1.GetLong(), v2!);
                break;
            case Datediff:
                v1 = ValueBigint.Get(DatediffValue(session, field, v1, v2!));
                break;
            case LastDay:
                v1 = LastDayValue(session, v1);
                break;
            default:
                throw DbException.GetInternalError("function=" + function);
        }

        return v1;
    }

    /// <summary>
    /// Get the specified field of a date, however with years normalized to
    /// positive or negative, and month starting with 1.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="date">the date value</param>
    /// <param name="field">the field type</param>
    /// <returns>the value</returns>
    private static int ExtractInteger(SessionLocal session, Value date, int field)
        => date is ValueInterval ? ExtractInterval(date, field) : ExtractDateTime(session, date, field);

    private static int ExtractInterval(Value date, int field)
    {
        ValueInterval interval = (ValueInterval)date;
        IntervalQualifier qualifier = interval.GetQualifier();
        bool negative = interval.IsNegative();
        long leading = interval.GetLeading(), remaining = interval.GetRemaining();
        long v;
        switch (field)
        {
            case Year:
                v = IntervalUtils.YearsFromInterval(qualifier, negative, leading, remaining);
                break;
            case Month:
                v = IntervalUtils.MonthsFromInterval(qualifier, negative, leading, remaining);
                break;
            case Day:
            case DayOfYear:
                v = IntervalUtils.DaysFromInterval(qualifier, negative, leading, remaining);
                break;
            case Hour:
                v = IntervalUtils.HoursFromInterval(qualifier, negative, leading, remaining);
                break;
            case Minute:
                v = IntervalUtils.MinutesFromInterval(qualifier, negative, leading, remaining);
                break;
            case Second:
                v = IntervalUtils.NanosFromInterval(qualifier, negative, leading, remaining)
                    / DateTimeUtils.NanosPerSecond;
                break;
            case Millisecond:
                v = IntervalUtils.NanosFromInterval(qualifier, negative, leading, remaining) / 1_000_000 % 1_000;
                break;
            case Microsecond:
                v = IntervalUtils.NanosFromInterval(qualifier, negative, leading, remaining) / 1_000 % 1_000_000;
                break;
            case Nanosecond:
                v = IntervalUtils.NanosFromInterval(qualifier, negative, leading, remaining)
                    % DateTimeUtils.NanosPerSecond;
                break;
            default:
                throw DbException.GetUnsupportedException("getDatePart(" + date + ", " + field + ')');
        }

        return (int)v;
    }

    internal static int ExtractDateTime(SessionLocal session, Value date, int field)
    {
        long[] a = DateTimeUtils.DateAndTimeFromValue(date, session);
        long dateValue = a[0];
        long timeNanos = a[1];
        switch (field)
        {
            case Year:
                return DateTimeUtils.YearFromDateValue(dateValue);
            case Month:
                return DateTimeUtils.MonthFromDateValue(dateValue);
            case Day:
                return DateTimeUtils.DayFromDateValue(dateValue);
            case Hour:
                return (int)(timeNanos / DateTimeUtils.NanosPerHour % 24);
            case Minute:
                return (int)(timeNanos / DateTimeUtils.NanosPerMinute % 60);
            case Second:
                return (int)(timeNanos / DateTimeUtils.NanosPerSecond % 60);
            case Millisecond:
                return (int)(timeNanos / 1_000_000 % 1_000);
            case Microsecond:
                return (int)(timeNanos / 1_000 % 1_000_000);
            case Nanosecond:
                return (int)(timeNanos % DateTimeUtils.NanosPerSecond);
            case Millennium:
                return MillenniumValue(DateTimeUtils.YearFromDateValue(dateValue));
            case Century:
                return CenturyValue(DateTimeUtils.YearFromDateValue(dateValue));
            case Decade:
                return DecadeValue(DateTimeUtils.YearFromDateValue(dateValue));
            case DayOfYear:
                return DateTimeUtils.GetDayOfYear(dateValue);
            case Dow:
                if (session.GetMode().Enum == Mode.ModeEnum.PostgreSql)
                {
                    return DateTimeUtils.GetSundayDayOfWeek(dateValue) - 1;
                }

                goto case DayOfWeek;
            case DayOfWeek:
                return GetLocalDayOfWeek(dateValue);
            case Week:
                return GetLocalWeekOfYear(dateValue);
            case WeekYear:
                {
                    WeekFields wf = GetWeekFields();
                    return DateTimeUtils.GetWeekYear(
                        dateValue, wf.FirstDayOfWeek, wf.MinimalDaysInFirstWeek);
                }

            case Quarter:
                return ((DateTimeUtils.MonthFromDateValue(dateValue) - 1) / 3) + 1;
            case IsoWeekYear:
                return DateTimeUtils.GetIsoWeekYear(dateValue);
            case IsoWeek:
                return DateTimeUtils.GetIsoWeekOfYear(dateValue);
            case IsoDayOfWeek:
                return DateTimeUtils.GetIsoDayOfWeek(dateValue);
            case TimezoneHour:
            case TimezoneMinute:
            case TimezoneSecond:
                {
                    int offsetSeconds;
                    if (date is ValueTimestampTimeZone timestampTimeZone)
                    {
                        offsetSeconds = timestampTimeZone.GetTimeZoneOffsetSeconds();
                    }
                    else if (date is ValueTimeTimeZone timeTimeZone)
                    {
                        offsetSeconds = timeTimeZone.GetTimeZoneOffsetSeconds();
                    }
                    else
                    {
                        offsetSeconds = session.CurrentTimeZone().GetTimeZoneOffsetLocal(dateValue, timeNanos);
                    }

                    if (field == TimezoneHour)
                    {
                        return offsetSeconds / 3_600;
                    }
                    else if (field == TimezoneMinute)
                    {
                        return offsetSeconds % 3_600 / 60;
                    }
                    else
                    {
                        return offsetSeconds % 60;
                    }
                }

            default:
                throw DbException.GetUnsupportedException(
                    "EXTRACT(" + GetFieldName(field) + " FROM " + date + ')');
        }
    }

    /// <summary>
    /// Truncate the given date-time value to the specified field.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="field">the date-time field</param>
    /// <param name="value">the date-time value</param>
    /// <returns>date the truncated value</returns>
    private static Value TruncateDate(SessionLocal session, int field, Value value)
    {
        long[] fieldDateAndTime = DateTimeUtils.DateAndTimeFromValue(value, session);
        long dateValue = fieldDateAndTime[0];
        long timeNanos = fieldDateAndTime[1];
        switch (field)
        {
            case Microsecond:
                timeNanos = timeNanos / 1_000L * 1_000L;
                break;
            case Millisecond:
                timeNanos = timeNanos / 1_000_000L * 1_000_000L;
                break;
            case Second:
                timeNanos = timeNanos / DateTimeUtils.NanosPerSecond * DateTimeUtils.NanosPerSecond;
                break;
            case Minute:
                timeNanos = timeNanos / DateTimeUtils.NanosPerMinute * DateTimeUtils.NanosPerMinute;
                break;
            case Hour:
                timeNanos = timeNanos / DateTimeUtils.NanosPerHour * DateTimeUtils.NanosPerHour;
                break;
            case Day:
                timeNanos = 0L;
                break;
            case IsoWeek:
                dateValue = TruncateToWeek(dateValue, 1);
                timeNanos = 0L;
                break;
            case Week:
                dateValue = TruncateToWeek(dateValue, GetWeekFields().FirstDayOfWeek);
                timeNanos = 0L;
                break;
            case IsoWeekYear:
                dateValue = TruncateToWeekYear(dateValue, 1, 4);
                timeNanos = 0L;
                break;
            case WeekYear:
                {
                    WeekFields fields = GetWeekFields();
                    dateValue = TruncateToWeekYear(dateValue, fields.FirstDayOfWeek, fields.MinimalDaysInFirstWeek);
                    break;
                }

            case Month:
                dateValue = (dateValue & (-1L << DateTimeUtils.ShiftMonth)) | 1L;
                timeNanos = 0L;
                break;
            case Quarter:
                dateValue = DateTimeUtils.DateValue(
                    DateTimeUtils.YearFromDateValue(dateValue),
                    (((DateTimeUtils.MonthFromDateValue(dateValue) - 1) / 3) * 3) + 1, 1);
                timeNanos = 0L;
                break;
            case Year:
                dateValue = (dateValue & (-1L << DateTimeUtils.ShiftYear))
                    | (1L << DateTimeUtils.ShiftMonth | 1L);
                timeNanos = 0L;
                break;
            case Decade:
                {
                    int year = DateTimeUtils.YearFromDateValue(dateValue);
                    if (year >= 0)
                    {
                        year = year / 10 * 10;
                    }
                    else
                    {
                        year = (year - 9) / 10 * 10;
                    }

                    dateValue = DateTimeUtils.DateValue(year, 1, 1);
                    timeNanos = 0L;
                    break;
                }

            case Century:
                {
                    int year = DateTimeUtils.YearFromDateValue(dateValue);
                    if (year > 0)
                    {
                        year = ((year - 1) / 100 * 100) + 1;
                    }
                    else
                    {
                        year = (year / 100 * 100) - 99;
                    }

                    dateValue = DateTimeUtils.DateValue(year, 1, 1);
                    timeNanos = 0L;
                    break;
                }

            case Millennium:
                {
                    int year = DateTimeUtils.YearFromDateValue(dateValue);
                    if (year > 0)
                    {
                        year = ((year - 1) / 1_000 * 1_000) + 1;
                    }
                    else
                    {
                        year = (year / 1_000 * 1_000) - 999;
                    }

                    dateValue = DateTimeUtils.DateValue(year, 1, 1);
                    timeNanos = 0L;
                    break;
                }

            default:
                throw DbException.GetUnsupportedException("DATE_TRUNC " + GetFieldName(field));
        }

        Value result = DateTimeUtils.DateTimeToValue(value, dateValue, timeNanos);
        if (session.GetMode().Enum == Mode.ModeEnum.PostgreSql && result.GetValueType() == Value.Date)
        {
            result = result.ConvertToForComparison(Value.TimestampTz, session);
        }

        return result;
    }

    private static long TruncateToWeek(long dateValue, int firstDayOfWeek)
    {
        long absoluteDay = DateTimeUtils.AbsoluteDayFromDateValue(dateValue);
        int dayOfWeek = DateTimeUtils.GetDayOfWeekFromAbsolute(absoluteDay, firstDayOfWeek);
        if (dayOfWeek != 1)
        {
            dateValue = DateTimeUtils.DateValueFromAbsoluteDay(absoluteDay - dayOfWeek + 1);
        }

        return dateValue;
    }

    private static long TruncateToWeekYear(long dateValue, int firstDayOfWeek, int minimalDaysInFirstWeek)
    {
        long abs = DateTimeUtils.AbsoluteDayFromDateValue(dateValue);
        int year = DateTimeUtils.YearFromDateValue(dateValue);
        long weekYearStart = DateTimeUtils.GetWeekYearAbsoluteStart(year, firstDayOfWeek, minimalDaysInFirstWeek);
        if (abs < weekYearStart)
        {
            weekYearStart = DateTimeUtils.GetWeekYearAbsoluteStart(
                year - 1, firstDayOfWeek, minimalDaysInFirstWeek);
        }
        else if (DateTimeUtils.MonthFromDateValue(dateValue) == 12
            && 24 + minimalDaysInFirstWeek < DateTimeUtils.DayFromDateValue(dateValue))
        {
            long next = DateTimeUtils.GetWeekYearAbsoluteStart(
                year + 1, firstDayOfWeek, minimalDaysInFirstWeek);
            if (abs >= next)
            {
                weekYearStart = next;
            }
        }

        return DateTimeUtils.DateValueFromAbsoluteDay(weekYearStart);
    }

    /// <summary>
    /// DATEADD function.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="field">the date-time field</param>
    /// <param name="count">count to add</param>
    /// <param name="v">value to add to</param>
    /// <returns>result</returns>
    public static Value DateaddValue(SessionLocal session, int field, long count, Value v)
    {
        if (field != Millisecond && field != Microsecond && field != Nanosecond
            && (count > int.MaxValue || count < int.MinValue))
        {
            throw DbException.GetInvalidValueException("DATEADD count", count);
        }

        long[] a = DateTimeUtils.DateAndTimeFromValue(v, session);
        long dateValue = a[0];
        long timeNanos = a[1];
        int type = v.GetValueType();
        switch (field)
        {
            case Millennium:
                return AddYearsMonths(field, true, count * 1_000, v, type, dateValue, timeNanos);
            case Century:
                return AddYearsMonths(field, true, count * 100, v, type, dateValue, timeNanos);
            case Decade:
                return AddYearsMonths(field, true, count * 10, v, type, dateValue, timeNanos);
            case Year:
                return AddYearsMonths(field, true, count, v, type, dateValue, timeNanos);
            case Quarter:
                return AddYearsMonths(field, false, count *= 3, v, type, dateValue, timeNanos);
            case Month:
                return AddYearsMonths(field, false, count, v, type, dateValue, timeNanos);
            case Week:
            case IsoWeek:
                count *= 7;
                goto case DayOfWeek;
            case DayOfWeek:
            case Dow:
            case IsoDayOfWeek:
            case Day:
            case DayOfYear:
                if (type == Value.Time || type == Value.TimeTz)
                {
                    throw DbException.GetInvalidValueException("DATEADD time part", GetFieldName(field));
                }

                dateValue = DateTimeUtils.DateValueFromAbsoluteDay(
                    DateTimeUtils.AbsoluteDayFromDateValue(dateValue) + count);
                return DateTimeUtils.DateTimeToValue(v, dateValue, timeNanos);
            case Hour:
                count *= DateTimeUtils.NanosPerHour;
                break;
            case Minute:
                count *= DateTimeUtils.NanosPerMinute;
                break;
            case Second:
            case Epoch:
                count *= DateTimeUtils.NanosPerSecond;
                break;
            case Millisecond:
                count *= 1_000_000;
                break;
            case Microsecond:
                count *= 1_000;
                break;
            case Nanosecond:
                break;
            case TimezoneHour:
                return AddToTimeZone(field, count * 3_600, v, type, dateValue, timeNanos);
            case TimezoneMinute:
                return AddToTimeZone(field, count * 60, v, type, dateValue, timeNanos);
            case TimezoneSecond:
                return AddToTimeZone(field, count, v, type, dateValue, timeNanos);
            default:
                throw DbException.GetUnsupportedException("DATEADD " + GetFieldName(field));
        }

        timeNanos += count;
        if (timeNanos >= DateTimeUtils.NanosPerDay || timeNanos < 0)
        {
            long d;
            if (timeNanos >= DateTimeUtils.NanosPerDay)
            {
                d = timeNanos / DateTimeUtils.NanosPerDay;
            }
            else
            {
                d = (timeNanos - DateTimeUtils.NanosPerDay + 1) / DateTimeUtils.NanosPerDay;
            }

            dateValue = DateTimeUtils.DateValueFromAbsoluteDay(
                DateTimeUtils.AbsoluteDayFromDateValue(dateValue) + d);
            timeNanos -= d * DateTimeUtils.NanosPerDay;
        }

        if (type == Value.Date)
        {
            return ValueTimestamp.FromDateValueAndNanos(dateValue, timeNanos);
        }

        return DateTimeUtils.DateTimeToValue(v, dateValue, timeNanos);
    }

    private static Value AddYearsMonths(
        int field, bool years, long count, Value v, int type, long dateValue, long timeNanos)
    {
        if (type == Value.Time || type == Value.TimeTz)
        {
            throw DbException.GetInvalidValueException("DATEADD time part", GetFieldName(field));
        }

        long year = DateTimeUtils.YearFromDateValue(dateValue);
        long month = DateTimeUtils.MonthFromDateValue(dateValue);
        if (years)
        {
            year += count;
        }
        else
        {
            month += count;
        }

        return DateTimeUtils.DateTimeToValue(v,
            DateTimeUtils.DateValueFromDenormalizedDate(year, month, DateTimeUtils.DayFromDateValue(dateValue)),
            timeNanos);
    }

    private static Value AddToTimeZone(int field, long count, Value v, int type, long dateValue, long timeNanos)
    {
        if (type == Value.TimestampTz)
        {
            return ValueTimestampTimeZone.FromDateValueAndNanos(dateValue, timeNanos,
                MathUtils.ConvertLongToInt(count + ((ValueTimestampTimeZone)v).GetTimeZoneOffsetSeconds()));
        }
        else if (type == Value.TimeTz)
        {
            return ValueTimeTimeZone.FromNanos(timeNanos,
                MathUtils.ConvertLongToInt(count + ((ValueTimeTimeZone)v).GetTimeZoneOffsetSeconds()));
        }
        else
        {
            throw DbException.GetUnsupportedException("DATEADD " + GetFieldName(field));
        }
    }

    /// <summary>
    /// Calculate the number of crossed unit boundaries between two timestamps.
    /// This method is supported for MS SQL Server compatibility.
    /// </summary>
    /// <remarks>
    /// <c>DATEDIFF(YEAR, '2004-12-31', '2005-01-01') = 1</c>
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="field">the date-time field</param>
    /// <param name="v1">the first date-time value</param>
    /// <param name="v2">the second date-time value</param>
    /// <returns>the number of crossed boundaries</returns>
    private static long DatediffValue(SessionLocal session, int field, Value v1, Value v2)
    {
        long[] a1 = DateTimeUtils.DateAndTimeFromValue(v1, session);
        long dateValue1 = a1[0];
        long absolute1 = DateTimeUtils.AbsoluteDayFromDateValue(dateValue1);
        long[] a2 = DateTimeUtils.DateAndTimeFromValue(v2, session);
        long dateValue2 = a2[0];
        long absolute2 = DateTimeUtils.AbsoluteDayFromDateValue(dateValue2);
        switch (field)
        {
            case Nanosecond:
            case Microsecond:
            case Millisecond:
            case Second:
            case Epoch:
            case Minute:
            case Hour:
                long timeNanos1 = a1[1];
                long timeNanos2 = a2[1];
                switch (field)
                {
                    case Nanosecond:
                        return ((absolute2 - absolute1) * DateTimeUtils.NanosPerDay) + (timeNanos2 - timeNanos1);
                    case Microsecond:
                        return ((absolute2 - absolute1) * (DateTimeUtils.MillisPerDay * 1_000))
                            + ((timeNanos2 / 1_000) - (timeNanos1 / 1_000));
                    case Millisecond:
                        return ((absolute2 - absolute1) * DateTimeUtils.MillisPerDay)
                            + ((timeNanos2 / 1_000_000) - (timeNanos1 / 1_000_000));
                    case Second:
                    case Epoch:
                        return ((absolute2 - absolute1) * 86_400)
                            + ((timeNanos2 / DateTimeUtils.NanosPerSecond) - (timeNanos1 / DateTimeUtils.NanosPerSecond));
                    case Minute:
                        return ((absolute2 - absolute1) * 1_440)
                            + ((timeNanos2 / DateTimeUtils.NanosPerMinute) - (timeNanos1 / DateTimeUtils.NanosPerMinute));
                    case Hour:
                        return ((absolute2 - absolute1) * 24)
                            + ((timeNanos2 / DateTimeUtils.NanosPerHour) - (timeNanos1 / DateTimeUtils.NanosPerHour));
                }

                goto case Day;
            case Day:
            case DayOfYear:
            case DayOfWeek:
            case Dow:
            case IsoDayOfWeek:
                return absolute2 - absolute1;
            case Week:
                return Weekdiff(absolute1, absolute2, GetWeekFields().FirstDayOfWeek);
            case IsoWeek:
                return Weekdiff(absolute1, absolute2, 1);
            case Month:
                return ((DateTimeUtils.YearFromDateValue(dateValue2)
                    - DateTimeUtils.YearFromDateValue(dateValue1)) * 12)
                    + DateTimeUtils.MonthFromDateValue(dateValue2) - DateTimeUtils.MonthFromDateValue(dateValue1);
            case Quarter:
                return ((DateTimeUtils.YearFromDateValue(dateValue2)
                    - DateTimeUtils.YearFromDateValue(dateValue1)) * 4)
                    + ((DateTimeUtils.MonthFromDateValue(dateValue2) - 1) / 3)
                    - ((DateTimeUtils.MonthFromDateValue(dateValue1) - 1) / 3);
            case Millennium:
                return MillenniumValue(DateTimeUtils.YearFromDateValue(dateValue2))
                    - MillenniumValue(DateTimeUtils.YearFromDateValue(dateValue1));
            case Century:
                return CenturyValue(DateTimeUtils.YearFromDateValue(dateValue2))
                    - CenturyValue(DateTimeUtils.YearFromDateValue(dateValue1));
            case Decade:
                return DecadeValue(DateTimeUtils.YearFromDateValue(dateValue2))
                    - DecadeValue(DateTimeUtils.YearFromDateValue(dateValue1));
            case Year:
                return DateTimeUtils.YearFromDateValue(dateValue2) - DateTimeUtils.YearFromDateValue(dateValue1);
            case TimezoneHour:
            case TimezoneMinute:
            case TimezoneSecond:
                {
                    int offsetSeconds1;
                    if (v1 is ValueTimestampTimeZone timestampTimeZone1)
                    {
                        offsetSeconds1 = timestampTimeZone1.GetTimeZoneOffsetSeconds();
                    }
                    else if (v1 is ValueTimeTimeZone timeTimeZone1)
                    {
                        offsetSeconds1 = timeTimeZone1.GetTimeZoneOffsetSeconds();
                    }
                    else
                    {
                        offsetSeconds1 = session.CurrentTimeZone().GetTimeZoneOffsetLocal(dateValue1, a1[1]);
                    }

                    int offsetSeconds2;
                    if (v2 is ValueTimestampTimeZone timestampTimeZone2)
                    {
                        offsetSeconds2 = timestampTimeZone2.GetTimeZoneOffsetSeconds();
                    }
                    else if (v2 is ValueTimeTimeZone timeTimeZone2)
                    {
                        offsetSeconds2 = timeTimeZone2.GetTimeZoneOffsetSeconds();
                    }
                    else
                    {
                        offsetSeconds2 = session.CurrentTimeZone().GetTimeZoneOffsetLocal(dateValue2, a2[1]);
                    }

                    if (field == TimezoneHour)
                    {
                        return (offsetSeconds2 / 3_600) - (offsetSeconds1 / 3_600);
                    }
                    else if (field == TimezoneMinute)
                    {
                        return (offsetSeconds2 / 60) - (offsetSeconds1 / 60);
                    }
                    else
                    {
                        return offsetSeconds2 - offsetSeconds1;
                    }
                }

            default:
                throw DbException.GetUnsupportedException("DATEDIFF " + GetFieldName(field));
        }
    }

    private static long Weekdiff(long absolute1, long absolute2, int firstDayOfWeek)
    {
        absolute1 += 4 - firstDayOfWeek;
        long r1 = absolute1 / 7;
        if (absolute1 < 0 && (r1 * 7 != absolute1))
        {
            r1--;
        }

        absolute2 += 4 - firstDayOfWeek;
        long r2 = absolute2 / 7;
        if (absolute2 < 0 && (r2 * 7 != absolute2))
        {
            r2--;
        }

        return r2 - r1;
    }

    private static int MillenniumValue(int year) => year > 0 ? (year + 999) / 1_000 : year / 1_000;

    private static int CenturyValue(int year) => year > 0 ? (year + 99) / 100 : year / 100;

    private static int DecadeValue(int year) => year >= 0 ? year / 10 : (year - 9) / 10;

    private static int GetLocalDayOfWeek(long dateValue)
        => DateTimeUtils.GetDayOfWeek(dateValue, GetWeekFields().FirstDayOfWeek);

    private static int GetLocalWeekOfYear(long dateValue)
    {
        WeekFields fields = GetWeekFields();
        return DateTimeUtils.GetWeekOfYear(dateValue, fields.FirstDayOfWeek, fields.MinimalDaysInFirstWeek);
    }

    private static WeekFields GetWeekFields()
    {
        WeekFields? fields = weekFields;
        if (fields == null)
        {
            weekFields = fields = WeekFields.Of(CultureInfo.CurrentCulture);
        }

        return fields;
    }

    private static Value ExtractEpoch(SessionLocal session, Value value)
    {
        ValueNumeric result;
        if (value is ValueInterval interval)
        {
            if (IntervalQualifiers.IsYearMonth(interval.GetQualifier()))
            {
                interval = (ValueInterval)interval.ConvertToType(TypeInfo.TypeIntervalYearToMonth);
                long leading = interval.GetLeading();
                long remaining = interval.GetRemaining();
                BigInteger bi = (new BigInteger(leading) * new BigInteger(31_557_600))
                    + new BigInteger(remaining * 2_592_000);
                if (interval.IsNegative())
                {
                    bi = -bi;
                }

                return ValueNumeric.Get(bi);
            }
            else
            {
                return ValueNumeric.Get(
                    BigDecimal.FromBigInteger(IntervalUtils.IntervalToAbsolute(interval))
                        .Divide(BdNanosPerSecond));
            }
        }

        long[] a = DateTimeUtils.DateAndTimeFromValue(value, session);
        long dateValue = a[0];
        long timeNanos = a[1];
        if (value is ValueTime)
        {
            result = ValueNumeric.Get(BigDecimal.FromLong(timeNanos).Divide(BdNanosPerSecond));
        }
        else if (value is ValueDate)
        {
            result = ValueNumeric.Get(
                new BigInteger(DateTimeUtils.AbsoluteDayFromDateValue(dateValue)) * BiSecondsPerDay);
        }
        else
        {
            BigDecimal bd = BigDecimal.FromLong(timeNanos).Divide(BdNanosPerSecond)
                .Add(BigDecimal.FromLong(DateTimeUtils.AbsoluteDayFromDateValue(dateValue))
                    .Multiply(BdSecondsPerDay));
            if (value is ValueTimestampTimeZone timestampTimeZone)
            {
                result = ValueNumeric.Get(
                    bd.Subtract(BigDecimal.FromLong(timestampTimeZone.GetTimeZoneOffsetSeconds())));
            }
            else if (value is ValueTimeTimeZone timeTimeZone)
            {
                result = ValueNumeric.Get(
                    bd.Subtract(BigDecimal.FromLong(timeTimeZone.GetTimeZoneOffsetSeconds())));
            }
            else
            {
                result = ValueNumeric.Get(bd);
            }
        }

        return result;
    }

    private static Value LastDayValue(SessionLocal session, Value v)
    {
        long dateValue;
        int valueType = v.GetValueType();
        switch (valueType)
        {
            case Value.Date:
                dateValue = ((ValueDate)v).GetDateValue();
                break;
            case Value.Timestamp:
                dateValue = ((ValueTimestamp)v).GetDateValue();
                break;
            case Value.TimestampTz:
                dateValue = ((ValueTimestampTimeZone)v).GetDateValue();
                break;
            default:
                dateValue = ((ValueTimestampTimeZone)DateTimeUtils.ParseTimestamp(v.GetString()!, session, true))
                    .GetDateValue();
                break;
        }

        int year = DateTimeUtils.YearFromDateValue(dateValue), month = DateTimeUtils.MonthFromDateValue(dateValue);
        int day = DateTimeUtils.GetDaysInMonth(year, month);
        long lastDay = DateTimeUtils.DateValue(year, month, day);
        if (lastDay == dateValue && valueType == Value.Date)
        {
            return v;
        }

        return ValueDate.FromDateValue(lastDay);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        if (right != null)
        {
            right = right.Optimize(session);
        }

        switch (function)
        {
            case Extract:
                type = field == Epoch
                    ? TypeInfo.GetTypeInfo(Value.Numeric,
                        ValueBigint.DecimalPrecision + ValueTimestamp.MaximumScale,
                        ValueTimestamp.MaximumScale, null)
                    : TypeInfo.TypeInteger;
                break;
            case DateTrunc:
                {
                    type = left.GetTypeInfo();
                    int valueType = type.GetValueType();
                    // TODO set scale when possible
                    if (!DataType.IsDateTimeType(valueType))
                    {
                        throw MvStore.Db.Store.GetInvalidExpressionTypeException("DATE_TRUNC datetime argument", left);
                    }
                    else if (session.GetMode().Enum == Mode.ModeEnum.PostgreSql && valueType == Value.Date)
                    {
                        type = TypeInfo.TypeTimestampTz;
                    }

                    break;
                }

            case Dateadd:
                {
                    int valueType = right!.GetTypeInfo().GetValueType();
                    if (valueType == Value.Date)
                    {
                        switch (field)
                        {
                            case Hour:
                            case Minute:
                            case Second:
                            case Millisecond:
                            case Microsecond:
                            case Nanosecond:
                            case Epoch:
                                valueType = Value.Timestamp;
                                break;
                        }
                    }

                    type = TypeInfo.GetTypeInfo(valueType);
                    break;
                }

            case Datediff:
                type = TypeInfo.TypeBigint;
                break;
            case LastDay:
                type = TypeInfo.TypeDate;
                break;
            default:
                throw DbException.GetInternalError("function=" + function);
        }

        if (left.IsConstant() && (right == null || right.IsConstant()) && !session.IsParsingCreateView())
        {
            return TypedValueExpression.GetTypedIfNull(GetValue(session), type);
        }

        return this;
    }

    /// <inheritdoc/>
    public override string GetName()
    {
        return Names[function];
    }

    /// <inheritdoc/>
    public override System.Text.StringBuilder GetUnenclosedSql(System.Text.StringBuilder builder, int sqlFlags)
    {
        _ = builder.Append(GetName()).Append('(');
        if (function == LastDay)
        {
            left.GetUnenclosedSql(builder, sqlFlags);
        }
        else
        {
            _ = builder.Append(GetFieldName(field));
            switch (function)
            {
                case Extract:
                    left.GetUnenclosedSql(builder.Append(" FROM "), sqlFlags);
                    break;
                case DateTrunc:
                    left.GetUnenclosedSql(builder.Append(", "), sqlFlags);
                    break;
                case Dateadd:
                case Datediff:
                    left.GetUnenclosedSql(builder.Append(", "), sqlFlags).Append(", ");
                    right!.GetUnenclosedSql(builder, sqlFlags);
                    break;
                default:
                    throw DbException.GetInternalError("function=" + function);
            }
        }

        return builder.Append(')');
    }

    /// <summary>
    /// The locale's definition of a week: the first day of the week and the least number of days
    /// that make the first week of the year.
    /// </summary>
    /// <remarks>
    /// A stand-in for <c>java.time.temporal.WeekFields</c>, which .NET does not have; the two
    /// numbers are read off the culture's own calendar settings.
    /// </remarks>
    private sealed class WeekFields
    {
        private WeekFields(int firstDayOfWeek, int minimalDaysInFirstWeek)
        {
            FirstDayOfWeek = firstDayOfWeek;
            MinimalDaysInFirstWeek = minimalDaysInFirstWeek;
        }

        /// <summary>
        /// The first day of the week, numbered as the JDK numbers it: Monday is 1 and Sunday is 7.
        /// </summary>
        public int FirstDayOfWeek { get; }

        /// <summary>
        /// The least number of days in the first week of the year.
        /// </summary>
        public int MinimalDaysInFirstWeek { get; }

        public static WeekFields Of(CultureInfo culture)
        {
            DateTimeFormatInfo format = culture.DateTimeFormat;
            int firstDayOfWeek = format.FirstDayOfWeek == System.DayOfWeek.Sunday
                ? 7
                : (int)format.FirstDayOfWeek;
            int minimalDaysInFirstWeek = format.CalendarWeekRule switch
            {
                CalendarWeekRule.FirstDay => 1,
                CalendarWeekRule.FirstFourDayWeek => 4,
                CalendarWeekRule.FirstFullWeek => 7,
                _ => throw new ArgumentOutOfRangeException(nameof(culture)),
            };
            return new WeekFields(firstDayOfWeek, minimalDaysInFirstWeek);
        }
    }
}
