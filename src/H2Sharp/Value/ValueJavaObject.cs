/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.value.ValueJavaObject.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// Implementation of the JAVA_OBJECT data type.
/// </summary>
/// <remarks>
/// The bytes here are whatever a serializer wrote and this port never looks inside them, which
/// is the whole of the type's behaviour: it is a byte string that refuses to be text. That
/// refusal is why <see cref="GetString"/> throws instead of decoding — an object has no text,
/// and rendering one would invent a representation that nothing can read back.
/// </remarks>
public sealed class ValueJavaObject : ValueBytesBase
{
    /// <summary>
    /// The empty object, which is a value like any other: unlike the varying types there is no
    /// second thing an empty value could mean.
    /// </summary>
    private static readonly ValueJavaObject Empty = new([]);

    private ValueJavaObject(byte[] v)
        : base(v)
    {
    }

    /// <summary>
    /// Get or create a java object value for the given byte array, without cloning the data.
    /// </summary>
    /// <param name="b">the byte array</param>
    /// <returns>the value</returns>
    public static ValueJavaObject GetNoCopy(byte[] b)
    {
        if (b.Length == 0)
        {
            return Empty;
        }

        ValueJavaObject obj = new(b);
        if (b.Length > SysProperties.ObjectCacheMaxPerElementSize)
        {
            return obj;
        }

        return (ValueJavaObject)Value.Cache(obj)!;
    }

    /// <summary>
    /// Get or create a java object value for the given byte array, cloning the data.
    /// </summary>
    /// <param name="b">the byte array</param>
    /// <returns>the value</returns>
    public static ValueJavaObject Get(byte[] b) => GetNoCopy(Utils.CloneByteArray(b));

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => TypeInfo.TypeJavaObject;

    /// <inheritdoc/>
    public override int GetValueType() => Value.JavaObject;

    /// <inheritdoc/>
    public override string? GetString()
        => throw DbException.Get(
            ErrorCode.DataConversionError1, "JAVA_OBJECT to CHARACTER VARYING");

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => Dialect.AppendJavaObject(builder, this, sqlFlags);
}
