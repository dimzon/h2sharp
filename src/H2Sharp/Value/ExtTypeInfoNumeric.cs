/*
 * Copyright 2004-2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Ported from org.h2.value.ExtTypeInfoNumeric.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;

namespace H2Sharp.Values;

/// <summary>
/// Extended parameters of the NUMERIC data type.
/// </summary>
/// <remarks>
/// There is exactly one instance. NUMERIC is the only type whose parameterised factory
/// inspects the extended type, and it does so to decide whether to keep it: a caller that
/// passed something else gets null instead, which is why this type carries no state at all.
/// </remarks>
public sealed class ExtTypeInfoNumeric : ExtTypeInfo
{
    /// <summary>
    /// The DECIMAL data type.
    /// </summary>
    public static readonly ExtTypeInfoNumeric Decimal = new();

    private ExtTypeInfoNumeric()
    {
    }

    /// <inheritdoc/>
    public override System.Text.StringBuilder GetSql(
        System.Text.StringBuilder builder, int sqlFlags) => builder.Append("DECIMAL");

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ExtTypeInfoNumeric;

    /// <inheritdoc/>
    public override int GetHashCode() => 0;
}
