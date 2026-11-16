/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.server.pg.PgServer (type constants and convertType only).
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Only the type mapping used by PG_CATALOG is ported; the PostgreSQL wire server itself is
 * not part of this port and is listed as outstanding work in MIGRATION_STATUS.md.
 */
using H2Sharp.Values;

namespace H2Sharp.Server.Pg;

/// <summary>
/// PostgreSQL type identifiers and the H2-to-PostgreSQL type mapping.
/// </summary>
public static class PgServer
{
    /// <summary>The default port to use for the PG server.</summary>
    public const int DefaultPort = 5435;

    /// <summary>The VARCHAR type.</summary>
    public const int PgTypeVarchar = 1043;

    /// <summary>The BOOL type.</summary>
    public const int PgTypeBool = 16;

    /// <summary>The BYTEA type.</summary>
    public const int PgTypeBytea = 17;

    /// <summary>The BPCHAR type.</summary>
    public const int PgTypeBpchar = 1042;

    /// <summary>The INT8 type.</summary>
    public const int PgTypeInt8 = 20;

    /// <summary>The INT2 type.</summary>
    public const int PgTypeInt2 = 21;

    /// <summary>The INT4 type.</summary>
    public const int PgTypeInt4 = 23;

    /// <summary>The TEXT type.</summary>
    public const int PgTypeText = 25;

    /// <summary>The FLOAT4 type.</summary>
    public const int PgTypeFloat4 = 700;

    /// <summary>The FLOAT8 type.</summary>
    public const int PgTypeFloat8 = 701;

    /// <summary>The UNKNOWN type.</summary>
    public const int PgTypeUnknown = 705;

    /// <summary>The INT2 array type.</summary>
    public const int PgTypeInt2Array = 1005;

    /// <summary>The INT4 array type.</summary>
    public const int PgTypeInt4Array = 1007;

    /// <summary>The VARCHAR array type.</summary>
    public const int PgTypeVarcharArray = 1015;

    /// <summary>The DATE type.</summary>
    public const int PgTypeDate = 1082;

    /// <summary>The TIME type.</summary>
    public const int PgTypeTime = 1083;

    /// <summary>The TIMETZ type.</summary>
    public const int PgTypeTimetz = 1266;

    /// <summary>The TIMESTAMP type.</summary>
    public const int PgTypeTimestamp = 1114;

    /// <summary>The TIMESTAMPTZ type.</summary>
    public const int PgTypeTimestamptz = 1184;

    /// <summary>The NUMERIC type.</summary>
    public const int PgTypeNumeric = 1700;

    /// <summary>
    /// Converts a data type to its PostgreSQL type identifier.
    /// </summary>
    /// <param name="type">the type</param>
    /// <returns>the identifier, or <see cref="PgTypeUnknown"/></returns>
    public static int ConvertType(TypeInfo type) => type.GetValueType() switch
    {
        Value.Boolean => PgTypeBool,
        Value.Varchar => PgTypeVarchar,
        Value.Null or Value.Clob => PgTypeText,
        Value.Char => PgTypeBpchar,
        Value.Smallint => PgTypeInt2,
        Value.Integer => PgTypeInt4,
        Value.Bigint => PgTypeInt8,
        Value.Numeric or Value.Decfloat => PgTypeNumeric,
        Value.Real => PgTypeFloat4,
        Value.Double => PgTypeFloat8,
        Value.Time => PgTypeTime,
        Value.TimeTz => PgTypeTimetz,
        Value.Date => PgTypeDate,
        Value.Timestamp => PgTypeTimestamp,
        Value.TimestampTz => PgTypeTimestamptz,
        Value.Binary or Value.Varbinary => PgTypeBytea,
        Value.Array => ComponentType(type.GetExtTypeInfo() as TypeInfo),
        _ => PgTypeUnknown,
    };

    /// <summary>
    /// Formats a PostgreSQL type identifier as its name.
    /// </summary>
    /// <param name="pgType">the identifier</param>
    /// <returns>the name, <c>"-"</c> for the void type and <c>"???"</c> for an unknown one</returns>
    public static string FormatType(int pgType) => pgType switch
    {
        0 => "-",
        PgTypeBool => Value.GetTypeName(Value.Boolean),
        PgTypeBytea => Value.GetTypeName(Value.Varbinary),
        18 => "char",
        19 => "name",
        PgTypeInt8 => Value.GetTypeName(Value.Bigint),
        PgTypeInt2 => Value.GetTypeName(Value.Smallint),
        22 => "int2vector",
        PgTypeInt4 => Value.GetTypeName(Value.Integer),
        24 => "regproc",
        PgTypeText => Value.GetTypeName(Value.Clob),
        PgTypeFloat4 => Value.GetTypeName(Value.Real),
        PgTypeFloat8 => Value.GetTypeName(Value.Double),
        PgTypeInt2Array => "smallint[]",
        PgTypeInt4Array => "integer[]",
        PgTypeVarcharArray => "character varying[]",
        PgTypeBpchar => Value.GetTypeName(Value.Char),
        PgTypeVarchar => Value.GetTypeName(Value.Varchar),
        PgTypeDate => Value.GetTypeName(Value.Date),
        PgTypeTime => Value.GetTypeName(Value.Time),
        PgTypeTimetz => Value.GetTypeName(Value.TimeTz),
        PgTypeTimestamp => Value.GetTypeName(Value.Timestamp),
        PgTypeTimestamptz => Value.GetTypeName(Value.TimestampTz),
        PgTypeNumeric => Value.GetTypeName(Value.Numeric),
        2205 => "regclass",
        _ => "???",
    };

    private static int ComponentType(TypeInfo? type) => type?.GetValueType() switch
    {
        Value.Smallint => PgTypeInt2Array,
        Value.Integer => PgTypeInt4Array,
        Value.Varchar => PgTypeVarcharArray,
        _ => PgTypeVarcharArray,
    };
}
