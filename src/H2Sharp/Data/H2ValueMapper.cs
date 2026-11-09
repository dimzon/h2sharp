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
using System.IO;
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
    /// <param name="handler">the data handler, for LOBs beyond the inline size</param>
    /// <returns>the engine value</returns>
    internal static Value ToValue(object? value, DbType? dbType, Engine.CastDataProvider? provider = null,
        Store.DataHandler? handler = null)
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
            string s => ValueVarchar.Get(s, provider),
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
            System.IO.Stream stream => ReadStream(stream, handler),
            // The ADO.NET spelling of the reference's setCharacterStream: a character
            // stream parameter is a CLOB, converted to the column type on assignment.
            System.IO.TextReader reader => ReadText(reader, handler),
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

    private static Value ReadStream(System.IO.Stream stream, Store.DataHandler? handler)
    {
        long length = -1;
        if (stream.CanSeek)
        {
            try
            {
                length = stream.Length;
            }
            catch (NotSupportedException)
            {
            }
        }

        // The reference maps a binary parameter stream to a BLOB, not to a VARBINARY.
        // A stream that fits in memory is kept as a small BLOB; a stream beyond that goes
        // to the database's LOB storage when there is a database, and otherwise to a
        // temporary file so the value never has to be a single byte array.
        int inlineLimit = handler?.GetMaxLengthInplaceLob() ?? Engine.Constants.MaxStringLength;
        if (length >= 0 && length <= inlineLimit)
        {
            // The value owns its bytes, so the array is allocated once at the stream's
            // advertised length and read into directly: the reference's
            // setBinaryStream(index, in, length) reads no more than the length either.
            // A stream that ends early shrinks the array to what it actually produced.
            byte[] bytes = GC.AllocateUninitializedArray<byte>((int)length);
            int read = 0;
            while (read < bytes.Length)
            {
                int n = stream.Read(bytes, read, bytes.Length - read);
                if (n == 0)
                {
                    break;
                }

                read += n;
            }

            if (read != bytes.Length)
            {
                Array.Resize(ref bytes, read);
            }

            return Values.ValueBlob.CreateSmall(bytes);
        }

        if (handler is not null)
        {
            return handler.GetLobStorage().CreateBlob(stream, length);
        }

        string fileName = Store.FileSystem.FileUtils.CreateTempFile(
            "h2lob", Engine.Constants.SuffixTempFile, true);
        long written = 0;
        try
        {
            using (System.IO.FileStream output = new(fileName, System.IO.FileMode.Create))
            {
                byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(64 * 1024);
                try
                {
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, read);
                        written += read;
                    }
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            return new Values.ValueBlob(
                new Values.Lobs.LobDataFile(fileName, written), written);
        }
        catch
        {
            System.IO.File.Delete(fileName);
            throw;
        }
    }

    /// <summary>
    /// Maps a character parameter stream to a CLOB: inline when it fits, otherwise through the
    /// database's LOB storage or a temporary file.
    /// </summary>
    private static Value ReadText(System.IO.TextReader reader, Store.DataHandler? handler)
    {
        int inlineLimit = handler?.GetMaxLengthInplaceLob() ?? Engine.Constants.MaxStringLength;
        // Read the head into a fixed buffer: if the text fits the inline limit, that is
        // the whole value and no big-array or temp file is ever touched. The buffer is
        // inlineLimit + 1 so the single read below also tells the size is bigger.
        char[] head = new char[inlineLimit + 1];
        int headLen = 0;
        while (headLen <= inlineLimit)
        {
            int n = reader.Read(head, headLen, head.Length - headLen);
            if (n == 0)
            {
                break;
            }

            headLen += n;
        }

        if (headLen <= inlineLimit)
        {
            return Values.ValueClob.CreateSmall(new string(head, 0, headLen));
        }

        if (handler is not null)
        {
            // The LOB storage needs the character length before it writes; such readers are
            // rare here, so the full read is the fallback for that path.
            string text = new string(head, 0, headLen) + reader.ReadToEnd();
            return handler.GetLobStorage().CreateClob(new StringReader(text), text.Length);
        }

        string fileName = Store.FileSystem.FileUtils.CreateTempFile(
            "h2lob", Engine.Constants.SuffixTempFile, true);
        try
        {
            // Stream the rest into the file in one pass, counting characters; the byte count
            // is the file's own length after the writer flushed, so UTF-8 is never counted
            // separately.
            long charCount = headLen;
            using (System.IO.StreamWriter writer = new(
                fileName, append: false, new System.Text.UTF8Encoding(false)))
            {
                writer.Write(head, 0, headLen);
                char[] tail = new char[4096];
                int n;
                while ((n = reader.Read(tail, 0, tail.Length)) > 0)
                {
                    writer.Write(tail, 0, n);
                    charCount += n;
                }

                writer.Flush();
            }

            int byteCount = (int)new System.IO.FileInfo(fileName).Length;
            return new Values.ValueClob(
                new Values.Lobs.LobDataFile(fileName, byteCount), byteCount, charCount);
        }
        catch
        {
            System.IO.File.Delete(fileName);
            throw;
        }
    }

    /// <summary>
    /// Converts an engine value to a caller value: DBNull for NULL, otherwise the CLR
    /// value of the matching type.
    /// </summary>
    /// <param name="value">the engine value, possibly null</param>
    /// <returns>the caller value</returns>
    internal static object? ToObject(Value? value) => ToObject(value, null);

    /// <summary>
    /// Converts an engine value to the caller shape the connection configured.
    /// </summary>
    /// <param name="value">the engine value, possibly null</param>
    /// <param name="formats">the connection's projection, or null for the defaults</param>
    /// <param name="detached">
    /// whether a stream or reader must outlive the result that produced it; ExecuteScalar
    /// closes its result before returning, so it asks for a detached copy
    /// </param>
    /// <returns>the caller value</returns>
    internal static object? ToObject(Value? value, H2DataFormatOptions? formats, bool detached = false)
    {
        if (value is null or ValueNull)
        {
            return DBNull.Value;
        }

        H2DataFormatOptions options = formats ?? H2DataFormatOptions.Default;
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
            Value.Char or Value.Varchar or Value.VarcharIgnorecase
                or Value.Enum => value.GetString(),
            Value.Clob => ToClobObject(value, options, detached),
            // H2 ValueToObjectConverter.valueToDefaultObject: JSON uses UTF-8 byte[];
            // passing the returned bytes back as a parameter preserves document semantics.
            Value.Binary or Value.Varbinary => value.GetBytes(),
            Value.Blob => ToBlobObject(value, options, detached),
            Value.Json => ToJsonObject(value, options),
            Value.Date => ToDateObject(value, options),
            Value.Time => ToTimeObject(value, options),
            Value.TimeTz => ToTimeTzObject(value, options),
            Value.Timestamp => ToTimestampObject(value, options),
            Value.TimestampTz => ToTimestampTzObject(value, options),
            Value.Uuid => ((ValueUuid)value).GetUuid(),
            Value.Geometry => ((ValueGeometry)value).GetString(),
            Value.Array => ToArrayValue((ValueArray)value, options),
            Value.Row => ToRowObject((ValueRow)value),
            _ when IsIntervalType(value.GetValueType()) => ToTimeSpan((ValueInterval)value),
            _ => throw new TODOException(
                "H2ValueMapper.ToObject: no caller value for engine type "
                + Value.GetTypeName(value.GetValueType())),
        };
    }

    private static object? ToBlobObject(Value value, H2DataFormatOptions options, bool detached)
    {
        if (options.BlobFormat != H2BlobFormat.Stream)
        {
            return value.GetBytes();
        }

        return detached
            ? new MemoryStream(value.GetBytes()!, writable: false)
            : value.GetInputStream();
    }

    private static object? ToClobObject(Value value, H2DataFormatOptions options, bool detached)
    {
        if (options.ClobFormat != H2ClobFormat.TextReader)
        {
            return value.GetString();
        }

        return detached
            ? new StringReader(value.GetString()!)
            : value.GetReader();
    }

    /// <summary>
    /// The H2 date value of <see cref="DateTime.MinValue"/>, used by TIME shapes that have
    /// to choose a date for a time-of-day.
    /// </summary>
    private static readonly long MinDateValue = DateTimeUtils.DateValue(1, 1, 1);

    private static object ToDateObject(Value value, H2DataFormatOptions options)
    {
        long dateValue = ((ValueDate)value).GetDateValue();
        DateTime wall = FromDateValue(dateValue, 0L);
        if (options.DateFormat == H2TemporalFormat.DateTimeOffset)
        {
            return new DateTimeOffset(wall, SessionOffset(options, dateValue, 0L));
        }

        return SpecifyKind(wall, options.DateFormat);
    }

    private static object ToTimeObject(Value value, H2DataFormatOptions options)
    {
        long nanos = ((ValueTime)value).GetNanos();
        if (options.TimeFormat == H2TimeFormat.TimeSpan)
        {
            return new TimeSpan(nanos / 100L);
        }

        DateTime wall = DateTime.MinValue.Date.AddTicks(nanos / 100L);
        H2TemporalFormat shape = TemporalShape(options.TimeFormat);
        if (shape == H2TemporalFormat.DateTimeOffset)
        {
            return new DateTimeOffset(wall, SessionOffset(options, MinDateValue, nanos));
        }

        return SpecifyKind(wall, shape);
    }

    private static object ToTimeTzObject(Value value, H2DataFormatOptions options)
    {
        ValueTimeTimeZone time = (ValueTimeTimeZone)value;
        DateTime wall = DateTime.MinValue.Date.AddTicks(time.GetNanos() / 100L);
        DateTimeOffset instant = new(
            wall, TimeSpan.FromSeconds(time.GetTimeZoneOffsetSeconds()));
        if (options.TimeTzFormat == H2TemporalFormat.DateTimeUnspecified)
        {
            return wall;
        }

        if (options.TimeTzFormat == H2TemporalFormat.DateTimeUtc)
        {
            return instant.UtcDateTime;
        }

        if (options.TimeTzFormat == H2TemporalFormat.DateTimeLocal)
        {
            return instant.LocalDateTime;
        }

        return instant;
    }

    private static object ToTimestampObject(Value value, H2DataFormatOptions options)
    {
        ValueTimestamp timestamp = (ValueTimestamp)value;
        long dateValue = timestamp.GetDateValue();
        long nanos = timestamp.GetTimeNanos();
        DateTime wall = FromDateValue(dateValue, nanos);
        if (options.TimestampFormat == H2TemporalFormat.DateTimeOffset)
        {
            return new DateTimeOffset(wall, SessionOffset(options, dateValue, nanos));
        }

        return SpecifyKind(wall, options.TimestampFormat);
    }

    private static object ToTimestampTzObject(Value value, H2DataFormatOptions options)
    {
        ValueTimestampTimeZone timestamp = (ValueTimestampTimeZone)value;
        DateTime wall = FromDateValue(timestamp.GetDateValue(), timestamp.GetTimeNanos());
        DateTimeOffset instant = new(
            wall, TimeSpan.FromSeconds(timestamp.GetTimeZoneOffsetSeconds()));
        if (options.TimestampTzFormat == H2TemporalFormat.DateTimeUnspecified)
        {
            return wall;
        }

        if (options.TimestampTzFormat == H2TemporalFormat.DateTimeUtc)
        {
            return instant.UtcDateTime;
        }

        if (options.TimestampTzFormat == H2TemporalFormat.DateTimeLocal)
        {
            return instant.LocalDateTime;
        }

        return instant;
    }

    private static DateTime SpecifyKind(DateTime value, H2TemporalFormat format)
        => DateTime.SpecifyKind(value, format switch
        {
            H2TemporalFormat.DateTimeUtc => DateTimeKind.Utc,
            H2TemporalFormat.DateTimeLocal => DateTimeKind.Local,
            _ => DateTimeKind.Unspecified,
        });

    private static H2TemporalFormat TemporalShape(H2TimeFormat format)
        => format switch
        {
            H2TimeFormat.DateTimeUtc => H2TemporalFormat.DateTimeUtc,
            H2TimeFormat.DateTimeLocal => H2TemporalFormat.DateTimeLocal,
            H2TimeFormat.DateTimeOffset => H2TemporalFormat.DateTimeOffset,
            _ => H2TemporalFormat.DateTimeUnspecified,
        };

    private static TimeSpan SessionOffset(H2DataFormatOptions options, long dateValue, long nanos)
        => TimeSpan.FromSeconds(
            (options.SessionTimeZone ?? TimeZoneProvider.GetDefault())
                .GetTimeZoneOffsetLocal(dateValue, nanos));

    private static object? ToJsonObject(Value value, H2DataFormatOptions options)
    {
        return options.JsonFormat switch
        {
            H2JsonFormat.String => value.GetString(),
            H2JsonFormat.TextReader => value.GetReader(),
            H2JsonFormat.Stream => value.GetInputStream(),
            H2JsonFormat.JsonDocument => ToJsonDocument(value),
            _ => value.GetBytes(),
        };
    }

    /// <summary>
    /// Parses a JSON value into an independent document the caller owns.
    /// </summary>
    /// <param name="value">the JSON value</param>
    /// <returns>the document</returns>
    internal static JsonDocument ToJsonDocument(Value value)
        => JsonDocument.Parse(value.GetBytes()!);

    private static object ToArrayValue(ValueArray value, H2DataFormatOptions options)
    {
        if (options.ArrayFormat != H2ArrayFormat.List || IsBinaryArrayComponent(value.GetComponentType()))
        {
            return ToArrayObject(value);
        }

        Array array = ToArrayObject(value);
        Type elementType = array.GetType().GetElementType()!;
        IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
        foreach (object? item in array)
        {
            list.Add(item);
        }

        return list;
    }

    /// <summary>
    /// Whether an ARRAY component is represented as binary data, in which case the List
    /// projection is not applied and the reference CLR array stays.
    /// </summary>
    /// <param name="component">the component type</param>
    /// <returns>true for the binary family</returns>
    private static bool IsBinaryArrayComponent(TypeInfo component)
        => component.GetValueType() is Value.Binary or Value.Varbinary or Value.Blob or Value.Json;

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

    // Approved CLR adaptation: SQL ROW reads as an object array, one mapped value per field.
    internal static object?[] ToRowObject(ValueRow value)
    {
        Value[] values = value.GetList();
        object?[] result = new object?[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            result[i] = values[i] is ValueNull ? DBNull.Value : ToObject(values[i]);
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
    internal static Type GetFieldType(TypeInfo type) => GetFieldType(type, null);

    /// <summary>
    /// The CLR type a column of the given engine type reads as under the configured projection.
    /// </summary>
    /// <param name="type">the engine type</param>
    /// <param name="formats">the connection's projection, or null for the defaults</param>
    /// <returns>the CLR type</returns>
    internal static Type GetFieldType(TypeInfo type, H2DataFormatOptions? formats)
    {
        H2DataFormatOptions options = formats ?? H2DataFormatOptions.Default;
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
                or Value.Enum or Value.Null or Value.Unknown => typeof(string),
            Value.Clob => options.ClobFormat == H2ClobFormat.TextReader ? typeof(TextReader) : typeof(string),
            Value.Binary or Value.Varbinary => typeof(byte[]),
            Value.Blob => options.BlobFormat == H2BlobFormat.Stream ? typeof(Stream) : typeof(byte[]),
            Value.Json => options.JsonFormat switch
            {
                H2JsonFormat.String => typeof(string),
                H2JsonFormat.TextReader => typeof(TextReader),
                H2JsonFormat.Stream => typeof(Stream),
                H2JsonFormat.JsonDocument => typeof(JsonDocument),
                _ => typeof(byte[]),
            },
            Value.Date => TemporalType(options.DateFormat),
            Value.Time => TimeType(options.TimeFormat),
            Value.TimeTz => TemporalType(options.TimeTzFormat),
            Value.Timestamp => TemporalType(options.TimestampFormat),
            Value.TimestampTz => TemporalType(options.TimestampTzFormat),
            Value.Uuid => typeof(Guid),
            Value.Array => ArrayFieldType(type, options),
            _ when IsIntervalType(type.GetValueType()) => typeof(TimeSpan),
            _ => typeof(object),
        };
    }

    private static Type TemporalType(H2TemporalFormat format)
        => format == H2TemporalFormat.DateTimeOffset ? typeof(DateTimeOffset) : typeof(DateTime);

    private static Type TimeType(H2TimeFormat format)
        => format == H2TimeFormat.TimeSpan ? typeof(TimeSpan) : TemporalType(TemporalShape(format));

    private static Type ArrayFieldType(TypeInfo type, H2DataFormatOptions options)
    {
        TypeInfo component = (TypeInfo)type.GetExtTypeInfo()!;
        Type element = GetFieldType(component);
        if (options.ArrayFormat == H2ArrayFormat.List && !IsBinaryArrayComponent(component))
        {
            return typeof(List<>).MakeGenericType(element);
        }

        return element.MakeArrayType();
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
            System.IO.TextReader => DbType.String,
            byte[] => DbType.Binary,
            System.IO.Stream => DbType.Binary,
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
            // Keep the NUMERIC scale when decimal can represent it: 0.00 must not
            // become scale-zero 0 on the ADO.NET boundary. Wider zero scales reduce
            // to decimal's maximum just as trailing zero digits do below.
            return new decimal(0, 0, 0, false, (byte)Math.Clamp(scale, 0, 28));
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
        if (IntervalQualifiers.IsYearMonth(interval.GetQualifier()))
        {
            throw new TODOException(
                "H2ValueMapper.ToObject: a YEAR TO MONTH interval has no TimeSpan equivalent ("
                + interval.GetQualifier() + ")");
        }

        BigInteger total = IntervalUtils.IntervalToAbsolute(interval);
        long ticks = (long)(total / 100);
        return new TimeSpan(ticks);
    }
}

