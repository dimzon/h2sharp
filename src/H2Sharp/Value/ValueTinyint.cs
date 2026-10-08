/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.value.ValueTinyint.
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
/// Implementation of the TINYINT data type.
/// </summary>
/// <remarks>
/// A TINYINT is a signed byte, which is the reason the port's other accessors hand back
/// <see cref="sbyte"/>: Java's byte is signed and .NET's is not, and a port that used the unsigned
/// one would answer 255 for the string "-1".
/// </remarks>
public sealed class ValueTinyint : Value
{
    /// <summary>
    /// The precision in bits.
    /// </summary>
    internal const int Precision = 8;

    /// <summary>
    /// The approximate precision in decimal digits.
    /// </summary>
    public const int DecimalPrecision = 3;

    /// <summary>
    /// The display size for a TINYINT, for example -127.
    /// </summary>
    internal const int DisplaySize = 4;

    /// <summary>
    /// Every possible value, built once. A TINYINT has 256 of them and no arithmetic ever leaves
    /// that range, so the type allocates nothing after start up.
    /// </summary>
    private static readonly ValueTinyint[] StaticCache = BuildCache();

    private readonly sbyte value;

    private ValueTinyint(sbyte v)
    {
        value = v;
    }

    private static ValueTinyint[] BuildCache()
    {
        ValueTinyint[] cache = new ValueTinyint[256];
        for (int i = 0; i < 256; i++)
        {
            cache[i] = new ValueTinyint((sbyte)(i - 128));
        }

        return cache;
    }

    /// <inheritdoc/>
    public override Value Add(Value v)
        => CheckRange(value + ((ValueTinyint)v).value);

    /// <inheritdoc/>
    public override Value Subtract(Value v)
        => CheckRange(value - ((ValueTinyint)v).value);

    /// <inheritdoc/>
    public override Value Negate() => CheckRange(-(int)value);

    /// <inheritdoc/>
    public override Value Multiply(Value v)
        => CheckRange(value * ((ValueTinyint)v).value);

    /// <inheritdoc/>
    public override Value Divide(Value v, TypeInfo quotientType)
    {
        sbyte divisor = ((ValueTinyint)v).value;
        if (divisor == 0)
        {
            throw DbException.Get(ErrorCode.DivisionByZero1, GetSql(new StringBuilder(), 0).ToString());
        }

        return CheckRange(value / divisor);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A remainder of a TINYINT is always inside the type, so it is not range checked: the divisor
    /// is non zero, and the remainder of a division by it cannot exceed the dividend in size.
    /// </remarks>
    public override Value Modulus(Value v)
    {
        sbyte divisor = ((ValueTinyint)v).value;
        if (divisor == 0)
        {
            throw DbException.Get(ErrorCode.DivisionByZero1, GetSql(new StringBuilder(), 0).ToString());
        }

        return Get((sbyte)(value % divisor));
    }

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        if ((sqlFlags & HasSql.NoCasts) == 0)
        {
            return builder.Append("CAST(").Append(value).Append(" AS TINYINT)");
        }

        return builder.Append(value);
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => TypeInfo.TypeTinyint;

    /// <inheritdoc/>
    public override int GetValueType() => Value.Tinyint;

    /// <inheritdoc/>
    public override int GetSignum() => Math.Sign(value);

    /// <inheritdoc/>
    /// <remarks>
    /// Every possible value exists from the start, so a TINYINT costs nothing to keep.
    /// </remarks>
    public override int GetMemory() => 0;

    /// <inheritdoc/>
    /// <remarks>
    /// The array holds the same bit pattern Java's does, which .NET spells as an unsigned byte; a
    /// reader that wants the number rather than the bytes asks <see cref="GetByte"/>.
    /// </remarks>
    public override byte[]? GetBytes() => [(byte)value];

    /// <inheritdoc/>
    public override sbyte GetByte() => value;

    /// <inheritdoc/>
    public override short GetShort() => value;

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
    /// <remarks>
    /// The sign and not the difference. A .NET <c>sbyte</c> compares with the distance between the
    /// numbers, so 0 against 2 answers -2 where Java's <c>compare</c> answers -1, and that number
    /// is what a caller would hand back as an ordering.
    /// </remarks>
    public override int CompareTypeSafe(Value o, CompareMode mode, CastDataProvider? provider)
    {
        sbyte other = ((ValueTinyint)o).value;
        return value < other ? -1 : value > other ? 1 : 0;
    }

    /// <inheritdoc/>
    public override string? GetString()
        => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public override int GetHashCode() => value;

    /// <summary>
    /// Get a TINYINT value for the given byte.
    /// </summary>
    /// <param name="i">the byte</param>
    /// <returns>the value</returns>
    public static ValueTinyint Get(sbyte i) => StaticCache[i + 128];

    /// <summary>
    /// Checks that a result of arithmetic still fits the type, and returns it.
    /// </summary>
    /// <param name="x">the result</param>
    /// <returns>the value</returns>
    /// <exception cref="H2SharpException">if the result does not fit</exception>
    private static ValueTinyint CheckRange(int x)
    {
        if ((sbyte)x != x)
        {
            throw DbException.Get(
                ErrorCode.NumericValueOutOfRange1,
                x.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return Get((sbyte)x);
    }

    /// <inheritdoc/>
    public sealed override bool Equals(object? other)
        => other is ValueTinyint tinyint && value == tinyint.value;
}
