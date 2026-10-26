/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Value conversion for host-provided scalar functions.
 */
using System;
using H2Sharp.Data;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Extensions;

/// <summary>
/// Converts between the engine's <see cref="Value"/> and the CLR types of a registered UDF.
/// </summary>
internal static class ClrValueConverter
{
    internal static TypeInfo GetTypeInfo(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(bool)) return TypeInfo.TypeBoolean;
        if (type == typeof(byte)) return TypeInfo.TypeSmallint;
        if (type == typeof(sbyte)) return TypeInfo.TypeTinyint;
        if (type == typeof(short)) return TypeInfo.TypeSmallint;
        if (type == typeof(ushort)) return TypeInfo.TypeInteger;
        if (type == typeof(int)) return TypeInfo.TypeInteger;
        if (type == typeof(uint)) return TypeInfo.TypeBigint;
        if (type == typeof(long)) return TypeInfo.TypeBigint;
        if (type == typeof(ulong)) return TypeInfo.TypeNumericBigint;
        if (type == typeof(float)) return TypeInfo.TypeReal;
        if (type == typeof(double)) return TypeInfo.TypeDouble;
        if (type == typeof(decimal)) return TypeInfo.TypeNumericFloatingPoint;
        if (type == typeof(char)) return TypeInfo.TypeChar;
        if (type == typeof(string)) return TypeInfo.TypeVarchar;
        if (type == typeof(byte[])) return TypeInfo.TypeVarbinary;
        if (type == typeof(Guid)) return TypeInfo.TypeUuid;
        if (type == typeof(DateTime)) return TypeInfo.TypeTimestamp;
        if (type == typeof(DateTimeOffset)) return TypeInfo.TypeTimestampTz;
        throw new TODOException(
            "IH2Extensions.RegisterUdf: the CLR type " + type.Name + " has no SQL value mapping yet");
    }

    internal static bool IsNullable(Type type)
        => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    internal static object? FromValue(Value value, Type target)
    {
        Type underlying = Nullable.GetUnderlyingType(target) ?? target;
        if (underlying == typeof(bool)) return value.GetBoolean();
        if (underlying == typeof(byte)) return checked((byte)value.GetInt());
        if (underlying == typeof(sbyte)) return checked((sbyte)value.GetInt());
        if (underlying == typeof(short)) return checked((short)value.GetInt());
        if (underlying == typeof(ushort)) return checked((ushort)value.GetInt());
        if (underlying == typeof(int)) return value.GetInt();
        if (underlying == typeof(uint)) return checked((uint)value.GetLong());
        if (underlying == typeof(long)) return value.GetLong();
        if (underlying == typeof(ulong)) return checked((ulong)value.GetBigInteger());
        if (underlying == typeof(float)) return value.GetFloat();
        if (underlying == typeof(double)) return value.GetDouble();
        if (underlying == typeof(decimal)) return H2ValueMapper.ToDecimal(value);
        if (underlying == typeof(char))
        {
            string? s = value.GetString();
            if (s is null || s.Length != 1)
            {
                throw DbException.GetInvalidValueException("CHAR argument", value);
            }

            return s[0];
        }

        if (underlying == typeof(string)) return value.GetString();
        if (underlying == typeof(byte[])) return value.GetBytes();
        if (underlying == typeof(Guid))
        {
            ValueUuid uuid = (ValueUuid)value.ConvertToType(TypeInfo.TypeUuid);
            return uuid.GetUuid();
        }

        if (underlying == typeof(DateTime))
        {
            return H2ValueMapper.ToObject(value.ConvertToType(TypeInfo.TypeTimestamp));
        }

        if (underlying == typeof(DateTimeOffset))
        {
            return H2ValueMapper.ToObject(value.ConvertToType(TypeInfo.TypeTimestampTz));
        }

        throw new TODOException(
            "IH2Extensions.RegisterUdf: the CLR type " + target.Name + " has no SQL value mapping yet");
    }

    internal static Value ToValue(object? value) => H2ValueMapper.ToValue(value, dbType: null);
}
