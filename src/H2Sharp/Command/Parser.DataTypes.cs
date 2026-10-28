/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.Parser (the data type reader).
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The data type reader of the reference: readIfDataType and the readers it calls.
 *
 * The reference reads a type from the tokens, and this is that reader. DDL's existing
 * statement DTO retains the source spelling, so ParseDataType invokes this same reader on
 * that text. Type parsing and ARRAY suffixes share the reference grammar with CAST.
 *
 * What is left out, and why:
 *
 * * The mode branches. The reference asks the mode about a dozen flags here (dateIsTimestamp0,
 *   variableBinary, numericIsDecfloat, datetimeTypes, disallowedTypes, allNumericTypesHavePrecision,
 *   forBitData, charAndByteLengthUnits, and the case-insensitive database). The port's mode has
 *   none of them, so there is nothing to ask, and the branch is left out rather than answered
 *   with a guess.
 * * Domains. The reference falls back to a lookup in the schema when the name is not a type.
 *   The port has no domains, so a name that is not a type is an unknown type.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Util.Geometry;
using H2Sharp.Values;

namespace H2Sharp.Command;

public sealed partial class Parser
{
    /// <summary>
    /// Reads a data type, if the tokens spell one.
    /// </summary>
    /// <returns>the type, or null if the tokens do not start a type</returns>
    private TypeInfo? ReadIfDataType()
    {
        TypeInfo? typeInfo = ReadIfDataType1();
        if (typeInfo is not null)
        {
            while (ReadIf(ParserUtil.Array))
            {
                typeInfo = ParseArrayType(typeInfo);
            }
        }

        return typeInfo;
    }

    /// <summary>
    /// Reads one data type, without the array suffixes.
    /// </summary>
    /// <returns>the type, or null if the tokens do not start a type</returns>
    private TypeInfo? ReadIfDataType1()
    {
        int typeIndex = position;
        switch (CurrentType)
        {
            case ParserUtil.Identifier:
                if (Current.IsQuoted)
                {
                    return null;
                }

                break;
            case ParserUtil.Interval:
                {
                    Read();
                    TypeInfo? typeInfo = ReadIntervalQualifier();
                    if (typeInfo is null)
                    {
                        throw DbException.GetSyntaxError(sql, Current.Start, "interval");
                    }

                    return typeInfo;
                }

            case ParserUtil.Null:
                Read();
                return TypeInfo.TypeNull;
            case ParserUtil.Row:
                Read();
                return ParseRowType();
            case ParserUtil.Array:
                // The reference reads an array type here in quirks mode only.
                throw DbException.GetSyntaxError(sql, Current.Start, "data type");
            default:
                if (CurrentType >= ParserUtil.FirstKeyword && CurrentType <= ParserUtil.LastKeyword)
                {
                    break;
                }

                throw DbException.GetSyntaxError(sql, Current.Start, "data type");
        }

        string originalCase = Current.AsIdentifier ?? string.Empty;
        Read();
        if (CurrentType == Token.Dot)
        {
            // A qualified name is a name and not a type: its schema and domain are resolved
            // by the caller that reads a column type.
            position = typeIndex;
            return null;
        }

        string original = originalCase.ToUpperInvariant();
        switch (original)
        {
            case "BINARY":
                if (IsWord("VARYING"))
                {
                    original = "BINARY VARYING";
                }
                else if (IsWord("LARGE"))
                {
                    ExpectWord("OBJECT");
                    original = "BINARY LARGE OBJECT";
                }

                break;
            case "CHAR":
                if (IsWord("VARYING"))
                {
                    original = "CHAR VARYING";
                }
                else if (IsWord("LARGE"))
                {
                    ExpectWord("OBJECT");
                    original = "CHAR LARGE OBJECT";
                }

                break;
            case "CHARACTER":
                if (IsWord("VARYING"))
                {
                    original = "CHARACTER VARYING";
                }
                else if (IsWord("LARGE"))
                {
                    ExpectWord("OBJECT");
                    original = "CHARACTER LARGE OBJECT";
                }

                break;
            case "DATE":
                return TypeInfo.TypeDate;
            case "DEC":
            case "DECIMAL":
                return ParseNumericType(isDecimal: true);
            case "DECFLOAT":
                return ParseDecfloatType();
            case "DOUBLE":
                if (IsWord("PRECISION"))
                {
                    original = "DOUBLE PRECISION";
                }

                break;
            case "ENUM":
                return ParseEnumType();
            case "FLOAT":
                return ParseFloatType();
            case "GEOMETRY":
                return ParseGeometryType();
            case "LONG":
                if (IsWord("RAW"))
                {
                    original = "LONG RAW";
                }

                break;
            case "NATIONAL":
                if (IsWord("CHARACTER"))
                {
                    if (IsWord("VARYING"))
                    {
                        original = "NATIONAL CHARACTER VARYING";
                    }
                    else if (IsWord("LARGE"))
                    {
                        ExpectWord("OBJECT");
                        original = "NATIONAL CHARACTER LARGE OBJECT";
                    }
                    else
                    {
                        original = "NATIONAL CHARACTER";
                    }
                }
                else
                {
                    ExpectWord("CHAR");
                    if (IsWord("VARYING"))
                    {
                        original = "NATIONAL CHAR VARYING";
                    }
                    else
                    {
                        original = "NATIONAL CHAR";
                    }
                }

                break;
            case "NCHAR":
                if (IsWord("VARYING"))
                {
                    original = "NCHAR VARYING";
                }
                else if (IsWord("LARGE"))
                {
                    ExpectWord("OBJECT");
                    original = "NCHAR LARGE OBJECT";
                }
                else
                {
                    original = "NCHAR";
                }

                break;
            case "NUMBER":
            case "NUMERIC":
                return ParseNumericType(isDecimal: false);
            case "TIME":
                return ParseTimeType();
            case "TIMESTAMP":
                return ParseTimestampType();
        }

        // A domain of the current schema wins over a built-in type of the same name, as in
        // the reference's readIfDataType1. Multi-word names cannot be domain names.
        if (session is not null && originalCase.Length == original.Length)
        {
            Schemas.Schema current = session.GetDatabase().GetSchema(session.GetCurrentSchemaName());
            if (current.FindDomain(originalCase) is not null)
            {
                position = typeIndex;
                return null;
            }
        }

        if (provider.GetMode().DisallowedTypes.Contains(original))
        {
            throw DbException.Get(ErrorCode.UnknownDataType1, original);
        }

        DataType? dataType = DataType.GetTypeByName(original);
        if (dataType is null)
        {
            throw DbException.Get(ErrorCode.UnknownDataType1, original);
        }

        long precision;
        int scale;
        if (dataType.SpecialPrecisionScale)
        {
            precision = dataType.DefaultPrecision;
            scale = dataType.DefaultScale;
        }
        else
        {
            precision = -1L;
            scale = -1;
        }

        int type = dataType.Type;
        if ((dataType.SupportsPrecision || dataType.SupportsScale) && ReadIf(Token.OpenParen))
        {
            if (!IsWord("MAX"))
            {
                if (dataType.SupportsPrecision)
                {
                    precision = ReadPrecision(type);
                    if (precision < dataType.MinPrecision)
                    {
                        throw GetInvalidPrecisionException(dataType, precision);
                    }

                    if (precision > dataType.MaxPrecision)
                    {
                        throw GetInvalidPrecisionException(dataType, precision);
                    }

                    if (dataType.SupportsScale && ReadIf(Token.Comma))
                    {
                        scale = ReadInt();
                        if (scale < dataType.MinScale || scale > dataType.MaxScale)
                        {
                            throw DbException.Get(
                                ErrorCode.InvalidValueScale,
                                scale.ToString(CultureInfo.InvariantCulture),
                                dataType.MinScale.ToString(CultureInfo.InvariantCulture),
                                dataType.MaxScale.ToString(CultureInfo.InvariantCulture));
                        }
                    }
                }
                else
                {
                    scale = ReadInt();
                    if (scale < dataType.MinScale || scale > dataType.MaxScale)
                    {
                        throw DbException.Get(
                            ErrorCode.InvalidValueScale,
                            scale.ToString(CultureInfo.InvariantCulture),
                            dataType.MinScale.ToString(CultureInfo.InvariantCulture),
                            dataType.MaxScale.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }

            Expect(Token.CloseParen, ")");
        }

        if (provider.GetMode().AllNumericTypesHavePrecision
            && (DataType.IsNumericType(type) || type == Value.Boolean))
        {
            // MySQL and MariaDB accept INT(11), BIGINT(20) and the like. The width is
            // display metadata only, so the reference reads it and ignores it.
            if (ReadIf(Token.OpenParen))
            {
                _ = ReadNonNegativeInt();
                Expect(Token.CloseParen, ")");
            }

            _ = IsWord("UNSIGNED");
        }

        return TypeInfo.GetTypeInfo(type, precision, scale, null);
    }

    /// <summary>
    /// Reads the FLOAT type, whose precision decides between REAL and DOUBLE.
    /// </summary>
    /// <returns>the type</returns>
    private TypeInfo ParseFloatType()
    {
        int type = Value.Double;
        int precision;
        if (ReadIf(Token.OpenParen))
        {
            precision = ReadNonNegativeInt();
            Expect(Token.CloseParen, ")");
            if (precision < 1 || precision > 53)
            {
                throw DbException.Get(
                    ErrorCode.InvalidValuePrecision,
                    precision.ToString(CultureInfo.InvariantCulture), "1", "53");
            }

            if (precision <= 24)
            {
                type = Value.Real;
            }
        }
        else
        {
            precision = 0;
        }

        return TypeInfo.GetTypeInfo(type, precision, -1, null);
    }

    /// <summary>
    /// Reads the NUMERIC type, with its precision and scale.
    /// </summary>
    /// <param name="decimal">true for DEC and DECIMAL, false for NUMERIC and NUMBER</param>
    /// <returns>the type</returns>
    private TypeInfo ParseNumericType(bool isDecimal)
    {
        long precision = -1L;
        int scale = -1;
        if (ReadIf(Token.OpenParen))
        {
            precision = ReadPrecision(Value.Numeric);
            if (precision < 1)
            {
                throw GetInvalidNumericPrecisionException(precision);
            }

            if (precision > Constants.MaxNumericPrecision)
            {
                throw GetInvalidNumericPrecisionException(precision);
            }

            if (ReadIf(Token.Comma))
            {
                scale = ReadInt();
                if (scale < 0 || scale > ValueNumeric.MaximumScale)
                {
                    throw DbException.Get(
                        ErrorCode.InvalidValueScale,
                        scale.ToString(CultureInfo.InvariantCulture), "0",
                        ValueNumeric.MaximumScale.ToString(CultureInfo.InvariantCulture));
                }
            }

            Expect(Token.CloseParen, ")");
        }

        return TypeInfo.GetTypeInfo(
            Value.Numeric, precision, scale, isDecimal ? ExtTypeInfoNumeric.Decimal : null);
    }

    /// <summary>
    /// Reads the DECFLOAT type.
    /// </summary>
    /// <returns>the type</returns>
    private TypeInfo ParseDecfloatType()
    {
        long precision = -1L;
        if (ReadIf(Token.OpenParen))
        {
            precision = ReadPrecision(Value.Decfloat);
            if (precision < 1 || precision > Constants.MaxNumericPrecision)
            {
                throw GetInvalidNumericPrecisionException(precision);
            }

            Expect(Token.CloseParen, ")");
        }

        return TypeInfo.GetTypeInfo(Value.Decfloat, precision, -1, null);
    }

    /// <summary>
    /// Reads the TIME type, with or without the time zone.
    /// </summary>
    /// <returns>the type</returns>
    private TypeInfo ParseTimeType()
    {
        int scale = -1;
        if (ReadIf(Token.OpenParen))
        {
            scale = ReadNonNegativeInt();
            if (scale > ValueTime.MaximumScale)
            {
                throw DbException.Get(
                    ErrorCode.InvalidValueScale,
                    scale.ToString(CultureInfo.InvariantCulture), "0",
                    ValueTime.MaximumScale.ToString(CultureInfo.InvariantCulture));
            }

            Expect(Token.CloseParen, ")");
        }

        int type = Value.Time;
        if (IsWord("WITH"))
        {
            ExpectWord("TIME");
            ExpectWord("ZONE");
            type = Value.TimeTz;
        }
        else if (IsWord("WITHOUT"))
        {
            ExpectWord("TIME");
            ExpectWord("ZONE");
        }

        return TypeInfo.GetTypeInfo(type, -1L, scale, null);
    }

    /// <summary>
    /// Reads the TIMESTAMP type, with or without the time zone.
    /// </summary>
    /// <returns>the type</returns>
    private TypeInfo ParseTimestampType()
    {
        int scale = -1;
        if (ReadIf(Token.OpenParen))
        {
            scale = ReadNonNegativeInt();

            // Allow non-standard TIMESTAMP(..., ...) syntax
            if (ReadIf(Token.Comma))
            {
                scale = ReadNonNegativeInt();
            }

            if (scale > ValueTimestamp.MaximumScale)
            {
                throw DbException.Get(
                    ErrorCode.InvalidValueScale,
                    scale.ToString(CultureInfo.InvariantCulture), "0",
                    ValueTimestamp.MaximumScale.ToString(CultureInfo.InvariantCulture));
            }

            Expect(Token.CloseParen, ")");
        }

        int type = Value.Timestamp;
        if (IsWord("WITH"))
        {
            ExpectWord("TIME");
            ExpectWord("ZONE");
            type = Value.TimestampTz;
        }
        else if (IsWord("WITHOUT"))
        {
            ExpectWord("TIME");
            ExpectWord("ZONE");
        }

        return TypeInfo.GetTypeInfo(type, -1L, scale, null);
    }

    /// <summary>
    /// Reads the array suffix of a type.
    /// </summary>
    /// <param name="componentType">the type the array holds</param>
    /// <returns>the type</returns>
    private TypeInfo ParseArrayType(TypeInfo componentType)
    {
        int precision = -1;
        if (ReadIf(Token.OpenBracket))
        {
            precision = ReadNonNegativeInt();
            if (precision > Engine.Constants.MaxArrayCardinality)
            {
                throw DbException.Get(ErrorCode.InvalidValuePrecision,
                    precision.ToString(CultureInfo.InvariantCulture), "0",
                    Engine.Constants.MaxArrayCardinality.ToString(CultureInfo.InvariantCulture));
            }

            Expect(Token.CloseBracket, "]");
        }

        return TypeInfo.GetTypeInfo(Value.Array, precision, -1, componentType);
    }

    /// <summary>
    /// Reads the ENUM type and the names it holds.
    /// </summary>
    /// <returns>the type</returns>
    private TypeInfo ParseEnumType()
    {
        Expect(Token.OpenParen, "(");
        List<string> enumeratorList = [];
        do
        {
            enumeratorList.Add(ReadString());
        }
        while (ReadIfMore());

        return TypeInfo.GetTypeInfo(
            Value.Enum, -1L, -1, new ExtTypeInfoEnum([.. enumeratorList]));
    }

    /// <summary>
    /// Reads the GEOMETRY type, with the kind and the reference system it is narrowed to.
    /// </summary>
    /// <returns>the type</returns>
    private TypeInfo ParseGeometryType()
    {
        ExtTypeInfoGeometry? extTypeInfo = null;
        if (ReadIf(Token.OpenParen))
        {
            int type = 0;
            if (CurrentType != ParserUtil.Identifier || Current.IsQuoted)
            {
                throw DbException.GetSyntaxError(sql, Current.Start, "geometry type");
            }

            if (!IsWord("GEOMETRY"))
            {
                try
                {
                    type = EWKTUtils.ParseGeometryType(Current.AsIdentifier!);
                    Read();
                    if (type / 1_000 == 0 && CurrentType == ParserUtil.Identifier && !Current.IsQuoted)
                    {
                        type += EWKTUtils.ParseDimensionSystem(Current.AsIdentifier!) * 1_000;
                        Read();
                    }
                }
                catch (ArgumentException)
                {
                    throw DbException.GetSyntaxError(sql, Current.Start, "geometry type");
                }
            }

            int? srid = null;
            if (ReadIf(Token.Comma))
            {
                srid = ReadInt();
            }

            Expect(Token.CloseParen, ")");
            extTypeInfo = new ExtTypeInfoGeometry(type, srid);
        }

        return TypeInfo.GetTypeInfo(Value.Geometry, -1L, -1, extTypeInfo);
    }

    /// <summary>
    /// Reads the ROW type and the fields it holds.
    /// </summary>
    /// <returns>the type</returns>
    private TypeInfo ParseRowType()
    {
        Expect(Token.OpenParen, "(");
        OrderedStringMap<TypeInfo> fields = new();
        do
        {
            string name = ReadIdentifier();
            TypeInfo type = ReadDataType();
            if (fields.ContainsKey(name))
            {
                throw DbException.Get(ErrorCode.DuplicateColumnName1, name);
            }

            fields.Put(name, type);
        }
        while (ReadIfMore());

        return TypeInfo.GetTypeInfo(Value.Row, -1L, -1, new ExtTypeInfoRow(fields));
    }

    /// <summary>
    /// Reads the qualifier of an interval type.
    /// </summary>
    /// <returns>the type, or null if the tokens are not an interval qualifier</returns>
    private TypeInfo? ReadIntervalQualifier()
    {
        IntervalQualifier qualifier;
        int precision = -1;
        int scale = -1;
        switch (CurrentType)
        {
            case ParserUtil.Year:
                Read();
                if (ReadIf(Token.OpenParen))
                {
                    precision = ReadNonNegativeInt();
                    Expect(Token.CloseParen, ")");
                }

                if (ReadIf(ParserUtil.To) && IsWord("MONTH"))
                {
                    qualifier = IntervalQualifier.YearToMonth;
                }
                else
                {
                    qualifier = IntervalQualifier.Year;
                }

                break;
            case ParserUtil.Month:
                Read();
                if (ReadIf(Token.OpenParen))
                {
                    precision = ReadNonNegativeInt();
                    Expect(Token.CloseParen, ")");
                }

                qualifier = IntervalQualifier.Month;
                break;
            case ParserUtil.Day:
                Read();
                if (ReadIf(Token.OpenParen))
                {
                    precision = ReadNonNegativeInt();
                    Expect(Token.CloseParen, ")");
                }

                if (ReadIf(ParserUtil.To))
                {
                    switch (CurrentType)
                    {
                        case ParserUtil.Hour:
                            Read();
                            qualifier = IntervalQualifier.DayToHour;
                            break;
                        case ParserUtil.Minute:
                            Read();
                            qualifier = IntervalQualifier.DayToMinute;
                            break;
                        case ParserUtil.Second:
                            Read();
                            if (ReadIf(Token.OpenParen))
                            {
                                scale = ReadNonNegativeInt();
                                Expect(Token.CloseParen, ")");
                            }

                            qualifier = IntervalQualifier.DayToSecond;
                            break;
                        default:
                            throw DbException.GetSyntaxError(sql, Current.Start, "interval");
                    }
                }
                else
                {
                    qualifier = IntervalQualifier.Day;
                }

                break;
            case ParserUtil.Hour:
                Read();
                if (ReadIf(Token.OpenParen))
                {
                    precision = ReadNonNegativeInt();
                    Expect(Token.CloseParen, ")");
                }

                if (ReadIf(ParserUtil.To))
                {
                    switch (CurrentType)
                    {
                        case ParserUtil.Minute:
                            Read();
                            qualifier = IntervalQualifier.HourToMinute;
                            break;
                        case ParserUtil.Second:
                            Read();
                            if (ReadIf(Token.OpenParen))
                            {
                                scale = ReadNonNegativeInt();
                                Expect(Token.CloseParen, ")");
                            }

                            qualifier = IntervalQualifier.HourToSecond;
                            break;
                        default:
                            throw DbException.GetSyntaxError(sql, Current.Start, "interval");
                    }
                }
                else
                {
                    qualifier = IntervalQualifier.Hour;
                }

                break;
            case ParserUtil.Minute:
                Read();
                if (ReadIf(Token.OpenParen))
                {
                    precision = ReadNonNegativeInt();
                    Expect(Token.CloseParen, ")");
                }

                if (ReadIf(ParserUtil.To) && IsWord("SECOND"))
                {
                    if (ReadIf(Token.OpenParen))
                    {
                        scale = ReadNonNegativeInt();
                        Expect(Token.CloseParen, ")");
                    }

                    qualifier = IntervalQualifier.MinuteToSecond;
                }
                else
                {
                    qualifier = IntervalQualifier.Minute;
                }

                break;
            case ParserUtil.Second:
                Read();
                if (ReadIf(Token.OpenParen))
                {
                    precision = ReadNonNegativeInt();
                    if (ReadIf(Token.Comma))
                    {
                        scale = ReadNonNegativeInt();
                    }

                    Expect(Token.CloseParen, ")");
                }

                qualifier = IntervalQualifier.Second;
                break;
            default:
                return null;
        }

        if (precision >= 0 && (precision == 0 || precision > ValueInterval.MaximumPrecision))
        {
            throw DbException.Get(
                ErrorCode.InvalidValuePrecision,
                precision.ToString(CultureInfo.InvariantCulture), "1",
                ValueInterval.MaximumPrecision.ToString(CultureInfo.InvariantCulture));
        }

        if (scale >= 0 && scale > ValueInterval.MaximumScale)
        {
            throw DbException.Get(
                ErrorCode.InvalidValueScale,
                scale.ToString(CultureInfo.InvariantCulture), "0",
                ValueInterval.MaximumScale.ToString(CultureInfo.InvariantCulture));
        }

        return TypeInfo.GetTypeInfo(
            Value.IntervalYear + (int)qualifier, precision, scale, null);
    }

    /// <summary>
    /// Reads the precision of a type, which may be scaled by K, M, G, T or P.
    /// </summary>
    /// <param name="valueType">the type the precision belongs to</param>
    /// <returns>the precision</returns>
    private long ReadPrecision(int valueType)
    {
        long p = ReadPositiveLong();
        if (CurrentType != ParserUtil.Identifier || Current.IsQuoted)
        {
            return p;
        }

        string? name = Current.AsIdentifier;
        if ((valueType == Value.Blob || valueType == Value.Clob) && name is { Length: 1 })
        {
            long mul;
            switch (char.ToUpperInvariant(name[0]))
            {
                case 'K':
                    mul = 1L << 10;
                    break;
                case 'M':
                    mul = 1L << 20;
                    break;
                case 'G':
                    mul = 1L << 30;
                    break;
                case 'T':
                    mul = 1L << 40;
                    break;
                case 'P':
                    mul = 1L << 50;
                    break;
                default:
                    throw DbException.GetSyntaxError(sql, Current.Start, "precision");
            }

            if (p > long.MaxValue / mul)
            {
                throw DbException.GetInvalidValueException("precision", p + name);
            }

            p *= mul;
            Read();
            if (CurrentType != ParserUtil.Identifier || Current.IsQuoted)
            {
                return p;
            }
        }

        switch (valueType)
        {
            case Value.Varchar:
            case Value.VarcharIgnorecase:
            case Value.Clob:
            case Value.Char:
                if (!IsWord("CHARACTERS") && !IsWord("OCTETS"))
                {
                    IsWord("CHAR");
                    IsWord("BYTE");
                }

                break;
        }

        return p;
    }

    /// <summary>
    /// Reads a type, and refuses the tokens that are not one.
    /// </summary>
    /// <returns>the type</returns>
    private TypeInfo ReadDataType()
    {
        TypeInfo? typeInfo = ReadIfDataType();
        if (typeInfo is null)
        {
            throw DbException.GetSyntaxError(sql, Current.Start, "data type");
        }

        return typeInfo;
    }

    /// <summary>
    /// Reads the type of a column: a type, or the name of a domain.
    /// </summary>
    /// <remarks>
    /// The reference reads a name that is not a type as a domain and looks it up in the
    /// schema. The port has no domains, so the lookup has one answer, and it is the answer
    /// the reference gives when the schema has no such domain.
    /// </remarks>
    /// <returns>the type</returns>
    private TypeInfo ReadColumnWithType()
    {
        TypeInfo? typeInfo = ReadIfDataType();
        if (typeInfo is not null)
        {
            return typeInfo;
        }

        if (session is null)
        {
            throw DbException.Get(ErrorCode.DomainNotFound1, ReadIdentifier());
        }

        string name = ReadIdentifier();
        Schemas.Schema schema = session.GetCurrentSchema();
        if (ReadIf(Token.Dot))
        {
            string first = name;
            name = ReadIdentifier();
            if (CurrentType == Token.Dot
                && first.Equals(session.GetDatabase().GetShortName(), StringComparison.OrdinalIgnoreCase))
            {
                Read();
                schema = session.GetDatabase().GetSchema(name);
                name = ReadIdentifier();
            }
            else
            {
                // The port does not create the PG_CATALOG schema yet; the reference resolves
                // it and reports the missing domain instead. A really missing schema keeps
                // the reference's SCHEMA_NOT_FOUND.
                schema = session.GetDatabase().FindSchema(first)
                    ?? (first.Equals(Engine.Constants.SchemaPgCatalog, StringComparison.OrdinalIgnoreCase)
                        ? throw DbException.Get(ErrorCode.DomainNotFound1, name)
                        : throw DbException.Get(ErrorCode.SchemaNotFound1, first));
            }
        }

        Schemas.Domain? domain = schema.FindDomain(name);
        if (domain is null)
        {
            throw DbException.Get(ErrorCode.DomainNotFound1, name);
        }

        return domain.GetDataType();
    }

    /// <summary>
    /// Reads a string literal.
    /// </summary>
    /// <returns>the string</returns>
    private string ReadString()
    {
        Value? value = Current.GetValue(provider);
        if (value is null || !DataType.IsCharacterStringType(value.GetValueType()))
        {
            throw DbException.GetSyntaxError(sql, Current.Start, "string");
        }

        Read();
        return value.GetString() ?? string.Empty;
    }

    /// <summary>
    /// Reads an integer literal.
    /// </summary>
    /// <returns>the value</returns>
    private int ReadInt()
    {
        bool minus = false;
        if (CurrentType == Token.MinusSign)
        {
            minus = true;
            Read();
        }
        else if (CurrentType == Token.PlusSign)
        {
            Read();
        }

        if (CurrentType != Token.Literal)
        {
            throw DbException.GetSyntaxError(sql, Current.Start, "integer");
        }

        Value value = Current.GetValue(provider)
            ?? throw DbException.GetSyntaxError(sql, Current.Start, "integer");
        if (minus)
        {
            value = value.Negate();
        }

        int i = value.GetInt();
        Read();
        return i;
    }

    /// <summary>
    /// Reads an integer literal that is not negative.
    /// </summary>
    /// <returns>the value</returns>
    private int ReadNonNegativeInt()
    {
        int v = ReadInt();
        if (v < 0)
        {
            throw DbException.GetInvalidValueException("non-negative integer", v);
        }

        return v;
    }

    /// <summary>
    /// Reads a long literal.
    /// </summary>
    /// <returns>the value</returns>
    private long ReadLong()
    {
        bool minus = false;
        if (CurrentType == Token.MinusSign)
        {
            minus = true;
            Read();
        }
        else if (CurrentType == Token.PlusSign)
        {
            Read();
        }

        if (CurrentType != Token.Literal)
        {
            throw DbException.GetSyntaxError(sql, Current.Start, "long");
        }

        Value value = Current.GetValue(provider)
            ?? throw DbException.GetSyntaxError(sql, Current.Start, "long");
        if (minus)
        {
            value = value.Negate();
        }

        long i = value.GetLong();
        Read();
        return i;
    }

    /// <summary>
    /// Reads a long literal that is positive.
    /// </summary>
    /// <returns>the value</returns>
    private long ReadPositiveLong()
    {
        long v = ReadLong();
        if (v <= 0)
        {
            throw DbException.GetInvalidValueException("positive long", v);
        }

        return v;
    }

    /// <summary>
    /// The error of a precision that is out of range.
    /// </summary>
    /// <param name="dataType">the type</param>
    /// <param name="precision">the precision</param>
    /// <returns>the exception</returns>
    private static H2SharpException GetInvalidPrecisionException(DataType dataType, long precision)
        => DbException.Get(
            ErrorCode.InvalidValuePrecision,
            precision.ToString(CultureInfo.InvariantCulture),
            dataType.MinPrecision.ToString(CultureInfo.InvariantCulture),
            dataType.MaxPrecision.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// The error of a numeric precision that is out of range.
    /// </summary>
    /// <param name="precision">the precision</param>
    /// <returns>the exception</returns>
    private static H2SharpException GetInvalidNumericPrecisionException(long precision)
        => DbException.Get(
            ErrorCode.InvalidValuePrecision,
            precision.ToString(CultureInfo.InvariantCulture), "1",
            Constants.MaxNumericPrecision.ToString(CultureInfo.InvariantCulture));
}
