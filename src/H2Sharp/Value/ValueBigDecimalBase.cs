/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueBigDecimalBase.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// Base class for values built on an arbitrary precision decimal.
/// </summary>
/// <remarks>
/// The check here is the one that decides what may be stored at all: a value wider than the
/// precision a NUMERIC may declare is refused where it is created rather than where it is written,
/// so the column never has to find out. The reference also insists the decimal is exactly the
/// platform's class and refuses a subclass; the port has no subclasses to refuse, and the check
/// would be a claim about types rather than about values.
/// </remarks>
public abstract class ValueBigDecimalBase : Value
{
    /// <summary>
    /// The value.
    /// </summary>
    protected readonly BigDecimal value;

    private TypeInfo? type;

    /// <summary>
    /// Creates a decimal based value.
    /// </summary>
    /// <param name="v">the decimal value</param>
    /// <exception cref="H2SharpException">if the value is wider than any numeric type allows</exception>
    protected ValueBigDecimalBase(BigDecimal v)
    {
        value = v;
        int precision = v.Precision();
        if (precision > Constants.MaxNumericPrecision)
        {
            throw DbException.GetValueTooLongException(
                GetTypeName(GetValueType()), v.ToString(), precision);
        }
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo()
    {
        TypeInfo? cached = type;
        if (cached is null)
        {
            type = cached = new TypeInfo(GetValueType(), value.Precision(), value.Scale, null);
        }

        return cached;
    }

    /// <inheritdoc/>
    public override int GetSignum() => value.Signum();

    /// <inheritdoc/>
    public override H2Sharp.Util.BigDecimal GetBigDecimal() => value;

    /// <inheritdoc/>
    public override double GetDouble() => value.ToDouble();

    /// <inheritdoc/>
    public override float GetFloat() => (float)value.ToDouble();

    /// <inheritdoc/>
    public sealed override bool Equals(object? other)
        => other is not null
            && GetType() == other.GetType()
            && value.Equals(((ValueBigDecimalBase)other).value);

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
            int decimalHash = value.GetHashCode();
            return classHash * 31 + decimalHash;
        }
    }
}
