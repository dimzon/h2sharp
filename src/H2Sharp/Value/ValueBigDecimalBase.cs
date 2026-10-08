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
/// precision a numeric type may declare is refused where it is created rather than where it is
/// written, so the column never has to find out. The reference also insists the decimal is
/// exactly the platform's class and refuses a subclass; the port has no subclasses to refuse, and
/// the check would be a claim about types rather than about values.
/// <para>
/// The decimal is null for the three values of DECFLOAT that are not numbers, which is why the
/// check is written the way the reference writes it and why the classes that can hold one say so
/// themselves instead of the base saying it for them.
/// </para>
/// </remarks>
public abstract class ValueBigDecimalBase : Value
{
    /// <summary>
    /// The value, or null for a value that is not a number.
    /// </summary>
    protected readonly BigDecimal? value;

    /// <summary>
    /// The type of this value, worked out once.
    /// </summary>
    protected TypeInfo? type;

    /// <summary>
    /// Creates a decimal based value.
    /// </summary>
    /// <param name="v">the decimal value, or null for a value that is not a number</param>
    /// <exception cref="H2SharpException">if the value is wider than any numeric type allows</exception>
    protected ValueBigDecimalBase(BigDecimal? v)
    {
        value = v;
        if (v is not null)
        {
            int precision = v.Precision();
            if (precision > Constants.MaxNumericPrecision)
            {
                throw DbException.GetValueTooLongException(
                    GetTypeName(GetValueType()), v.ToString(), precision);
            }
        }
    }
}
