/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueBigint.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// Implementation of the BIGINT data type.
/// </summary>
public sealed class ValueBigint : Value
{
    /// <summary>
    /// The minimum BIGINT value.
    /// </summary>
    public static readonly ValueBigint Min = Get(long.MinValue);

    /// <summary>
    /// The maximum BIGINT value.
    /// </summary>
    public static readonly ValueBigint Max = Get(long.MaxValue);

    /// <summary>
    /// The precision in bits.
    /// </summary>
    public const int Precision = 64;

    /// <summary>
    /// The largest long, as a big integer.
    /// </summary>
    /// <remarks>
    /// The tokenizer needs the number rather than the value, because a literal too wide for a long
    /// is compared against it before it is decided to be a NUMERIC, and comparing a big integer to
    /// a value would be comparing a number to itself.
    /// </remarks>
    public static readonly BigInteger MaxBi = new(long.MaxValue);

    /// <summary>
    /// The approximate precision in decimal digits.
    /// </summary>
    public const int DecimalPrecision = 19;

    /// <summary>
    /// The maximum display size of a BIGINT.
    /// Example: -9223372036854775808
    /// </summary>
    public const int DisplaySize = 20;

    private const int StaticSize = 100;
    private static readonly ValueBigint[] StaticCache = new ValueBigint[StaticSize];

    private readonly long value;

    static ValueBigint()
    {
        for (int i = 0; i < StaticSize; i++)
        {
            StaticCache[i] = new ValueBigint(i);
        }
    }

    private ValueBigint(long value)
    {
        this.value = value;
    }

    /// <summary>
    /// Get or create a BIGINT value for the given long.
    /// </summary>
    /// <remarks>
    /// Small values are preallocated and the rest go through the shared value cache, so that a
    /// query mentioning the same big number twice does not build it twice.
    /// </remarks>
    /// <param name="i">the long</param>
    /// <returns>the value</returns>
    public static ValueBigint Get(long i)
    {
        if (i >= 0 && i < StaticSize)
        {
            return StaticCache[(int)i];
        }

        return (ValueBigint)Cache(new ValueBigint(i));
    }

    /// <inheritdoc/>
    public override Value Add(Value v)
    {
        long x = value;
        long y = ((ValueBigint)v).value;

        // Unchecked on purpose, so that the wrap is the same one Java has and the sign test
        // below is what decides whether it was an overflow. In checked arithmetic the addition
        // would have thrown first, with .NET's message instead of the reference's.
        long result = unchecked(x + y);

        // If the signs of both summands are different from the sign of the sum there is an
        // overflow. This test is exact for addition, subtraction and multiplication in two's
        // complement, which is why the reference uses it and why no extra check is needed.
        if (((x ^ result) & (y ^ result)) < 0)
        {
            throw GetOverflow();
        }

        return Get(result);
    }

    /// <inheritdoc/>
    public override int GetSignum() => Math.Sign(value);

    /// <inheritdoc/>
    public override Value Negate()
    {
        // Negating long.MinValue is not representable, and Java wraps it to itself, which would
        // silently answer the wrong question.
        if (value == long.MinValue)
        {
            throw GetOverflow();
        }

        return Get(-value);
    }

    private H2SharpException GetOverflow()
        => DbException.Get(
            ErrorCode.NumericValueOutOfRange1,
            value.ToString(CultureInfo.InvariantCulture));

    /// <inheritdoc/>
    public override Value Subtract(Value v)
    {
        long x = value;
        long y = ((ValueBigint)v).value;
        long result = unchecked(x - y);

        // If minuend and subtrahend have different signs and minuend and difference have
        // different signs there is an overflow.
        if (((x ^ y) & (x ^ result)) < 0)
        {
            throw GetOverflow();
        }

        return Get(result);
    }

    /// <inheritdoc/>
    public override Value Multiply(Value v)
    {
        long x = value;
        long y = ((ValueBigint)v).value;
        long result = unchecked(x * y);

        // Check whether the numbers are large enough to overflow and the second is not zero,
        // then check with division, and then the one case the division check cannot see.
        //
        // The two cases are tested in the opposite order from the reference, and that is
        // forced: .NET raises OverflowException for long.MinValue divided by -1 even in
        // unchecked arithmetic, because the division itself overflows on the processor,
        // while Java wraps it to long.MinValue and lets the comparison run. So the special
        // pair is recognised first, and after that the division cannot overflow.
        if ((JavaAbs(x) | JavaAbs(y)) >> 31 != 0 && y != 0)
        {
            if (x == long.MinValue && y == -1)
            {
                throw GetOverflow();
            }

            if (unchecked(result / y) != x)
            {
                throw GetOverflow();
            }
        }

        return Get(result);
    }

    /// <inheritdoc/>
    public override Value Divide(Value v, TypeInfo quotientType)
    {
        long y = ((ValueBigint)v).value;
        if (y == 0)
        {
            throw DbException.Get(ErrorCode.DivisionByZero1, GetTraceSql());
        }

        long x = value;
        if (x == long.MinValue && y == -1)
        {
            throw GetOverflow();
        }

        return Get(x / y);
    }

    /// <inheritdoc/>
    public override Value Modulus(Value v)
    {
        ValueBigint other = (ValueBigint)v;
        if (other.value == 0)
        {
            throw DbException.Get(ErrorCode.DivisionByZero1, GetTraceSql());
        }

        // Unlike the division, this one is not refused for long.MinValue modulo -1: the
        // remainder is mathematically zero, and Java returns it.
        return Get(value == long.MinValue && other.value == -1 ? 0 : value % other.value);
    }

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        // A bigint that fits in an int is printed as a cast, so that reading the SQL back does
        // not silently widen the type.
        if ((sqlFlags & HasSql.NoCasts) == 0 && value == (int)value)
        {
            return builder.Append("CAST(")
                .Append(value.ToString(CultureInfo.InvariantCulture))
                .Append(" AS BIGINT)");
        }

        return builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => TypeInfo.TypeBigint;

    /// <inheritdoc/>
    public override int GetValueType() => Value.Bigint;

    /// <inheritdoc/>
    public override byte[]? GetBytes()
    {
        byte[] b = new byte[8];
        Bits.LongToBigEndian(b, 0, GetLong());
        return b;
    }

    /// <inheritdoc/>
    public override long GetLong() => value;

    /// <summary>
    /// Get the value as an int.
    /// </summary>
    /// <remarks>
    /// Narrowing is checked, not truncating. The reference does not override <c>getInt</c>
    /// here, so the call goes through the base's converting accessor and its range check, and a
    /// BIGINT that does not fit an int is refused with the same code and message as an
    /// arithmetic overflow. Casting would answer -1 for 9223372036854775807 and call it a day.
    /// </remarks>
    /// <returns>the int value</returns>
    /// <exception cref="H2SharpException">if the value does not fit an int</exception>
    public override int GetInt()
    {
        if (value < int.MinValue || value > int.MaxValue)
        {
            throw GetOverflow();
        }

        return (int)value;
    }

    /// <inheritdoc/>
    public override BigInteger GetBigInteger() => new(value);

    /// <inheritdoc/>
    public override H2Sharp.Util.BigDecimal GetBigDecimal() => H2Sharp.Util.BigDecimal.FromLong(value);

    /// <inheritdoc/>
    public override float GetFloat() => value;

    /// <inheritdoc/>
    public override double GetDouble() => value;

    /// <inheritdoc/>
    /// <remarks>
    /// The sign, not the distance: .NET's <c>Int64.CompareTo</c> answers with the difference between
    /// the numbers, where Java's <c>Long.compare</c> answers -1, 0 or 1. Subtracting and taking the
    /// sign would fix that and introduce a worse one, because the difference of two longs overflows
    /// where it matters most: long.MinValue against -1 wraps to long.MaxValue and answers the wrong
    /// way round. So the numbers are compared and never subtracted.
    /// </remarks>
    public override int CompareTypeSafe(Value o, CompareMode mode, CastDataProvider? provider)
    {
        long other = ((ValueBigint)o).value;
        return value < other ? -1 : value > other ? 1 : 0;
    }

    /// <inheritdoc/>
    public override string GetString() => value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public override int GetHashCode() => (int)(value ^ (value >> 32));

    /// <inheritdoc/>
    public override bool Equals(object? other)
        => other is ValueBigint v && value == v.value;

    /// <summary>
    /// The absolute value, as Java's <c>Math.abs</c> gives it.
    /// </summary>
    /// <remarks>
    /// Java returns <c>long.MinValue</c> for its own minimum, because negation wraps. .NET's
    /// <see cref="Math.Abs(long)"/> throws in checked arithmetic and the project builds checked
    /// in some configurations, so the wrap is spelled out here. The callers use the result only
    /// to look at bit 62, where both answers agree.
    /// </remarks>
    /// <param name="value">the value</param>
    /// <returns>the absolute value, wrapping for the minimum</returns>
    private static long JavaAbs(long value) => value < 0 ? unchecked(-value) : value;
}
