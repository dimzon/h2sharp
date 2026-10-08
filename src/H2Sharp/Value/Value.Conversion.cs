/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.Value.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The part of the reference's convertTo that the comparison needs.
 *
 * The reference has one convertTo with three modes, CONVERT_TO, CAST_TO and ASSIGN_TO, and a long
 * switch over every type. Only the string and byte array families are ported so far, so this is
 * the narrow version of it: given the type the comparison is going to happen in, put both values
 * into it and hand them back. The general method arrives with the remaining types, and when it
 * does this is where its mode parameter will live.
 */
using System;
using H2Sharp.Engine;

namespace H2Sharp.Values;

/// <summary>
/// A value of some type. Every value knows its type number, and every type knows how to
/// describe itself as SQL.
/// </summary>
public abstract partial class Value
{
    /// <summary>
    /// Puts both values into the type their comparison is going to be made in.
    /// </summary>
    /// <remarks>
    /// The bytes of the two binary types are the same bytes, so nothing has to happen there; the
    /// string types do have to be changed, because the conversion to a fixed length type drops
    /// trailing spaces and that is the difference between 'a' and 'a ' being equal.
    /// </remarks>
    /// <param name="l">the left value</param>
    /// <param name="r">the right value</param>
    /// <param name="dataType">the type number both values are converted to</param>
    /// <param name="provider">the cast information provider</param>
    /// <returns>both values, converted</returns>
    private static (Value Left, Value Right) ConvertToForComparison(
        Value l,
        Value r,
        int dataType,
        CastDataProvider? provider)
    {
        switch (dataType)
        {
            case Char:
                return (ConvertToChar(l), ConvertToChar(r));
            case Varchar:
            case VarcharIgnorecase:
                return (ConvertToVarchar(l), ConvertToVarchar(r));
            case Binary:
            case Varbinary:
                return (ConvertToBinary(l), ConvertToBinary(r));
            default:
                throw NotPortedConversion("compare", GetTypeName(dataType));
        }
    }

    /// <summary>
    /// Converts a value to CHARACTER, which is to say: drops the trailing spaces that the padding
    /// of a fixed length type consists of.
    /// </summary>
    /// <param name="v">the value</param>
    /// <returns>the value as a CHAR</returns>
    private static Value ConvertToChar(Value v)
    {
        if (v.GetValueType() == Char)
        {
            return v;
        }

        string? s = v.GetString();
        if (s is null)
        {
            throw NotPortedConversion("convert", GetTypeName(v.GetValueType()));
        }

        int end = s.Length;
        while (end > 0 && s[end - 1] == ' ')
        {
            end--;
        }

        return end == s.Length ? ValueChar.Get(s) : ValueChar.Get(s[..end]);
    }

    /// <summary>
    /// Converts a value to VARCHAR, which every string value already is.
    /// </summary>
    /// <param name="v">the value</param>
    /// <returns>the value as a VARCHAR</returns>
    private static Value ConvertToVarchar(Value v)
    {
        int valueType = v.GetValueType();
        if (valueType == Varchar || valueType == Char)
        {
            return v;
        }

        throw NotPortedConversion("convert", GetTypeName(valueType));
    }

    /// <summary>
    /// Converts a value to a byte array type, which every byte array value already is.
    /// </summary>
    /// <param name="v">the value</param>
    /// <returns>the value as a binary value</returns>
    private static Value ConvertToBinary(Value v)
    {
        int valueType = v.GetValueType();
        if (valueType == Binary || valueType == Varbinary)
        {
            return v;
        }

        throw NotPortedConversion("convert", GetTypeName(valueType));
    }
}
