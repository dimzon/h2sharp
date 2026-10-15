/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueDouble.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Globalization;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// Implementation of the DOUBLE PRECISION data type.
/// </summary>
public sealed class ValueDouble : Value
{
    /// <summary>
    /// The precision in bits.
    /// </summary>
    internal const int Precision = 53;

    /// <summary>
    /// The approximate precision in decimal digits.
    /// </summary>
    public const int DecimalPrecision = 17;

    /// <summary>
    /// The maximum display size of a DOUBLE.
    /// Example: -3.3333333333333334E-100
    /// </summary>
    public const int DisplaySize = 24;

    /// <summary>
    /// <c>Double.doubleToLongBits(0d)</c>.
    /// </summary>
    public const long ZeroBits = 0L;

    /// <summary>
    /// The value 0.
    /// </summary>
    public static readonly ValueDouble Zero = new(0d);

    /// <summary>
    /// The value 1.
    /// </summary>
    public static readonly ValueDouble One = new(1d);

    /// <summary>
    /// Java's <c>Double.NaN</c> bit pattern.
    /// </summary>
    /// <remarks>
    /// .NET's <see cref="double.NaN"/> has the sign bit set, <c>0xFFF8000000000000</c>, while
    /// Java's is <c>0x7FF8000000000000</c>. The two are the same NaN for every operation and
    /// every comparison, but they hash differently, and a type whose hash is part of what a
    /// grouping produces should hash the way the reference does. The singleton is therefore
    /// built from Java's bits.
    /// </remarks>
    private const long JavaNanBits = 0x7FF8000000000000L;

    private static readonly ValueDouble Nan = new(BitConverter.Int64BitsToDouble(JavaNanBits));

    private readonly double value;

    private ValueDouble(double value)
    {
        this.value = value;
    }

    /// <summary>
    /// Get or create a DOUBLE PRECISION value for the given double.
    /// </summary>
    /// <remarks>
    /// Negative zero answers with the positive zero, so the two have one representation. NaN has
    /// one as well, which is what makes the raw bit pattern in <see cref="GetHashCode"/> safe to
    /// use: all NaNs here have the same bits.
    /// </remarks>
    /// <param name="d">the double</param>
    /// <returns>the value</returns>
    public static ValueDouble Get(double d)
    {
        if (d == 1.0)
        {
            return One;
        }
        else if (d == 0.0)
        {
            return Zero;
        }
        else if (double.IsNaN(d))
        {
            return Nan;
        }

        return (ValueDouble)Cache(new ValueDouble(d));
    }

    /// <inheritdoc/>
    public override Value Add(Value v) => Get(value + ((ValueDouble)v).value);

    /// <inheritdoc/>
    public override Value Subtract(Value v) => Get(value - ((ValueDouble)v).value);

    /// <inheritdoc/>
    public override Value Negate() => Get(-value);

    /// <inheritdoc/>
    public override Value Multiply(Value v) => Get(value * ((ValueDouble)v).value);

    /// <inheritdoc/>
    public override Value Divide(Value v, TypeInfo quotientType)
    {
        ValueDouble v2 = (ValueDouble)v;
        if (v2.value == 0.0)
        {
            throw DbException.Get(ErrorCode.DivisionByZero1, GetTraceSql());
        }

        return Get(value / v2.value);
    }

    /// <inheritdoc/>
    public override Value Modulus(Value v)
    {
        ValueDouble other = (ValueDouble)v;
        if (other.value == 0)
        {
            throw DbException.Get(ErrorCode.DivisionByZero1, GetTraceSql());
        }

        return Get(value % other.value);
    }

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        if ((sqlFlags & HasSql.NoCasts) == 0)
        {
            return AppendValue(_ = builder.Append("CAST(")).Append(" AS DOUBLE PRECISION)");
        }

        return AppendValue(builder);
    }

    /// <summary>
    /// Appends the value, quoting the three values that are not numbers in SQL.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <returns>the specified builder</returns>
    private StringBuilder AppendValue(StringBuilder builder)
    {
        if (double.IsPositiveInfinity(value))
        {
            return builder.Append("'Infinity'");
        }
        else if (double.IsNegativeInfinity(value))
        {
            return builder.Append("'-Infinity'");
        }
        else if (double.IsNaN(value))
        {
            return builder.Append("'NaN'");
        }

        // The Java spelling, so that 0.0 prints as 0.0 and 1e7 as 1.0E7.
        return builder.Append(JavaNumberFormat.DoubleToString(value));
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => TypeInfo.TypeDouble;

    /// <inheritdoc/>
    public override int GetValueType() => Value.Double;

    /// <inheritdoc/>
    public override int CompareTypeSafe(Value o, CompareMode mode, CastDataProvider? provider)
        => JavaCompareDouble(value, ((ValueDouble)o).value);

    /// <inheritdoc/>
    public override int GetSignum()
        => value == 0 || double.IsNaN(value) ? 0 : value < 0 ? -1 : 1;

    /// <summary>
    /// Get the value as an arbitrary precision decimal.
    /// </summary>
    /// <remarks>
    /// Not ported, and refused rather than approximated. The reference builds a
    /// <c>BigDecimal</c> from the shortest decimal string of the value, which is a different
    /// number from both the exact binary value and from what .NET's <see cref="decimal"/> can
    /// hold: .NET keeps 28 digits and rounds, Java keeps the shortest form and the scale it
    /// implies. Returning <c>(decimal)value</c> would quietly give a different number, so this
    /// throws until NUMERIC brings a real arbitrary precision decimal with it.
    /// </remarks>
    /// <remarks>
    /// A double is turned into the shortest decimal that reads back as the same double, which is
    /// what the reference's <c>doubleValue</c> hands to a decimal and what the JVM prints for a
    /// double. It is not the shortest decimal that keeps the number, since no such decimal has
    /// the same value.
    /// </remarks>
    /// <returns>the decimal value</returns>
    public override H2Sharp.Util.BigDecimal GetBigDecimal() => H2Sharp.Util.BigDecimal.Parse(JavaNumberFormat.DoubleToString(value));

    /// <inheritdoc/>
    public override float GetFloat() => (float)value;

    /// <inheritdoc/>
    public override double GetDouble() => value;

    /// <inheritdoc/>
    public override string GetString() => JavaNumberFormat.DoubleToString(value);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        // NaNs are normalised in Get(), so it is safe to use the bits directly rather
        // than the raw ones, exactly as the reference says: every NaN this class can hold is
        // the single NAN instance, whose quiet bits are the ones the conversion returns.
        long hash = BitConverter.DoubleToInt64Bits(value);
        return (int)(hash ^ (hash >> 32));
    }

    /// <inheritdoc/>
    public override bool Equals(object? other)
        => other is ValueDouble v && JavaCompareDouble(value, v.value) == 0;
}
