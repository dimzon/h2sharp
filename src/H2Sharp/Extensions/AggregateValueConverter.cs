/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Value conversion for host-provided aggregates. The ADO.NET mapper answers DBNull for
 * SQL NULL; the aggregate API passes null, and a SQL ROW is normalised the same way.
 */
using System;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Values;

namespace H2Sharp.Extensions;

internal static class AggregateValueConverter
{
    internal static object? ToDefaultObject(Value? value)
    {
        if (value is null or ValueNull)
        {
            return null;
        }

        return Normalize(H2ValueMapper.ToObject(value));
    }

    internal static Value ToValue(object? value, TypeInfo type, SessionLocal session)
    {
        if (value is null or DBNull)
        {
            return ValueNull.Instance;
        }

        Value v = value is Value engineValue
            ? engineValue
            : H2ValueMapper.ToValue(value, dbType: null, session.CastDataProvider);

        return type.GetValueType() is Value.Null or Value.Unknown
            ? v
            : v.CastToValue(type, session.CastDataProvider);
    }

    private static object? Normalize(object? value)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        if (value is object?[] row)
        {
            for (int i = 0; i < row.Length; i++)
            {
                row[i] = Normalize(row[i]);
            }
        }

        return value;
    }
}
