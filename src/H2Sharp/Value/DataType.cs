/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.DataType.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Values;

/// <summary>
/// The catalogue of the types a column may have: what a type is called in SQL, how wide it is, and
/// how a literal of it is written.
/// </summary>
/// <remarks>
/// A value type is a number and a type is a name and a set of limits; this class is the second of
/// those, and the parser asks it two questions on every type it reads: what type is this name, and
/// what are the limits of it. The names are the SQL standard's names and the ones the reference
/// adds for compatibility, and several names may be one type: CHARACTER, CHAR and NCHAR are all
/// one row.
/// <para>
/// The table has a row for every type of the reference, including the ones whose values are not
/// ported yet: a catalogue with holes in it is a catalogue the parser cannot use, because the
/// question it asks is "is this name a type" and the answer for an absent row would be "no" for a
/// name that is one. What the row holds for such a type is the width the reference declares, which
/// is a number in a class that is not ported yet; the numbers are written out here and the oracle
/// reads them back out of the reference's table, so a wrong width here is a failing test rather
/// than a wrong answer.
/// </para>
/// </remarks>
public sealed class DataType
{
    /// <summary>
    /// The value type this is, one of the numbers in <see cref="Value"/>.
    /// </summary>
    public int Type { get; private set; }

    /// <summary>
    /// The SQL type, one of the numbers in <see cref="System.Data.SqlDbType"/>.
    /// </summary>
    public int SqlType { get; private set; }

    /// <summary>
    /// The smallest number of digits or characters the type may declare.
    /// </summary>
    public long MinPrecision { get; private set; }

    /// <summary>
    /// The largest number of digits or characters the type may declare.
    /// </summary>
    public long MaxPrecision { get; private set; }

    /// <summary>
    /// The smallest number of decimal places the type may declare.
    /// </summary>
    public int MinScale { get; private set; }

    /// <summary>
    /// The largest number of decimal places the type may declare.
    /// </summary>
    public int MaxScale { get; private set; }

    /// <summary>
    /// What a literal of the type is written with in front of it.
    /// </summary>
    public string? Prefix { get; private set; }

    /// <summary>
    /// What a literal of the type is written with after it.
    /// </summary>
    public string? Suffix { get; private set; }

    /// <summary>
    /// The names of the parameters the type takes, for a message that says what a column needs.
    /// </summary>
    public string? Params { get; private set; }

    /// <summary>
    /// Whether the type distinguishes letters that differ in case.
    /// </summary>
    public bool CaseSensitive { get; private set; }

    /// <summary>
    /// Whether the type may be declared with a width.
    /// </summary>
    public bool SupportsPrecision { get; private set; }

    /// <summary>
    /// Whether the type may be declared with a number of decimal places.
    /// </summary>
    public bool SupportsScale { get; private set; }

    /// <summary>
    /// The width a column of the type has when it does not declare one.
    /// </summary>
    public long DefaultPrecision { get; private set; }

    /// <summary>
    /// The number of decimal places a column of the type has when it does not declare one.
    /// </summary>
    public int DefaultScale { get; private set; }

    /// <summary>
    /// Whether the type declares its width in characters rather than in bytes.
    /// </summary>
    public bool SpecialPrecisionScale { get; private set; }

    /// <summary>
    /// The widths of the types whose values are not ported yet.
    /// </summary>
    /// <remarks>
    /// These are the numbers the reference's own classes declare, written here because the classes
    /// are not ported yet. Each of them is read back out of the reference by
    /// DataTypeOracleTests, so a number that drifts here is a number that fails a test.
    /// </remarks>
    internal static class Widths
    {
        /// <summary>
        /// The width of a TIME WITH TIME ZONE.
        /// </summary>
        internal const int TimeTzDefaultPrecision = 14;

        /// <summary>
        /// The largest width of a TIME WITH TIME ZONE.
        /// </summary>
        internal const int TimeTzMaximumPrecision = 24;

        /// <summary>
        /// The width of a TIMESTAMP WITH TIME ZONE.
        /// </summary>
        internal const int TimestampTzDefaultPrecision = 32;

        /// <summary>
        /// The largest width of a TIMESTAMP WITH TIME ZONE.
        /// </summary>
        internal const int TimestampTzMaximumPrecision = 35;

        /// <summary>
        /// The width of an INTERVAL.
        /// </summary>
        internal const int IntervalDefaultPrecision = 2;

        /// <summary>
        /// The largest width of an INTERVAL.
        /// </summary>
        internal const int IntervalMaximumPrecision = 18;

        /// <summary>
        /// The number of decimal places of an INTERVAL with seconds.
        /// </summary>
        internal const int IntervalDefaultScale = 6;

        /// <summary>
        /// The largest number of decimal places of an INTERVAL with seconds.
        /// </summary>
        internal const int IntervalMaximumScale = 9;

        /// <summary>
        /// The width of a UUID.
        /// </summary>
        internal const int UuidPrecision = 16;

        /// <summary>
        /// The width of a NULL.
        /// </summary>
        internal const int NullPrecision = 1;
    }

    private static readonly DataType?[] TypesByValueType = new DataType?[128];

    private static readonly Dictionary<string, DataType> TypesByName = new(StringComparer.Ordinal);

    private DataType()
    {
    }

    static DataType()
    {
        DataType dataType = new();
        dataType.DefaultPrecision = dataType.MaxPrecision = dataType.MinPrecision = Widths.NullPrecision;
        Add(Value.Null, System.Data.SqlDbType.Variant, dataType, "NULL");
        Add(
            Value.Char, System.Data.SqlDbType.Char, CreateString(true, true),
            "CHARACTER", "CHAR", "NCHAR", "NATIONAL CHARACTER", "NATIONAL CHAR");
        Add(
            Value.Varchar, System.Data.SqlDbType.VarChar, CreateString(true, false),
            "CHARACTER VARYING", "VARCHAR", "CHAR VARYING",
            "NCHAR VARYING", "NATIONAL CHARACTER VARYING", "NATIONAL CHAR VARYING",
            "VARCHAR2", "NVARCHAR", "NVARCHAR2",
            "VARCHAR_CASESENSITIVE", "TID",
            "LONGVARCHAR", "LONGNVARCHAR",
            "TINYTEXT", "TEXT", "MEDIUMTEXT", "LONGTEXT", "NTEXT");
        Add(
            Value.Clob, System.Data.SqlDbType.Image, CreateLob(true),
            "CHARACTER LARGE OBJECT", "CLOB", "CHAR LARGE OBJECT",
            "NCLOB", "NCHAR LARGE OBJECT", "NATIONAL CHARACTER LARGE OBJECT");
        Add(
            Value.VarcharIgnorecase, System.Data.SqlDbType.VarChar, CreateString(false, false),
            "VARCHAR_IGNORECASE");
        Add(Value.Binary, System.Data.SqlDbType.Binary, CreateBinary(true), "BINARY");
        Add(
            Value.Varbinary, System.Data.SqlDbType.VarBinary, CreateBinary(false),
            "BINARY VARYING", "VARBINARY", "RAW", "BYTEA", "LONG RAW", "LONGVARBINARY");
        Add(
            Value.Blob, System.Data.SqlDbType.Image, CreateLob(false),
            "BINARY LARGE OBJECT", "BLOB", "TINYBLOB", "MEDIUMBLOB", "LONGBLOB", "IMAGE");
        Add(
            Value.Boolean, System.Data.SqlDbType.Bit, CreateNumeric(ValueBoolean.Precision, 0),
            "BOOLEAN", "BIT", "BOOL");
        Add(Value.Tinyint, System.Data.SqlDbType.TinyInt, CreateNumeric(ValueTinyint.Precision, 0), "TINYINT");
        Add(Value.Smallint, System.Data.SqlDbType.SmallInt, CreateNumeric(ValueSmallint.Precision, 0), "SMALLINT", "INT2");
        Add(
            Value.Integer, System.Data.SqlDbType.Int, CreateNumeric(ValueInteger.Precision, 0),
            "INTEGER", "INT", "MEDIUMINT", "INT4", "SIGNED");
        Add(
            Value.Bigint, System.Data.SqlDbType.BigInt, CreateNumeric(ValueBigint.Precision, 0),
            "BIGINT", "INT8", "LONG");

        dataType = new DataType();
        dataType.MinPrecision = 1;
        dataType.DefaultPrecision = dataType.MaxPrecision = Constants.MaxNumericPrecision;
        dataType.DefaultScale = ValueNumeric.DefaultScale;
        dataType.MaxScale = ValueNumeric.MaximumScale;
        dataType.MinScale = 0;
        dataType.Params = "PRECISION,SCALE";
        dataType.SupportsPrecision = true;
        dataType.SupportsScale = true;
        Add(Value.Numeric, System.Data.SqlDbType.Decimal, dataType, "NUMERIC", "DECIMAL", "DEC");

        Add(
            Value.Real, System.Data.SqlDbType.Real, CreateNumeric(ValueReal.Precision, 0),
            "REAL", "FLOAT4");
        Add(
            Value.Double, System.Data.SqlDbType.Float, CreateNumeric(ValueDouble.Precision, 0),
            "DOUBLE PRECISION", "DOUBLE", "FLOAT8");
        Add(Value.Double, System.Data.SqlDbType.Float, CreateNumeric(ValueDouble.Precision, 0), "FLOAT");

        dataType = new DataType();
        dataType.MinPrecision = 1;
        dataType.DefaultPrecision = dataType.MaxPrecision = Constants.MaxNumericPrecision;
        dataType.Params = "PRECISION";
        dataType.SupportsPrecision = true;
        Add(Value.Decfloat, System.Data.SqlDbType.Decimal, dataType, "DECFLOAT");

        Add(
            Value.Date, System.Data.SqlDbType.Date,
            CreateDate(ValueDate.Precision, ValueDate.Precision, "DATE", false, 0, 0),
            "DATE");
        Add(
            Value.Time, System.Data.SqlDbType.Time,
            CreateDate(
                ValueTime.MaximumPrecision, ValueTime.DefaultPrecision,
                "TIME", true, ValueTime.DefaultScale, ValueTime.MaximumScale),
            "TIME", "TIME WITHOUT TIME ZONE");
        Add(
            Value.TimeTz, System.Data.SqlDbType.DateTimeOffset,
            CreateDate(
                Widths.TimeTzMaximumPrecision, Widths.TimeTzDefaultPrecision,
                "TIME WITH TIME ZONE", true, ValueTime.DefaultScale, ValueTime.MaximumScale),
            "TIME WITH TIME ZONE");
        Add(
            Value.Timestamp, System.Data.SqlDbType.DateTime,
            CreateDate(
                ValueTimestamp.MaximumPrecision, ValueTimestamp.DefaultPrecision,
                "TIMESTAMP", true, ValueTimestamp.DefaultScale, ValueTimestamp.MaximumScale),
            "TIMESTAMP", "TIMESTAMP WITHOUT TIME ZONE");
        Add(
            Value.TimestampTz, System.Data.SqlDbType.DateTime2,
            CreateDate(
                Widths.TimestampTzMaximumPrecision, Widths.TimestampTzDefaultPrecision,
                "TIMESTAMP WITH TIME ZONE", true, ValueTimestamp.DefaultScale, ValueTimestamp.MaximumScale),
            "TIMESTAMP WITH TIME ZONE");

        for (int i = Value.IntervalYear; i <= Value.IntervalMinuteToSecond; i++)
        {
            AddInterval(i);
        }

        Add(
            Value.JavaObject, System.Data.SqlDbType.Variant, CreateBinary(false),
            "JAVA_OBJECT", "OBJECT", "OTHER");

        dataType = CreateString(false, false);
        dataType.SupportsPrecision = false;
        dataType.Params = "ELEMENT [,...]";
        Add(Value.Enum, System.Data.SqlDbType.Variant, dataType, "ENUM");

        Add(Value.Geometry, System.Data.SqlDbType.Variant, CreateGeometry(), "GEOMETRY");

        Add(
            Value.Json, System.Data.SqlDbType.Variant, CreateString(true, false, "JSON '", "'"),
            "JSON");

        dataType = new DataType();
        dataType.Prefix = dataType.Suffix = "'";
        dataType.DefaultPrecision = dataType.MaxPrecision = dataType.MinPrecision = Widths.UuidPrecision;
        Add(Value.Uuid, System.Data.SqlDbType.Binary, dataType, "UUID");

        dataType = new DataType();
        dataType.Prefix = "ARRAY[";
        dataType.Suffix = "]";
        dataType.Params = "CARDINALITY";
        dataType.SupportsPrecision = true;
        dataType.DefaultPrecision = dataType.MaxPrecision = Constants.MaxArrayCardinality;
        Add(Value.Array, System.Data.SqlDbType.Structured, dataType, "ARRAY");

        dataType = new DataType();
        dataType.Prefix = "ROW(";
        dataType.Suffix = ")";
        dataType.Params = "NAME DATA_TYPE [,...]";
        Add(Value.Row, System.Data.SqlDbType.Variant, dataType, "ROW");
    }

    /// <summary>
    /// The type of the given value type.
    /// </summary>
    /// <param name="type">the value type</param>
    /// <returns>the type, or a type with nothing in it when the value type is not one of the
    /// reference's</returns>
    public static DataType GetDataType(int type)
    {
        if (type < 0 || type >= TypesByValueType.Length)
        {
            return new DataType();
        }

        return TypesByValueType[type] ?? new DataType();
    }

    /// <summary>
    /// The type with the given SQL name, or null when there is no such type.
    /// </summary>
    /// <param name="s">the name, in upper case as the standard writes it</param>
    /// <returns>the type, or null</returns>
    public static DataType? GetTypeByName(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return TypesByName.GetValueOrDefault(s);
    }

    /// <summary>
    /// Whether the name is a type name.
    /// </summary>
    /// <param name="s">the name, in upper case as the standard writes it</param>
    /// <returns>true if the name is a type</returns>
    public static bool IsTypeName(string s) => GetTypeByName(s) is not null;

    /// <summary>
    /// The SQL type a value type is reported as.
    /// </summary>
    /// <param name="type">the type</param>
    /// <returns>the SQL type</returns>
    public static int ConvertTypeToSqlType(TypeInfo type)
    {
        int valueType = type.GetValueType();
        switch (valueType)
        {
            case Value.Numeric:
                // .NET has no SQL type that means "a numeric which is not a decimal", so the two the
                // reference distinguishes are one here. The distinction is the reference's, and it
                // is decided by the extended type information; both answers are SqlDbType.Decimal.
                return (int)System.Data.SqlDbType.Decimal;

            case Value.Real:
            case Value.Double:
                // A type that declares a width is a FLOAT, which is how a FLOAT(p) column is
                // reported; one that does not is reported as the type itself.
                if (type.GetPrecision() >= 0)
                {
                    return (int)System.Data.SqlDbType.Float;
                }

                break;
        }

        return GetDataType(valueType).SqlType;
    }

    /// <summary>
    /// The largest value type the reference has, which is where a walk of the catalogue stops.
    /// </summary>
    internal const int MaxValueTypeProbe = 128;

    /// <summary>
    /// Whether the catalogue has a row for a value type.
    /// </summary>
    /// <param name="valueType">the value type</param>
    /// <returns>true if there is a row</returns>
    internal static bool HasRow(int valueType)
        => valueType >= 0 && valueType < TypesByValueType.Length && TypesByValueType[valueType] is not null;

    /// <summary>
    /// Every type name in the catalogue, in the order the reference adds them.
    /// </summary>
    /// <returns>the names</returns>
    internal static IEnumerable<string> AllNames() => TypesByName.Keys;

    /// <summary>
    /// How many types are in the catalogue, which is the number of rows and not the number of
    /// names: several names may be one type.
    /// </summary>
    /// <returns>the number of rows</returns>
    internal static int RowCount()
    {
        int rows = 0;
        foreach (DataType? dataType in TypesByValueType)
        {
            if (dataType is not null)
            {
                rows++;
            }
        }

        return rows;
    }

    private static void AddInterval(int type)
    {
        IntervalQualifier qualifier = (IntervalQualifier)(type - Value.IntervalYear);
        string name = IntervalQualifiers.Name(qualifier);
        DataType dataType = new()
        {
            Prefix = "INTERVAL '",
            Suffix = "' " + name,
            SupportsPrecision = true,
            DefaultPrecision = Widths.IntervalDefaultPrecision,
            MinPrecision = 1,
            MaxPrecision = Widths.IntervalMaximumPrecision,
        };
        if (IntervalQualifiers.HasSeconds(qualifier))
        {
            dataType.SupportsScale = true;
            dataType.DefaultScale = Widths.IntervalDefaultScale;
            dataType.MaxScale = Widths.IntervalMaximumScale;
            dataType.Params = "PRECISION,SCALE";
        }
        else
        {
            dataType.Params = "PRECISION";
        }

        Add(type, System.Data.SqlDbType.Variant, dataType, "INTERVAL " + name);
    }

    private static void Add(int type, System.Data.SqlDbType sqlType, DataType dataType, params string[] names)
    {
        dataType.Type = type;
        dataType.SqlType = (int)sqlType;
        if (TypesByValueType[type] is null)
        {
            TypesByValueType[type] = dataType;
        }

        foreach (string name in names)
        {
            TypesByName[name] = dataType;
        }
    }

    /// <summary>
    /// A numeric type that takes no width and no scale.
    /// </summary>
    /// <param name="precision">the width</param>
    /// <param name="scale">the number of decimal places</param>
    /// <returns>the type</returns>
    public static DataType CreateNumeric(int precision, int scale)
    {
        DataType dataType = new();
        dataType.DefaultPrecision = dataType.MaxPrecision = dataType.MinPrecision = precision;
        dataType.DefaultScale = dataType.MaxScale = dataType.MinScale = scale;
        return dataType;
    }

    /// <summary>
    /// A date or time type, which writes its literals with the name in front of the quotes.
    /// </summary>
    /// <param name="maxPrecision">the largest width</param>
    /// <param name="precision">the width when none is declared</param>
    /// <param name="prefix">the name of the type</param>
    /// <param name="supportsScale">whether the type takes decimal places</param>
    /// <param name="scale">the number of decimal places when none is declared</param>
    /// <param name="maxScale">the largest number of decimal places</param>
    /// <returns>the type</returns>
    public static DataType CreateDate(
        int maxPrecision, int precision, string prefix, bool supportsScale, int scale, int maxScale)
    {
        DataType dataType = new();
        dataType.Prefix = prefix + " '";
        dataType.Suffix = "'";
        dataType.MaxPrecision = maxPrecision;
        dataType.DefaultPrecision = dataType.MinPrecision = precision;
        if (supportsScale)
        {
            dataType.Params = "SCALE";
            dataType.SupportsScale = true;
            dataType.MaxScale = maxScale;
            dataType.DefaultScale = scale;
        }

        return dataType;
    }

    private static DataType CreateString(bool caseSensitive, bool fixedLength)
        => CreateString(caseSensitive, fixedLength, "'", "'");

    private static DataType CreateBinary(bool fixedLength)
        => CreateString(false, fixedLength, "X'", "'");

    private static DataType CreateString(
        bool caseSensitive, bool fixedLength, string prefix, string suffix)
    {
        DataType dataType = new();
        dataType.Prefix = prefix;
        dataType.Suffix = suffix;
        dataType.Params = "LENGTH";
        dataType.CaseSensitive = caseSensitive;
        dataType.SupportsPrecision = true;
        dataType.MinPrecision = 1;
        dataType.MaxPrecision = Constants.MaxStringLength;
        dataType.DefaultPrecision = fixedLength ? 1 : Constants.MaxStringLength;
        return dataType;
    }

    private static DataType CreateLob(bool clob)
    {
        DataType t = clob ? CreateString(true, false) : CreateBinary(false);
        t.MaxPrecision = long.MaxValue;
        t.DefaultPrecision = long.MaxValue;
        return t;
    }

    private static DataType CreateGeometry()
    {
        DataType dataType = new();
        dataType.Prefix = "'";
        dataType.Suffix = "'";
        dataType.Params = "TYPE,SRID";
        dataType.MaxPrecision = long.MaxValue;
        dataType.DefaultPrecision = long.MaxValue;
        return dataType;
    }
}
