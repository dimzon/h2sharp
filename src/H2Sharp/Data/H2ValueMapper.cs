/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * The two directions between the caller's world and the engine's.
 *
 * JDBC has no boxing problem: setObject takes anything and the driver converts by the
 * column type it knows. Here the value arrives untyped and the column type is known only
 * inside the statement, so the conversion happens twice — once here, guessing from the
 * CLR type, and once in the engine, which converts to the column it is going into. A
 * guess that loses information would be silent corruption, so anything without an exact
 * CLR home refuses by name rather than answering wrongly.
 *
 * The conventions where one side has no counterpart are written down, not discovered:
 * a DateTime keeps no zone because TIMESTAMP has none; a TIME_TZ reads with the date
 * of DateTime.MinValue because a time with an offset is not a point in time; sub-tick
 * nanoseconds do not survive into a TimeSpan because a tick is 100 of them. A value with
 * no engine home — a stream is read into bytes, anything else refuses as invalid — because
 * the conformance suite demands InvalidOperationException or NotSupportedException there,
 * and a TODOException would fail the demand while answering the same question.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Numerics;
using System.Text.Json;

using H2Sharp.Api;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Data;

/// <summary>
/// Converts between caller values and engine values.
/// </summary>
internal static class H2ValueMapper
{
    /// <summary>
    /// Converts a caller value to an engine value, with the type the caller declared.
    /// </summary>
    /// <remarks>
    /// The declared type matters for one value only. A <see cref="TimeSpan"/> is a length of
    /// time, and the engine has two types for it: a TIME is a time of day, and an INTERVAL
    /// DAY TO SECOND is a length. The caller says which by the type it declared — ADO.NET's
    /// <see cref="DbType.Time"/> is the time of day, the way the reference's JDBC takes a
    /// <c>java.sql.Time</c> or a <c>LocalTime</c> for a TIME column, and a <c>Duration</c> for
    /// an INTERVAL. A value with no declared type is a length, because that is what a
    /// <see cref="TimeSpan"/> is in .NET.
    /// </remarks>
    /// <param name="value">the caller value, possibly null</param>
    /// <param name="dbType">the type the caller declared, or null when it declared none</param>
    /// <param name="provider">the session used to promote and convert array elements</param>
    /// <returns>the engine value</returns>
    internal static Value ToValue(object? value, DbType? dbType, Engine.CastDataProvider? provider = null)
    {
        if (value is null or DBNull)
        {
            return ValueNull.Instance;
        }

        if (value.GetType().IsEnum)
        {
            return ToValue(
                Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()), CultureInfo.InvariantCulture),
                dbType, provider);
        }

        return value switch
        {
            bool b => ValueBoolean.Get(b),
            // A byte holds 0..255 and TINYINT holds -128..127: SMALLINT carries all
            // of it exactly, and the engine narrows to the column it goes into.
            byte b => ValueSmallint.Get(b),
            sbyte b => ValueTinyint.Get(b),
            short s => ValueSmallint.Get(s),
            int i => ValueInteger.Get(i),
            long l => ValueBigint.Get(l),
            BigInteger bi => ValueNumeric.Get(new BigDecimal(bi, 0)),
            float f => ValueReal.Get(f),
            double d => ValueDouble.Get(d),
            decimal m => ValueNumeric.Get(FromDecimal(m)),
            char c => ValueChar.Get(c.ToString()),
            string s => ValueVarchar.Get(s),
            byte[] bytes => ValueVarbinary.Get(bytes),
            // Approved ADO.NET adaptation: STJ values explicitly mean JSON documents,
            // like JDBC setObject(value, H2Type.JSON), not VARCHAR JSON-string scalars.
            JsonDocument document => ValueJson.FromJson(document.RootElement.GetRawText()),
            JsonElement element => ValueJson.FromJson(element.GetRawText()),
            DateTime dt => ValueTimestamp.FromDateValueAndNanos(
                DateTimeUtils.DateValue(dt.Year, dt.Month, dt.Day), dt.TimeOfDay.Ticks * 100L),
            DateOnly d => ValueDate.FromDateValue(
                DateTimeUtils.DateValue(d.Year, d.Month, d.Day)),
            TimeOnly t => ValueTime.FromNanos(t.ToTimeSpan().Ticks * 100L),
            TimeSpan span => dbType == DbType.Time
                ? ValueTime.FromNanos(span.Ticks * 100L)
                : FromTimeSpan(span),
            DateTimeOffset dto => ValueTimestampTimeZone.FromDateValueAndNanos(
                DateTimeUtils.DateValue(dto.Year, dto.Month, dto.Day),
                dto.TimeOfDay.Ticks * 100L,
                (int)dto.Offset.TotalSeconds),
            Guid g => ValueUuid.Get(g),
            System.IO.Stream stream => ReadStream(stream),
            IEnumerable enumerable => ToArrayValue(enumerable, dbType, provider),
            _ => throw new InvalidOperationException(
                "H2ValueMapper.ToValue: no engine value for " + value.GetType().Name),
        };
    }

    // JDBC ValueToObjectConverter.arrayToValue converts each element then lets ValueArray
    // infer the common SQL type. The ADO.NET adapter also accepts CLR value arrays, lists,
    // and lazy IEnumerable values. strings and byte[] retain their scalar mappings above.
    private static ValueArray ToArrayValue(IEnumerable source, DbType? dbType, Engine.CastDataProvider? provider)
    {
        if (source is Array array && array.Rank != 1)
        {
            throw new NotSupportedException("Use jagged arrays for nested SQL ARRAY values");
        }

        List<Value> values = [];
        foreach (object? item in source)
        {
            values.Add(ToValue(item, dbType, provider));
            if (values.Count > Engine.Constants.MaxArrayCardinality)
            {
                throw Message.DbException.GetValueTooLongException("ARRAY", "ARRAY", values.Count);
            }
        }

        Value[] elements = [.. values];
        TypeInfo component = TypeInfo.GetHigherType(elements);
        if (component.GetValueType() == Value.Null)
        {
            // CLR generic element types preserve the type of empty / all-NULL sequences.
            // JDBC Object[] cannot preserve a primitive element type this way.
            Type? elementType = source.GetType().GetElementType();
            if (elementType is null)
            {
                foreach (Type candidate in source.GetType().GetInterfaces())
                {
                    if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    {
                        elementType = candidate.GetGenericArguments()[0];
                        break;
                    }
                }
            }

            component = GetElementTypeInfo(elementType, dbType) ?? component;
        }

        return ValueArray.Get(component, elements, provider);
    }

    private static TypeInfo? GetElementTypeInfo(Type? type, DbType? dbType)
    {
        if (type is null || type == typeof(object))
        {
            return null;
        }

        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsEnum)
        {
            type = Enum.GetUnderlyingType(type);
        }

        if (type == typeof(byte[])) return TypeInfo.TypeVarbinary;
        if (type == typeof(JsonDocument) || type == typeof(JsonElement)) return TypeInfo.TypeJson;
        if (type.IsArray)
        {
            TypeInfo? component = GetElementTypeInfo(type.GetElementType(), dbType);
            return component is null ? null : TypeInfo.GetTypeInfo(Value.Array, -1, -1, component);
        }

        if (type == typeof(Guid)) return TypeInfo.TypeUuid;
        if (type == typeof(DateOnly)) return TypeInfo.TypeDate;
        if (type == typeof(TimeOnly)) return TypeInfo.TypeTime;
        if (type == typeof(TimeSpan)) return dbType == DbType.Time ? TypeInfo.TypeTime : TypeInfo.TypeIntervalDayToSecond;
        if (type == typeof(DateTimeOffset)) return TypeInfo.TypeTimestampTz;
        if (type == typeof(BigInteger)) return TypeInfo.TypeNumericScale0;
        return Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean => TypeInfo.TypeBoolean,
            TypeCode.SByte => TypeInfo.TypeTinyint,
            TypeCode.Byte or TypeCode.Int16 => TypeInfo.TypeSmallint,
            TypeCode.Int32 => TypeInfo.TypeInteger,
            TypeCode.Int64 => TypeInfo.TypeBigint,
            TypeCode.Single => TypeInfo.TypeReal,
            TypeCode.Double => TypeInfo.TypeDouble,
            TypeCode.Decimal => TypeInfo.TypeNumericFloatingPoint,
            TypeCode.Char => TypeInfo.TypeChar,
            TypeCode.String => TypeInfo.TypeVarchar,
            TypeCode.DateTime => TypeInfo.TypeTimestamp,
            _ => null,
        };
    }

    private static Value ReadStream(System.IO.Stream stream)
    {
        using System.IO.MemoryStream copy = new();
        stream.CopyTo(copy);
        return ValueVarbinary.Get(copy.ToArray());
    }

    /// <summary>
    /// Converts an engine value to a caller value: DBNull for NULL, otherwise the CLR
    /// value of the matching type.
    /// </summary>
    /// <param name="value">the engine value, possibly null</param>
    /// <returns>the caller value</returns>
    internal static object? ToObject(Value? value)
    {
        if (value is null or ValueNull)
        {
            return DBNull.Value;
        }

        return value.GetValueType() switch
        {
            Value.Boolean => value.GetBoolean(),
            Value.Tinyint => (sbyte)value.GetInt(),
            Value.Smallint => (short)value.GetInt(),
            Value.Integer => value.GetInt(),
            Value.Bigint => value.GetLong(),
            Value.Real => value.GetFloat(),
            Value.Double => value.GetDouble(),
            Value.Numeric or Value.Decfloat => ToDecimal(value),
            Value.Char or Value.Varchar or Value.VarcharIgnorecase or Value.Clob
                or Value.Enum => value.GetString(),
            // H2 ValueToObjectConverter.valueToDefaultObject: JSON uses UTF-8 byte[];
            // passing the returned bytes back as a parameter preserves document semantics.
            Value.Binary or Value.Varbinary or Value.Blob or Value.Json => value.GetBytes(),
            Value.Date => FromDateValue(((ValueDate)value).GetDateValue(), 0L),
            Value.Time => new TimeSpan(((ValueTime)value).GetNanos() / 100L),
            Value.TimeTz => new DateTimeOffset(
                DateTime.MinValue.Date.AddTicks(((ValueTimeTimeZone)value).GetNanos() / 100L),
                TimeSpan.FromSeconds(((ValueTimeTimeZone)value).GetTimeZoneOffsetSeconds())),
            Value.Timestamp => FromDateValue(
                ((ValueTimestamp)value).GetDateValue(), ((ValueTimestamp)value).GetTimeNanos()),
            Value.TimestampTz => new DateTimeOffset(
                FromDateValue(
                    ((ValueTimestampTimeZone)value).GetDateValue(),
                    ((ValueTimestampTimeZone)value).GetTimeNanos()),
                TimeSpan.FromSeconds(((ValueTimestampTimeZone)value).GetTimeZoneOffsetSeconds())),
            Value.Uuid => ((ValueUuid)value).GetUuid(),
            Value.Array => ToArrayObject((ValueArray)value),
            _ when IsIntervalType(value.GetValueType()) => ToTimeSpan((ValueInterval)value),
            _ => throw new TODOException(
                "H2ValueMapper.ToObject: no caller value for engine type "
                + Value.GetTypeName(value.GetValueType())),
        };
    }

    // Approved CLR adaptation: SQL ARRAY reads as a typed CLR array. A NULL element in a
    // value-type array uses Nullable<T>, never zero / false. NULL columns remain DBNull.
    internal static Array ToArrayObject(ValueArray value, Type? requestedType = null)
    {
        Type arrayType = requestedType ?? GetArrayObjectType(value);
        if (!arrayType.IsArray || arrayType.GetArrayRank() != 1)
        {
            throw new InvalidCastException("SQL ARRAY requires a one-dimensional CLR array type");
        }

        Type elementType = arrayType.GetElementType()!;
        Value[] elements = value.GetList();
        Array result = Array.CreateInstance(elementType, elements.Length);
        for (int i = 0; i < elements.Length; i++)
        {
            object? element;
            if (elements[i] is ValueNull)
            {
                if (elementType.IsValueType && Nullable.GetUnderlyingType(elementType) is null)
                {
                    throw new InvalidCastException("Cannot read a NULL array element as " + elementType.Name);
                }

                element = elementType == typeof(object) ? DBNull.Value : null;
            }
            else if (elements[i] is ValueArray nested && elementType.IsArray)
            {
                element = ToArrayObject(nested, elementType);
            }
            else
            {
                element = ToObject(elements[i]);
                Type target = Nullable.GetUnderlyingType(elementType) ?? elementType;
                if (element is not null && !target.IsInstanceOfType(element))
                {
                    // Widening / checked narrowing for explicitly requested numeric arrays,
                    // matching the reader's scalar numeric conversion convention.
                    element = Convert.ChangeType(element, target, CultureInfo.InvariantCulture);
                }
            }

            result.SetValue(element, i);
        }

        return result;
    }

    private static Type GetArrayObjectType(ValueArray value)
    {
        Type component = GetFieldType(value.GetComponentType());
        Value[] elements = value.GetList();
        if (value.GetComponentType().GetValueType() == Value.Array)
        {
            foreach (Value element in elements)
            {
                if (element is ValueArray nested)
                {
                    Type nestedType = GetArrayObjectType(nested);
                    if (nestedType != component)
                    {
                        // Nested rows may have different nullable element layouts. object[]
                        // holds each typed child without flattening or dropping NULLs.
                        component = typeof(object);
                        break;
                    }
                }
            }
        }
        else if (component.IsValueType)
        {
            foreach (Value element in elements)
            {
                if (element is ValueNull)
                {
                    component = typeof(Nullable<>).MakeGenericType(component);
                    break;
                }
            }
        }

        return component.MakeArrayType();
    }

    /// <summary>
    /// The CLR type a column of the given engine type reads as.
    /// </summary>
    /// <param name="type">the engine type</param>
    /// <returns>the CLR type</returns>
    internal static Type GetFieldType(TypeInfo type)
    {
        return type.GetValueType() switch
        {
            Value.Boolean => typeof(bool),
            Value.Tinyint => typeof(sbyte),
            Value.Smallint => typeof(short),
            Value.Integer => typeof(int),
            Value.Bigint => typeof(long),
            Value.Numeric or Value.Decfloat => typeof(decimal),
            Value.Real => typeof(float),
            Value.Double => typeof(double),
            Value.Char or Value.Varchar or Value.VarcharIgnorecase
                or Value.Clob or Value.Enum => typeof(string),
            Value.Binary or Value.Varbinary or Value.Blob or Value.Json => typeof(byte[]),
            Value.Date or Value.Timestamp => typeof(DateTime),
            Value.Time => typeof(TimeSpan),
            Value.TimeTz or Value.TimestampTz => typeof(DateTimeOffset),
            Value.Uuid => typeof(Guid),
            Value.Array => GetFieldType((TypeInfo)type.GetExtTypeInfo()!).MakeArrayType(),
            _ when IsIntervalType(type.GetValueType()) => typeof(TimeSpan),
            _ => typeof(object),
        };
    }

    /// <summary>
    /// The DbType a parameter value is sent as, for callers that ask.
    /// </summary>
    /// <param name="value">the caller value, possibly null</param>
    /// <returns>the database type</returns>
    internal static DbType GetDbType(object? value)
    {
        if (value is not null && value.GetType().IsEnum)
        {
            return GetDbType(Convert.ChangeType(
                value, Enum.GetUnderlyingType(value.GetType()), CultureInfo.InvariantCulture));
        }

        return value switch
        {
            null or DBNull => DbType.Object,
            bool => DbType.Boolean,
            byte or sbyte => DbType.SByte,
            short => DbType.Int16,
            int => DbType.Int32,
            long => DbType.Int64,
            BigInteger => DbType.VarNumeric,
            float => DbType.Single,
            double => DbType.Double,
            decimal => DbType.Decimal,
            char or string => DbType.String,
            byte[] => DbType.Binary,
            DateTime => DbType.DateTime2,
            DateOnly => DbType.Date,
            TimeOnly => DbType.Time,
            TimeSpan => DbType.Object,
            DateTimeOffset => DbType.DateTimeOffset,
            Guid => DbType.Guid,
            _ => DbType.Object,
        };
    }

    private static DateTime FromDateValue(long dateValue, long nanos)
        => new DateTime(
            DateTimeUtils.YearFromDateValue(dateValue),
            DateTimeUtils.MonthFromDateValue(dateValue),
            DateTimeUtils.DayFromDateValue(dateValue)).AddTicks(nanos / 100L);

    private static bool IsIntervalType(int type) => type is
        Value.IntervalYearToMonth or Value.IntervalDayToSecond
        or Value.IntervalYear or Value.IntervalMonth or Value.IntervalDay
        or Value.IntervalHour or Value.IntervalMinute or Value.IntervalSecond
        or Value.IntervalDayToHour or Value.IntervalDayToMinute
        or Value.IntervalHourToMinute or Value.IntervalHourToSecond
        or Value.IntervalMinuteToSecond;

    /// <summary>
    /// Builds a BigDecimal out of a decimal, bit for bit: the 96-bit integer plus the
    /// scale ride along unchanged, so no precision is lost and none is invented.
    /// </summary>
    /// <param name="value">the decimal</param>
    /// <returns>the BigDecimal with the same value</returns>
    private static BigDecimal FromDecimal(decimal value)
    {
        // GetBits has a span overload: the four ints land on the stack instead of an array.
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        BigInteger unscaled = new BigInteger((uint)bits[0])
            | (new BigInteger((uint)bits[1]) << 32)
            | (new BigInteger((uint)bits[2]) << 64);
        if ((bits[3] & int.MinValue) != 0)
        {
            unscaled = BigInteger.Negate(unscaled);
        }

        return new BigDecimal(unscaled, (bits[3] >> 16) & 0xFF);
    }

    internal static decimal ToDecimal(Value value)
    {
        if (value is ValueDecfloat decfloat && !decfloat.IsFinite())
        {
            throw new InvalidCastException(
                "Cannot read " + value.GetString() + " as Decimal");
        }

        return ToDecimalExact(value.GetBigDecimal());
    }

    /// <summary>
    /// Builds a decimal out of a BigDecimal, exactly or not at all: a scale above 28 with
    /// significant digits past it, or 96 bits that do not fit, refuse with OverflowException
    /// rather than rounding quietly. Trailing zeros strip first, so a whole value with a
    /// wide scale reads fine.
    /// </summary>
    /// <param name="value">the BigDecimal</param>
    /// <returns>the decimal with the same value</returns>
    private static decimal ToDecimalExact(BigDecimal value)
    {
        BigInteger unscaled = value.UnscaledValue;
        int scale = value.Scale;
        if (unscaled.IsZero)
        {
            return decimal.Zero;
        }

        bool negative = unscaled.Sign < 0;
        if (negative)
        {
            unscaled = BigInteger.Negate(unscaled);
        }

        while (scale < 0)
        {
            unscaled *= 10;
            scale++;
        }

        // Trailing zeros can exhaust the 96-bit coefficient even at a legal scale:
        // 2643.000... at scale 28 must reduce further to fit exactly as decimal.
        while (scale > 0 && (scale > 28 || unscaled.GetByteCount(isUnsigned: true) > 12)
            && unscaled % 10 == BigInteger.Zero)
        {
            unscaled /= 10;
            scale--;
        }

        if (scale > 28)
        {
            throw new OverflowException(
                "Cannot read " + value.ToPlainString() + " as Decimal: scale " + scale + " exceeds 28");
        }

        // The magnitude is written little-endian into a fixed buffer: a decimal is at most
        // 96 bits, so twelve bytes hold it and a wider one is the overflow below.
        Span<byte> bytes = stackalloc byte[12];
        if (!unscaled.TryWriteBytes(bytes, out int written, isUnsigned: true, isBigEndian: false))
        {
            throw new OverflowException(
                "Cannot read " + value.ToPlainString() + " as Decimal: out of range");
        }

        uint lo = Word(bytes, 0);
        uint mid = Word(bytes, 4);
        uint hi = Word(bytes, 8);
        return new decimal(unchecked((int)lo), unchecked((int)mid), unchecked((int)hi), negative, (byte)scale);
    }

    private static uint Word(ReadOnlySpan<byte> bytes, int offset)
    {
        uint word = 0;
        for (int i = 0; i < 4 && offset + i < bytes.Length; i++)
        {
            word |= (uint)bytes[offset + i] << (8 * i);
        }

        return word;
    }

    private static ValueInterval FromTimeSpan(TimeSpan span)
    {
        BigInteger total = new BigInteger(span.Ticks) * 100;
        bool negative = total.Sign < 0;
        BigInteger absolute = BigInteger.Abs(total);
        BigInteger day = IntervalUtils.NanosPerDayBi;
        return ValueInterval.From(
            IntervalQualifier.DayToSecond, negative,
            (long)(absolute / day), (long)(absolute % day));
    }

    private static TimeSpan ToTimeSpan(ValueInterval interval)
    {
        if (interval.GetQualifier() != IntervalQualifier.DayToSecond)
        {
            throw new TODOException(
                "H2ValueMapper.ToObject: only DAY TO SECOND intervals read as TimeSpan ("
                + interval.GetQualifier() + ")");
        }

        BigInteger total = new BigInteger(interval.GetLeading()) * IntervalUtils.NanosPerDayBi
            + new BigInteger(interval.GetRemaining());
        long ticks = (long)(total / 100);
        return new TimeSpan(interval.IsNegative() ? -ticks : ticks);
    }
}
