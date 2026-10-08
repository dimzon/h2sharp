/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueVarbinary.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Engine;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// Implementation of the BINARY VARYING data type.
/// </summary>
public sealed class ValueVarbinary : ValueBytesBase
{
    /// <summary>
    /// Empty value.
    /// </summary>
    public static readonly ValueVarbinary Empty = new(Utils.EmptyBytes);

    private ValueVarbinary(byte[] v)
        : base(v)
    {
    }

    /// <summary>
    /// Get or create a VARBINARY value for the given byte array, cloning the data.
    /// </summary>
    /// <param name="b">the byte array</param>
    /// <returns>the value</returns>
    public static ValueVarbinary Get(byte[] b)
    {
        if (b.Length == 0)
        {
            return Empty;
        }

        return GetNoCopy(Utils.CloneByteArray(b));
    }

    /// <summary>
    /// Get or create a VARBINARY value for the given byte array, without cloning the data.
    /// </summary>
    /// <param name="b">the byte array</param>
    /// <returns>the value</returns>
    public static ValueVarbinary GetNoCopy(byte[] b)
    {
        if (b.Length == 0)
        {
            return Empty;
        }

        ValueVarbinary obj = new(b);
        if (b.Length > SysProperties.ObjectCacheMaxPerElementSize)
        {
            return obj;
        }

        return (ValueVarbinary)Value.Cache(obj)!;
    }

    /// <inheritdoc/>
    public override int GetValueType() => Value.Varbinary;

    /// <inheritdoc/>
    /// <remarks>
    /// Bytes that are not valid UTF-8 still produce text rather than an error, but the text has
    /// to be the reference's text: it puts one replacement character where a whole malformed
    /// sequence stands, and .NET would put one per byte. See <see cref="JavaUtf8"/>.
    /// </remarks>
    public sealed override string GetString() => JavaUtf8.GetString(value);
}
