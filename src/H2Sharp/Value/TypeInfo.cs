/*
 * Copyright 2004-2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Ported from org.h2.value.TypeInfo.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Scope of this file: the type model itself. Fields, constructors, accessors, equality,
 * the singletons for every fixed type, and the type lookups that need nothing more than a
 * value type number.
 *
 * Deliberately not here yet, and named in MIGRATION_STATUS.md as the next unit:
 *
 *   - getTypeInfo(type, precision, scale, extTypeInfo), whose ENUM, GEOMETRY, ARRAY and ROW
 *     branches need ExtTypeInfoEnum, ExtTypeInfoGeometry and ExtTypeInfoRow;
 *   - getHigherType and its array, geometry and row helpers, which promote through those
 *     same subclasses;
 *   - checkComparable, haveSameOrdering and areComparable, whose ROW branch reads a row's
 *     field set from ExtTypeInfoRow.
 *
 * Approximating those with a placeholder would have produced a type system that looks
 * complete and is not, which is worse than one that admits its edge.
 */
using System;
using System.Diagnostics.CodeAnalysis;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;

namespace H2Sharp.Values;

/// <summary>
/// Describes a data type: its value type number, and the precision, scale and extended
/// information that some types carry.
/// </summary>
public sealed class TypeInfo : ExtTypeInfo
{
    // The reference reads these from the concrete value classes, which are not ported yet.
    // They are reproduced here so the singletons below are the reference's values rather
    // than remembered ones, and TypeInfoOracleTests pins each of them against the reference
    // so moving them to their own classes cannot change a number unnoticed.
    internal const long TinyintDecimalPrecision = 3;
    internal const long SmallintDecimalPrecision = 5;
    internal const long IntegerDecimalPrecision = 10;
    internal const long BigintDecimalPrecision = 19;
    internal const long RealDecimalPrecision = 7;
    internal const long DoubleDecimalPrecision = 17;
    internal const int NumericMaximumScale = 100_000;

    internal const long BigintDecimalPrecisionLong = 19;
    internal const int TimeMaximumScale = 9;
    internal const int TimestampMaximumScale = 9;
    internal const long IntervalMaximumPrecision = 18;
    internal const int IntervalMaximumScale = 9;

    // The declared precision of the types that have one, and the default scale of the
    // temporal and interval types. The reference reads these from the concrete value
    // classes as well; they are reproduced here for the same reason as the ones above.
    private const long NullPrecision = 1;
    private const long BooleanPrecision = 1;
    private const long TinyintPrecision = 8;
    private const long SmallintPrecision = 16;
    private const long IntegerPrecision = 32;
    private const long BigintPrecision = 64;
    private const long RealPrecision = 24;
    private const long DoublePrecision = 53;
    private const long DatePrecision = 10;
    private const long UuidPrecision = 16;
    private const int TimeDefaultScale = 0;
    private const int TimestampDefaultScale = 6;
    private const long IntervalDefaultPrecision = 2;
    private const int IntervalDefaultScale = 6;

    /// <summary>
    /// The unknown type. Also the type of an unresolvable parameter.
    /// </summary>
    public static readonly TypeInfo TypeUnknown = new(Value.Unknown);

    /// <summary>
    /// The NULL type.
    /// </summary>
    public static readonly TypeInfo TypeNull = new(Value.Null);

    /// <summary>
    /// CHARACTER with default parameters.
    /// </summary>
    public static readonly TypeInfo TypeChar = new(Value.Char, -1L);

    /// <summary>
    /// CHARACTER VARYING with maximum parameters.
    /// </summary>
    public static readonly TypeInfo TypeVarchar = new(Value.Varchar);

    /// <summary>
    /// VARCHAR_IGNORECASE with maximum parameters.
    /// </summary>
    public static readonly TypeInfo TypeVarcharIgnorecase = new(Value.VarcharIgnorecase);

    /// <summary>
    /// CHARACTER LARGE OBJECT with maximum parameters.
    /// </summary>
    public static readonly TypeInfo TypeClob = new(Value.Clob);

    /// <summary>
    /// BINARY with default parameters.
    /// </summary>
    public static readonly TypeInfo TypeBinary = new(Value.Binary, -1L);

    /// <summary>
    /// BINARY VARYING with maximum parameters.
    /// </summary>
    public static readonly TypeInfo TypeVarbinary = new(Value.Varbinary);

    /// <summary>
    /// BINARY LARGE OBJECT with maximum parameters.
    /// </summary>
    public static readonly TypeInfo TypeBlob = new(Value.Blob);

    /// <summary>
    /// BOOLEAN type.
    /// </summary>
    public static readonly TypeInfo TypeBoolean = new(Value.Boolean);

    /// <summary>
    /// TINYINT type.
    /// </summary>
    public static readonly TypeInfo TypeTinyint = new(Value.Tinyint);

    /// <summary>
    /// SMALLINT type.
    /// </summary>
    public static readonly TypeInfo TypeSmallint = new(Value.Smallint);

    /// <summary>
    /// INTEGER type.
    /// </summary>
    public static readonly TypeInfo TypeInteger = new(Value.Integer);

    /// <summary>
    /// BIGINT type.
    /// </summary>
    public static readonly TypeInfo TypeBigint = new(Value.Bigint);

    /// <summary>
    /// NUMERIC type with scale 0.
    /// </summary>
    public static readonly TypeInfo TypeNumericScale0 =
        new(Value.Numeric, Constants.MaxNumericPrecision, 0, null);

    /// <summary>
    /// NUMERIC type with a precision that can hold any BIGINT.
    /// </summary>
    public static readonly TypeInfo TypeNumericBigint =
        new(Value.Numeric, BigintDecimalPrecisionLong, 0, null);

    /// <summary>
    /// NUMERIC type with floating point precision.
    /// </summary>
    public static readonly TypeInfo TypeNumericFloatingPoint =
        new(Value.Numeric, Constants.MaxNumericPrecision, Constants.MaxNumericPrecision / 2, null);

    /// <summary>
    /// REAL type.
    /// </summary>
    public static readonly TypeInfo TypeReal = new(Value.Real);

    /// <summary>
    /// DOUBLE type.
    /// </summary>
    public static readonly TypeInfo TypeDouble = new(Value.Double);

    /// <summary>
    /// DECFLOAT type with maximum precision.
    /// </summary>
    public static readonly TypeInfo TypeDecfloat = new(Value.Decfloat);

    /// <summary>
    /// DECFLOAT type with a precision that can hold any BIGINT.
    /// </summary>
    public static readonly TypeInfo TypeDecfloatBigint = new(Value.Decfloat, BigintDecimalPrecisionLong);

    /// <summary>
    /// DATE type.
    /// </summary>
    public static readonly TypeInfo TypeDate = new(Value.Date);

    /// <summary>
    /// TIME type with the default scale.
    /// </summary>
    public static readonly TypeInfo TypeTime = new(Value.Time, TimeMaximumScale);

    /// <summary>
    /// TIME WITH TIME ZONE type with the default scale.
    /// </summary>
    public static readonly TypeInfo TypeTimeTz = new(Value.TimeTz, TimeMaximumScale);

    /// <summary>
    /// TIMESTAMP type with the default scale.
    /// </summary>
    public static readonly TypeInfo TypeTimestamp = new(Value.Timestamp, TimestampMaximumScale);

    /// <summary>
    /// TIMESTAMP WITH TIME ZONE type with the default scale.
    /// </summary>
    public static readonly TypeInfo TypeTimestampTz = new(Value.TimestampTz, TimestampMaximumScale);

    /// <summary>
    /// INTERVAL DAY type with the default precision.
    /// </summary>
    public static readonly TypeInfo TypeIntervalDay;

    /// <summary>
    /// INTERVAL YEAR TO MONTH type with the default precision.
    /// </summary>
    public static readonly TypeInfo TypeIntervalYearToMonth;

    /// <summary>
    /// INTERVAL DAY TO SECOND type with the default precision.
    /// </summary>
    public static readonly TypeInfo TypeIntervalDayToSecond;

    /// <summary>
    /// INTERVAL HOUR TO SECOND type with the default precision.
    /// </summary>
    public static readonly TypeInfo TypeIntervalHourToSecond;

    /// <summary>
    /// JAVA_OBJECT type.
    /// </summary>
    public static readonly TypeInfo TypeJavaObject = new(Value.JavaObject);

    /// <summary>
    /// ENUM type without a definition.
    /// </summary>
    public static readonly TypeInfo TypeEnumUndefined = new(Value.Enum);

    /// <summary>
    /// GEOMETRY type.
    /// </summary>
    public static readonly TypeInfo TypeGeometry = new(Value.Geometry);

    /// <summary>
    /// JSON type.
    /// </summary>
    public static readonly TypeInfo TypeJson = new(Value.Json);

    /// <summary>
    /// UUID type.
    /// </summary>
    public static readonly TypeInfo TypeUuid = new(Value.Uuid);

    /// <summary>
    /// ARRAY type without an element type.
    /// </summary>
    public static readonly TypeInfo TypeArrayUnknown = new(Value.Array);

    /// <summary>
    /// ROW type without a field list.
    /// </summary>
    public static readonly TypeInfo TypeRowEmpty = new(Value.Row);

    /// <summary>
    /// The fixed type for each value type number, or null where the type needs parameters.
    /// </summary>
    private static readonly TypeInfo?[] TypeInfosByValueType = new TypeInfo?[Value.TypeCount];

    private readonly int valueType;
    private readonly long precision;
    private readonly int scale;
    private readonly ExtTypeInfo? extTypeInfo;

    static TypeInfo()
    {
        TypeInfosByValueType[Value.Null] = TypeNull;
        TypeInfosByValueType[Value.Char] = TypeChar;
        TypeInfosByValueType[Value.Varchar] = TypeVarchar;
        TypeInfosByValueType[Value.Clob] = TypeClob;
        TypeInfosByValueType[Value.VarcharIgnorecase] = TypeVarcharIgnorecase;
        TypeInfosByValueType[Value.Binary] = TypeBinary;
        TypeInfosByValueType[Value.Varbinary] = TypeVarbinary;
        TypeInfosByValueType[Value.Blob] = TypeBlob;
        TypeInfosByValueType[Value.Boolean] = TypeBoolean;
        TypeInfosByValueType[Value.Tinyint] = TypeTinyint;
        TypeInfosByValueType[Value.Smallint] = TypeSmallint;
        TypeInfosByValueType[Value.Integer] = TypeInteger;
        TypeInfosByValueType[Value.Bigint] = TypeBigint;

        // NUMERIC is parameterised, so the default entry is the floating point variant.
        TypeInfosByValueType[Value.Numeric] = TypeNumericFloatingPoint;
        TypeInfosByValueType[Value.Real] = TypeReal;
        TypeInfosByValueType[Value.Double] = TypeDouble;

        // The other two DECFLOAT shapes are reachable through the parameterised factory and
        // through toNumericType, so they are not the default entry.
        TypeInfosByValueType[Value.Decfloat] = TypeDecfloat;
        TypeInfosByValueType[Value.Date] = TypeDate;
        TypeInfosByValueType[Value.Time] = TypeTime;
        TypeInfosByValueType[Value.TimeTz] = TypeTimeTz;
        TypeInfosByValueType[Value.Timestamp] = TypeTimestamp;
        TypeInfosByValueType[Value.TimestampTz] = TypeTimestampTz;

        for (int type = Value.IntervalYear; type <= Value.IntervalMinuteToSecond; type++)
        {
            TypeInfosByValueType[type] = new TypeInfo(
                type,
                IntervalMaximumPrecision,
                HasSeconds(type) ? IntervalMaximumScale : -1,
                null);
        }

        TypeIntervalDay = TypeInfosByValueType[Value.IntervalDay]!;
        TypeIntervalYearToMonth = TypeInfosByValueType[Value.IntervalYearToMonth]!;
        TypeIntervalDayToSecond = TypeInfosByValueType[Value.IntervalDayToSecond]!;
        TypeIntervalHourToSecond = TypeInfosByValueType[Value.IntervalHourToSecond]!;

        TypeInfosByValueType[Value.JavaObject] = TypeJavaObject;
        TypeInfosByValueType[Value.Enum] = TypeEnumUndefined;
        TypeInfosByValueType[Value.Geometry] = TypeGeometry;
        TypeInfosByValueType[Value.Json] = TypeJson;
        TypeInfosByValueType[Value.Uuid] = TypeUuid;
        TypeInfosByValueType[Value.Array] = TypeArrayUnknown;
        TypeInfosByValueType[Value.Row] = TypeRowEmpty;
    }

    private TypeInfo(int valueType)
        : this(valueType, -1L, -1, null)
    {
    }

    private TypeInfo(int valueType, long precision)
        : this(valueType, precision, -1, null)
    {
    }

    private TypeInfo(int valueType, int scale)
        : this(valueType, -1L, scale, null)
    {
    }

    /// <summary>
    /// Creates a new instance of a data type with parameters.
    /// </summary>
    /// <param name="valueType">the value type</param>
    /// <param name="precision">the precision</param>
    /// <param name="scale">the scale</param>
    /// <param name="extTypeInfo">the extended type information, or null</param>
    public TypeInfo(int valueType, long precision, int scale, ExtTypeInfo? extTypeInfo)
    {
        this.valueType = valueType;
        this.precision = precision;
        this.scale = scale;
        this.extTypeInfo = extTypeInfo;
    }

    /// <summary>
    /// Whether an interval type carries a seconds component, which decides its scale. The
    /// reference reads this from IntervalQualifier.hasSeconds(); taken from the reference
    /// rather than derived, because the set is exactly the four second-bearing intervals.
    /// </summary>
    private static bool HasSeconds(int type) => type
        is Value.IntervalSecond or Value.IntervalDayToSecond
            or Value.IntervalHourToSecond or Value.IntervalMinuteToSecond;

    /// <summary>
    /// Get the data type of the specified value type.
    /// </summary>
    /// <param name="type">the value type</param>
    /// <returns>the data type</returns>
    public static TypeInfo GetTypeInfo(int type)
    {
        if (type == Value.Unknown)
        {
            throw DbException.Get(ErrorCode.UnknownDataType1, "?");
        }

        if (type >= Value.Null && type < Value.TypeCount)
        {
            TypeInfo? known = TypeInfosByValueType[type];
            if (known is not null)
            {
                return known;
            }
        }

        return TypeNull;
    }

    /// <summary>
    /// The value type this describes.
    /// </summary>
    /// <returns>the value type</returns>
    public int GetValueType() => valueType;

    /// <summary>
    /// The precision of this type.
    /// </summary>
    /// <remarks>
    /// Derived, not the stored field, and that distinction matters: the reference reports
    /// the declared precision when one was set and the type's maximum otherwise, so a
    /// VARCHAR with nothing stored reports MAX_STRING_LENGTH and a CLOB reports
    /// Long.MAX_VALUE. Returning the raw field here looked plausible and was wrong for
    /// most types.
    /// </remarks>
    /// <returns>the precision, or -1 if unknown</returns>
    public long GetPrecision() => valueType switch
    {
        Value.Unknown => -1L,
        Value.Null => NullPrecision,
        Value.Char or Value.Binary => precision >= 0L ? precision : 1L,
        Value.Varchar or Value.VarcharIgnorecase or Value.Varbinary or Value.JavaObject
            or Value.Enum or Value.Geometry or Value.Json
            => precision >= 0L ? precision : Constants.MaxStringLength,
        Value.Clob or Value.Blob => precision >= 0L ? precision : long.MaxValue,
        Value.Boolean => BooleanPrecision,
        Value.Tinyint => TinyintPrecision,
        Value.Smallint => SmallintPrecision,
        Value.Integer => IntegerPrecision,
        Value.Bigint => BigintPrecision,
        Value.Numeric => precision >= 0L ? precision : Constants.MaxNumericPrecision,
        Value.Real => RealPrecision,
        Value.Double => DoublePrecision,
        Value.Decfloat => precision >= 0L ? precision : Constants.MaxNumericPrecision,
        Value.Date => DatePrecision,
        Value.Time => TimePrecision(scale),
        Value.TimeTz => TimeTzPrecision(scale),
        Value.Timestamp => TimestampPrecision(scale),
        Value.TimestampTz => TimestampTzPrecision(scale),
        Value.IntervalYear or Value.IntervalMonth or Value.IntervalDay or Value.IntervalHour
            or Value.IntervalMinute or Value.IntervalSecond or Value.IntervalYearToMonth
            or Value.IntervalDayToHour or Value.IntervalDayToMinute
            or Value.IntervalDayToSecond or Value.IntervalHourToMinute
            or Value.IntervalHourToSecond or Value.IntervalMinuteToSecond
            => precision >= 0L ? precision : IntervalDefaultPrecision,
        Value.Row => int.MaxValue,
        Value.Uuid => UuidPrecision,
        Value.Array => precision >= 0L ? precision : Constants.MaxArrayCardinality,
        _ => -1L,
    };

    private static long TimePrecision(int scale)
    {
        int s = scale >= 0 ? scale : TimeDefaultScale;
        return s == 0 ? 8 : 9 + s;
    }

    private static long TimeTzPrecision(int scale)
    {
        int s = scale >= 0 ? scale : TimeDefaultScale;
        return s == 0 ? 14 : 15 + s;
    }

    private static long TimestampPrecision(int scale)
    {
        int s = scale >= 0 ? scale : TimestampDefaultScale;
        return s == 0 ? 19 : 20 + s;
    }

    private static long TimestampTzPrecision(int scale)
    {
        int s = scale >= 0 ? scale : TimestampDefaultScale;
        return s == 0 ? 25 : 26 + s;
    }

    /// <summary>
    /// The scale of this type.
    /// </summary>
    /// <remarks>
    /// Also derived: types without a scale report zero rather than the stored -1, which
    /// means the stored sentinel is not what callers see.
    /// </remarks>
    /// <returns>the scale</returns>
    public int GetScale() => valueType switch
    {
        Value.Unknown => -1,
        Value.Null or Value.Char or Value.Varchar or Value.Clob or Value.VarcharIgnorecase
            or Value.Binary or Value.Varbinary or Value.Blob or Value.Boolean or Value.Tinyint
            or Value.Smallint or Value.Integer or Value.Bigint or Value.Real or Value.Double
            or Value.Decfloat or Value.Date or Value.IntervalYear or Value.IntervalMonth
            or Value.IntervalDay or Value.IntervalHour or Value.IntervalMinute
            or Value.IntervalYearToMonth or Value.IntervalDayToHour or Value.IntervalDayToMinute
            or Value.IntervalHourToMinute or Value.JavaObject or Value.Enum or Value.Geometry
            or Value.Json or Value.Uuid or Value.Array or Value.Row
            => 0,
        Value.Numeric => scale >= 0 ? scale : 0,
        Value.Time or Value.TimeTz => scale >= 0 ? scale : TimeDefaultScale,
        Value.Timestamp or Value.TimestampTz => scale >= 0 ? scale : TimestampDefaultScale,
        Value.IntervalSecond or Value.IntervalDayToSecond or Value.IntervalHourToSecond
            or Value.IntervalMinuteToSecond
            => scale >= 0 ? scale : IntervalDefaultScale,
        _ => scale,
    };


    /// <summary>
    /// The extended type information, or null.
    /// </summary>
    /// <returns>the extended type information</returns>
    public ExtTypeInfo? GetExtTypeInfo() => extTypeInfo;


    /// <summary>
    /// The precision this type needs to hold its values exactly, which differs from the
    /// declared precision for the fixed precision integer and approximate numeric types.
    /// </summary>
    /// <returns>the decimal precision</returns>
    public long GetDecimalPrecision() => valueType switch
    {
        Value.Tinyint => TinyintDecimalPrecision,
        Value.Smallint => SmallintDecimalPrecision,
        Value.Integer => IntegerDecimalPrecision,
        Value.Bigint => BigintDecimalPrecision,
        Value.Real => RealDecimalPrecision,
        Value.Double => DoubleDecimalPrecision,
        _ => precision,
    };

    /// <summary>
    /// This type as a NUMERIC type, so that integers and approximate numbers take part in
    /// exact arithmetic after a conversion.
    /// </summary>
    /// <returns>the NUMERIC type</returns>
    public TypeInfo ToNumericType() => valueType switch
    {
        Value.Boolean or Value.Tinyint or Value.Smallint or Value.Integer
            => new TypeInfo(Value.Numeric, GetDecimalPrecision(), 0, null),
        Value.Bigint => TypeNumericBigint,
        Value.Numeric => this,
        Value.Real => new TypeInfo(Value.Numeric, NumericMaximumScale, NumericMaximumScale / 2, null),
        Value.Double => new TypeInfo(Value.Numeric, NumericMaximumScale * 2, NumericMaximumScale, null),
        Value.Decfloat => TypeNumericFloatingPoint,
        _ => this,
    };

    /// <summary>
    /// Whether the two types are the same, comparing array element types recursively.
    /// </summary>
    /// <param name="t1">the first type</param>
    /// <param name="t2">the second type</param>
    /// <returns>true if the types are the same</returns>
    public static bool AreSameTypes(TypeInfo t1, TypeInfo t2)
    {
        while (true)
        {
            int valueType = t1.GetValueType();
            if (valueType != t2.GetValueType())
            {
                return false;
            }

            ExtTypeInfo? ext1 = t1.GetExtTypeInfo(), ext2 = t2.GetExtTypeInfo();
            if (valueType != Value.Array)
            {
                return Equals(ext1, ext2);
            }

            t1 = (TypeInfo)ext1!;
            t2 = (TypeInfo)ext2!;
        }
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(valueType, precision, scale);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is TypeInfo other
            && valueType == other.valueType
            && precision == other.precision
            && scale == other.scale
            && Equals(extTypeInfo, other.extTypeInfo);
    }

    /// <inheritdoc/>
    public override string ToString()
        => $"{Value.GetTypeName(valueType)}({precision}, {scale})";
}
