/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueInteger.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Numerics;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// Implementation of the INTEGER data type.
/// </summary>
public sealed class ValueInteger : Value
{
    /// <summary>
    /// The precision in bits.
    /// </summary>
    public const int Precision = 32;

    /// <summary>
    /// The approximate precision in decimal digits.
    /// </summary>
    public const int DecimalPrecision = 10;

    /// <summary>
    /// The maximum display size of an INT.
    /// Example: -2147483648
    /// </summary>
    public const int DisplaySize = 11;

    private const int StaticSize = 128;
    // Must be a power of 2.
    private const int DynamicSize = 256;
    private static readonly ValueInteger[] StaticCache = new ValueInteger[StaticSize];
    private static readonly ValueInteger?[] DynamicCache = new ValueInteger?[DynamicSize];

    private readonly int value;

    static ValueInteger()
    {
        for (int i = 0; i < StaticSize; i++)
        {
            StaticCache[i] = new ValueInteger(i);
        }
    }

    private ValueInteger(int value)
    {
        this.value = value;
    }

    /// <summary>
    /// Get or create an INTEGER value for the given int.
    /// </summary>
    /// <remarks>
    /// Small non-negative values are preallocated and anything else lands in a direct mapped
    /// cache, so that repeated literals in a query do not allocate. The cache is not
    /// synchronised, exactly as the reference's is not: a race can only cost an allocation,
    /// because the entry is replaced, never corrupted. A torn read cannot happen either, since
    /// the reference type has no fields to tear.
    /// </remarks>
    /// <param name="i">the int</param>
    /// <returns>the value</returns>
    public static ValueInteger Get(int i)
    {
        if (i >= 0 && i < StaticSize)
        {
            return StaticCache[i];
        }

        ValueInteger? v = DynamicCache[i & (DynamicSize - 1)];
        if (v is null || v.value != i)
        {
            v = new ValueInteger(i);
            DynamicCache[i & (DynamicSize - 1)] = v;
        }

        return v;
    }

    /// <inheritdoc/>
    public override Value Add(Value v)
    {
        ValueInteger other = (ValueInteger)v;
        return CheckRange((long)value + other.value);
    }

    private static ValueInteger CheckRange(long x)
    {
        if ((int)x != x)
        {
            throw DbException.Get(ErrorCode.NumericValueOutOfRange1, x.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return Get((int)x);
    }

    /// <inheritdoc/>
    public override int GetSignum() => Math.Sign(value);

    /// <inheritdoc/>
    public override Value Negate() => CheckRange(-(long)value);

    /// <inheritdoc/>
    public override Value Subtract(Value v)
    {
        ValueInteger other = (ValueInteger)v;
        return CheckRange((long)value - other.value);
    }

    /// <inheritdoc/>
    public override Value Multiply(Value v)
    {
        ValueInteger other = (ValueInteger)v;
        return CheckRange((long)value * other.value);
    }

    /// <inheritdoc/>
    public override Value Divide(Value v, TypeInfo quotientType)
    {
        int y = ((ValueInteger)v).value;
        if (y == 0)
        {
            throw DbException.Get(ErrorCode.DivisionByZero1, GetTraceSql());
        }

        int x = value;

        // Java's integer division truncates towards zero and int.MinValue / -1 overflows to
        // int.MinValue in Java, so the reference refuses it rather than returning a negative
        // that was never asked for. Checked arithmetic in .NET would throw on its own, which
        // would be the wrong exception and the wrong message.
        if (x == int.MinValue && y == -1)
        {
            throw DbException.Get(ErrorCode.NumericValueOutOfRange1, "2147483648");
        }

        return Get(x / y);
    }

    /// <inheritdoc/>
    public override Value Modulus(Value v)
    {
        ValueInteger other = (ValueInteger)v;
        if (other.value == 0)
        {
            throw DbException.Get(ErrorCode.DivisionByZero1, GetTraceSql());
        }

        if (value == int.MinValue && other.value == -1)
        {
            // The remainder of this division is mathematically zero and Java would produce it,
            // so unlike the division above this one is not refused.
            return Get(0);
        }

        return Get(value % other.value);
    }

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => builder.Append(value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => TypeInfo.TypeInteger;

    /// <inheritdoc/>
    public override int GetValueType() => Value.Integer;

    /// <inheritdoc/>
    public override byte[]? GetBytes()
    {
        byte[] b = new byte[4];
        Bits.IntToBigEndian(b, 0, GetInt());
        return b;
    }

    /// <inheritdoc/>
    public override int GetInt() => value;

    /// <inheritdoc/>
    public override long GetLong() => value;

    /// <inheritdoc/>
    public override BigInteger GetBigInteger() => new(value);

    /// <inheritdoc/>
    public override H2Sharp.Util.BigDecimal GetBigDecimal() => H2Sharp.Util.BigDecimal.FromLong(value);

    /// <inheritdoc/>
    public override float GetFloat() => value;

    /// <inheritdoc/>
    public override double GetDouble() => value;

    /// <inheritdoc/>
    public override int CompareTypeSafe(Value o, CompareMode mode, CastDataProvider? provider)
        => Math.Sign(value - (long)((ValueInteger)o).value);

    /// <inheritdoc/>
    public override string GetString()
        => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public override int GetHashCode() => value;

    /// <inheritdoc/>
    public override bool Equals(object? other)
        => other is ValueInteger v && value == v.value;
}
