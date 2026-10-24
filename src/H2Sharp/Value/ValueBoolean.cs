/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueBoolean.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Numerics;
using System.Text;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// Implementation of the BOOLEAN data type.
/// </summary>
/// <remarks>
/// There are two of these and they are both kept, so equality is the instance: two booleans that
/// hold the same thing are the same object, and a port that built a boolean per call would hand out
/// equal values with different identities and pay for both.
/// </remarks>
public sealed class ValueBoolean : Value
{
    /// <summary>
    /// The precision in digits.
    /// </summary>
    public const int Precision = 1;

    /// <summary>
    /// The maximum display size of a boolean, for example FALSE.
    /// </summary>
    public const int DisplaySize = 5;

    /// <summary>
    /// The true value.
    /// </summary>
    public static readonly ValueBoolean True = new(true);

    /// <summary>
    /// The false value.
    /// </summary>
    public static readonly ValueBoolean False = new(false);

    private readonly bool value;

    private ValueBoolean(bool v)
    {
        value = v;
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => TypeInfo.TypeBoolean;

    /// <inheritdoc/>
    public override int GetValueType() => Value.Boolean;

    /// <inheritdoc/>
    /// <remarks>
    /// Both values exist from the start, so a boolean costs nothing to keep.
    /// </remarks>
    public override int GetMemory() => 0;

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => Dialect.AppendBoolean(builder, this, sqlFlags);

    /// <inheritdoc/>
    public override string? GetString() => value ? "TRUE" : "FALSE";

    /// <inheritdoc/>
    public override bool GetBoolean() => value;

    /// <inheritdoc/>
    public override sbyte GetByte() => value ? (sbyte)1 : (sbyte)0;

    /// <inheritdoc/>
    public override short GetShort() => value ? (short)1 : (short)0;

    /// <inheritdoc/>
    public override int GetInt() => value ? 1 : 0;

    /// <inheritdoc/>
    public override long GetLong() => value ? 1L : 0L;

    /// <inheritdoc/>
    public override BigInteger GetBigInteger() => value ? BigInteger.One : BigInteger.Zero;

    /// <inheritdoc/>
    public override H2Sharp.Util.BigDecimal GetBigDecimal() => value ? BigDecimal.One : BigDecimal.Zero;

    /// <inheritdoc/>
    public override float GetFloat() => value ? 1f : 0f;

    /// <inheritdoc/>
    public override double GetDouble() => value ? 1d : 0d;

    /// <inheritdoc/>
    public override Value Negate() => value ? False : True;

    /// <inheritdoc/>
    public override int CompareTypeSafe(Value o, CompareMode mode, CastDataProvider? provider)
        => value.CompareTo(((ValueBoolean)o).value);

    /// <inheritdoc/>
    public override int GetHashCode() => value ? 1 : 0;

    /// <summary>
    /// Get the boolean value for the given boolean.
    /// </summary>
    /// <param name="b">the boolean</param>
    /// <returns>the value</returns>
    public static ValueBoolean Get(bool b) => b ? True : False;

    /// <inheritdoc/>
    public sealed override bool Equals(object? other)
    {
        // There are only ever two instances, so the instance must match.
        return ReferenceEquals(this, other);
    }
}
