/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueSmallint.
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
/// Implementation of the SMALLINT data type.
/// </summary>
/// <remarks>
/// Unlike the TINYINT this one goes through the value cache rather than a table of every possible
/// value: a small int has 65536 of them, and building them all for a type a query touches a handful
/// of times would cost more than it saves.
/// </remarks>
public sealed class ValueSmallint : Value
{
    /// <summary>
    /// The precision in bits.
    /// </summary>
    internal const int Precision = 16;

    /// <summary>
    /// The approximate precision in decimal digits.
    /// </summary>
    public const int DecimalPrecision = 5;

    /// <summary>
    /// The display size for a SMALLINT, for example -32768.
    /// </summary>
    internal const int DisplaySize = 6;

    private readonly short value;

    private ValueSmallint(short v)
    {
        value = v;
    }

    /// <inheritdoc/>
    public override Value Add(Value v) => CheckRange(value + ((ValueSmallint)v).value);

    /// <inheritdoc/>
    public override Value Subtract(Value v) => CheckRange(value - ((ValueSmallint)v).value);

    /// <inheritdoc/>
    public override Value Negate() => CheckRange(-(int)value);

    /// <inheritdoc/>
    public override Value Multiply(Value v) => CheckRange(value * ((ValueSmallint)v).value);

    /// <inheritdoc/>
    public override Value Divide(Value v, TypeInfo quotientType)
    {
        short divisor = ((ValueSmallint)v).value;
        if (divisor == 0)
        {
            throw DbException.Get(ErrorCode.DivisionByZero1, GetTraceSql());
        }

        return CheckRange(value / divisor);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The remainder of a division by a non zero divisor cannot leave the range of the dividend, so
    /// it needs no range check here either.
    /// </remarks>
    public override Value Modulus(Value v)
    {
        short divisor = ((ValueSmallint)v).value;
        if (divisor == 0)
        {
            throw DbException.Get(ErrorCode.DivisionByZero1, GetTraceSql());
        }

        return Get((short)(value % divisor));
    }

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        if ((sqlFlags & HasSql.NoCasts) == 0)
        {
            return builder.Append("CAST(").Append(value).Append(" AS SMALLINT)");
        }

        return builder.Append(value);
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => TypeInfo.TypeSmallint;

    /// <inheritdoc/>
    public override int GetValueType() => Value.Smallint;

    /// <inheritdoc/>
    public override int GetSignum() => Math.Sign(value);

    /// <inheritdoc/>
    public override byte[]? GetBytes() => [(byte)(value >> 8), (byte)value];

    /// <inheritdoc/>
    public override short GetShort() => value;

    /// <inheritdoc/>
    public override int GetInt() => value;

    /// <inheritdoc/>
    public override long GetLong() => value;

    /// <inheritdoc/>
    public override BigInteger GetBigInteger() => value;

    /// <inheritdoc/>
    public override H2Sharp.Util.BigDecimal GetBigDecimal() => H2Sharp.Util.BigDecimal.FromLong(value);

    /// <inheritdoc/>
    public override float GetFloat() => value;

    /// <inheritdoc/>
    public override double GetDouble() => value;

    /// <inheritdoc/>
    /// <remarks>
    /// The sign and not the difference. A .NET <c>short</c> compares with the distance between the
    /// numbers, so 0 against 2 answers -2 where Java's <c>compare</c> answers -1, and that number
    /// is what a caller would hand back as an ordering.
    /// </remarks>
    public override int CompareTypeSafe(Value o, CompareMode mode, CastDataProvider? provider)
    {
        short other = ((ValueSmallint)o).value;
        return value < other ? -1 : value > other ? 1 : 0;
    }

    /// <inheritdoc/>
    public override string? GetString()
        => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public override int GetHashCode() => value;

    /// <summary>
    /// Get or create a SMALLINT value for the given short.
    /// </summary>
    /// <param name="i">the short</param>
    /// <returns>the value</returns>
    public static ValueSmallint Get(short i) => (ValueSmallint)Value.Cache(new ValueSmallint(i))!;

    /// <summary>
    /// Checks that a result of arithmetic still fits the type, and returns it.
    /// </summary>
    /// <param name="x">the result</param>
    /// <returns>the value</returns>
    /// <exception cref="H2SharpException">if the result does not fit</exception>
    private static ValueSmallint CheckRange(int x)
    {
        if ((short)x != x)
        {
            throw DbException.Get(
                ErrorCode.NumericValueOutOfRange1,
                x.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return Get((short)x);
    }

    /// <inheritdoc/>
    public sealed override bool Equals(object? other)
        => other is ValueSmallint smallint && value == smallint.value;
}
