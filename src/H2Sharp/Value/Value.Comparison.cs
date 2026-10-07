/*
 * Copyright 2004-2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Ported from org.h2.value.Value (the type ordering table).
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The ordering table decides which of two value types wins when an expression needs both,
 * which is why it is ported before TypeInfo: that class's promotion logic calls into it.
 *
 * Verified exhaustively against the reference: every pair of the 42 type numbers goes
 * through getHigherOrder and the result, or the error, is compared. The table is dense
 * nested switches over the value type numbers, which is what the reference has; a lookup
 * matrix would be easier to read but would hide the grouping that the reference relies on.
 */
using System;
using System.Globalization;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Message;

namespace H2Sharp.Values;

public static partial class Value
{
    /// <summary>
    /// The group each type belongs to, indexed by the type number. Two types can only be
    /// combined if their groups allow it, which is what most of this file decides.
    /// </summary>
    private static ReadOnlySpan<byte> Groups =>
    [
        GroupNull,                                                 // NULL
        GroupCharacterString, GroupCharacterString,                // CHAR, VARCHAR
        GroupCharacterString, GroupCharacterString,                // CLOB, VARCHAR_IGNORECASE
        GroupBinaryString, GroupBinaryString, GroupBinaryString,   // BINARY, VARBINARY, BLOB
        GroupBoolean,                                              // BOOLEAN
GroupNumeric, GroupNumeric, GroupNumeric, GroupNumeric, GroupNumeric, GroupNumeric, GroupNumeric,
        GroupNumeric,                                           // REAL, DOUBLE, DECFLOAT
        GroupDatetime, GroupDatetime, GroupDatetime,               // DATE, TIME, TIME_TZ
        GroupDatetime, GroupDatetime,                               // TIMESTAMP, TIMESTAMP_TZ
        GroupIntervalYm, GroupIntervalYm,                           // INTERVAL_YEAR, MONTH
        GroupIntervalDt, GroupIntervalDt, GroupIntervalDt, GroupIntervalDt, // DAY .. SECOND
        GroupIntervalYm,                                           // INTERVAL_YEAR_TO_MONTH
        GroupIntervalDt, GroupIntervalDt,                          // DAY_TO_HOUR, DAY_TO_MINUTE
        GroupIntervalDt, GroupIntervalDt,                          // DAY_TO_SECOND, HOUR_TO_MINUTE
        GroupIntervalDt, GroupIntervalDt,                          // HOUR_TO_SECOND, MINUTE_TO_SECOND
        GroupOther, GroupOther, GroupOther, GroupOther, GroupOther, // JAVA_OBJECT .. UUID
        GroupCollection, GroupCollection,                          // ARRAY, ROW
    ];

    /// <summary>
    /// The group the given type number belongs to.
    /// </summary>
    /// <param name="type">the value type</param>
    /// <returns>the group</returns>
    /// <remarks>
    /// Internal rather than private so the conformance tests can check the table's shape
    /// directly. A span built from literals cannot be reflected over, and the table being
    /// one entry short shifts every group after the gap, which is worth catching without a
    /// JVM in the loop.
    /// </remarks>
    internal static byte GroupOf(int type) => Groups[type];

    /// <summary>
    /// The number of entries in the group table, which has to match the number of value
    /// types.
    /// </summary>
    internal static int GroupCount => Groups.Length;

    /// <summary>
    /// The SQL name of each type, indexed by the type number plus one, so that the array
    /// starts at UNKNOWN. Used in the conversion error message.
    /// </summary>
    private static readonly string[] Names =
    [
        "UNKNOWN",
        "NULL",
        "CHARACTER", "CHARACTER VARYING", "CHARACTER LARGE OBJECT", "VARCHAR_IGNORECASE",
        "BINARY", "BINARY VARYING", "BINARY LARGE OBJECT",
        "BOOLEAN",
        "TINYINT", "SMALLINT", "INTEGER", "BIGINT",
        "NUMERIC", "REAL", "DOUBLE PRECISION", "DECFLOAT",
        "DATE", "TIME", "TIME WITH TIME ZONE", "TIMESTAMP", "TIMESTAMP WITH TIME ZONE",
        "INTERVAL YEAR", "INTERVAL MONTH",
        "INTERVAL DAY", "INTERVAL HOUR", "INTERVAL MINUTE", "INTERVAL SECOND",
        "INTERVAL YEAR TO MONTH",
        "INTERVAL DAY TO HOUR", "INTERVAL DAY TO MINUTE", "INTERVAL DAY TO SECOND",
        "INTERVAL HOUR TO MINUTE", "INTERVAL HOUR TO SECOND", "INTERVAL MINUTE TO SECOND",
        "JAVA_OBJECT", "ENUM", "GEOMETRY", "JSON", "UUID",
        "ARRAY", "ROW",
    ];

    /// <summary>
    /// Returns the name of the specified data type.
    /// </summary>
    /// <param name="valueType">the value type</param>
    /// <returns>the name</returns>
    public static string GetTypeName(int valueType) => Names[valueType + 1];

    /// <summary>
    /// Get the higher value order type of two value types. If values need to be converted
    /// to match the other operand's value type, the value with the lower order is converted
    /// to the value with the higher order.
    /// </summary>
    /// <param name="t1">the first value type</param>
    /// <param name="t2">the second value type</param>
    /// <returns>the higher value type of the two</returns>
    public static int GetHigherOrder(int t1, int t2)
    {
        if (t1 == t2)
        {
            if (t1 == Unknown)
            {
                throw DbException.Get(ErrorCode.UnknownDataType1, "?, ?");
            }

            return t1;
        }

        if (t1 < t2)
        {
            (t1, t2) = (t2, t1);
        }

        if (t1 == Unknown)
        {
            if (t2 == Null)
            {
                throw DbException.Get(ErrorCode.UnknownDataType1, "?, NULL");
            }

            return t2;
        }

        if (t2 == Unknown)
        {
            if (t1 == Null)
            {
                throw DbException.Get(ErrorCode.UnknownDataType1, "NULL, ?");
            }

            return t1;
        }

        if (t2 == Null)
        {
            return t1;
        }

        return GetHigherOrderKnown(t1, t2);
    }

    internal static int GetHigherOrderNonNull(int t1, int t2)
    {
        if (t1 == t2)
        {
            return t1;
        }

        if (t1 < t2)
        {
            (t1, t2) = (t2, t1);
        }

        return GetHigherOrderKnown(t1, t2);
    }

    /// <summary>
    /// The ordering table proper, for two types that are neither UNKNOWN nor NULL and are
    /// not equal, with the larger type number first.
    /// </summary>
    internal static int GetHigherOrderKnown(int t1, int t2)
    {
        int g1 = Groups[t1], g2 = Groups[t2];
        return g1 switch
        {
            GroupBoolean => g2 == GroupBinaryString
                ? throw DataTypeCombinationException(Boolean, t2)
                : t1,
            GroupNumeric => GetHigherNumeric(t1, t2, g2),
            GroupDatetime => GetHigherDateTime(t1, t2, g2),
            GroupIntervalYm => GetHigherIntervalYearMonth(t1, t2, g2),
            GroupIntervalDt => GetHigherIntervalDayTime(t1, t2, g2),
            GroupOther => GetHigherOther(t1, t2, g2),
            _ => t1,
        };
    }

    private static int GetHigherNumeric(int t1, int t2, int g2)
    {
        if (g2 == GroupNumeric)
        {
            return t1 switch
            {
                Real => t2 switch
                {
                    Integer => Double,
                    Bigint or Numeric => Decfloat,
                    _ => Real,
                },
                Double => t2 switch
                {
                    Bigint or Numeric => Decfloat,
                    _ => Double,
                },
                _ => t1,
            };
        }

        if (g2 == GroupBinaryString)
        {
            throw DataTypeCombinationException(t1, t2);
        }

        return t1;
    }

    private static int GetHigherDateTime(int t1, int t2, int g2)
    {
        if (g2 == GroupCharacterString)
        {
            return t1;
        }

        if (g2 != GroupDatetime)
        {
            throw DataTypeCombinationException(t1, t2);
        }

        return t1 switch
        {
            Time => t2 == Date ? Timestamp : Time,
            TimeTz => t2 == Date ? TimestampTz : TimeTz,
            Timestamp => t2 == TimeTz ? TimestampTz : Timestamp,
            _ => t1,
        };
    }

    private static int GetHigherIntervalYearMonth(int t1, int t2, int g2)
    {
        switch (g2)
        {
            case GroupIntervalYm:
                if (t1 == IntervalMonth && t2 == IntervalYear)
                {
                    return IntervalYearToMonth;
                }

                goto case GroupCharacterString;

            case GroupCharacterString:
            case GroupNumeric:
                return t1;

            default:
                throw DataTypeCombinationException(t1, t2);
        }
    }

    private static int GetHigherIntervalDayTime(int t1, int t2, int g2)
    {
        switch (g2)
        {
            case GroupIntervalDt:
                break;

            case GroupCharacterString:
            case GroupNumeric:
                return t1;

            default:
                throw DataTypeCombinationException(t1, t2);
        }

        switch (t1)
        {
            case IntervalHour:
                return IntervalDayToHour;

            case IntervalMinute:
                if (t2 == IntervalDay)
                {
                    return IntervalDayToMinute;
                }

                return IntervalHourToMinute;

            case IntervalSecond:
                if (t2 == IntervalDay)
                {
                    return IntervalDayToSecond;
                }

                if (t2 == IntervalHour)
                {
                    return IntervalHourToSecond;
                }

                return IntervalMinuteToSecond;

            case IntervalDayToHour:
                if (t2 == IntervalMinute)
                {
                    return IntervalDayToMinute;
                }

                if (t2 == IntervalSecond)
                {
                    return IntervalDayToSecond;
                }

                break;

            case IntervalDayToMinute:
                if (t2 == IntervalSecond)
                {
                    return IntervalDayToSecond;
                }

                break;

            case IntervalHourToMinute:
                switch (t2)
                {
                    case IntervalDay:
                    case IntervalDayToHour:
                    case IntervalDayToMinute:
                        return IntervalDayToMinute;

                    case IntervalSecond:
                        return IntervalHourToSecond;

                    case IntervalDayToSecond:
                        return IntervalDayToSecond;
                }

                break;

            case IntervalHourToSecond:
                switch (t2)
                {
                    case IntervalDay:
                    case IntervalDayToHour:
                    case IntervalDayToMinute:
                    case IntervalDayToSecond:
                        return IntervalDayToSecond;
                }

                break;

            case IntervalMinuteToSecond:
                switch (t2)
                {
                    case IntervalDay:
                    case IntervalDayToHour:
                    case IntervalDayToMinute:
                    case IntervalDayToSecond:
                        return IntervalDayToSecond;

                    case IntervalHour:
                    case IntervalHourToMinute:
                    case IntervalHourToSecond:
                        return IntervalHourToSecond;
                }

                break;
        }

        return t1;
    }

    private static int GetHigherOther(int t1, int t2, int g2)
    {
        switch (t1)
        {
            case JavaObject:
                if (g2 != GroupBinaryString)
                {
                    throw DataTypeCombinationException(t1, t2);
                }

                break;

            case Enum:
                if (g2 != GroupCharacterString && (g2 != GroupNumeric || t2 > Integer))
                {
                    throw DataTypeCombinationException(t1, t2);
                }

                break;

            case Geometry:
                if (g2 != GroupCharacterString && g2 != GroupBinaryString)
                {
                    throw DataTypeCombinationException(t1, t2);
                }

                break;

            case Json:
                switch (g2)
                {
                    case GroupDatetime:
                    case GroupIntervalYm:
                    case GroupIntervalDt:
                    case GroupOther:
                        throw DataTypeCombinationException(t1, t2);
                }

                break;

            case Uuid:
                switch (g2)
                {
                    case GroupCharacterString:
                    case GroupBinaryString:
                        break;

                    case GroupOther:
                        if (t2 == JavaObject)
                        {
                            break;
                        }

                        goto default;

                    default:
                        throw DataTypeCombinationException(t1, t2);
                }

                break;
        }

        return t1;
    }

    /// <summary>
    /// The reference returns a DbException from a factory method and throws it at the call
    /// site. C# has no exception type to return here because DbException is a static
    /// factory, so the helper returns the message and the call site throws.
    /// </summary>
    private static H2SharpException DataTypeCombinationException(int t1, int t2)
        => DbException.Get(
            ErrorCode.DataConversionError1,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{GetTypeName(t1)}, {GetTypeName(t2)}"));
}
