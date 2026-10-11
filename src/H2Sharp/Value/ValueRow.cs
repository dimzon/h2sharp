/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.value.ValueRow.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// Row value.
/// </summary>
public sealed class ValueRow : ValueCollectionBase
{
    /// <summary>
    /// Empty row.
    /// </summary>
    public static readonly ValueRow Empty = Get(Value.EmptyValues);

    private TypeInfo? type;

    /// <summary>
    /// Replaces a null with the NULL value.
    /// </summary>
    /// <remarks>
    /// A row never holds a null: NULL is a value in this engine, not the absence of one, and a
    /// row that held a real null would fail its own comparison and its own hash. So a list that
    /// arrives from a statement being built is normalized once, here, rather than every time
    /// something reads the row.
    /// </remarks>
    /// <param name="list">the values</param>
    /// <returns>the same values, with no nulls in them</returns>
    private static Value[] Normalize(Value?[] list)
    {
        Value[] values = new Value[list.Length];
        for (int i = 0; i < list.Length; i++)
        {
            values[i] = list[i] ?? ValueNull.Instance;
        }

        return values;
    }

    private ValueRow(TypeInfo? type, Value?[] list)
        : base(Normalize(list))
    {
        int degree = list.Length;
        if (degree > Constants.MaxColumns)
        {
            throw DbException.Get(
                ErrorCode.TooManyColumns1,
                Constants.MaxColumns.ToString(CultureInfo.InvariantCulture));
        }

        if (type is not null)
        {
            // A row's type carries its fields, so a type that is not a ROW, or a ROW of a
            // different width, describes a different thing entirely. The reference treats this
            // as a bug in its own caller rather than a user error.
            if (type.GetValueType() != Value.Row || ((ExtTypeInfoRow)type.GetExtTypeInfo()!).FieldCount != degree)
            {
                throw DbException.GetInternalError();
            }

            this.type = type;
        }
    }

    /// <summary>
    /// Get or create a row value for the given value array. Do not clone the data.
    /// </summary>
    /// <param name="list">the value array</param>
    /// <returns>the value</returns>
    public static ValueRow Get(Value?[] list) => new(null, list);

    /// <summary>
    /// Makes a row of values out of a list that may hold nulls.
    /// </summary>
    /// <remarks>
    /// A second entry point rather than a nullable parameter on the first: a row read out of
    /// storage never holds a null — NULL is <see cref="ValueNull.Instance"/> there — while a row
    /// a statement is still building does, and the two are different things to the caller. A
    /// caller that passes a null here gets the NULL value's behaviour through the row, not a
    /// null-pointer exception from a method that promised a row.
    /// </remarks>
    /// <param name="list">the values</param>
    /// <returns>the row</returns>
    public static ValueRow GetNullable(Value?[] list) => new(null, list);

    /// <summary>
    /// Get or create a typed row value for the given value array. Do not clone the data.
    /// </summary>
    /// <param name="extTypeInfo">the extended data type information</param>
    /// <param name="list">the value array</param>
    /// <returns>the value</returns>
    public static ValueRow Get(ExtTypeInfoRow extTypeInfo, Value[] list)
        => Get(new TypeInfo(Value.Row, -1, -1, extTypeInfo), list);

    /// <summary>
    /// Get or create a typed row value for the given value array. Do not clone the data.
    /// </summary>
    /// <param name="typeInfo">the data type information</param>
    /// <param name="list">the value array</param>
    /// <returns>the value</returns>
    public static ValueRow Get(TypeInfo typeInfo, Value[] list) => new(typeInfo, list);

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo()
    {
        TypeInfo? t = type;
        if (t is null)
        {
            // The reference hands the values straight to ExtTypeInfoRow, which asks each one for
            // its type. Ours takes the types, so they are collected here — a loop rather than a
            // projection, because this runs once per untyped row and allocates either way.
            TypeInfo[] fieldTypes = new TypeInfo[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                fieldTypes[i] = values[i].GetTypeInfo();
            }

            this.type = t = TypeInfo.GetTypeInfo(Value.Row, 0, 0, new ExtTypeInfoRow(fieldTypes));
        }

        return t;
    }

    /// <inheritdoc/>
    public override int GetValueType() => Value.Row;

    /// <inheritdoc/>
    public override string? GetString()
    {
        StringBuilder builder = new();
        _ = builder.Append("ROW (");
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            // A NULL element renders as the word "null" for the same reason as in ValueArray:
            // Java's StringBuilder writes a null reference as those four letters, and .NET
            // writes nothing at all.
            _ = builder.AppendJ(values[i].GetString());
        }

        return _ = builder.Append(')').ToString();
    }

    /// <inheritdoc/>
    public override int CompareTypeSafe(Value o, CompareMode mode, CastDataProvider? provider)
    {
        ValueRow v = (ValueRow)o;
        if (ReferenceEquals(values, v.values))
        {
            return 0;
        }

        int len = values.Length;
        if (len != v.values.Length)
        {
            throw DbException.Get(ErrorCode.ColumnCountDoesNotMatch);
        }

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

        return 0;
    }

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        _ = builder.Append("ROW (");
        int length = values.Length;
        for (int i = 0; i < length; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            values[i].GetSql(builder, sqlFlags);
        }

        return builder.Append(')');
    }

    /// <summary>
    /// Creates a copy of this row but the new instance will contain the values according to
    /// <paramref name="newOrder"/>, and its type's fields will be reordered the same way.
    /// </summary>
    /// <param name="newOrder">
    /// array of indexes to create the new values array; for example
    /// ROW('a', 'b') with [1, 0] gives ROW('b', 'a')
    /// </param>
    /// <returns>the row with the fields in the new order</returns>
    /// <exception cref="H2SharpException">if the order does not have one index per field</exception>
    public ValueRow CloneWithOrder(int[] newOrder)
    {
        int length = values.Length;
        if (newOrder.Length != length)
        {
            throw DbException.GetInternalError("Length of the new orders is different than values count.");
        }

        Value[] newValues = new Value[length];
        for (int i = 0; i < length; i++)
        {
            newValues[i] = values[newOrder[i]];
        }

        // The fields move with their values: a row type is a list of named types, and renaming
        // the values without renaming the types would describe a row that does not exist.
        ExtTypeInfoRow typeInfoRow = (ExtTypeInfoRow)GetTypeInfo().GetExtTypeInfo()!;
        List<KeyValuePair<string, TypeInfo>> fields = [.. typeInfoRow.GetFields()];
        OrderedStringMap<TypeInfo> newFields = new(length);
        for (int i = 0; i < length; i++)
        {
            KeyValuePair<string, TypeInfo> field = fields[newOrder[i]];
            newFields.Put(field.Key, field.Value);
        }

        ExtTypeInfoRow newTypeInfoRow = new(newFields);
        TypeInfo t = GetTypeInfo();
        TypeInfo newType = new(
            t.GetValueType(), t.GetDeclaredPrecision(), t.GetDeclaredScale(), newTypeInfoRow);

        return new ValueRow(newType, newValues);
    }

    /// <summary>
    /// The hash of the collection, which the base already computes and which this only has to
    /// name.
    /// </summary>
    /// <remarks>
    /// Written for the same reason as the one in <c>ValueArray</c>: C# pairs <c>Equals</c> with
    /// a hash code, Java does not, and the pair is a forwarding call rather than a second
    /// definition of the same number.
    /// </remarks>
    /// <returns>the hash code</returns>
    public sealed override int GetHashCode() => base.GetHashCode();

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is not ValueRow v)
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
