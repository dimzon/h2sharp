/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueReal.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// Implementation of the REAL data type.
/// </summary>
public sealed class ValueReal : Value
{
    /// <summary>
    /// The precision in bits.
    /// </summary>
    internal const int Precision = 24;

    /// <summary>
    /// The approximate precision in decimal digits.
    /// </summary>
    public const int DecimalPrecision = 7;

    /// <summary>
    /// The maximum display size of a REAL, for example -1.12345676E-20.
    /// </summary>
    public const int DisplaySize = 15;

    /// <summary>
    /// The bit pattern of the zero float, which the reference keeps because a caller compares
    /// against it.
    /// </summary>
    public static readonly int ZeroBits = 0;

    /// <summary>
    /// The value zero.
    /// </summary>
    public static readonly ValueReal Zero = new(0f);

    /// <summary>
    /// The value one.
    /// </summary>
    public static readonly ValueReal One = new(1f);

    private static readonly ValueReal Nan = new(BitConverter.Int32BitsToSingle(JavaNanBits));

    /// <summary>
    /// Java's <c>Float.NaN</c> bit pattern.
    /// </summary>
    /// <remarks>
    /// .NET's NaN has the sign bit set and Java's does not, so the two agree that the value is
    /// NaN and disagree about its bits. This type's hash is the bit pattern, which makes the
    /// difference visible where every other type's hash is an identity hash nobody can compare. The
    /// singleton is therefore built from Java's bits, which is the same answer the DOUBLE type
    /// gives.
    /// </remarks>
    private const int JavaNanBits = unchecked((int)0x7FC00000);

    private readonly float value;

    private ValueReal(float v)
    {
        value = v;
    }

    /// <inheritdoc/>
    public override Value Add(Value v) => Get(value + ((ValueReal)v).value);

    /// <inheritdoc/>
    public override Value Subtract(Value v) => Get(value - ((ValueReal)v).value);

    /// <inheritdoc/>
    public override Value Negate() => Get(-value);

    /// <inheritdoc/>
    public override Value Multiply(Value v) => Get(value * ((ValueReal)v).value);

    /// <inheritdoc/>
    public override Value Divide(Value v, TypeInfo quotientType)
    {
        float divisor = ((ValueReal)v).value;
        if (divisor == 0.0f)
        {
            throw DbException.Get(ErrorCode.DivisionByZero1, GetSql(new StringBuilder(), 0).ToString());
        }

        return Get(value / divisor);
    }

    /// <inheritdoc/>
    public override Value Modulus(Value v)
    {
        float divisor = ((ValueReal)v).value;
        if (divisor == 0f)
        {
            throw DbException.Get(ErrorCode.DivisionByZero1, GetSql(new StringBuilder(), 0).ToString());
        }

        return Get(value % divisor);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A float literal is a REAL or a DOUBLE depending on what follows it, so the reference says
    /// which one this is with a CAST unless the caller says it already knows.
    /// </remarks>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        if ((sqlFlags & HasSql.NoCasts) == 0)
        {
            return AppendNumber(builder.Append("CAST(")).Append(" AS REAL)");
        }

        return AppendNumber(builder);
    }

    /// <summary>
    /// Writes the number, and the three values that are not numbers as the quoted words the
    /// reference uses.
    /// </summary>
    /// <remarks>
    /// Infinity and NaN are not numbers and cannot be written as literals, so the reference quotes
    /// them as text and lets the conversion back do the work. A .NET formatter would print
    /// "Infinity" and "NaN" the same way but without the quotes, which would be a syntax error in a
    /// statement that has to run again.
    /// </remarks>
    /// <param name="builder">the builder to append to</param>
    /// <returns>the same builder</returns>
    private StringBuilder AppendNumber(StringBuilder builder)
    {
        if (float.IsPositiveInfinity(value))
        {
            return builder.Append("'Infinity'");
        }

        if (float.IsNegativeInfinity(value))
        {
            return builder.Append("'-Infinity'");
        }

        if (float.IsNaN(value))
        {
            return builder.Append("'NaN'");
        }

        return builder.Append(JavaNumberFormat.FloatToString(value));
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => TypeInfo.TypeReal;

    /// <inheritdoc/>
    public override int GetValueType() => Value.Real;

    /// <inheritdoc/>
    public override int CompareTypeSafe(Value o, CompareMode mode, CastDataProvider? provider)
        => JavaCompareFloat(value, ((ValueReal)o).value);

    /// <inheritdoc/>
    /// <remarks>
    /// NaN has no sign at all, so its sign is zero rather than negative, which is the reference's
    /// answer and the reason a NaN sorts above every number while comparing as neither positive nor
    /// negative.
    /// </remarks>
    public override int GetSignum() => value == 0 || float.IsNaN(value) ? 0 : value < 0 ? -1 : 1;

    /// <inheritdoc/>
    public override H2Sharp.Util.BigDecimal GetBigDecimal()
    {
        if (float.IsFinite(value))
        {
            // The decimal the float prints as, rather than the exact value of the float: a decimal
            // built from the bits would be the number the float stands for and not the number it
            // was written as, and the reference says so in a comment.
            return H2Sharp.Util.BigDecimal.Parse(JavaNumberFormat.FloatToString(value));
        }

        throw DbException.Get(
            ErrorCode.DataConversionError1, JavaNumberFormat.FloatToString(value));
    }

    /// <inheritdoc/>
    public override float GetFloat() => value;

    /// <inheritdoc/>
    public override double GetDouble() => value;

    /// <inheritdoc/>
    public override string? GetString() => JavaNumberFormat.FloatToString(value);

    /// <inheritdoc/>
    /// <remarks>
    /// Unlike every other type here, this hash is a reproducible number: the reference uses the raw
    /// bit pattern of the float, and the raw bits are what .NET gives as well. NaN is normalised
    /// when the value is created, so the one NaN instance cannot answer two different patterns.
    /// </remarks>
    public override int GetHashCode() => BitConverter.SingleToInt32Bits(value);

    /// <summary>
    /// Get or create a REAL value for the given float.
    /// </summary>
    /// <remarks>
    /// One and both zeroes are the reference's own instances, and a negative zero becomes a positive
    /// one on the way: the two are equal as numbers, and a value that answers two different hashes
    /// for one number would be worse than a value that answers a positive zero.
    /// </remarks>
    /// <param name="d">the float</param>
    /// <returns>the value</returns>
    public static ValueReal Get(float d)
    {
        if (d == 1.0f)
        {
            return One;
        }

        if (d == 0.0f)
        {
            return Zero;
        }

        if (float.IsNaN(d))
        {
            return Nan;
        }

        return (ValueReal)Value.Cache(new ValueReal(d))!;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Equality is the comparison, not the bits: two NaNs are equal here, and they would not be if
    /// the bits were compared, because a NaN has as many bit patterns as the standard says it has.
    /// </remarks>
    public sealed override bool Equals(object? other)
        => other is ValueReal real && JavaCompareFloat(value, real.value) == 0;
}
