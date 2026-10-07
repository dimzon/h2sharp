/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.Value.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Comparing two values.
 *
 * This is where the type system earns its keep: comparing values of different types means
 * deciding which type can hold both, converting, and only then comparing. The three steps are
 * kept as three steps, because skipping the middle one is exactly the bug that makes an
 * engine's comparisons disagree with its own types.
 *
 * Ported so far: identity, NULL, same type, and the numeric promotion between integers. The
 * rest of the reference dispatches on conversions that need value types which are not ported,
 * and those paths throw with a reason instead of returning a plausible answer.
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
    /// Compare this value to another value.
    /// </summary>
    /// <param name="v">the other value</param>
    /// <param name="provider">the cast information provider</param>
    /// <param name="compareMode">the compare mode</param>
    /// <returns>0 if both values are equal, -1 if this value is smaller, 1 if it is larger</returns>
    /// <remarks>
    /// The reference declares this <c>final</c>, that is, not overridable. C# has no modifier
    /// for that on a method that is not virtual, so it is simply not virtual here: a derived
    /// class would have to declare <c>new</c> to shadow it, which is as visible as it is in
    /// Java.
    /// </remarks>
    public int CompareTo(Value? v, CastDataProvider? provider, CompareMode compareMode)
    {
        if (ReferenceEquals(this, v))
        {
            return 0;
        }

        if (ReferenceEquals(this, ValueNull.Instance))
        {
            return -1;
        }
        else if (ReferenceEquals(v, ValueNull.Instance))
        {
            return 1;
        }

        return CompareToNotNullable(this, v!, provider, compareMode);
    }

    /// <summary>
    /// Compare two values, neither of which is NULL.
    /// </summary>
    /// <remarks>
    /// Two values of the same type are compared directly. Two of different types are promoted
    /// to a type that can hold both, converted to it, and then compared, so that the answer
    /// agrees with the answer the type system gives.
    /// </remarks>
    /// <param name="l">the left value</param>
    /// <param name="r">the right value</param>
    /// <param name="provider">the cast information provider</param>
    /// <param name="compareMode">the compare mode</param>
    /// <returns>0 if both values are equal, -1 if the left is smaller, 1 if it is larger</returns>
    private static int CompareToNotNullable(
        Value l,
        Value r,
        CastDataProvider? provider,
        CompareMode compareMode)
    {
        int leftType = l.GetValueType();
        int rightType = r.GetValueType();
        if (leftType != rightType || leftType == Value.Enum)
        {
            int dataType = GetHigherOrderNonNull(leftType, rightType);
            if (IsNumericType(dataType))
            {
                return CompareNumeric(l, r, leftType, rightType, dataType);
            }

            if (dataType == Value.Enum)
            {
                throw new NotSupportedException(
                    "Comparing two ENUM values needs the ENUM conversion, which is not ported "
                    + "yet. See MIGRATION_STATUS.md.")
                {
                };
            }

            if (dataType <= Value.Blob)
            {
                if (dataType <= Value.Clob)
                {
                    // A CHAR and a longer string promote to CHAR, so that the comparison is
                    // the shorter one's: otherwise a CHAR would compare against a padded
                    // CLOB and disagree with what the user sees.
                    if (leftType == Value.Char || rightType == Value.Char)
                    {
                        dataType = Value.Char;
                    }
                }
                else if (dataType >= Value.Binary && (leftType == Value.Binary || rightType == Value.Binary))
                {
                    dataType = Value.Binary;
                }
            }

            throw NotPortedConversion("compare", Value.GetTypeName(dataType));
        }

        return l.CompareTypeSafe(r, compareMode, provider);
    }

    /// <summary>
    /// Compare two numeric values, promoting them to the type both fit into.
    /// </summary>
    /// <remarks>
    /// The reference falls back to DECFLOAT for any pair that is not two integers or two
    /// reals, which is why this one is the widest exact type in the numeric family. That
    /// fallback is not ported: it needs <c>ValueDecfloat</c> and <c>ValueNumeric</c>, and
    /// until they exist the mixed pairs below throw rather than answer approximately.
    /// </remarks>
    /// <param name="l">the left value</param>
    /// <param name="r">the right value</param>
    /// <param name="leftType">the type number of the left value</param>
    /// <param name="rightType">the type number of the right value</param>
    /// <param name="dataType">the type number both values are promoted to</param>
    /// <returns>0 if both values are equal, -1 if the left is smaller, 1 if it is larger</returns>
    private static int CompareNumeric(
        Value l,
        Value r,
        int leftType,
        int rightType,
        int dataType)
    {
        if (IsNumericType(leftType) && IsNumericType(rightType))
        {
            switch (dataType)
            {
                case Value.Integer:
                    return Math.Sign(l.GetInt() - (long)r.GetInt());
                case Value.Bigint:
                    return l.GetLong().CompareTo(r.GetLong());
                case Value.Real:
                    return l.GetFloat().CompareTo(r.GetFloat());
                case Value.Double:
                    return l.GetDouble().CompareTo(r.GetDouble());
            }
        }

        throw new NotSupportedException(
            $"Comparing {Value.GetTypeName(leftType)} with {Value.GetTypeName(rightType)} needs "
            + "the DECFLOAT conversion, which is not ported yet. See MIGRATION_STATUS.md.")
        {
        };
    }

    /// <summary>
    /// Compare this value against another value using the specified compare mode.
    /// </summary>
    /// <param name="v">the other value</param>
    /// <param name="forEquality">perform only check for equality</param>
    /// <param name="provider">the cast information provider</param>
    /// <param name="compareMode">the compare mode</param>
    /// <returns>
    /// 0 if both values are equal, -1 if this value is smaller, 1 if other value is larger,
    /// <see cref="int.MinValue"/> if order is not defined due to NULL comparison
    /// </returns>
    public int CompareWithNull(
        Value? v,
        bool forEquality,
        CastDataProvider? provider,
        CompareMode compareMode)
    {
        if (ReferenceEquals(this, ValueNull.Instance) || ReferenceEquals(v, ValueNull.Instance))
        {
            return int.MinValue;
        }

        return CompareToNotNullable(this, v!, provider, compareMode);
    }

    /// <summary>
    /// Whether the type number is one of the numeric types.
    /// </summary>
    /// <remarks>
    /// The reference asks <c>DataType.isNumericType</c>, which is exactly this range check
    /// from TINYINT through DECFLOAT. It is stated here rather than waiting for the whole
    /// DataType registry, because the comparison path needs the answer and the registry has
    /// 772 lines of names this one line does not use.
    /// </remarks>
    /// <param name="type">the type number</param>
    /// <returns>whether the type is numeric</returns>
    internal static bool IsNumericType(int type) => type >= Value.Tinyint && type <= Value.Decfloat;

    private static NotSupportedException NotPortedConversion(string op, string type)
        => new(
            $"Converting for {op} to {type} needs a value type that is not ported yet. "
            + "See MIGRATION_STATUS.md.")
        {
        };
}
