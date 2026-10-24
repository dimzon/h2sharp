/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueBinary.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// Implementation of the BINARY data type.
/// </summary>
public sealed class ValueBinary : ValueBytesBase
{
    private ValueBinary(byte[] v)
        : base(v)
    {
    }

    /// <summary>
    /// Get or create a BINARY value for the given byte array, cloning the data.
    /// </summary>
    /// <param name="b">the byte array</param>
    /// <returns>the value</returns>
    public static ValueBinary Get(byte[] b) => GetNoCopy(Utils.CloneByteArray(b));

    /// <summary>
    /// Get or create a BINARY value for the given byte array, without cloning the data.
    /// </summary>
    /// <param name="b">the byte array</param>
    /// <returns>the value</returns>
    /// <remarks>
    /// Unlike the varying type this has no empty value of its own: an empty BINARY is a value
    /// like any other, because its length is part of its type and the two are not the same thing.
    /// </remarks>
    public static ValueBinary GetNoCopy(byte[] b)
    {
        ValueBinary obj = new(b);
        if (b.Length > SysProperties.ObjectCacheMaxPerElementSize)
        {
            return obj;
        }

        return (ValueBinary)Value.Cache(obj)!;
    }

    /// <inheritdoc/>
    public override int GetValueType() => Value.Binary;

    /// <inheritdoc/>
    /// <remarks>
    /// A hex literal is a VARBINARY, so a BINARY has to say what it is: the reference wraps it
    /// in a CAST, and the length it writes is at least 1 because a BINARY of length 0 is spelled
    /// that way rather than as BINARY(0). A caller that sets NO_CASTS gets the bare literal, which
    /// is what a store that already knows the type asks for.
    /// </remarks>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => Dialect.AppendBinary(builder, this, sqlFlags);

    /// <inheritdoc/>
    /// <remarks>
    /// Bytes that are not valid UTF-8 still produce text rather than an error, and it is the
    /// reference's text: one replacement character where a whole malformed sequence stands.
    /// See <see cref="JavaUtf8"/>.
    /// </remarks>
    public sealed override string GetString() => JavaUtf8.GetString(value);
}
