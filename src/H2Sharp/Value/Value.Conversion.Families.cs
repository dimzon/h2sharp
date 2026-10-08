/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.value.Value.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The conversion family: castTo, convertForAssignTo and the convertTo behind both of them.
 *
 * The reference has one convertTo with three modes and a switch over every target type. The three
 * modes are what make it one method rather than three: CONVERT_TO ignores precision and scale,
 * CAST_TO truncates to fit them, and ASSIGN_TO refuses to lose anything. Everything else about
 * the three is the same, and the difference shows up in exactly one place per type — a length
 * check that becomes a truncation or an error. That is why the mode is a parameter here too and
 * not three methods, and why a call site that does not care passes ConvertTo.
 *
 * The narrow version that the comparison needed lives in ConvertToForComparison below and is
 * unchanged; it converts both operands of a comparison at once, which is not what this does.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values.Lobs;

namespace H2Sharp.Values;

/// <summary>
/// A value of some type. Every value knows its type number, and every type knows how to
/// describe itself as SQL.
/// </summary>
public abstract partial class Value
{
    /// <summary>
    /// The largest long as a decimal, for the conversions that have to check a decimal against
    /// the range of a long before rounding it.
    /// </summary>
    private static readonly BigDecimal MaxLongDecimal = BigDecimal.FromLong(long.MaxValue);

    /// <summary>
    /// The smallest long as a decimal, for the same reason.
    /// </summary>
    private static readonly BigDecimal MinLongDecimal = BigDecimal.FromLong(long.MinValue);

    /// <summary>
    /// Cast a value to the specified type.
    /// </summary>
    /// <remarks>
    /// Named <c>CastToValue</c> and not <c>CastTo</c> because this class already carries a
    /// constant called <c>CastTo</c> — the conversion mode — and C# will not let one class hold
    /// a constant and a method under one name. The reference's <c>castTo</c> and its
    /// <c>CAST_TO</c> are two different things; the names differ only by case there, which is
    /// exactly what C# cannot do. The constant keeps the reference's name because the constant
    /// table is compared against the JVM field by field, and a suffix there would fail a
    /// comparison that is meant to catch a dropped constant.
    /// </remarks>
    /// <param name="provider">the cast information provider</param>
    /// <returns>the converted value</returns>
    public Value CastToValue(TypeInfo targetType, CastDataProvider? provider)
        => ConvertToValue(targetType, provider, CastTo, null);

    /// <summary>
    /// Cast a value to the specified type for assignment. The scale is set if applicable, and if
    /// precision is too large an exception is thrown.
    /// </summary>
    /// <param name="targetType">the type of the returned value</param>
    /// <param name="provider">the cast information provider</param>
    /// <param name="column">the column, used to improve the error message if conversion fails</param>
    /// <returns>the converted value</returns>
    public Value ConvertForAssignTo(TypeInfo targetType, CastDataProvider? provider, object? column)
        => ConvertToValue(targetType, provider, AssignTo, column);

    /// <summary>
    /// Convert a value to the specified type.
    /// </summary>
    /// <param name="targetType">the type of the returned value</param>
    /// <param name="provider">the cast information provider</param>
    /// <param name="conversionMode">conversion mode</param>
    /// <param name="column">the column, if any, used to improve the error message on failure</param>
    /// <returns>the converted value</returns>
    private Value ConvertToValue(
        TypeInfo targetType,
        CastDataProvider? provider,
        int conversionMode,
        object? column)
    {
        int valueType = GetValueType();
        int targetValueType = targetType.GetValueType();

        // The shortcut is narrow on purpose. Same type is enough only when the mode says scale
        // and precision are not the point, the target carries no extended parameters, and the
        // type is not CHAR — CHAR has to go through its own conversion because that is where
        // the padding is dropped.
        if (valueType == Null
            || (valueType == targetValueType && conversionMode == ConvertTo
                && targetType.GetExtTypeInfo() is null && valueType != Char))
        {
            return this;
        }

        return targetValueType switch
        {
            Null => ValueNull.Instance,
            Char => ConvertToCharType(targetType, provider, conversionMode, column),
            Varchar => ConvertToVarcharType(targetType, provider, conversionMode, column),
            VarcharIgnorecase => ConvertToVarcharIgnoreCaseType(targetType, conversionMode, column),
            Binary => ConvertToBinaryType(targetType, conversionMode, column),
            Varbinary => ConvertToVarbinaryType(targetType, conversionMode, column),
            Blob => ConvertToBlobType(targetType, conversionMode, column),
            Clob => ConvertToClobType(targetType, conversionMode, column),
            Boolean => ConvertToBooleanType(),
            Tinyint => ConvertToTinyintType(column),
            Smallint => ConvertToSmallintType(column),
            Integer => ConvertToIntType(column),
            Bigint => ConvertToBigintType(column),
            Numeric => ConvertToNumericType(targetType, provider, conversionMode, column),
            Real => ConvertToRealType(),
            Double => ConvertToDoubleType(),
            Decfloat => ConvertToDecfloat(targetType, conversionMode),
            Date => ConvertToDateType(provider),
            Time => throw NotPortedConversion("convert", GetTypeName(targetValueType)),
            TimeTz => throw NotPortedConversion("convert", GetTypeName(targetValueType)),
            Timestamp => throw NotPortedConversion("convert", GetTypeName(targetValueType)),
            TimestampTz => throw NotPortedConversion("convert", GetTypeName(targetValueType)),
            IntervalYear or IntervalMonth or IntervalYearToMonth
                => throw NotPortedConversion("convert", GetTypeName(targetValueType)),
            IntervalDay or IntervalHour or IntervalMinute or IntervalSecond or IntervalDayToHour
                or IntervalDayToMinute or IntervalDayToSecond or IntervalHourToMinute
                or IntervalHourToSecond or IntervalMinuteToSecond
                => throw NotPortedConversion("convert", GetTypeName(targetValueType)),
            JavaObject => throw NotPortedConversion("convert", GetTypeName(targetValueType)),
            Enum => ConvertToEnum((ExtTypeInfoEnum)targetType.GetExtTypeInfo()!, provider),
            Geometry => ConvertToGeometryType(targetType),
            Json => throw NotPortedConversion("convert", GetTypeName(targetValueType)),
            Uuid => ConvertToUuidType(),
            Array => ConvertToArrayType(targetType, provider, conversionMode, column),
            Row => ConvertToRow(targetType, provider, conversionMode, column),
            _ => throw GetDataConversionErrorException(targetValueType),
        };
    }

    /// <summary>
    /// Converts this value to a CHAR value. May not be called on a NULL value.
    /// </summary>
    /// <returns>a CHAR value</returns>
    public ValueChar ConvertToChar() => ConvertToCharType(TypeInfo.GetTypeInfo(Char), null, ConvertTo, null);

    private ValueChar ConvertToCharType(
        TypeInfo targetType,
        CastDataProvider? provider,
        int conversionMode,
        object? column)
    {
        int valueType = GetValueType();
        if (valueType is Blob or JavaObject)
        {
            throw GetDataConversionErrorException(Char);
        }

        string s = GetString() ?? throw new NullReferenceException();
        int length = s.Length;
        int newLength = length;
        if (conversionMode == ConvertTo)
        {
            // Converting drops the padding: a CHAR is padded out to its declared length, and
            // that padding is not part of the value it holds.
            while (newLength > 0 && s[newLength - 1] == ' ')
            {
                newLength--;
            }
        }
        else
        {
            int p = MathUtils.ConvertLongToInt(targetType.GetPrecision());
            if (provider is null || provider.GetMode().CharPadding == CharPadding.Always)
            {
                if (newLength != p)
                {
                    if (newLength < p)
                    {
                        return ValueChar.Get(StringUtils.Pad(s, p, null, true));
                    }

                    if (conversionMode == CastTo)
                    {
                        newLength = p;
                    }
                    else
                    {
                        // Assignment refuses to lose anything, and the only thing it may lose is
                        // padding, so it walks back over the spaces until the value fits.
                        do
                        {
                            if (s[--newLength] != ' ')
                            {
                                throw GetValueTooLongException(targetType, column);
                            }
                        }
                        while (newLength > p);
                    }
                }
            }
            else
            {
                if (conversionMode == CastTo && newLength > p)
                {
                    newLength = p;
                }

                while (newLength > 0 && s[newLength - 1] == ' ')
                {
                    newLength--;
                }

                if (conversionMode == AssignTo && newLength > p)
                {
                    throw GetValueTooLongException(targetType, column);
                }
            }
        }

        if (length != newLength)
        {
            s = s[..newLength];
        }
        else if (valueType == Char)
        {
            return (ValueChar)this;
        }

        return ValueChar.Get(s);
    }

    private Value ConvertToVarcharType(
        TypeInfo targetType,
        CastDataProvider? provider,
        int conversionMode,
        object? column)
    {
        int valueType = GetValueType();
        if (valueType is Blob or JavaObject)
        {
            throw GetDataConversionErrorException(Varchar);
        }

        if (conversionMode != ConvertTo)
        {
            string? s = GetString();
            int p = MathUtils.ConvertLongToInt(targetType.GetPrecision());
            if (s is not null && s.Length > p)
            {
                // Casting truncates and assignment refuses; this is the whole difference between
                // the two modes for this type.
                if (conversionMode != CastTo)
                {
                    throw GetValueTooLongException(targetType, column);
                }

                return ValueVarchar.Get(s[..p], provider);
            }
        }

        return valueType == Varchar ? this : ValueVarchar.Get(GetString() ?? string.Empty, provider);
    }

    private Value ConvertToVarcharIgnoreCaseType(TypeInfo targetType, int conversionMode, object? column)
    {
        int valueType = GetValueType();
        if (valueType is Blob or JavaObject)
        {
            throw GetDataConversionErrorException(VarcharIgnorecase);
        }

        if (conversionMode != ConvertTo)
        {
            string? s = GetString();
            int p = MathUtils.ConvertLongToInt(targetType.GetPrecision());
            if (s is not null && s.Length > p)
            {
                if (conversionMode != CastTo)
                {
                    throw GetValueTooLongException(targetType, column);
                }

                return ValueVarcharIgnoreCase.Get(s[..p]);
            }
        }

        return valueType == VarcharIgnorecase ? this : ValueVarcharIgnoreCase.Get(GetString()!);
    }

    private ValueBinary ConvertToBinaryType(TypeInfo targetType, int conversionMode, object? column)
    {
        ValueBinary v;
        if (GetValueType() == Binary)
        {
            v = (ValueBinary)this;
        }
        else
        {
            byte[]? bytes = GetBytesNoCopy();
            if (bytes is null)
            {
                throw GetDataConversionErrorException(Binary);
            }

            v = ValueBinary.GetNoCopy(bytes);
        }

        if (conversionMode != ConvertTo)
        {
            // Unlike a string, a BINARY is padded out to its declared length as well, so a
            // conversion to it has to restore the length rather than drop it.
            byte[] value = v.GetBytesNoCopy()!;
            int length = value.Length;
            int p = MathUtils.ConvertLongToInt(targetType.GetPrecision());
            if (length != p)
            {
                if (conversionMode == AssignTo && length > p)
                {
                    throw v.GetValueTooLongException(targetType, column);
                }

                byte[] resized = new byte[p];
                System.Array.Copy(value, resized, Math.Min(length, p));
                v = ValueBinary.GetNoCopy(resized);
            }
        }

        return v;
    }

    private ValueVarbinary ConvertToVarbinaryType(TypeInfo targetType, int conversionMode, object? column)
    {
        ValueVarbinary v;
        if (GetValueType() == Varbinary)
        {
            v = (ValueVarbinary)this;
        }
        else
        {
            v = ValueVarbinary.GetNoCopy(GetBytesNoCopy()!);
        }

        if (conversionMode != ConvertTo)
        {
            byte[] value = v.GetBytesNoCopy()!;
            int length = value.Length;
            int p = MathUtils.ConvertLongToInt(targetType.GetPrecision());
            if (conversionMode == CastTo)
            {
                if (length > p)
                {
                    v = ValueVarbinary.GetNoCopy(value[..p]);
                }
            }
            else if (length > p)
            {
                throw v.GetValueTooLongException(targetType, column);
            }
        }

        return v;
    }

    private ValueBlob ConvertToBlobType(TypeInfo targetType, int conversionMode, object? column)
    {
        ValueBlob v = GetValueType() == Blob
            ? (ValueBlob)this
            : ValueBlob.CreateSmall(GetBytesNoCopy() ?? throw GetDataConversionErrorException(Blob));

        if (conversionMode != ConvertTo)
        {
            if (conversionMode == CastTo)
            {
                return v.ConvertPrecision(targetType.GetPrecision());
            }

            if (v.OctetLength() > targetType.GetPrecision())
            {
                throw v.GetValueTooLongException(targetType, column);
            }
        }

        return v;
    }

    private ValueClob ConvertToClobType(TypeInfo targetType, int conversionMode, object? column)
    {
        ValueClob v;
        switch (GetValueType())
        {
            case Clob:
                v = (ValueClob)this;
                break;
            case Blob:
                {
                    // Reusing the bytes is worth a check: a BLOB read as UTF-8 and written back
                    // gives the same array only when the bytes were valid UTF-8 to begin with.
                    // Where they were not, the round trip replaces them and the array has to be
                    // copied instead, which is what the comparison below decides.
                    LobData data = ((ValueBlob)this).LobData;
                    if (data is LobDataInMemory memory)
                    {
                        byte[] small = memory.GetSmall()!;
                        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(
                            System.Text.Encoding.UTF8.GetString(small));
                        v = ValueClob.CreateSmall(bytes.AsSpan().SequenceEqual(small) ? small : bytes);
                    }
                    else
                    {
                        v = ValueClob.CreateSmall(GetString() ?? string.Empty);
                    }

                    break;
                }

            default:
                v = ValueClob.CreateSmall(GetString() ?? string.Empty);
                break;
        }

        if (conversionMode != ConvertTo)
        {
            if (conversionMode == CastTo)
            {
                return v.ConvertPrecision(targetType.GetPrecision());
            }

            if (v.CharLength() > targetType.GetPrecision())
            {
                throw v.GetValueTooLongException(targetType, column);
            }
        }

        return v;
    }

    private ValueBoolean ConvertToBooleanType()
    {
        int valueType = GetValueType();
        if (valueType == Boolean)
        {
            return (ValueBoolean)this;
        }

        switch (valueType)
        {
            case Char:
            case Varchar:
            case VarcharIgnorecase:
                return ValueBoolean.Get(GetBoolean());

            // Every numeric type answers by whether it is zero, so this one case covers all of
            // them rather than listing ten type numbers.
            case Tinyint:
            case Smallint:
            case Integer:
            case Bigint:
            case Numeric:
            case Double:
            case Real:
            case Decfloat:
                return ValueBoolean.Get(GetSignum() != 0);

            case Null:
                throw DbException.GetInternalError();

            default:
                throw GetDataConversionErrorException(Boolean);
        }
    }

    private ValueTinyint ConvertToTinyintType(object? column)
    {
        int valueType = GetValueType();
        if (valueType == Tinyint)
        {
            return (ValueTinyint)this;
        }

        switch (valueType)
        {
            case Char:
            case Varchar:
            case VarcharIgnorecase:
            case Boolean:
                return ValueTinyint.Get(GetByte());

            case Smallint:
            case Enum:
            case Integer:
                return ValueTinyint.Get(ConvertToByteChecked(GetInt(), column));

            case Bigint:
            case IntervalYear:
            case IntervalMonth:
            case IntervalDay:
            case IntervalHour:
            case IntervalMinute:
            case IntervalSecond:
            case IntervalYearToMonth:
            case IntervalDayToHour:
            case IntervalDayToMinute:
            case IntervalDayToSecond:
            case IntervalHourToMinute:
            case IntervalHourToSecond:
            case IntervalMinuteToSecond:
                return ValueTinyint.Get(ConvertToByteChecked(GetLong(), column));

            case Numeric:
            case Decfloat:
                return ValueTinyint.Get(
                    ConvertToByteChecked(ConvertToLongChecked(GetBigDecimal(), column), column));

            case Real:
            case Double:
                return ValueTinyint.Get(ConvertToByteChecked(ConvertToLongChecked(GetDouble(), column), column));

            case Binary:
            case Varbinary:
                {
                    // A single byte is a TINYINT. Any other length is not a number at all, so it
                    // falls through to the error below rather than taking the first byte.
                    byte[] bytes = GetBytesNoCopy()!;
                    if (bytes.Length == 1)
                    {
                        return ValueTinyint.Get(unchecked((sbyte)bytes[0]));
                    }

                    throw GetDataConversionErrorException(Tinyint);
                }

            case Null:
                throw DbException.GetInternalError();

            default:
                throw GetDataConversionErrorException(Tinyint);
        }
    }

    private ValueSmallint ConvertToSmallintType(object? column)
    {
        int valueType = GetValueType();
        if (valueType == Smallint)
        {
            return (ValueSmallint)this;
        }

        switch (valueType)
        {
            case Char:
            case Varchar:
            case VarcharIgnorecase:
            case Boolean:
            case Tinyint:
                return ValueSmallint.Get(GetShort());

            case Enum:
            case Integer:
                return ValueSmallint.Get(ConvertToShortChecked(GetInt(), column));

            case Bigint:
            case IntervalYear:
            case IntervalMonth:
            case IntervalDay:
            case IntervalHour:
            case IntervalMinute:
            case IntervalSecond:
            case IntervalYearToMonth:
            case IntervalDayToHour:
            case IntervalDayToMinute:
            case IntervalDayToSecond:
            case IntervalHourToMinute:
            case IntervalHourToSecond:
            case IntervalMinuteToSecond:
                return ValueSmallint.Get(ConvertToShortChecked(GetLong(), column));

            case Numeric:
            case Decfloat:
                return ValueSmallint.Get(
                    ConvertToShortChecked(ConvertToLongChecked(GetBigDecimal(), column), column));

            case Real:
            case Double:
                return ValueSmallint.Get(ConvertToShortChecked(ConvertToLongChecked(GetDouble(), column), column));

            case Null:
                throw DbException.GetInternalError();

            default:
                throw GetDataConversionErrorException(Smallint);
        }
    }

    internal ValueInteger ConvertToIntType(object? column)
    {
        int valueType = GetValueType();
        if (valueType == Integer)
        {
            return (ValueInteger)this;
        }

        switch (valueType)
        {
            case Char:
            case Varchar:
            case VarcharIgnorecase:
            case Boolean:
            case Tinyint:
            case Enum:
            case Smallint:
                return ValueInteger.Get(GetInt());

            case Bigint:
            case IntervalYear:
            case IntervalMonth:
            case IntervalDay:
            case IntervalHour:
            case IntervalMinute:
            case IntervalSecond:
            case IntervalYearToMonth:
            case IntervalDayToHour:
            case IntervalDayToMinute:
            case IntervalDayToSecond:
            case IntervalHourToMinute:
            case IntervalHourToSecond:
            case IntervalMinuteToSecond:
                return ValueInteger.Get(ConvertToIntChecked(GetLong(), column));

            case Numeric:
            case Decfloat:
                return ValueInteger.Get(
                    ConvertToIntChecked(ConvertToLongChecked(GetBigDecimal(), column), column));

            case Real:
            case Double:
                return ValueInteger.Get(ConvertToIntChecked(ConvertToLongChecked(GetDouble(), column), column));

            case Binary:
            case Varbinary:
                {
                    // Four bytes are an INTEGER, read big-endian, which is how the reference's
                    // INT_VH_BE answers. Any other length is a conversion error, not a number.
                    byte[] bytes = GetBytesNoCopy()!;
                    if (bytes.Length == 4)
                    {
                        return ValueInteger.Get(ReadIntBigEndian(bytes));
                    }

                    throw GetDataConversionErrorException(Integer);
                }

            case Null:
                throw DbException.GetInternalError();

            default:
                throw GetDataConversionErrorException(Integer);
        }
    }

    private ValueBigint ConvertToBigintType(object? column)
    {
        int valueType = GetValueType();
        if (valueType == Bigint)
        {
            return (ValueBigint)this;
        }

        switch (valueType)
        {
            case Char:
            case Varchar:
            case VarcharIgnorecase:
            case Boolean:
            case Tinyint:
            case Smallint:
            case Integer:
            case IntervalYear:
            case IntervalMonth:
            case IntervalDay:
            case IntervalHour:
            case IntervalMinute:
            case IntervalSecond:
            case IntervalYearToMonth:
            case IntervalDayToHour:
            case IntervalDayToMinute:
            case IntervalDayToSecond:
            case IntervalHourToMinute:
            case IntervalHourToSecond:
            case IntervalMinuteToSecond:
            case Enum:
                return ValueBigint.Get(GetLong());

            case Numeric:
            case Decfloat:
                return ValueBigint.Get(ConvertToLongChecked(GetBigDecimal(), column));

            case Real:
            case Double:
                return ValueBigint.Get(ConvertToLongChecked(GetDouble(), column));

            case Binary:
            case Varbinary:
                {
                    byte[] bytes = GetBytesNoCopy()!;
                    if (bytes.Length == 8)
                    {
                        return ValueBigint.Get(ReadLongBigEndian(bytes));
                    }

                    throw GetDataConversionErrorException(Bigint);
                }

            case Null:
                throw DbException.GetInternalError();

            default:
                throw GetDataConversionErrorException(Bigint);
        }
    }

    private ValueNumeric ConvertToNumericType(
        TypeInfo targetType,
        CastDataProvider? provider,
        int conversionMode,
        object? column)
    {
        ValueNumeric v;
        switch (GetValueType())
        {
            case Numeric:
                v = (ValueNumeric)this;
                break;

            case Boolean:
                v = GetBoolean() ? ValueNumeric.One : ValueNumeric.Zero;
                break;

            default:
                {
                    BigDecimal value = GetBigDecimal();
                    int targetScale = targetType.GetScale();
                    int scale = value.Scale;
                    if (scale < 0
                        || scale > ValueNumeric.MaximumScale
                        || (conversionMode != ConvertTo && scale != targetScale
                            && (scale >= targetScale || !provider!.GetMode().ConvertOnlyToSmallerScale)))
                    {
                        value = ValueNumeric.SetScale(value, targetScale);
                    }

                    if (conversionMode != ConvertTo
                        && value.Precision() > targetType.GetPrecision() - targetScale + value.Scale)
                    {
                        throw GetValueTooLongException(targetType, column);
                    }

                    return ValueNumeric.Get(value);
                }
        }

        if (conversionMode != ConvertTo)
        {
            int targetScale = targetType.GetScale();
            BigDecimal value = v.GetBigDecimal();
            int scale = value.Scale;
            if (scale != targetScale && (scale >= targetScale || !provider!.GetMode().ConvertOnlyToSmallerScale))
            {
                v = ValueNumeric.Get(ValueNumeric.SetScale(value, targetScale));
            }

            BigDecimal bd = v.GetBigDecimal();
            if (bd.Precision() > targetType.GetPrecision() - targetScale + bd.Scale)
            {
                throw v.GetValueTooLongException(targetType, column);
            }
        }

        return v;
    }

    /// <summary>
    /// Converts this value to an ENUM value of the given list. May not be called on a NULL value.
    /// </summary>
    /// <remarks>
    /// An ENUM is reached from a number or from a label, and nothing else: a boolean is not an
    /// enum even where one of the labels reads TRUE, because the reference does not list BOOLEAN
    /// here and a port that added it would accept a conversion the engine refuses.
    /// <para>
    /// An ENUM converting to an ENUM with the same list is itself and is returned as it is. A
    /// different list means the label has to be looked up again, because the ordinals of two
    /// lists have nothing to do with each other.
    /// </para>
    /// </remarks>
    /// <param name="extTypeInfo">the target list of enumerators</param>
    /// <param name="provider">the cast information provider</param>
    /// <returns>the ENUM value</returns>
    public ValueEnum ConvertToEnum(ExtTypeInfoEnum extTypeInfo, CastDataProvider? provider)
    {
        switch (GetValueType())
        {
            case Enum:
                {
                    ValueEnum v = (ValueEnum)this;
                    if (extTypeInfo.Equals(v.GetEnumerators()))
                    {
                        return v;
                    }

                    // A different list: the label is what travels, and the new list decides the
                    // ordinal. A label the new list does not have is an error there, which is the
                    // reference's answer too.
                    return extTypeInfo.GetValue(v.GetString() ?? string.Empty, provider);
                }

            case Tinyint:
            case Smallint:
            case Integer:
            case Bigint:
            case Numeric:
            case Decfloat:
                return extTypeInfo.GetValue(GetInt(), provider);

            case Varchar:
            case VarcharIgnorecase:
            case Char:
                return extTypeInfo.GetValue(GetString() ?? string.Empty, provider);

            case Null:
                throw DbException.GetInternalError();

            default:
                throw GetDataConversionErrorException(Enum);
        }
    }

    /// <summary>
    /// Converts this value to a DECFLOAT value. May not be called on a NULL value.
    /// </summary>
    /// <remarks>
    /// DECFLOAT is the fallback of the whole type system rather than a type a user asks for
    /// often: it is where a comparison of two values of unrelated types goes, and it is the
    /// widest exact numeric type there is. That is why the string branch parses rather than
    /// refuses — the point of the conversion is that something from any family can reach it.
    /// </remarks>
    /// <param name="targetType">the type of the returned value</param>
    /// <param name="conversionMode">conversion mode</param>
    /// <returns>the DECFLOAT value</returns>
    public ValueDecfloat ConvertToDecfloat(TypeInfo? targetType, int conversionMode)
    {
        ValueDecfloat v;
        int valueType = GetValueType();
        if (valueType == Decfloat)
        {
            v = (ValueDecfloat)this;
            if (v.GetDecimalValue() is null)
            {
                return v;
            }
        }
        else if (valueType is Char or Varchar or VarcharIgnorecase)
        {
            // Trimmed first: a string with a space around it is a number to a user, and the
            // infinities and the NaNs are spelled as words rather than parsed as numbers.
            string s = (GetString() ?? string.Empty).Trim();
            try
            {
                v = ValueDecfloat.Get(BigDecimal.Parse(s));
            }
            catch (FormatException)
            {
                return s switch
                {
                    "-Infinity" => ValueDecfloat.NegativeInfinity,
                    "Infinity" or "+Infinity" => ValueDecfloat.PositiveInfinity,
                    "NaN" or "-NaN" or "+NaN" => ValueDecfloat.NotANumber,
                    _ => throw GetDataConversionErrorException(Decfloat),
                };
            }
        }
        else if (valueType == Boolean)
        {
            v = GetBoolean() ? ValueDecfloat.One : ValueDecfloat.Zero;
        }
        else if (valueType == Real)
        {
            float f = GetFloat();
            if (float.IsFinite(f))
            {
                v = ValueDecfloat.Get(BigDecimal.Parse(JavaNumberFormat.FloatToString(f)));
            }
            else if (f == float.PositiveInfinity)
            {
                return ValueDecfloat.PositiveInfinity;
            }
            else if (f == float.NegativeInfinity)
            {
                return ValueDecfloat.NegativeInfinity;
            }
            else
            {
                return ValueDecfloat.NotANumber;
            }
        }
        else if (valueType == Double)
        {
            double d = GetDouble();
            if (double.IsFinite(d))
            {
                v = ValueDecfloat.Get(BigDecimal.Parse(JavaNumberFormat.DoubleToString(d)));
            }
            else if (d == double.PositiveInfinity)
            {
                return ValueDecfloat.PositiveInfinity;
            }
            else if (d == double.NegativeInfinity)
            {
                return ValueDecfloat.NegativeInfinity;
            }
            else
            {
                return ValueDecfloat.NotANumber;
            }
        }
        else if (valueType == Null)
        {
            throw DbException.GetInternalError();
        }
        else
        {
            try
            {
                v = ValueDecfloat.Get(GetBigDecimal());
            }
            catch (FormatException)
            {
                throw GetDataConversionErrorException(Decfloat);
            }
        }

        if (conversionMode != ConvertTo && targetType is not null)
        {
            BigDecimal bd = v.GetDecimalValue()!;
            int precision = bd.Precision();
            int targetPrecision = MathUtils.ConvertLongToInt(targetType.GetPrecision());
            if (precision > targetPrecision)
            {
                // Fewer digits, same value: the scale drops by exactly as many digits as the
                // precision is short by, so the number keeps its magnitude.
                v = ValueDecfloat.Get(bd.SetScale(
                    bd.Scale - precision + targetPrecision, RoundingMode.HalfUp));
            }
        }

        return v;
    }

    private ValueReal ConvertToRealType()
    {
        int valueType = GetValueType();
        if (valueType == Real)
        {
            return (ValueReal)this;
        }

        if (valueType == Boolean)
        {
            return GetBoolean() ? ValueReal.One : ValueReal.Zero;
        }

        if (valueType == Null)
        {
            throw DbException.GetInternalError();
        }

        return ValueReal.Get(GetFloat());
    }

    private ValueDouble ConvertToDoubleType()
    {
        int valueType = GetValueType();
        if (valueType == Double)
        {
            return (ValueDouble)this;
        }

        if (valueType == Boolean)
        {
            return GetBoolean() ? ValueDouble.One : ValueDouble.Zero;
        }

        if (valueType == Null)
        {
            throw DbException.GetInternalError();
        }

        return ValueDouble.Get(GetDouble());
    }

    private ValueDate ConvertToDateType(CastDataProvider? provider)
    {
        switch (GetValueType())
        {
            case Date:
                return (ValueDate)this;

            // A TIMESTAMP becomes a DATE by keeping only its date part, and a string becomes one
            // by being parsed, which is the same answer for both.
            case Timestamp:
                return ValueDate.FromDateValue(((ValueTimestamp)this).GetDateValue());

            case Varchar:
            case VarcharIgnorecase:
            case Char:
                return ValueDate.Parse((GetString() ?? string.Empty).Trim());

            case Null:
                throw DbException.GetInternalError();

            default:
                throw GetDataConversionErrorException(Date);
        }
    }

    private Value ConvertToGeometryType(TypeInfo targetType)
    {
        if (GetValueType() == Geometry)
        {
            return (ValueGeometry)this;
        }

        // Only the string family parses into a geometry: the reference does not convert a BLOB
        // to one, because a geometry is text with a type, not bytes.
        switch (GetValueType())
        {
            case Varchar:
            case VarcharIgnorecase:
            case Char:
                return ValueGeometry.Get(GetString() ?? string.Empty);

            case Null:
                throw DbException.GetInternalError();

            default:
                throw GetDataConversionErrorException(Geometry);
        }
    }

    private ValueUuid ConvertToUuidType()
    {
        int valueType = GetValueType();
        switch (valueType)
        {
            case Uuid:
                return (ValueUuid)this;

            case Binary:
            case Varbinary:
                return ValueUuid.Get(GetBytesNoCopy()!);

            case Char:
            case Varchar:
            case VarcharIgnorecase:
                return ValueUuid.Get(GetString() ?? string.Empty);

            case Null:
                throw DbException.GetInternalError();

            default:
                throw GetDataConversionErrorException(Uuid);
        }
    }

    private Value ConvertToArrayType(
        TypeInfo targetType,
        CastDataProvider? provider,
        int conversionMode,
        object? column)
    {
        TypeInfo componentType = (TypeInfo)targetType.GetExtTypeInfo()!;
        ValueArray v;
        if (GetValueType() == Array)
        {
            v = (ValueArray)this;
        }
        else
        {
            // A scalar becomes a one element array, and a LOB becomes one element array of the
            // bytes or the text it holds, because that is what the user means by an array of it.
            Value[] a;
            switch (GetValueType())
            {
                case Blob:
                    a = [ValueVarbinary.GetNoCopy(GetBytesNoCopy()!)];
                    break;

                case Clob:
                    a = [ValueVarchar.Get(GetString() ?? string.Empty)];
                    break;

                default:
                    a = [this];
                    break;
            }

            v = ValueArray.Get(a, provider);
        }

        if (componentType is not null)
        {
            Value[] values = v.GetList();
            int length = values.Length;
            for (int i = 0; i < length; i++)
            {
                Value v1 = values[i];
                Value v2 = v1.ConvertToValue(componentType, provider, conversionMode, column);
                if (!ReferenceEquals(v1, v2))
                {
                    // The first element that changed ends the walk: the rest are converted
                    // against the component type rather than against the previous element,
                    // because an element that already had it would be left as it is.
                    Value[] newValues = new Value[length];
                    System.Array.Copy(values, newValues, i);
                    newValues[i] = v2;
                    while (++i < length)
                    {
                        newValues[i] = values[i].ConvertToValue(componentType, provider, conversionMode, column);
                    }

                    v = ValueArray.Get(componentType, newValues, provider);
                    break;
                }
            }
        }

        if (conversionMode != ConvertTo)
        {
            Value[] values = v.GetList();
            int cardinality = values.Length;
            if (conversionMode == CastTo)
            {
                // A cast truncates an over-long array; assignment refuses it below.
                int p = MathUtils.ConvertLongToInt(targetType.GetPrecision());
                if (cardinality > p)
                {
                    v = ValueArray.Get(v.GetComponentType(), values[..p], provider);
                }
            }
            else if (cardinality > targetType.GetPrecision())
            {
                throw v.GetValueTooLongException(targetType, column);
            }
        }

        return v;
    }

    private Value ConvertToRow(
        TypeInfo targetType,
        CastDataProvider? provider,
        int conversionMode,
        object? column)
    {
        ValueRow v = GetValueType() == Row ? (ValueRow)this : ValueRow.Get([this]);
        if (targetType.GetExtTypeInfo() is ExtTypeInfoRow ext)
        {
            Value[] values = v.GetList();
            int length = values.Length;
            List<KeyValuePair<string, TypeInfo>> fields = [.. ext.GetFields()];
            if (length != fields.Count)
            {
                throw GetDataConversionErrorException(targetType);
            }

            for (int i = 0; i < length; i++)
            {
                Value v1 = values[i];
                TypeInfo componentType = fields[i].Value;
                Value v2 = v1.ConvertToValue(componentType, provider, conversionMode, column);
                if (!ReferenceEquals(v1, v2))
                {
                    // Same walk as the array's: the first element that changed is replaced and
                    // the rest are converted against the field types, not against each other.
                    Value[] newValues = new Value[length];
                    System.Array.Copy(values, newValues, i);
                    newValues[i] = v2;
                    while (++i < length)
                    {
                        newValues[i] = values[i].ConvertToValue(
                            fields[i].Value, provider, conversionMode, column);
                    }

                    v = ValueRow.Get(targetType, newValues);
                    break;
                }
            }
        }

        return v;
    }

    /// <summary>
    /// Converts a long to a byte, refusing a value that does not fit.
    /// </summary>
    /// <param name="x">the value</param>
    /// <param name="column">the column, for the error message</param>
    /// <returns>the signed byte</returns>
    private static sbyte ConvertToByteChecked(long x, object? column)
    {
        if (x > sbyte.MaxValue + 128 || x < sbyte.MinValue - 128)
        {
            throw GetOutOfRangeException(x.ToString(System.Globalization.CultureInfo.InvariantCulture), column);
        }

        return (sbyte)x;
    }

    /// <summary>
    /// Converts a long to a short, refusing a value that does not fit.
    /// </summary>
    /// <param name="x">the value</param>
    /// <param name="column">the column, for the error message</param>
    /// <returns>the short</returns>
    private static short ConvertToShortChecked(long x, object? column)
    {
        if (x > short.MaxValue || x < short.MinValue)
        {
            throw GetOutOfRangeException(x.ToString(System.Globalization.CultureInfo.InvariantCulture), column);
        }

        return (short)x;
    }

    /// <summary>
    /// Converts a long to an int, refusing a value that does not fit.
    /// </summary>
    /// <param name="x">the value</param>
    /// <param name="column">the column, for the error message</param>
    /// <returns>the int</returns>
    private static int ConvertToIntChecked(long x, object? column)
    {
        if (x > int.MaxValue || x < int.MinValue)
        {
            throw GetOutOfRangeException(x.ToString(System.Globalization.CultureInfo.InvariantCulture), column);
        }

        return (int)x;
    }

    /// <summary>
    /// Converts a double to a long, rounding to nearest and refusing a value outside the range.
    /// </summary>
    /// <remarks>
    /// A NaN answers zero, because a comparison of NaN with the limits is false both ways and
    /// the reference's <c>Math.round</c> says zero. Infinity is not caught by the range check
    /// either, and the reference's comment on this method admits it; both are inherited rather
    /// than repaired, because a repair here would differ from the reference on exactly the
    /// values this method is pinned for.
    /// </remarks>
    /// <param name="x">the value</param>
    /// <param name="column">the column, for the error message</param>
    /// <returns>the long</returns>
    private static long ConvertToLongChecked(double x, object? column)
    {
        if (x > long.MaxValue || x < long.MinValue)
        {
            throw GetOutOfRangeException(
                JavaNumberFormat.DoubleToString(x), column);
        }

        return (long)Math.Round(x, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Converts a decimal to a long, rounding half up and refusing a value outside the range.
    /// </summary>
    /// <param name="x">the value</param>
    /// <param name="column">the column, for the error message</param>
    /// <returns>the long</returns>
    public static long ConvertToLongChecked(BigDecimal x, object? column)
    {
        if (x.CompareTo(MaxLongDecimal) > 0 || x.CompareTo(MinLongDecimal) < 0)
        {
            throw GetOutOfRangeException(x.ToString(), column);
        }

        return (long)x.SetScale(0, RoundingMode.HalfUp).UnscaledValue;
    }

    /// <summary>
    /// The out of range error, naming the column when there is one.
    /// </summary>
    /// <param name="value">the value that did not fit</param>
    /// <param name="column">the column, if any</param>
    /// <returns>the error, which is not thrown by this method</returns>
    private static H2SharpException GetOutOfRangeException(string value, object? column)
        => column is null
            ? DbException.Get(ErrorCode.NumericValueOutOfRange1, value)
            : DbException.Get(
                ErrorCode.NumericValueOutOfRange2, value, column.ToString() ?? string.Empty);

    /// <summary>
    /// Reads four bytes as a big-endian int, which is how a four byte BINARY is an INTEGER.
    /// </summary>
    /// <param name="bytes">exactly four bytes</param>
    /// <returns>the int</returns>
    private static int ReadIntBigEndian(byte[] bytes)
    {
        uint value = 0;
        for (int i = 0; i < 4; i++)
        {
            value = (value << 8) | bytes[i];
        }

        return unchecked((int)value);
    }

    /// <summary>
    /// Reads eight bytes as a big-endian long, which is how an eight byte BINARY is a BIGINT.
    /// </summary>
    /// <param name="bytes">exactly eight bytes</param>
    /// <returns>the long</returns>
    private static long ReadLongBigEndian(byte[] bytes)
    {
        ulong value = 0;
        for (int i = 0; i < 8; i++)
        {
            value = (value << 8) | bytes[i];
        }

        return unchecked((long)value);
    }

    /// <summary>
    /// The too long error for this value against a target type, naming the column when there
    /// is one.
    /// </summary>
    /// <param name="targetType">the type the value did not fit</param>
    /// <param name="column">the column, if any</param>
    /// <returns>the error, which is not thrown by this method</returns>
    protected Exception GetValueTooLongException(TypeInfo targetType, object? column)
    {
        StringBuilder builder = new();
        if (column is not null)
        {
            _ = builder.Append(column).Append(' ');
        }

        _ = targetType.GetSql(builder, HasSql.TraceSqlFlags);
        return DbException.GetValueTooLongException(
            builder.ToString(), GetTraceSql(), GetTypeInfo().GetPrecision());
    }
}
