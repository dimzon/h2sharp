/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueNumeric.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Numerics;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// Implementation of the NUMERIC data type.
/// </summary>
public sealed class ValueNumeric : ValueBigDecimalBase
{
    /// <summary>
    /// The Number zero.
    /// </summary>
    public static readonly ValueNumeric Zero = new(BigDecimal.Zero);

    /// <summary>
    /// The Number one.
    /// </summary>
    public static readonly ValueNumeric One = new(BigDecimal.One);

    /// <summary>
    /// The default scale for a NUMERIC Number.
    /// </summary>
    public const int DefaultScale = 0;

    /// <summary>
    /// The maximum scale.
    /// </summary>
    public const int MaximumScale = 100_000;

    /// <summary>
    /// The largest Number a long can hold, as a decimal, which is where the printed form of a NUMERIC
    /// stops needing a CAST.
    /// </summary>
    private static readonly BigDecimal MaxLongDecimal = BigDecimal.FromLong(long.MaxValue);

    /// <summary>
    /// The smallest Number a long can hold, as a decimal.
    /// </summary>
    private static readonly BigDecimal MinLongDecimal = BigDecimal.FromLong(long.MinValue);

    private ValueNumeric(BigDecimal v)
        : base(v)
    {
        int scale = v.Scale;
        if (scale < 0 || scale > MaximumScale)
        {
            throw DbException.Get(
                ErrorCode.InvalidValueScale,
                scale.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "0",
                MaximumScale.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A NUMERIC prints as a plain number, never with an exponent: it comes back to the user as
    /// something to read rather than as something to be parsed again. The reference writes the
    /// scale out in full for the same reason, and a Number with a negative scale prints the zeros
    /// that the scale stands for.
    /// </remarks>
    public override string? GetString() => Number.ToPlainString();

    /// <inheritdoc/>
    /// <remarks>
    /// A whole number that fits in a long prints with a CAST that says how wide it is, because a
    /// bare number would come back as whatever type the parser chose. Anything with a fractional
    /// part, or too wide for a long, prints as it is: the CAST would say nothing the number does
    /// not already say.
    /// </remarks>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        // The reference builds the plain string first and asks it whether it has a point in it.
        // That question has an answer without building it: the plain form of a Number has a point
        // exactly when its scale is positive, since a scale of zero writes the digits and a
        // negative one writes the digits and the zeros the scale stands for. So the common path,
        // which is a whole number that fits a long, costs nothing but the builder.
        if ((sqlFlags & HasSql.NoCasts) == 0
            && Number.Scale <= 0
            && Number.CompareTo(MaxLongDecimal) <= 0
            && Number.CompareTo(MinLongDecimal) >= 0)
        {
            return builder.Append("CAST(").Append(Number)
                .Append(" AS NUMERIC(")
                .Append(Number.Precision().ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append("))");
        }

        return builder.Append(Number.ToPlainString());
    }

    /// <inheritdoc/>
    public override int GetValueType() => Value.Numeric;

    /// <summary>
    /// The Number. The field is null only because DECFLOAT has three values that are not
    /// numbers; a NUMERIC has none of them, so this is where that is said once.
    /// </summary>
    private BigDecimal Number => value!;

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo()
    {
        TypeInfo? cached = type;
        if (cached is null)
        {
            type = cached = new TypeInfo(Value.Numeric, Number.Precision(), Number.Scale, null);
        }

        return cached;
    }

    /// <inheritdoc/>
    public override int GetSignum() => Number.Signum();

    /// <inheritdoc/>
    public override BigDecimal GetBigDecimal() => Number;

    /// <inheritdoc/>
    public override double GetDouble() => Number.ToDouble();

    /// <inheritdoc/>
    public override float GetFloat() => (float)Number.ToDouble();

    /// <inheritdoc/>
    public sealed override bool Equals(object? other)
        => other is not null
            && GetType() == other.GetType()
            && Number.Equals(((ValueNumeric)other).Number);

    /// <inheritdoc/>
    /// <remarks>
    /// The reference mixes in <c>getClass().hashCode()</c>, which in Java is an identity hash and
    /// differs on every run, so it is not a reproducible number on either side and is not pinned by
    /// the differential test. Equality, which is what a hash has to agree with, is compared.
    /// </remarks>
    public override int GetHashCode()
    {
        unchecked
        {
            int classHash = GetType().GetHashCode();
            int decimalHash = Number.GetHashCode();
            return classHash * 31 + decimalHash;
        }
    }

    /// <inheritdoc/>
    public override Value Add(Value v) => Get(Number.Add(((ValueNumeric)v).Number));

    /// <inheritdoc/>
    public override Value Subtract(Value v) => Get(Number.Subtract(((ValueNumeric)v).Number));

    /// <inheritdoc/>
    public override Value Negate() => Get(Number.Negate());

    /// <inheritdoc/>
    public override Value Multiply(Value v) => Get(Number.Multiply(((ValueNumeric)v).Number));

    /// <inheritdoc/>
    /// <remarks>
    /// The quotient is rounded HALF_DOWN to the scale of the column it is stored in, not to the
    /// scale of the operands, so that 1 divided by 3 in a NUMERIC(10,4) column is 0.3333 and not a
    /// number with forty digits in it.
    /// </remarks>
    /// <param name="v">the divisor</param>
    /// <param name="quotientType">the type the quotient must have</param>
    /// <returns>the result</returns>
    public override Value Divide(Value v, TypeInfo quotientType)
    {
        BigDecimal divisor = ((ValueNumeric)v).Number;
        if (divisor.Signum() == 0)
        {
            throw DbException.Get(ErrorCode.DivisionByZero1, GetSql(new StringBuilder(), 0).ToString());
        }

        return Get(Number.Divide(divisor, quotientType.GetScale(), RoundingMode.HalfDown));
    }

    /// <inheritdoc/>
    public override Value Modulus(Value v)
    {
        BigDecimal divisor = ((ValueNumeric)v).Number;
        if (divisor.Signum() == 0)
        {
            throw DbException.Get(ErrorCode.DivisionByZero1, GetSql(new StringBuilder(), 0).ToString());
        }

        return Get(Number.Remainder(divisor));
    }

    /// <inheritdoc/>
    public override int CompareTypeSafe(Value o, CompareMode mode, CastDataProvider? provider)
        => Number.CompareTo(((ValueNumeric)o).Number);

    /// <inheritdoc/>
    public override int GetMemory() => Number.Precision() + 120;

    /// <summary>
    /// Get or create a NUMERIC Number for the given decimal.
    /// </summary>
    /// <param name="dec">the decimal</param>
    /// <returns>the Number</returns>
    public static ValueNumeric Get(BigDecimal dec)
    {
        if (BigDecimal.Zero.Equals(dec))
        {
            return Zero;
        }

        if (BigDecimal.One.Equals(dec))
        {
            return One;
        }

        return (ValueNumeric)Value.Cache(new ValueNumeric(dec))!;
    }

    /// <summary>
    /// Get or create a NUMERIC Number for the given decimal, normalising a negative scale to zero.
    /// </summary>
    /// <remarks>
    /// A scale below zero says the Number has zeros the unscaled Number does not have, which is not a
    /// scale a NUMERIC can be declared with. Normalising means rounding the zeros away, which is
    /// exact, so nothing is lost on the way.
    /// </remarks>
    /// <param name="dec">the decimal, whose scale may be negative</param>
    /// <returns>the Number</returns>
    public static ValueNumeric GetAnyScale(BigDecimal dec)
        => dec.Scale < 0 ? Get(dec.SetScale(0, RoundingMode.Unnecessary)) : Get(dec);

    /// <summary>
    /// Get or create a NUMERIC Number for the given integer.
    /// </summary>
    /// <param name="bigInteger">the integer</param>
    /// <returns>the Number</returns>
    public static ValueNumeric Get(BigInteger bigInteger)
    {
        if (bigInteger.IsZero)
        {
            return Zero;
        }

        return BigInteger.One.Equals(bigInteger)
            ? One
            : (ValueNumeric)Value.Cache(new ValueNumeric(BigDecimal.FromBigInteger(bigInteger)))!;
    }

    /// <summary>
    /// Set the scale of a decimal, refusing a scale no NUMERIC can have.
    /// </summary>
    /// <param name="bd">the decimal</param>
    /// <param name="scale">the scale wanted</param>
    /// <returns>the decimal at that scale</returns>
    /// <exception cref="H2SharpException">if the scale is out of range</exception>
    public static BigDecimal SetScale(BigDecimal bd, int scale)
    {
        if (scale < 0 || scale > MaximumScale)
        {
            throw DbException.GetInvalidValueException("scale", scale);
        }

        return bd.SetScale(scale, RoundingMode.HalfUp);
    }
}
