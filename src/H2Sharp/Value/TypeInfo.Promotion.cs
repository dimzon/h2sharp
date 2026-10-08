/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.TypeInfo.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using H2Sharp.Api;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// The promotion rules: which of two types wins, and whether two may even be compared.
/// </summary>
/// <remarks>
/// <para>
/// This is the part of the type system an expression evaluator lives on. Every arithmetic
/// operator, every <c>INSERT</c> and every <c>IN</c> list has to reduce its operands to one
/// type, and the reduction is not "pick the wider one": precision and scale move, a narrower
/// decimal grows its scale to fit, and a row of fields is promoted field by field. So the
/// rules are ported whole and tested over pairs rather than sampled.
/// </para>
/// <para>
/// The GEOMETRY case is the one gap: it needs <c>ExtTypeInfoGeometry</c>, which drags in the
/// whole spatial package, so a geometry that carries an extended type fails loudly here. A
/// plain GEOMETRY still promotes correctly.
/// </para>
/// </remarks>
public sealed partial class TypeInfo
{
    /// <summary>
    /// Get the higher data type of two data types. If values need to be
    /// converted to match the other operand's data type, the value with the lower
    /// order is converted to the value with the higher order.
    /// </summary>
    /// <param name="type1">the first data type</param>
    /// <param name="type2">the second data type</param>
    /// <returns>the higher data type of the two</returns>
    /// <exception cref="H2SharpException">
    /// if the two cannot be combined, which includes the two ways an unknown type can be
    /// rejected: against another unknown, and against NULL
    /// </exception>
    public static TypeInfo GetHigherType(TypeInfo type1, TypeInfo type2)
    {
        int t1 = type1.valueType, t2 = type2.valueType, dataType;
        if (t1 == t2)
        {
            if (t1 == Value.Unknown)
            {
                throw DbException.Get(ErrorCode.UnknownDataType1, "?, ?");
            }

            dataType = t1;
        }
        else
        {
            if (t1 < t2)
            {
                (t1, t2) = (t2, t1);
                (type1, type2) = (type2, type1);
            }

            if (t1 == Value.Unknown)
            {
                if (t2 == Value.Null)
                {
                    throw DbException.Get(ErrorCode.UnknownDataType1, "?, NULL");
                }

                return type2;
            }
            else if (t2 == Value.Unknown)
            {
                if (t1 == Value.Null)
                {
                    throw DbException.Get(ErrorCode.UnknownDataType1, "NULL, ?");
                }

                return type1;
            }

            if (t2 == Value.Null)
            {
                return type1;
            }

            dataType = Value.GetHigherOrderKnown(t1, t2);
        }

        long precision;
        switch (dataType)
        {
            case Value.Numeric:
                {
                    TypeInfo numeric1 = type1.ToNumericType();
                    TypeInfo numeric2 = type2.ToNumericType();
                    long precision1 = numeric1.GetPrecision(), precision2 = numeric2.GetPrecision();
                    int scale1 = numeric1.GetScale(), scale2 = numeric2.GetScale(), scale;
                    if (scale1 < scale2)
                    {
                        // The narrower scale cannot hold the wider one's digits, so the extra
                        // room is taken out of the precision instead of being dropped.
                        precision1 += scale2 - scale1;
                        scale = scale2;
                    }
                    else
                    {
                        precision2 += scale1 - scale2;
                        scale = scale1;
                    }

                    return GetTypeInfo(
                        Value.Numeric,
                        Math.Max(precision1, precision2),
                        scale,
                        null);
                }

            case Value.Real:
            case Value.Double:
                precision = -1L;
                break;

            case Value.Geometry:
                return GetHigherGeometry(type1, type2);

            case Value.Array:
                return GetHigherArray(
                    type1, type2, Dimensions(type1), Dimensions(type2));

            case Value.Row:
                return GetHigherRow(type1, type2);

            default:
                precision = Math.Max(type1.GetPrecision(), type2.GetPrecision());
                break;
        }

        ExtTypeInfo? ext1 = type1.extTypeInfo;
        return GetTypeInfo(
            dataType,
            precision,
            Math.Max(type1.GetScale(), type2.GetScale()),
            dataType == t1 && ext1 is not null
                ? ext1
                : dataType == t2 ? type2.extTypeInfo : null);
    }

    /// <summary>
    /// Get the higher data type of a list of types.
    /// </summary>
    /// <remarks>
    /// The reference takes <c>Typed[]</c> here, and this takes <see cref="TypeInfo"/>: the
    /// interface is not ported, and a value's type is a type, so nothing is lost. UNKNOWN and
    /// NULL are counted rather than promoted, which is what makes <c>(NULL, ?)</c> an error
    /// while <c>(?, 1)</c> is not.
    /// </remarks>
    /// <param name="types">the types</param>
    /// <returns>the higher data type of the list, or NULL for an empty one</returns>
    /// <exception cref="H2SharpException">
    /// if the list mixes an unknown type with NULL, or the types cannot be combined
    /// </exception>
    public static TypeInfo GetHigherType(TypeInfo[] types)
    {
        int cardinality = types.Length;
        if (cardinality == 0)
        {
            return TypeNull;
        }

        TypeInfo type = types[0];
        bool hasUnknown = false, hasNull = false;
        switch (type.valueType)
        {
            case Value.Unknown:
                hasUnknown = true;
                break;
            case Value.Null:
                hasNull = true;
                break;
        }

        for (int i = 1; i < cardinality; i++)
        {
            TypeInfo t = types[i];
            switch (t.valueType)
            {
                case Value.Unknown:
                    hasUnknown = true;
                    break;
                case Value.Null:
                    hasNull = true;
                    break;
                default:
                    type = GetHigherType(type, t);
                    break;
            }
        }

        if (type.valueType <= Value.Null && hasUnknown)
        {
            throw DbException.Get(
                ErrorCode.UnknownDataType1, hasNull ? "NULL, ?" : "?");
        }

        return type;
    }

    /// <summary>
    /// Get the higher data type of all values, which is how an ARRAY without a declared
    /// component type decides what its elements have in common.
    /// </summary>
    /// <remarks>
    /// The reference takes <c>Typed[]</c> and asks each value for its own type. This takes the
    /// values, because a <c>Value</c> already is one and nothing here would accept a type
    /// instead. The rest of the method is <see cref="GetHigherType(TypeInfo[])"/>, which asks
    /// the same question of a list of types that the caller already has.
    /// </remarks>
    /// <param name="values">the values</param>
    /// <returns>the higher data type</returns>
    /// <exception cref="H2SharpException">
    /// if the list mixes an unknown type with NULL, or the types cannot be combined
    /// </exception>
    public static TypeInfo GetHigherType(Value[] values)
    {
        int cardinality = values.Length;
        if (cardinality == 0)
        {
            return TypeNull;
        }

        // Asking every value for its type costs one virtual call per element, and a list of
        // values is always asked once, so the array is built rather than the loop being
        // unrolled around it.
        TypeInfo[] types = new TypeInfo[cardinality];
        for (int i = 0; i < cardinality; i++)
        {
            types[i] = values[i].GetTypeInfo();
        }

        return GetHigherType(types);
    }

    /// <summary>
    /// Checks whether two specified types are comparable and throws an exception
    /// otherwise.
    /// </summary>
    /// <param name="t1">first data type</param>
    /// <param name="t2">second data type</param>
    /// <exception cref="H2SharpException">if types aren't comparable</exception>
    public static void CheckComparable(TypeInfo t1, TypeInfo t2)
    {
        if (!AreComparable(t1, t2))
        {
            throw DbException.Get(ErrorCode.TypesAreNotComparable2, t1.GetTraceSql(), t2.GetTraceSql());
        }
    }

    /// <summary>
    /// Determines whether two specified types are comparable.
    /// </summary>
    /// <remarks>
    /// Grouped exactly as the reference groups it, including the cases that read like
    /// special cases: a DATE is not comparable with a TIME, and a collection is comparable
    /// with nothing but its own kind.
    /// </remarks>
    /// <param name="t1">first data type</param>
    /// <param name="t2">second data type</param>
    /// <returns>whether types are comparable</returns>
    private static bool AreComparable(TypeInfo t1, TypeInfo t2)
    {
        TypeInfo unwrapped1 = t1.UnwrapRow(), unwrapped2 = t2.UnwrapRow();
        int vt1 = unwrapped1.valueType, vt2 = unwrapped2.valueType;
        if (vt1 > vt2)
        {
            (vt1, vt2) = (vt2, vt1);
            (unwrapped1, unwrapped2) = (unwrapped2, unwrapped1);
        }

        if (vt1 <= Value.Null)
        {
            return true;
        }

        if (vt1 == vt2)
        {
            switch (vt1)
            {
                case Value.Array:
                    return AreComparable(
                        (TypeInfo)unwrapped1.extTypeInfo!,
                        (TypeInfo)unwrapped2.extTypeInfo!);

                case Value.Row:
                    {
                        List<KeyValuePair<string, TypeInfo>> f1 =
                            [.. ((ExtTypeInfoRow)unwrapped1.extTypeInfo!).GetFields()];
                        List<KeyValuePair<string, TypeInfo>> f2 =
                            [.. ((ExtTypeInfoRow)unwrapped2.extTypeInfo!).GetFields()];
                        if (f1.Count != f2.Count)
                        {
                            return false;
                        }

                        for (int i = 0; i < f1.Count; i++)
                        {
                            if (!AreComparable(f1[i].Value, f2[i].Value))
                            {
                                return false;
                            }
                        }

                        goto default;
                    }

                default:
                    return true;
            }
        }

        byte g1 = Value.GroupOf(vt1), g2 = Value.GroupOf(vt2);
        if (g1 == g2)
        {
            switch (g1)
            {
                case Value.GroupOther:
                case Value.GroupCollection:
                    return false;
                case Value.GroupDatetime:
                    // A DATE is not comparable with a TIME: the two mean different instants
                    // and the reference refuses the pair rather than guessing an order.
                    return vt1 != Value.Date || vt2 != Value.Time && vt2 != Value.TimeTz;
                default:
                    return true;
            }
        }

        switch (g1)
        {
            case Value.GroupCharacterString:
                switch (g2)
                {
                    case Value.GroupNumeric:
                    case Value.GroupDatetime:
                    case Value.GroupIntervalYm:
                    case Value.GroupIntervalDt:
                        return true;
                    case Value.GroupOther:
                        switch (vt2)
                        {
                            case Value.Enum:
                            case Value.Geometry:
                            case Value.Json:
                            case Value.Uuid:
                                return true;
                            default:
                                return false;
                        }
                    default:
                        return false;
                }

            case Value.GroupBinaryString:
                switch (vt2)
                {
                    case Value.JavaObject:
                    case Value.Geometry:
                    case Value.Json:
                    case Value.Uuid:
                        return true;
                    default:
                        return false;
                }
        }

        return false;
    }

    /// <summary>
    /// Determines whether two specified types have the same ordering rules.
    /// </summary>
    /// <param name="t1">first data type</param>
    /// <param name="t2">second data type</param>
    /// <returns>whether types are comparable</returns>
    public static bool HaveSameOrdering(TypeInfo t1, TypeInfo t2)
    {
        TypeInfo a = t1.UnwrapRow(), b = t2.UnwrapRow();
        int vt1 = a.valueType, vt2 = b.valueType;
        if (vt1 > vt2)
        {
            (vt1, vt2) = (vt2, vt1);
            (a, b) = (b, a);
        }

        if (vt1 <= Value.Null)
        {
            return true;
        }

        if (vt1 == vt2)
        {
            switch (vt1)
            {
                case Value.Array:
                    return HaveSameOrdering(
                        (TypeInfo)a.extTypeInfo!,
                        (TypeInfo)b.extTypeInfo!);

                case Value.Row:
                    {
                        List<KeyValuePair<string, TypeInfo>> f1 =
                            [.. ((ExtTypeInfoRow)a.extTypeInfo!).GetFields()];
                        List<KeyValuePair<string, TypeInfo>> f2 =
                            [.. ((ExtTypeInfoRow)b.extTypeInfo!).GetFields()];
                        if (f1.Count != f2.Count)
                        {
                            return false;
                        }

                        for (int i = 0; i < f1.Count; i++)
                        {
                            if (!HaveSameOrdering(f1[i].Value, f2[i].Value))
                            {
                                return false;
                            }
                        }

                        goto default;
                    }

                default:
                    return true;
            }
        }

        byte g1 = Value.GroupOf(vt1), g2 = Value.GroupOf(vt2);
        if (g1 == g2)
        {
            switch (g1)
            {
                case Value.GroupCharacterString:
                    // Two case sensitive strings order alike, and two case insensitive ones
                    // do, but a mix does not: 'A' and 'a' would compare differently.
                    return (vt1 == Value.VarcharIgnorecase) == (vt2 == Value.VarcharIgnorecase);
                case Value.GroupDatetime:
                    switch (vt1)
                    {
                        case Value.Date:
                            return vt2 is Value.Timestamp or Value.TimestampTz;
                        case Value.Time:
                        case Value.TimeTz:
                            return vt2 is Value.Time or Value.TimeTz;
                        default:
                            // TIMESTAMP against TIMESTAMP or TIMESTAMP_TZ
                            return true;
                    }
                case Value.GroupOther:
                case Value.GroupCollection:
                    return false;
                default:
                    return true;
            }
        }

        if (g1 == Value.GroupBinaryString)
        {
            switch (vt2)
            {
                case Value.JavaObject:
                case Value.Geometry:
                case Value.Json:
                case Value.Uuid:
                    return true;
                default:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// If this is a row with a single field, the type of that field; otherwise this type.
    /// </summary>
    /// <remarks>
    /// Unwrapping is what lets a row of one column be compared as that column, and it is
    /// applied recursively, so a row of a row of one column is the column's type. This is
    /// public in the reference because callers outside the value package need it.
    /// </remarks>
    /// <returns>the unwrapped type</returns>
    public TypeInfo UnwrapRow()
    {
        if (valueType == Value.Row)
        {
            List<KeyValuePair<string, TypeInfo>> fields =
                [.. ((ExtTypeInfoRow)extTypeInfo!).GetFields()];
            if (fields.Count == 1)
            {
                return fields[0].Value.UnwrapRow();
            }
        }

        return this;
    }

    /// <summary>
    /// The number of array dimensions, counting nested arrays.
    /// </summary>
    /// <param name="type">the type</param>
    /// <returns>the number of dimensions</returns>
    private static int Dimensions(TypeInfo type)
    {
        int result;
        for (result = 0; type.valueType == Value.Array; result++)
        {
            type = (TypeInfo)type.extTypeInfo!;
        }

        return result;
    }

    private static TypeInfo GetHigherArray(TypeInfo type1, TypeInfo type2, int d1, int d2)
    {
        long precision;
        if (d1 > d2)
        {
            d1--;
            precision = Math.Max(type1.GetPrecision(), 1L);
            type1 = (TypeInfo)type1.extTypeInfo!;
        }
        else if (d1 < d2)
        {
            d2--;
            precision = Math.Max(1L, type2.GetPrecision());
            type2 = (TypeInfo)type2.extTypeInfo!;
        }
        else if (d1 > 0)
        {
            d1--;
            d2--;
            precision = Math.Max(type1.GetPrecision(), type2.GetPrecision());
            type1 = (TypeInfo)type1.extTypeInfo!;
            type2 = (TypeInfo)type2.extTypeInfo!;
        }
        else
        {
            // Neither is an array any more, so what is left is the element type.
            return GetHigherType(type1, type2);
        }

        return GetTypeInfo(
            Value.Array, precision, 0, GetHigherArray(type1, type2, d1, d2));
    }

    private static TypeInfo GetHigherRow(TypeInfo type1, TypeInfo type2)
    {
        if (type1.valueType != Value.Row)
        {
            type1 = TypeToRow(type1);
        }

        if (type2.valueType != Value.Row)
        {
            type2 = TypeToRow(type2);
        }

        ExtTypeInfoRow ext1 = (ExtTypeInfoRow)type1.extTypeInfo!;
        ExtTypeInfoRow ext2 = (ExtTypeInfoRow)type2.extTypeInfo!;
        if (ext1.Equals(ext2))
        {
            return type1;
        }

        List<KeyValuePair<string, TypeInfo>> m1 = [.. ext1.GetFields()];
        List<KeyValuePair<string, TypeInfo>> m2 = [.. ext2.GetFields()];
        int degree = m1.Count;
        if (m2.Count != degree)
        {
            throw DbException.Get(ErrorCode.ColumnCountDoesNotMatch);
        }

        OrderedStringMap<TypeInfo> m = new((int)Math.Ceiling(degree / .75));
        for (int i = 0; i < degree; i++)
        {
            m.Put(m1[i].Key, GetHigherType(m1[i].Value, m2[i].Value));
        }

        return GetTypeInfo(Value.Row, 0, 0, new ExtTypeInfoRow(m));
    }

    private static TypeInfo TypeToRow(TypeInfo type)
    {
        OrderedStringMap<TypeInfo> map = new(2);
        map.Put("C1", type);
        return GetTypeInfo(Value.Row, 0, 0, new ExtTypeInfoRow(map));
    }

    /// <summary>
    /// The higher of two geometry types.
    /// </summary>
    /// <remarks>
    /// Only the part that needs no spatial type is ported: a geometry without an extended
    /// type promotes to the plain geometry singleton. A geometry that carries its dimension
    /// and SRID cannot be promoted without <c>ExtTypeInfoGeometry</c>, which would pull in
    /// the whole <c>org.h2.util.geometry</c> package, so that case fails loudly instead of
    /// quietly returning the plain geometry, which would silently drop the SRID.
    /// </remarks>
    private static TypeInfo GetHigherGeometry(TypeInfo type1, TypeInfo type2)
    {
        ExtTypeInfo? ext1 = type1.GetExtTypeInfo(), ext2 = type2.GetExtTypeInfo();
        if (ext1 is not null)
        {
            throw new NotSupportedException(
                "Promoting a geometry that has a dimension and an SRID needs "
                + "ExtTypeInfoGeometry, which is not ported yet. See MIGRATION_STATUS.md.");
        }

        if (ext2 is not null)
        {
            throw new NotSupportedException(
                "Promoting a geometry that has a dimension and an SRID needs "
                + "ExtTypeInfoGeometry, which is not ported yet. See MIGRATION_STATUS.md.");
        }

        // Neither carries spatial parameters, so the answer is the plain geometry, and the
        // reference reaches the same conclusion through its last branch.
        return TypeGeometry;
    }
}
