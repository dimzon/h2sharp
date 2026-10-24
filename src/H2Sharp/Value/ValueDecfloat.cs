/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueDecfloat.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Runtime.CompilerServices;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// A decimal floating point value, which is DECFLOAT: a decimal of up to 34 digits, and three
/// values that are not numbers at all.
/// </summary>
/// <remarks>
/// The three values that are not numbers are the three singletons <see cref="PositiveInfinity"/>,
/// <see cref="NegativeInfinity"/> and <see cref="NotANumber"/>, and they are told apart by being
/// those objects rather than by a flag, so the whole class compares with <c>is</c> where the
/// reference compares with <c>==</c>. A null decimal is what makes one of them; a NUMERIC has no
/// such value, which is why the base class allows the decimal to be null and this class is the one
/// that says so.
/// </remarks>
public sealed class ValueDecfloat : ValueBigDecimalBase
{
    /// <summary>
    /// The value 'zero'.
    /// </summary>
    public static readonly ValueDecfloat Zero = new(BigDecimal.Zero);

    /// <summary>
    /// The value 'one'.
    /// </summary>
    public static readonly ValueDecfloat One = new(BigDecimal.One);

    /// <summary>
    /// The positive infinity value.
    /// </summary>
    public static readonly ValueDecfloat PositiveInfinity = new(null);

    /// <summary>
    /// The negative infinity value.
    /// </summary>
    public static readonly ValueDecfloat NegativeInfinity = new(null);

    /// <summary>
    /// The not a number value.
    /// </summary>
    public static readonly ValueDecfloat NotANumber = new(null);

    private ValueDecfloat(BigDecimal? v)
        : base(v)
    {
    }

    /// <summary>
    /// The decimal this value holds, or null for one of the three that are not numbers.
    /// </summary>
    /// <remarks>
    /// The reference reads its own <c>value</c> field from <c>Value.convertToDecfloat</c>,
    /// which is another class in the same package, so package visibility is enough there and
    /// private is enough here. This is the accessor that replaces that visibility: it says the
    /// same thing and keeps the field private.
    /// </remarks>
    /// <returns>the decimal, or null</returns>
    internal BigDecimal? GetDecimalValue() => value;

    /// <inheritdoc/>
    public override string? GetString()
    {
        if (value is null)
        {
            if (this == PositiveInfinity)
            {
                return "Infinity";
            }

            return this == NegativeInfinity ? "-Infinity" : "NaN";
        }

        return value.ToString();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A DECFLOAT prints inside a CAST, because a bare number comes back as whatever type the
    /// parser chose and 34 digits are not what it would choose. The three values that are not
    /// numbers are printed as quoted words, which is how they are written.
    /// </remarks>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => Dialect.AppendDecfloat(builder, this, sqlFlags);

    /// <inheritdoc/>
    /// <remarks>
    /// The precision is the precision of the value itself, and one for a value that is not a
    /// number: that is the width of the word that stands for it.
    /// </remarks>
    public override TypeInfo GetTypeInfo()
    {
        TypeInfo? cached = type;
        if (cached is null)
        {
            type = cached = new TypeInfo(
                Value.Decfloat, value is not null ? value.Precision() : 1, 0, null);
        }

        return cached;
    }

    /// <inheritdoc/>
    public override int GetValueType() => Value.Decfloat;

    /// <inheritdoc/>
    public override Value Add(Value v)
    {
        BigDecimal? value2 = ((ValueDecfloat)v).value;
        if (value is not null)
        {
            if (value2 is not null)
            {
                return Get(value.Add(value2));
            }

            return v;
        }

        // Infinity and NaN: Infinity and infinity is that infinity, Infinity and NaN is NaN,
        // and NaN and anything is NaN.
        return value2 is not null || ReferenceEquals(this, v) ? this : NotANumber;
    }

    /// <inheritdoc/>
    public override Value Subtract(Value v)
    {
        BigDecimal? value2 = ((ValueDecfloat)v).value;
        if (value is not null)
        {
            if (value2 is not null)
            {
                return Get(value.Subtract(value2));
            }

            // Subtracting infinity from a number turns its sign.
            if (ReferenceEquals(v, PositiveInfinity))
            {
                return NegativeInfinity;
            }

            return ReferenceEquals(v, NegativeInfinity) ? PositiveInfinity : NotANumber;
        }

        if (value2 is not null)
        {
            return this;
        }

        if (this == PositiveInfinity)
        {
            if (ReferenceEquals(v, NegativeInfinity))
            {
                return PositiveInfinity;
            }
        }
        else if (this == NegativeInfinity && ReferenceEquals(v, PositiveInfinity))
        {
            return NegativeInfinity;
        }

        return NotANumber;
    }

    /// <inheritdoc/>
    public override Value Negate()
    {
        if (value is not null)
        {
            return Get(value.Negate());
        }

        if (this == PositiveInfinity)
        {
            return NegativeInfinity;
        }

        return this == NegativeInfinity ? PositiveInfinity : NotANumber;
    }

    /// <inheritdoc/>
    public override Value Multiply(Value v)
    {
        BigDecimal? value2 = ((ValueDecfloat)v).value;
        if (value is not null)
        {
            if (value2 is not null)
            {
                return Get(value.Multiply(value2));
            }

            // The sign of a number is the sign of the result, and the sign of zero is neither,
            // so zero times infinity is the one answer that is not an infinity.
            if (ReferenceEquals(v, PositiveInfinity))
            {
                int s = value.Signum();
                if (s > 0)
                {
                    return PositiveInfinity;
                }

                if (s < 0)
                {
                    return NegativeInfinity;
                }
            }
            else if (ReferenceEquals(v, NegativeInfinity))
            {
                int s = value.Signum();
                if (s > 0)
                {
                    return NegativeInfinity;
                }

                if (s < 0)
                {
                    return PositiveInfinity;
                }
            }
        }
        else if (value2 is not null)
        {
            if (this == PositiveInfinity)
            {
                int s = value2.Signum();
                if (s > 0)
                {
                    return PositiveInfinity;
                }

                if (s < 0)
                {
                    return NegativeInfinity;
                }
            }
            else if (this == NegativeInfinity)
            {
                int s = value2.Signum();
                if (s > 0)
                {
                    return NegativeInfinity;
                }

                if (s < 0)
                {
                    return PositiveInfinity;
                }
            }
        }
        else if (this == PositiveInfinity)
        {
            if (ReferenceEquals(v, PositiveInfinity))
            {
                return PositiveInfinity;
            }

            if (ReferenceEquals(v, NegativeInfinity))
            {
                return NegativeInfinity;
            }
        }
        else if (this == NegativeInfinity)
        {
            if (ReferenceEquals(v, PositiveInfinity))
            {
                return NegativeInfinity;
            }

            if (ReferenceEquals(v, NegativeInfinity))
            {
                return PositiveInfinity;
            }
        }

        return NotANumber;
    }

    /// <inheritdoc/>
    public override Value Divide(Value v, TypeInfo quotientType)
    {
        BigDecimal? value2 = ((ValueDecfloat)v).value;
        if (value2 is not null && value2.Signum() == 0)
        {
            throw DbException.Get(ErrorCode.DivisionByZero1, GetTraceSql());
        }

        if (value is not null)
        {
            if (value2 is not null)
            {
                return Divide(value, value2, quotientType);
            }

            // A number divided by infinity is zero, whatever the sign of the number is, unless
            // the divisor is not a number at all.
            if (!ReferenceEquals(v, NotANumber))
            {
                return Zero;
            }
        }
        else if (value2 is not null && this != NotANumber)
        {
            bool sameSign = (this == PositiveInfinity) == (value2.Signum() > 0);
            return sameSign ? PositiveInfinity : NegativeInfinity;
        }

        return NotANumber;
    }

    /// <summary>
    /// Divides two decimals and returns a DECFLOAT result of the specified data type.
    /// </summary>
    /// <param name="dividend">the dividend</param>
    /// <param name="divisor">the divisor</param>
    /// <param name="quotientType">the type of quotient</param>
    /// <returns>the quotient</returns>
    public static ValueDecfloat Divide(BigDecimal dividend, BigDecimal divisor, TypeInfo quotientType)
    {
        int quotientPrecision = (int)quotientType.GetPrecision();

        // The scale is worked out rather than guessed: the quotient needs as many digits before
        // the point as the dividend has, less the digits it has behind the point, and as many
        // after the point as the divisor has before it, less its own, and as many again as the
        // type asks for. Guessing this is what makes a division either lose digits or end in a
        // string of nines.
        BigDecimal quotient = dividend.Divide(
            divisor,
            dividend.Scale - dividend.Precision() + divisor.Precision() - divisor.Scale
                + quotientPrecision,
            RoundingMode.HalfDown);

        int precision = quotient.Precision();
        if (precision > quotientPrecision)
        {
            quotient = quotient.SetScale(
                quotient.Scale - precision + quotientPrecision, RoundingMode.HalfUp);
        }

        return Get(quotient);
    }

    /// <inheritdoc/>
    public override Value Modulus(Value v)
    {
        BigDecimal? value2 = ((ValueDecfloat)v).value;
        if (value2 is not null && value2.Signum() == 0)
        {
            throw DbException.Get(ErrorCode.DivisionByZero1, GetTraceSql());
        }

        if (value is not null)
        {
            if (value2 is not null)
            {
                return Get(value.Remainder(value2));
            }

            if (!ReferenceEquals(v, NotANumber))
            {
                return this;
            }
        }

        return NotANumber;
    }

    /// <inheritdoc/>
    public override int CompareTypeSafe(Value o, CompareMode mode, CastDataProvider? provider)
    {
        BigDecimal? value2 = ((ValueDecfloat)o).value;
        if (value is not null)
        {
            if (value2 is not null)
            {
                return value.CompareTo(value2);
            }

            return ReferenceEquals(o, NegativeInfinity) ? 1 : -1;
        }

        if (value2 is not null)
        {
            return this == NegativeInfinity ? -1 : 1;
        }

        // Three values that are not numbers compare among themselves: NaN is not equal to
        // anything including itself, and infinities are equal only to themselves.
        if (ReferenceEquals(this, o))
        {
            return 0;
        }

        if (this == NegativeInfinity)
        {
            return -1;
        }

        if (o == NegativeInfinity)
        {
            return 1;
        }

        return this == PositiveInfinity ? -1 : 1;
    }

    /// <inheritdoc/>
    public override int GetSignum()
    {
        if (value is not null)
        {
            return value.Signum();
        }

        return this == PositiveInfinity ? 1 : this == NegativeInfinity ? -1 : 0;
    }

    /// <inheritdoc/>
    /// <exception cref="H2SharpException">
    /// when the value is not a number, which has no decimal to give
    /// </exception>
    public override BigDecimal GetBigDecimal()
    {
        if (value is not null)
        {
            return value;
        }

        throw GetDataConversionErrorException(Value.Numeric);
    }

    /// <inheritdoc/>
    public override float GetFloat()
    {
        if (value is not null)
        {
            return (float)value.ToDouble();
        }

        if (this == PositiveInfinity)
        {
            return float.PositiveInfinity;
        }

        return this == NegativeInfinity ? float.NegativeInfinity : JavaNanFloat;
    }

    /// <inheritdoc/>
    public override double GetDouble()
    {
        if (value is not null)
        {
            return value.ToDouble();
        }

        if (this == PositiveInfinity)
        {
            return double.PositiveInfinity;
        }

        return this == NegativeInfinity ? double.NegativeInfinity : JavaNanDouble;
    }

    /// <summary>
    /// Java's <c>Float.NaN</c> and <c>Double.NaN</c>.
    /// </summary>
    /// <remarks>
    /// .NET's NaN has the sign bit set and Java's has not. The two are the same NaN for every
    /// operation and every comparison, but not the same bits, and a value that hands a NaN to a
    /// reader should hand over the bits the reference hands over.
    /// </remarks>
    private static readonly float JavaNanFloat = BitConverter.Int32BitsToSingle(0x7FC00000);

    private static readonly double JavaNanDouble = BitConverter.Int64BitsToDouble(0x7FF8000000000000L);

    /// <inheritdoc/>
    public sealed override bool Equals(object? other)
    {
        if (other is ValueDecfloat value2)
        {
            if (value is not null)
            {
                return value.Equals(value2.value);
            }

            // Two values that are not numbers are equal only when they are the same one.
            return value2.value is null && ReferenceEquals(this, other);
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if (value is not null)
        {
            unchecked
            {
                int classHash = GetType().GetHashCode();
                return classHash * 31 + value.GetHashCode();
            }
        }

        return RuntimeHelpers.GetHashCode(this);
    }

    /// <inheritdoc/>
    public override int GetMemory() => value is not null ? value.Precision() + 120 : 32;

    /// <summary>
    /// Whether this value is finite, which is to say whether it is a number at all.
    /// </summary>
    /// <returns>true if this value is finite, false otherwise</returns>
    public bool IsFinite() => value is not null;

    /// <summary>
    /// Get or create a DECFLOAT value for the given big decimal.
    /// </summary>
    /// <param name="dec">the big decimal</param>
    /// <returns>the value</returns>
    public static ValueDecfloat Get(BigDecimal dec)
    {
        dec = dec.StripTrailingZeros();

        if (BigDecimal.Zero.Equals(dec))
        {
            return Zero;
        }

        if (BigDecimal.One.Equals(dec))
        {
            return One;
        }

        return (ValueDecfloat)Value.Cache(new ValueDecfloat(dec))!;
    }


}
