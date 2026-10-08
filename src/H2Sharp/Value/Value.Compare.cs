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

            // Two ENUMs are compared as their ordinals, but the ordinals are only comparable once both
            // are in the same list, so the list of whichever side is an ENUM wins and the other
            // side is looked up in it again. This used to refuse, which made ENUM the one
            // numeric type the comparison could not answer for.
            if (dataType == Value.Enum)
            {
                ExtTypeInfoEnum enumerators = ExtTypeInfoEnum.GetEnumeratorsForBinaryOperation(l, r);
                return Math.Sign(l.ConvertToEnum(enumerators, provider).GetInt()
                    - r.ConvertToEnum(enumerators, provider).GetInt());
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

            // The reference converts both sides to the type the comparison is going to be made
            // in, and then compares. Converting rather than adjusting the comparison is the whole
            // point: a CHAR compared against a longer string has to drop that string's padding
            // before the two are put side by side, and the side that asks cannot be the one that
            // does it.
            //
            // This is the general conversion and not the narrow one that used to be here, and
            // the difference is not a matter of taste: the reference calls convertTo, and a
            // narrower stand-in answers a different question for every pair the narrow one does
            // not know. An ARRAY of INTEGER compared with an ARRAY of VARCHAR reaches the
            // conversion through here, one element at a time, and an INTEGER against a VARCHAR
            // is a conversion the general family does and the narrow one refused.
            l = l.ConvertToForComparison(dataType, provider);
            r = r.ConvertToForComparison(dataType, provider);
        }

        return l.CompareTypeSafe(r, compareMode, provider);
    }

    /// <summary>
    /// Compare two numeric values, promoting them to the type both fit into.
    /// </summary>
    /// <remarks>
    /// The reference falls back to DECFLOAT for any pair that is not two integers or two
    /// reals, which is why that one is the widest exact type in the numeric family. The
    /// fallback is not ported: it needs <c>ValueDecfloat</c> and <c>ValueNumeric</c>, and until
    /// they exist a pair that is neither two integers nor two reals is compared as decimals, which
    /// is the one answer that does not have to choose a scale first. A pair that needs DECFLOAT,
    /// which is where the reference would go for two arbitrary precision values, still throws.
    /// <para>
    /// The two reals go through <c>GetDouble</c> and then the reference's own
    /// <c>Double.compare</c>, which is not the same order as .NET's: Java sorts NaN above every
    /// number and negative zero below positive zero, and a port that used the platform compare
    /// would answer differently on both.
    /// </para>
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
                case Value.Numeric:
                    // Everything that is not two integers or two reals lands here, and the
                    // reference compares the two as decimals rather than widening one of them:
                    // a REAL compared with a NUMERIC is decided at the NUMERIC's scale, so
                    // 0.1 and a REAL nearest to it are equal.
                    return l.GetBigDecimal().CompareTo(r.GetBigDecimal());
                case Value.Real:
                    return JavaCompareFloat(l.GetFloat(), r.GetFloat());
                case Value.Double:
                    return JavaCompareDouble(l.GetDouble(), r.GetDouble());
            }
        }

        // A pair that is not two numbers — an INTEGER and a VARCHAR, say — is compared as two
        // DECFLOATs. That is the reference's answer and it is the only one available: DECFLOAT
        // is the widest exact numeric type, so anything of any family reaches it, and a string
        // parses into one. The switch above cannot answer for such a pair, and refusing here
        // would make the whole type system refuse a comparison the engine is expected to make.
        //
        // The reference passes a null compare mode here and so, in effect, does this: the
        // comparison is between two decimals, and a compare mode decides nothing about decimals.
        // The default mode is passed rather than a nullable parameter, because making the
        // parameter nullable would have touched all twenty-four overrides to accept a value
        // none of them reads.
        return l.ConvertToDecfloat(null, ConvertTo).CompareTypeSafe(
            r.ConvertToDecfloat(null, ConvertTo), CompareMode.GetInstance(null, 0), null);
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
    /// <remarks>
    /// Not <c>final</c> in the reference, and that matters: <c>ValueCollectionBase</c>
    /// overrides it, because a comparison between two values of different collections is
    /// decided field by field and cannot go through the promotion this method would do. A
    /// scalar value has no reason to answer differently from the base and does not.
    /// </remarks>
    public virtual int CompareWithNull(
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

    /// <summary>
    /// Compares two doubles the way Java's <c>Double.compare</c> does.
    /// </summary>
    /// <remarks>
    /// Java sorts NaN above every number and negative zero below positive zero; .NET does the
    /// opposite on both. The same rule lives in <see cref="ValueDouble"/>, where it is the
    /// whole of that type's comparison, and it is repeated here because this path compares two
    /// values that may be of different types and neither of them has to be a double. A shared
    /// helper would be nicer; it is one screenful of logic and both places say why.
    /// </remarks>
    /// <param name="a">the first value</param>
    /// <param name="b">the second value</param>
    /// <returns>-1, 0 or 1 as Java's compare answers</returns>
    internal static int JavaCompareDouble(double a, double b)
    {
        if (double.IsNaN(a))
        {
            return double.IsNaN(b) ? 0 : 1;
        }

        if (double.IsNaN(b))
        {
            return -1;
        }

        // The value decides first, and the sign decides only when the values are equal, which is
        // how a negative zero comes out below a positive one without any other pair being affected.
        // Testing the sign before the value is the mistake: it answers for a zero and a non zero
        // without looking at the second of them, so a zero compared with anything but a zero came
        // out equal to it.
        if (a < b)
        {
            return -1;
        }

        if (a > b)
        {
            return 1;
        }

        bool negativeA = double.IsNegative(a), negativeB = double.IsNegative(b);
        if (negativeA == negativeB)
        {
            return 0;
        }

        return negativeA ? -1 : 1;
    }

    /// <summary>
    /// Compares two floats the way Java's <c>Float.compare</c> does, with the same two rules
    /// as <see cref="JavaCompareDouble"/>.
    /// </summary>
    /// <param name="a">the first value</param>
    /// <param name="b">the second value</param>
    /// <returns>-1, 0 or 1 as Java's compare answers</returns>
    internal static int JavaCompareFloat(float a, float b)
    {
        if (float.IsNaN(a))
        {
            return float.IsNaN(b) ? 0 : 1;
        }

        if (float.IsNaN(b))
        {
            return -1;
        }

        // Same order of decisions as JavaCompareDouble, and the same reason: the value first, the
        // sign only when the values are equal.
        if (a < b)
        {
            return -1;
        }

        if (a > b)
        {
            return 1;
        }

        bool negativeA = float.IsNegative(a), negativeB = float.IsNegative(b);
        if (negativeA == negativeB)
        {
            return 0;
        }

        return negativeA ? -1 : 1;
    }

    private static NotSupportedException NotPortedConversion(string op, string type)
        => new(
            $"Converting for {op} to {type} needs a value type that is not ported yet. "
            + "See MIGRATION_STATUS.md.")
        {
        };
}
