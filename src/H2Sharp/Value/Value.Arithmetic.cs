/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.Value.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Arithmetic on values.
 *
 * Every operation here is refused by the base class and answered by the concrete type. That
 * is the reference's arrangement too, and it is not laziness: a value cannot add anything,
 * a date cannot multiply, and the refusal is a real answer rather than a missing one. The
 * exception names the data type and the operation, which is what the reference reports.
 */
namespace H2Sharp.Values;

/// <summary>
/// A value of some type. Every value knows its type number, and every type knows how to
/// describe itself as SQL.
/// </summary>
public abstract partial class Value
{
    /// <summary>
    /// Add a value and return the result.
    /// </summary>
    /// <param name="v">the value to add</param>
    /// <returns>the result</returns>
    public virtual Value Add(Value v) => throw GetUnsupportedExceptionForOperation("+");

    /// <summary>
    /// Subtract a value and return the result.
    /// </summary>
    /// <param name="v">the value to subtract</param>
    /// <returns>the result</returns>
    public virtual Value Subtract(Value v) => throw GetUnsupportedExceptionForOperation("-");

    /// <summary>
    /// Divide by another value.
    /// </summary>
    /// <param name="v">the divisor</param>
    /// <param name="quotientType">the type the quotient must have</param>
    /// <returns>the result</returns>
    public virtual Value Divide(Value v, TypeInfo quotientType)
        => throw GetUnsupportedExceptionForOperation("/");

    /// <summary>
    /// Multiply by another value and return the result.
    /// </summary>
    /// <param name="v">the value to multiply by</param>
    /// <returns>the result</returns>
    public virtual Value Multiply(Value v) => throw GetUnsupportedExceptionForOperation("*");

    /// <summary>
    /// Return the remainder of the division.
    /// </summary>
    /// <param name="v">the divisor</param>
    /// <returns>the remainder</returns>
    public virtual Value Modulus(Value v) => throw GetUnsupportedExceptionForOperation("%");

    /// <summary>
    /// Negate this value and return the result.
    /// </summary>
    /// <returns>the result</returns>
    public virtual Value Negate() => throw GetUnsupportedExceptionForOperation("NEG");
}
