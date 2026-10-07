/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueNull.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Implementation of NULL. NULL is not a regular data type.
 */
using System;
using System.IO;
using System.Text;
using H2Sharp.Message;

namespace H2Sharp.Values;

/// <summary>
/// Implementation of NULL. NULL is not a regular data type.
/// </summary>
public sealed class ValueNull : Value
{
    /// <summary>
    /// The main NULL instance.
    /// </summary>
    public static readonly ValueNull Instance = new();

    /// <summary>
    /// The precision of NULL.
    /// </summary>
    internal const int Precision = 1;

    /// <summary>
    /// The display size of the textual representation of NULL.
    /// </summary>
    internal const int DisplaySize = 4;

    private ValueNull()
    {
        // Don't allow construction.
    }

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => builder.Append("NULL");

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => TypeInfo.TypeNull;

    /// <inheritdoc/>
    public override int GetValueType() => Value.Null;

    /// <inheritdoc/>
    public override int GetMemory()
    {
        // Singleton value.
        return 0;
    }

    /// <inheritdoc/>
    public override string? GetString() => null;

    /// <inheritdoc/>
    public override TextReader? GetReader() => null;

    /// <inheritdoc/>
    public override byte[]? GetBytes() => null;

    /// <inheritdoc/>
    public override bool GetBoolean() => throw DbException.GetInternalError();

    /// <inheritdoc/>
    public override int GetInt() => throw DbException.GetInternalError();

    /// <inheritdoc/>
    public override long GetLong() => throw DbException.GetInternalError();

    /// <inheritdoc/>
    public override double GetDouble() => throw DbException.GetInternalError();

    /// <inheritdoc/>
    public override bool ContainsNull() => true;

    /// <inheritdoc/>
    public override int GetHashCode() => 0;

    /// <inheritdoc/>
    public override bool Equals(object? other) => ReferenceEquals(other, this);
}
