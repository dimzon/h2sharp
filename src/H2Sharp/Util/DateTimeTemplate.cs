using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Util;

/// <summary>
/// A date and time format that is also a parser: the same template both writes a value out and reads
/// one back.
/// </summary>
/// <remarks>
/// Templates are the letters H2 uses, and they are read strictly: one letter at a time, and anything
/// unexpected is a parse error rather than something to guess at. That strictness is the point, since
/// the template is what a user wrote and a template that quietly accepted something else would parse
/// a different format than the one that was declared.
/// <para>
/// A template also has to make sense as a whole, and that is checked once when it is parsed rather
/// than on every use: a second year, a day of the year together with a month and a day, an hour
/// together with a second of the day, a time zone second without a time zone minute. All of those are
/// refused at parse time, and a template that parses is one that can be used.
/// </para>
/// <para>
/// Parsed templates are cached, because the same handful of templates are used over and over and
/// parsing one is not free.
/// </para>
/// </remarks>
public sealed class DateTimeTemplate
{
    private const int Year = 0;
    private const int RoundedYear = 1;
    private const int Month = 2;
    private const int DayOfMonth = 3;
    private const int DayOfYear = 4;
    private const int Hour12 = 5;
    private const int Hour24 = 6;
    private const int Minute = 7;
    private const int SecondOfMinute = 8;
    private const int SecondOfDay = 9;
    private const int Fraction = 10;
    private const int AmPm = 11;
    private const int TimeZoneHour = 12;
    private const int TimeZoneMinute = 13;
    private const int TimeZoneSecond = 14;
    private const int DelimiterType = 15;

    private const long NanosPerSecond = DateTimeUtils.NanosPerSecond;
    private const long NanosPerMinute = DateTimeUtils.NanosPerMinute;
    private const long NanosPerHour = DateTimeUtils.NanosPerHour;
    private const int SecondsPerDay = 24 * 60 * 60;

    /// <summary>
    /// What one fractional second digit is worth.
    /// </summary>
    private static readonly int[] FractionalSecondsTable =
        [1_000_000_000, 100_000_000, 10_000_000, 1_000_000, 100_000, 10_000, 1_000, 100, 10, 1];

    private static readonly SmallLRUCache<string, DateTimeTemplate> Cache =
        SmallLRUCache<string, DateTimeTemplate>.NewInstance(100);

    private readonly Part[] parts;

    private readonly bool containsDate, containsTime, containsTimeZone;

    private DateTimeTemplate(Part[] parts, bool containsDate, bool containsTime, bool containsTimeZone)
    {
        this.parts = parts;
        this.containsDate = containsDate;
        this.containsTime = containsTime;
        this.containsTimeZone = containsTimeZone;
    }

    /// <summary>
    /// The template for a pattern, parsed once and kept.
    /// </summary>
    /// <param name="template">the pattern</param>
    /// <returns>the template</returns>
    public static DateTimeTemplate Of(string template)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(template, out DateTimeTemplate? cached))
            {
                return cached;
            }
        }

        DateTimeTemplate parsed = ParseTemplate(template);
        lock (Cache)
        {
            if (Cache.TryGetValue(template, out DateTimeTemplate? raced))
            {
                return raced;
            }

            _ = Cache.Put(template, parsed);
            return parsed;
        }
    }

    /// <summary>
    /// Writes a value out through this template.
    /// </summary>
    /// <param name="value">the value</param>
    /// <returns>the text, or null for a null value</returns>
    public string? Format(Value value)
    {
        long dateValue;
        long nanoOfDay;
        int offsetSeconds;
        switch (value.GetValueType())
        {
            case Value.Null:
                return null;

            case Value.Date:
                if (containsTime || containsTimeZone)
                {
                    throw DbException.Get(ErrorCode.ParseError1, "time or time zone fields with DATE");
                }

                dateValue = ((ValueDate)value).GetDateValue();
                nanoOfDay = 0L;
                offsetSeconds = 0;
                break;

            case Value.Time:
                if (containsDate || containsTimeZone)
                {
                    throw DbException.Get(ErrorCode.ParseError1, "date or time zone fields with TIME");
                }

                dateValue = 0L;
                nanoOfDay = ((ValueTime)value).GetNanos();
                offsetSeconds = 0;
                break;

            case Value.TimeTz:
                if (containsDate)
                {
                    throw DbException.Get(
                        ErrorCode.ParseError1, "date fields with TIME WITH TIME ZONE");
                }

                ValueTimeTimeZone timeZone = (ValueTimeTimeZone)value;
                dateValue = 0L;
                nanoOfDay = timeZone.GetNanos();
                offsetSeconds = timeZone.GetTimeZoneOffsetSeconds();
                break;

            case Value.Timestamp:
                if (containsTimeZone)
                {
                    throw DbException.Get(ErrorCode.ParseError1, "time zone fields with TIMESTAMP");
                }

                ValueTimestamp timestamp = (ValueTimestamp)value;
                dateValue = timestamp.GetDateValue();
                nanoOfDay = timestamp.GetTimeNanos();
                offsetSeconds = 0;
                break;

            case Value.TimestampTz:
                ValueTimestampTimeZone timestampTz = (ValueTimestampTimeZone)value;
                dateValue = timestampTz.GetDateValue();
                nanoOfDay = timestampTz.GetTimeNanos();
                offsetSeconds = timestampTz.GetTimeZoneOffsetSeconds();
                break;

            default:
                throw DbException.GetUnsupportedException(value.GetTypeInfo().GetTraceSql());
        }

        StringBuilder builder = new();
        foreach (Part part in parts)
        {
            part.Format(builder, dateValue, nanoOfDay, offsetSeconds);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads a value in through this template.
    /// </summary>
    /// <param name="string">the text</param>
    /// <param name="targetType">what to read it as</param>
    /// <param name="provider">supplies the current date for the parts the template leaves out</param>
    /// <returns>the value</returns>
    public Value Parse(string @string, TypeInfo targetType, CastDataProvider provider)
    {
        switch (targetType.GetValueType())
        {
            case Value.Date:
                if (containsTime || containsTimeZone)
                {
                    throw DbException.Get(ErrorCode.ParseError1, "time or time zone fields with DATE");
                }

                int[] yearMonthDate = YearMonth(provider);
                return ValueDate.FromDateValue(
                    ConstructDate(Parse(@string, yearMonthDate[0]), yearMonthDate));

            case Value.Time:
                if (containsDate || containsTimeZone)
                {
                    throw DbException.Get(ErrorCode.ParseError1, "date or time zone fields with TIME");
                }

                return ValueTime.FromNanos(ConstructTime(Parse(@string, 0)));

            case Value.TimeTz:
                if (containsDate)
                {
                    throw DbException.Get(
                        ErrorCode.ParseError1, "date fields with TIME WITH TIME ZONE");
                }

                int[] targetTimeZone = Parse(@string, 0);
                return ValueTimeTimeZone.FromNanos(
                    ConstructTime(targetTimeZone), ConstructOffset(targetTimeZone));

            case Value.Timestamp:
                if (containsTimeZone)
                {
                    throw DbException.Get(ErrorCode.ParseError1, "time zone fields with TIMESTAMP");
                }

                int[] yearMonthStamp = YearMonth(provider);
                int[] targetStamp = Parse(@string, yearMonthStamp[0]);
                return ValueTimestamp.FromDateValueAndNanos(
                    ConstructDate(targetStamp, yearMonthStamp), ConstructTime(targetStamp));

            case Value.TimestampTz:
                int[] yearMonthTz = YearMonth(provider);
                int[] targetTz = Parse(@string, yearMonthTz[0]);
                return ValueTimestampTimeZone.FromDateValueAndNanos(
                    ConstructDate(targetTz, yearMonthTz),
                    ConstructTime(targetTz),
                    ConstructOffset(targetTz));

            default:
                throw DbException.GetUnsupportedException(targetType.GetTraceSql());
        }
    }

    private static int[] YearMonth(CastDataProvider provider)
    {
        long dateValue = provider.CurrentTimestamp().GetDateValue();
        return
        [
            DateTimeUtils.YearFromDateValue(dateValue),
            DateTimeUtils.MonthFromDateValue(dateValue),
        ];
    }

    /// <summary>
    /// The current year and month, for the parts a template does not mention.
    /// </summary>
    private int[] Parse(string @string, int year)
    {
        int[] target = new int[15];
        Array.Fill(target, int.MinValue);
        Scanner s = new(@string);
        for (int i = 0, l = parts.Length - 1; i <= l; i++)
        {
            Part part = parts[i];
            part.Parse(
                target,
                s,

                // Left-delimited
                (i == 0
                    || ((1 << part.Type()) & (1 << AmPm | 1 << TimeZoneHour)) != 0
                    || ((1 << parts[i - 1].Type()) & (1 << DelimiterType | 1 << AmPm)) != 0)

                // Right-delimited
                && (i == l
                    || part.Type() == AmPm
                    || ((1 << parts[i + 1].Type()) & (1 << DelimiterType | 1 << AmPm | 1 << TimeZoneHour)) != 0),
                year);
        }

        return target;
    }

    private static long ConstructDate(int[] target, int[] yearMonth)
    {
        int year = target[Year];
        if (year == int.MinValue)
        {
            year = target[RoundedYear];
        }

        if (year == int.MinValue)
        {
            year = yearMonth[0];
        }

        int dayOfYear = target[DayOfYear];
        if (dayOfYear != int.MinValue)
        {
            if (dayOfYear < 1 || dayOfYear > (DateTimeUtils.IsLeapYear(year) ? 366 : 365))
            {
                throw DbException.Get(
                    ErrorCode.ParseError1, "Day of year " + dayOfYear.ToString(CultureInfo.InvariantCulture));
            }

            return DateTimeUtils.DateValueFromAbsoluteDay(
                DateTimeUtils.AbsoluteDayFromYear(year) + dayOfYear - 1);
        }

        int month = target[Month];
        if (month == int.MinValue)
        {
            month = yearMonth[1];
        }

        int day = target[DayOfMonth];
        if (day == int.MinValue)
        {
            day = 1;
        }

        if (!DateTimeUtils.IsValidDate(year, month, day))
        {
            throw DbException.Get(
                ErrorCode.ParseError1,
                "Invalid date, year=" + year.ToString(CultureInfo.InvariantCulture)
                + ", month=" + month.ToString(CultureInfo.InvariantCulture)
                + ", day=" + day.ToString(CultureInfo.InvariantCulture));
        }

        return DateTimeUtils.DateValue(year, month, day);
    }

    private static long ConstructTime(int[] target)
    {
        int secondOfDay = target[SecondOfDay];
        if (secondOfDay == int.MinValue)
        {
            int hour = target[Hour24];
            if (hour == int.MinValue)
            {
                hour = target[Hour12];
                if (hour == int.MinValue)
                {
                    hour = 0;
                }
                else
                {
                    if (hour < 1 || hour > 12)
                    {
                        throw DbException.Get(
                            ErrorCode.ParseError1,
                            "Hour(12) " + hour.ToString(CultureInfo.InvariantCulture));
                    }

                    if (hour == 12)
                    {
                        hour = 0;
                    }

                    hour += target[AmPm] * 12;
                }
            }
            else if (hour < 0 || hour > 23)
            {
                throw DbException.Get(
                    ErrorCode.ParseError1,
                    "Hour(24) " + hour.ToString(CultureInfo.InvariantCulture));
            }

            int minute = target[Minute];
            if (minute == int.MinValue)
            {
                minute = 0;
            }
            else if (minute < 0 || minute > 59)
            {
                throw DbException.Get(
                    ErrorCode.ParseError1,
                    "Minute " + minute.ToString(CultureInfo.InvariantCulture));
            }

            int second = target[SecondOfMinute];
            if (second == int.MinValue)
            {
                second = 0;
            }
            else if (second < 0 || second > 59)
            {
                throw DbException.Get(
                    ErrorCode.ParseError1,
                    "Second of minute " + second.ToString(CultureInfo.InvariantCulture));
            }

            secondOfDay = ((hour * 60) + minute) * 60 + second;
        }
        else if (secondOfDay < 0 || secondOfDay >= SecondsPerDay)
        {
            throw DbException.Get(
                ErrorCode.ParseError1,
                "Second of day " + secondOfDay.ToString(CultureInfo.InvariantCulture));
        }

        int fraction = target[Fraction];
        if (fraction == int.MinValue)
        {
            fraction = 0;
        }

        return (secondOfDay * NanosPerSecond) + fraction;
    }

    private static int ConstructOffset(int[] target)
    {
        int hour = target[TimeZoneHour];
        if (hour == int.MinValue)
        {
            return 0;
        }

        bool negative = hour < 0;
        if (negative)
        {
            if (hour == -100)
            {
                hour = 0;
            }
            else
            {
                hour = -hour;
            }
        }

        int minute = target[TimeZoneMinute];
        if (minute == int.MinValue)
        {
            minute = 0;
        }
        else if (minute > 59)
        {
            throw DbException.Get(
                ErrorCode.ParseError1,
                "Time zone minute " + minute.ToString(CultureInfo.InvariantCulture));
        }

        int second = target[TimeZoneSecond];
        if (second == int.MinValue)
        {
            second = 0;
        }
        else if (second > 59)
        {
            throw DbException.Get(
                ErrorCode.ParseError1,
                "Time zone second " + second.ToString(CultureInfo.InvariantCulture));
        }

        int offset = ((hour * 60) + minute) * 60 + second;
        if (offset > (18 * 60 * 60))
        {
            throw DbException.Get(ErrorCode.ParseError1, "Time zone offset is too large");
        }

        return negative ? -offset : offset;
    }

    private static int CheckUsed(int usedFields, int type, string template)
    {
        int newUsedFields = usedFields | (1 << type);
        if (usedFields == newUsedFields)
        {
            throw DbException.Get(ErrorCode.ParseError1, template);
        }

        return newUsedFields;
    }

    private static DateTimeTemplate ParseTemplate(string template)
    {
        List<Part> parts = [];
        Scanner s = new(template);
        int usedFields = 0;
        for (int c; (c = s.ReadChar()) >= 0;)
        {
            Part part;
            switch (c)
            {
                case '-':
                    part = Delimiter.MinusSign;
                    break;
                case '.':
                    part = Delimiter.Period;
                    break;
                case '/':
                    part = Delimiter.Solidus;
                    break;
                case ',':
                    part = Delimiter.Comma;
                    break;
                case '\'':
                    part = Delimiter.Apostrophe;
                    break;
                case ';':
                    part = Delimiter.Semicolon;
                    break;
                case ':':
                    part = Delimiter.Colon;
                    break;
                case ' ':
                    part = Delimiter.Space;
                    break;

                case 'Y':
                    usedFields = CheckUsed(usedFields, Year, template);
                    if (s.ReadCharIf('Y'))
                    {
                        if (s.ReadCharIf('Y'))
                        {
                            part = s.ReadCharIf('Y') ? Field.YYYY : Field.YYY;
                        }
                        else
                        {
                            part = Field.YY;
                        }
                    }
                    else
                    {
                        part = Field.Y;
                    }

                    break;

                case 'R':

                    // Year and rounded year may not be used together, mark both as
                    // year
                    usedFields = CheckUsed(usedFields, Year, template);
                    s.ReadChar('R');
                    if (s.ReadCharIf('R'))
                    {
                        s.ReadChar('R');
                        part = Field.RRRR;
                    }
                    else
                    {
                        part = Field.RR;
                    }

                    break;

                case 'M':
                    if (s.ReadCharIf('I'))
                    {
                        usedFields = CheckUsed(usedFields, Minute, template);
                        part = Field.MI;
                    }
                    else
                    {
                        s.ReadChar('M');
                        usedFields = CheckUsed(usedFields, Month, template);
                        part = Field.MM;
                    }

                    break;

                case 'D':
                    s.ReadChar('D');
                    if (s.ReadCharIf('D'))
                    {
                        usedFields = CheckUsed(usedFields, DayOfYear, template);
                        part = Field.DDD;
                    }
                    else
                    {
                        usedFields = CheckUsed(usedFields, DayOfMonth, template);
                        part = Field.DD;
                    }

                    break;

                case 'H':
                    s.ReadChar('H');
                    if (s.ReadCharIf('2'))
                    {
                        s.ReadChar('4');
                        usedFields = CheckUsed(usedFields, Hour24, template);
                        part = Field.HH24;
                    }
                    else
                    {
                        if (s.ReadCharIf('1'))
                        {
                            s.ReadChar('2');
                        }

                        usedFields = CheckUsed(usedFields, Hour12, template);
                        part = Field.HH12;
                    }

                    break;

                case 'S':
                    s.ReadChar('S');
                    if (s.ReadCharIf('S'))
                    {
                        s.ReadChar('S');
                        s.ReadChar('S');
                        usedFields = CheckUsed(usedFields, SecondOfDay, template);
                        part = Field.SSSSS;
                    }
                    else
                    {
                        usedFields = CheckUsed(usedFields, SecondOfMinute, template);
                        part = Field.SS;
                    }

                    break;

                case 'F':
                    s.ReadChar('F');
                    c = s.ReadChar();
                    if (c < '1' || c > '9')
                    {
                        throw DbException.Get(ErrorCode.ParseError1, template);
                    }

                    usedFields = CheckUsed(usedFields, Fraction, template);
                    part = Field.Ff(c - '0');
                    break;

                case 'A':
                case 'P':
                    s.ReadChar('.');
                    s.ReadChar('M');
                    s.ReadChar('.');
                    usedFields = CheckUsed(usedFields, AmPm, template);
                    part = Field.AmPm;
                    break;

                case 'T':
                    s.ReadChar('Z');
                    if (s.ReadCharIf('H'))
                    {
                        usedFields = CheckUsed(usedFields, TimeZoneHour, template);
                        part = Field.TZH;
                    }
                    else if (s.ReadCharIf('M'))
                    {
                        usedFields = CheckUsed(usedFields, TimeZoneMinute, template);
                        part = Field.TZM;
                    }
                    else
                    {
                        s.ReadChar('S');
                        usedFields = CheckUsed(usedFields, TimeZoneSecond, template);
                        part = Field.TZS;
                    }

                    break;

                default:
                    throw DbException.Get(ErrorCode.ParseError1, template);
            }

            parts.Add(part);
        }

        if ((((usedFields & (1 << DayOfYear)) != 0)
                && (usedFields & (1 << Month | 1 << DayOfMonth)) != 0)
            || (((usedFields & (1 << Hour12)) != 0) != ((usedFields & (1 << AmPm)) != 0))
            || ((usedFields & (1 << Hour24)) != 0 && (usedFields & (1 << Hour12)) != 0)
            || ((usedFields & (1 << SecondOfDay)) != 0
                && ((usedFields & (1 << Hour12 | 1 << Hour24 | 1 << Minute | 1 << SecondOfMinute)) != 0))
            || ((usedFields & (1 << TimeZoneSecond)) != 0 && (usedFields & (1 << TimeZoneMinute)) == 0)
            || ((usedFields & (1 << TimeZoneMinute)) != 0 && (usedFields & (1 << TimeZoneHour)) == 0))
        {
            throw DbException.Get(ErrorCode.ParseError1, template);
        }

        return new DateTimeTemplate(
            [.. parts],
            (usedFields & (1 << Year | 1 << Month | 1 << DayOfMonth | 1 << DayOfYear)) != 0,
            (usedFields
                & (1 << Hour24 | 1 << Hour12 | 1 << Minute | 1 << SecondOfMinute | 1 << SecondOfDay
                    | 1 << AmPm)) != 0,
            (usedFields & (1 << TimeZoneHour | 1 << TimeZoneMinute | 1 << TimeZoneSecond)) != 0);
    }

    private static void FormatLast(StringBuilder builder, int value, int digits)
    {
        if (digits == 2)
        {
            StringUtils.AppendTwoDigits(builder, value);
        }
        else
        {
            StringUtils.AppendZeroPadded(builder, digits, value);
        }
    }

    /// <summary>
    /// Reads a template, and everything it contains, one character at a time.
    /// </summary>
    private sealed class Scanner(string @string)
    {
        private int offset;

        private readonly int length = @string.Length;

        internal string String => @string;

        internal int ReadChar() => offset < length ? @string[offset++] : -1;

        internal void ReadChar(char c)
        {
            if (offset >= length || @string[offset] != c)
            {
                throw DbException.Get(ErrorCode.ParseError1, @string);
            }

            offset++;
        }

        internal bool ReadCharIf(char c)
        {
            if (offset < length && @string[offset] == c)
            {
                offset++;
                return true;
            }

            return false;
        }

        internal int ReadPositiveInt(int digits, bool delimited)
        {
            int start = offset, end;
            if (delimited)
            {
                end = start;
                for (char c; end < length && (c = @string[end]) is >= '0' and <= '9'; end++)
                {
                }

                if (start == end)
                {
                    throw DbException.Get(ErrorCode.ParseError1, @string);
                }
            }
            else
            {
                end = start + digits;
                if (end > length)
                {
                    throw DbException.Get(ErrorCode.ParseError1, @string);
                }
            }

            try
            {
                offset = end;
                return StringUtils.ParseUInt31(@string, start, end);
            }
            catch (FormatException)
            {
                throw DbException.Get(ErrorCode.ParseError1, @string);
            }
        }

        internal int ReadNanos(int digits, bool delimited)
        {
            int start = offset, end = start;
            int nanos = 0, mul = 100_000_000;
            if (delimited)
            {
                end = start;
                for (char c; end < length && (c = @string[end]) is >= '0' and <= '9'; end++)
                {
                    nanos += mul * (c - '0');
                    mul /= 10;
                }

                if (start == end)
                {
                    throw DbException.Get(ErrorCode.ParseError1, @string);
                }
            }
            else
            {
                end = start + digits;
                if (end > length)
                {
                    throw DbException.Get(ErrorCode.ParseError1, @string);
                }

                for (; start < end; start++)
                {
                    char c = @string[start];
                    if (c is < '0' or > '9')
                    {
                        throw DbException.Get(ErrorCode.ParseError1, @string);
                    }

                    nanos += mul * (c - '0');
                    mul /= 10;
                }
            }

            offset = end;
            return nanos;
        }
    }

    /// <summary>
    /// One piece of a template: either a field that writes a value, or a character that goes through
    /// unchanged and has to match on the way back.
    /// </summary>
    private abstract class Part
    {
        internal abstract int Type();

        internal abstract void Format(StringBuilder builder, long dateValue, long timeNanos, int offsetSeconds);

        internal abstract void Parse(int[] target, Scanner s, bool delimited, int year);
    }

    /// <summary>
    /// A character that is copied through and has to be matched when reading.
    /// </summary>
    private sealed class Delimiter(char delimiter) : Part
    {
        internal static readonly Delimiter MinusSign = new('-');
        internal static readonly Delimiter Period = new('.');
        internal static readonly Delimiter Solidus = new('/');
        internal static readonly Delimiter Comma = new(',');
        internal static readonly Delimiter Apostrophe = new('\'');
        internal static readonly Delimiter Semicolon = new(';');
        internal static readonly Delimiter Colon = new(':');
        internal static readonly Delimiter Space = new(' ');

        internal override int Type() => DateTimeTemplate.DelimiterType;

        internal override void Format(StringBuilder builder, long dateValue, long timeNanos, int offsetSeconds)
            => _ = builder.Append(delimiter);

        internal override void Parse(int[] target, Scanner s, bool delimited, int year)
            => s.ReadChar(delimiter);
    }

    /// <summary>
    /// A field of the value, written with however many digits it takes.
    /// </summary>
    private sealed class Field(int type, int digits) : Part
    {
        internal static readonly Field Y = new(DateTimeTemplate.Year, 1);
        internal static readonly Field YY = new(DateTimeTemplate.Year, 2);
        internal static readonly Field YYY = new(DateTimeTemplate.Year, 3);
        internal static readonly Field YYYY = new(DateTimeTemplate.Year, 4);

        internal static readonly Field RR = new(DateTimeTemplate.RoundedYear, 2);
        internal static readonly Field RRRR = new(DateTimeTemplate.RoundedYear, 4);

        internal static readonly Field MM = new(DateTimeTemplate.Month, 2);

        internal static readonly Field DD = new(DateTimeTemplate.DayOfMonth, 2);
        internal static readonly Field DDD = new(DateTimeTemplate.DayOfYear, 3);

        internal static readonly Field HH12 = new(DateTimeTemplate.Hour12, 2);
        internal static readonly Field HH24 = new(DateTimeTemplate.Hour24, 2);

        internal static readonly Field MI = new(DateTimeTemplate.Minute, 2);

        internal static readonly Field SS = new(DateTimeTemplate.SecondOfMinute, 2);
        internal static readonly Field SSSSS = new(DateTimeTemplate.SecondOfDay, 5);

        internal static readonly Field AmPm = new(DateTimeTemplate.AmPm, 4);

        internal static readonly Field TZH = new(DateTimeTemplate.TimeZoneHour, 2);
        internal static readonly Field TZM = new(DateTimeTemplate.TimeZoneMinute, 2);
        internal static readonly Field TZS = new(DateTimeTemplate.TimeZoneSecond, 2);

        private static readonly Field[] FractionFields = BuildFractions();

        internal static Field Ff(int digits) => FractionFields[digits - 1];

        private static Field[] BuildFractions()
        {
            Field[] fields = new Field[9];
            for (int i = 0; i < 9;)
            {
                fields[i] = new Field(DateTimeTemplate.Fraction, ++i);
            }

            return fields;
        }

        internal override int Type() => type;

        internal override void Format(StringBuilder builder, long dateValue, long timeNanos, int offsetSeconds)
        {
            switch (type)
            {
                case DateTimeTemplate.Year:
                case DateTimeTemplate.RoundedYear:
                    {
                        int y = DateTimeUtils.YearFromDateValue(dateValue);
                        if (y < 0)
                        {
                            _ = builder.Append('-');
                            y = -y;
                        }

                        switch (digits)
                        {
                            case 1:
                                y %= 10;
                                break;
                            case 2:
                                y %= 100;
                                break;
                            case 3:
                                y %= 1_000;
                                break;
                        }

                        FormatLast(builder, y, digits);
                        break;
                    }

                case DateTimeTemplate.Month:
                    StringUtils.AppendTwoDigits(builder, DateTimeUtils.MonthFromDateValue(dateValue));
                    break;

                case DateTimeTemplate.DayOfMonth:
                    StringUtils.AppendTwoDigits(builder, DateTimeUtils.DayFromDateValue(dateValue));
                    break;

                case DateTimeTemplate.DayOfYear:
                    StringUtils.AppendZeroPadded(builder, 3, DateTimeUtils.GetDayOfYear(dateValue));
                    break;

                case DateTimeTemplate.Hour12:
                    {
                        int h = (int)(timeNanos / DateTimeTemplate.NanosPerHour);
                        if (h == 0)
                        {
                            h = 12;
                        }
                        else if (h > 12)
                        {
                            h -= 12;
                        }

                        StringUtils.AppendTwoDigits(builder, h);
                        break;
                    }

                case DateTimeTemplate.Hour24:
                    StringUtils.AppendTwoDigits(builder, (int)(timeNanos / DateTimeTemplate.NanosPerHour));
                    break;

                case DateTimeTemplate.Minute:
                    StringUtils.AppendTwoDigits(
                        builder, (int)((timeNanos / DateTimeTemplate.NanosPerMinute) % 60));
                    break;

                case DateTimeTemplate.SecondOfMinute:
                    StringUtils.AppendTwoDigits(
                        builder, (int)((timeNanos / DateTimeTemplate.NanosPerSecond) % 60));
                    break;

                case DateTimeTemplate.SecondOfDay:
                    StringUtils.AppendZeroPadded(
                        builder, 5, (int)(timeNanos / DateTimeTemplate.NanosPerSecond));
                    break;

                case DateTimeTemplate.Fraction:
                    FormatLast(
                        builder,
                        (int)(timeNanos % DateTimeTemplate.NanosPerSecond) / FractionalSecondsTable[digits],
                        digits);
                    break;

                case DateTimeTemplate.AmPm:
                    {
                        int h = (int)(timeNanos / DateTimeTemplate.NanosPerHour);
                        _ = builder.Append(h < 12 ? "A.M." : "P.M.");
                        break;
                    }

                case DateTimeTemplate.TimeZoneHour:
                    {
                        int h = offsetSeconds / 3_600;
                        if (offsetSeconds >= 0)
                        {
                            _ = builder.Append('+');
                        }
                        else
                        {
                            h = -h;
                            _ = builder.Append('-');
                        }

                        StringUtils.AppendTwoDigits(builder, h);
                        break;
                    }

                case DateTimeTemplate.TimeZoneMinute:
                    StringUtils.AppendTwoDigits(builder, Math.Abs((offsetSeconds % 3_600) / 60));
                    break;

                case DateTimeTemplate.TimeZoneSecond:
                    StringUtils.AppendTwoDigits(builder, Math.Abs(offsetSeconds % 60));
                    break;
            }
        }

        internal override void Parse(int[] target, Scanner s, bool delimited, int year)
        {
            switch (type)
            {
                case DateTimeTemplate.Year:
                case DateTimeTemplate.RoundedYear:
                    {
                        bool negative = s.ReadCharIf('-');
                        if (!negative)
                        {
                            _ = s.ReadCharIf('+');
                        }

                        int v = s.ReadPositiveInt(digits, delimited);
                        if (negative)
                        {
                            if (digits < 4 || type == DateTimeTemplate.RoundedYear)
                            {
                                throw DbException.Get(ErrorCode.ParseError1, s.String);
                            }

                            v = -v;
                        }
                        else if (digits < 4)
                        {
                            if (digits == 1)
                            {
                                if (v > 9)
                                {
                                    throw DbException.Get(ErrorCode.ParseError1, s.String);
                                }

                                v += (year / 10) * 10;
                            }
                            else if (digits == 2)
                            {
                                if (v > 99)
                                {
                                    throw DbException.Get(ErrorCode.ParseError1, s.String);
                                }

                                v += (year / 100) * 100;
                                if (type == DateTimeTemplate.RoundedYear)
                                {
                                    if (v > year + 50)
                                    {
                                        v -= 100;
                                    }
                                    else if (v < year - 49)
                                    {
                                        v += 100;
                                    }
                                }
                            }
                            else if (digits == 3)
                            {
                                if (v > 999)
                                {
                                    throw DbException.Get(ErrorCode.ParseError1, s.String);
                                }

                                v += (year / 1_000) * 1_000;
                            }
                        }

                        target[type] = v;
                        break;
                    }

                case DateTimeTemplate.Month:
                case DateTimeTemplate.DayOfMonth:
                case DateTimeTemplate.DayOfYear:
                case DateTimeTemplate.Hour12:
                case DateTimeTemplate.Hour24:
                case DateTimeTemplate.Minute:
                case DateTimeTemplate.SecondOfMinute:
                case DateTimeTemplate.SecondOfDay:
                case DateTimeTemplate.TimeZoneMinute:
                case DateTimeTemplate.TimeZoneSecond:
                    target[type] = s.ReadPositiveInt(digits, delimited);
                    break;

                case DateTimeTemplate.Fraction:
                    target[DateTimeTemplate.Fraction] = s.ReadNanos(digits, delimited);
                    break;

                case DateTimeTemplate.AmPm:
                    {
                        int v;
                        if (s.ReadCharIf('A'))
                        {
                            v = 0;
                        }
                        else
                        {
                            s.ReadChar('P');
                            v = 1;
                        }

                        s.ReadChar('.');
                        s.ReadChar('M');
                        s.ReadChar('.');
                        target[DateTimeTemplate.AmPm] = v;
                        break;
                    }

                case DateTimeTemplate.TimeZoneHour:
                    {
                        bool negative = s.ReadCharIf('-');
                        if (!negative && !s.ReadCharIf('+'))
                        {
                            s.ReadChar(' ');
                        }

                        int v = s.ReadPositiveInt(digits, delimited);
                        if (v > 18)
                        {
                            throw DbException.Get(ErrorCode.ParseError1, s.String);
                        }

                        target[DateTimeTemplate.TimeZoneHour] = negative ? (v == 0 ? -100 : -v) : v;
                        break;
                    }
            }
        }
    }
}
