/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.value.ValueEnumBase.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// Base implementation of the ENUM data type.
/// </summary>
/// <remarks>
/// The reference keeps this base for one reason only: a 2.0 client talking to a 1.4 server gets
/// enum values that carry no enumerator list, and those values are this class. A value with a
/// list is a <see cref="ValueEnum"/>.
/// <para>
/// An enum value answers every numeric question with its ordinal, so it is a number wearing a
/// label: the arithmetic here is the arithmetic of an INTEGER, and the reference does it by
/// converting both sides to one rather than by writing the operations out.
/// </para>
/// </remarks>
public class ValueEnumBase : Value
{
    /// <summary>
    /// The label of this value, which is its text.
    /// </summary>
    protected readonly string label;

    private readonly int ordinal;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    /// <param name="label">the label</param>
    /// <param name="ordinal">the ordinal</param>
    protected ValueEnumBase(string label, int ordinal)
    {
        this.label = label;
        this.ordinal = ordinal;
    }

    /// <summary>
    /// Get or create an enum value with the given label and ordinal.
    /// </summary>
    /// <param name="label">the label</param>
    /// <param name="ordinal">the ordinal</param>
    /// <returns>the value</returns>
    public static ValueEnumBase Get(string label, int ordinal) => new(label, ordinal);

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => TypeInfo.TypeEnumUndefined;

    /// <inheritdoc/>
    public override int GetValueType() => Value.Enum;

    /// <inheritdoc/>
    public override string? GetString() => label;

    /// <inheritdoc/>
    public override int GetInt() => ordinal;

    /// <inheritdoc/>
    public override long GetLong() => ordinal;

    /// <inheritdoc/>
    public override BigInteger GetBigInteger() => new(ordinal);

    /// <inheritdoc/>
    public override BigDecimal GetBigDecimal() => BigDecimal.FromLong(ordinal);

    /// <inheritdoc/>
    public override float GetFloat() => ordinal;

    /// <inheritdoc/>
    public override double GetDouble() => ordinal;

    /// <inheritdoc/>
    public override int GetSignum() => Math.Sign(ordinal);

    /// <inheritdoc/>
    public override int GetMemory() => 120;

    /// <inheritdoc/>
    public override Value Add(Value v)
        => ConvertToIntType(null).Add(v.ConvertToIntType(null));

    /// <inheritdoc/>
    public override Value Subtract(Value v)
        => ConvertToIntType(null).Subtract(v.ConvertToIntType(null));

    /// <inheritdoc/>
    public override Value Multiply(Value v)
        => ConvertToIntType(null).Multiply(v.ConvertToIntType(null));

    /// <inheritdoc/>
    public override Value Divide(Value v, TypeInfo quotientType)
        => ConvertToIntType(null).Divide(v.ConvertToIntType(null), quotientType);

    /// <inheritdoc/>
    public override Value Modulus(Value v)
        => ConvertToIntType(null).Modulus(v.ConvertToIntType(null));

    /// <inheritdoc/>
    public override int CompareTypeSafe(Value v, CompareMode mode, CastDataProvider? provider)
        => GetInt().CompareTo(v.GetInt());

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => StringUtils.QuoteStringSql(builder, label);

    /// <summary>
    /// The ordinal as text, for the few places that print it.
    /// </summary>
    /// <returns>the ordinal</returns>
    internal string GetOrdinalText() => ordinal.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        // The reference's shape: 31, then the label's hash added, then the ordinal added. It
        // is written out rather than delegated because a hash built from JavaHashCode keeps
        // the number the same on every run, and .NET's string hash does not.
        unchecked
        {
            int results = 31;
            results += JavaHashCode(label);
            results += ordinal;
            return results;
        }
    }

    /// <inheritdoc/>
    public override bool Equals(object? other)
        => other is ValueEnumBase e && GetInt() == e.GetInt();

    private static int JavaHashCode(string? s)
    {
        if (s is null)
        {
            return 0;
        }

        unchecked
        {
            int hash = 0;
            foreach (char c in s)
            {
                hash = (31 * hash) + c;
            }

            return hash;
        }
    }
}
