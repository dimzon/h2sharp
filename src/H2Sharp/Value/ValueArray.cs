/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.value.ValueArray.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// Implementation of the ARRAY data type.
/// </summary>
public sealed class ValueArray : ValueCollectionBase
{
    /// <summary>
    /// Empty array.
    /// </summary>
    public static readonly ValueArray Empty = Get(TypeInfo.TypeNull, Value.EmptyValues, null);

    private TypeInfo? type;

    private readonly TypeInfo componentType;

    private ValueArray(TypeInfo componentType, Value[] list, CastDataProvider? provider)
        : base(list)
    {
        int length = list.Length;
        if (length > Constants.MaxArrayCardinality)
        {
            string typeName = GetTypeName(GetValueType());
            throw DbException.GetValueTooLongException(typeName, typeName, length);
        }

        // The elements are cast to the component type here, in the constructor, and this is the
        // one place the list is written through. It is why the reference's javadoc for the
        // factories says the data is not cloned: the caller's array is the value's array, and
        // the constructor fills it in. A collection that has not been cast yet cannot answer
        // anything else consistently.
        for (int i = 0; i < length; i++)
        {
            list[i] = list[i].CastToValue(componentType, provider);
        }

        this.componentType = componentType;
    }

    /// <summary>
    /// Get or create an array value for the given value array, taking the component type to be
    /// the one that holds all of the elements. Do not clone the data.
    /// </summary>
    /// <param name="list">the value array</param>
    /// <param name="provider">the cast information provider</param>
    /// <returns>the value</returns>
    public static ValueArray Get(Value[] list, CastDataProvider? provider)
        => Get(TypeInfo.GetHigherType(list), list, provider);

    /// <summary>
    /// Get or create an array value for the given value array. Do not clone the data.
    /// </summary>
    /// <param name="componentType">the type of elements</param>
    /// <param name="list">the value array</param>
    /// <param name="provider">the cast information provider</param>
    /// <returns>the value</returns>
    public static ValueArray Get(TypeInfo componentType, Value[] list, CastDataProvider? provider)
        => new(componentType, list, provider);

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo()
    {
        TypeInfo? t = type;
        if (t is null)
        {
            this.type = t = TypeInfo.GetTypeInfo(GetValueType(), values.Length, 0, GetComponentType());
        }

        return t;
    }

    /// <inheritdoc/>
    public override int GetValueType() => Value.Array;

    /// <summary>
    /// The type every element of this array has.
    /// </summary>
    /// <returns>the component type</returns>
    public TypeInfo GetComponentType() => componentType;

    /// <inheritdoc/>
    public override string? GetString()
    {
        StringBuilder builder = new();
        _ = builder.Append('[');
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            // A NULL element renders as the word "null" here, not as nothing and not as SQL's
            // NULL: the reference appends what getString gave it, and getString of a NULL is a
            // null reference, and Java's StringBuilder writes those four letters. The SQL
            // rendering below is a different method and does say NULL.
            _ = builder.AppendJ(values[i].GetString());
        }

        return _ = builder.Append(']').ToString();
    }

    /// <inheritdoc/>
    public override int CompareTypeSafe(Value o, CompareMode mode, CastDataProvider? provider)
    {
        ValueArray v = (ValueArray)o;
        if (ReferenceEquals(values, v.values))
        {
            return 0;
        }

        int l = values.Length;
        int ol = v.values.Length;
        int len = Math.Min(l, ol);
        for (int i = 0; i < len; i++)
        {
            Value v1 = values[i];
            Value v2 = v.values[i];
            int comp = v1.CompareTo(v2, provider, mode);
            if (comp != 0)
            {
                return comp;
            }
        }

        return Math.Sign(l.CompareTo(ol));
    }

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => Dialect.AppendArray(builder, this, sqlFlags);

    /// <summary>
    /// The hash of the collection, which the base already computes and which this only has to
    /// name.
    /// </summary>
    /// <remarks>
    /// The reference does not write this method, because Java's compiler does not pair
    /// <c>equals</c> with a hash code the way C#'s does. C# refuses a class that overrides
    /// <c>Equals</c> without overriding <c>GetHashCode</c>, and it is right to: the hash of a
    /// collection is exactly what the base's is, so forwarding is not a wrapper, it is the
    /// requirement made visible.
    /// </remarks>
    /// <returns>the hash code</returns>
    public sealed override int GetHashCode() => base.GetHashCode();

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is not ValueArray v)
        {
            return false;
        }

        if (ReferenceEquals(values, v.values))
        {
            return true;
        }

        int len = values.Length;
        if (len != v.values.Length)
        {
            return false;
        }

        for (int i = 0; i < len; i++)
        {
            if (!values[i].Equals(v.values[i]))
            {
                return false;
            }
        }

        return true;
    }
}
