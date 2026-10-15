/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ExtTypeInfoRow.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// Extended parameters of the ROW data type.
/// </summary>
/// <remarks>
/// <para>
/// The reference takes its fields as <c>Typed[]</c>, the interface a value exposes its type
/// through. That interface is not ported and nothing needs it here, so this takes
/// <see cref="TypeInfo"/> directly. A row field can only be a type, so the wider signature
/// would not have admitted anything this one rejects.
/// </para>
/// <para>
/// The field list is an insertion ordered map rather than a list, because the reference
/// stores a map and its equality is defined over the map's entries in order.
/// </para>
/// </remarks>
public sealed class ExtTypeInfoRow : ExtTypeInfo
{
    private readonly OrderedStringMap<TypeInfo> fields;

    private int hash;

    /// <summary>
    /// Creates new instance of extended parameters of ROW data type.
    /// </summary>
    /// <param name="fields">fields</param>
    public ExtTypeInfoRow(TypeInfo[] fields)
        : this(fields, fields.Length)
    {
    }

    /// <summary>
    /// Creates new instance of extended parameters of ROW data type.
    /// </summary>
    /// <param name="fields">fields</param>
    /// <param name="degree">number of fields to use</param>
    /// <exception cref="H2SharpException">if there are more fields than a table may have</exception>
    public ExtTypeInfoRow(TypeInfo[] fields, int degree)
    {
        if (degree > Constants.MaxColumns)
        {
            throw DbException.Get(
                ErrorCode.TooManyColumns1,
                Constants.MaxColumns.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // The reference sizes the map for a load factor of 0.75; the framework type takes a
        // capacity, and the exact number only affects when it grows, so the hint is kept
        // rather than dropped.
        OrderedStringMap<TypeInfo> map = new(
            (int)Math.Ceiling(degree / .75));
        for (int i = 0; i < degree;)
        {
            TypeInfo t = fields[i];
            map.Put("C" + (++i).ToString(System.Globalization.CultureInfo.InvariantCulture), t);
        }

        this.fields = map;
    }

    /// <summary>
    /// Creates new instance of extended parameters of ROW data type.
    /// </summary>
    /// <param name="fields">the fields, in order</param>
    /// <exception cref="H2SharpException">if there are more fields than a table may have</exception>
    public ExtTypeInfoRow(OrderedStringMap<TypeInfo> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Count > Constants.MaxColumns)
        {
            throw DbException.Get(
                ErrorCode.TooManyColumns1,
                Constants.MaxColumns.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        this.fields = fields;
    }

    /// <summary>
    /// Returns fields.
    /// </summary>
    /// <remarks>
    /// The reference returns the map's entry set, which is what a
    /// <c>LinkedHashMap</c> iterates in insertion order; the order is the point, since it is
    /// the order the fields are rendered and compared in.
    /// </remarks>
    /// <returns>the fields, in order</returns>
    public IEnumerable<KeyValuePair<string, TypeInfo>> GetFields() => fields.Entries;

    /// <summary>
    /// How many fields this row type has.
    /// </summary>
    /// <remarks>
    /// The reference asks <c>getFields().size()</c>, which for a <c>LinkedHashMap</c> is a
    /// constant lookup. So is this one, but only because it is written this way: counting
    /// <see cref="GetFields"/> instead would walk the whole map, and this is asked on every
    /// construction of a typed row to check that the type and the values agree.
    /// </remarks>
    /// <returns>the number of fields</returns>
    public int FieldCount => fields.Count;

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        _ = builder.Append('(');
        bool f = false;
        foreach (KeyValuePair<string, TypeInfo> field in fields.Entries)
        {
            if (f)
            {
                _ = builder.Append(", ");
            }

            f = true;
            _ = ParserUtil.QuoteIdentifier(_ = builder, field.Key, sqlFlags).Append(' ');
            _ = field.Value.GetSql(builder, sqlFlags);
        }

        return builder.Append(')');
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int h = hash;
        if (h != 0)
        {
            return h;
        }

        h = 67_378_403;
        foreach (KeyValuePair<string, TypeInfo> entry in fields.Entries)
        {
            h = ((h * 31) + Utils.JavaHashCode(entry.Key)) * 37 + entry.Value.GetHashCode();
        }

        hash = h;
        return h;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj is not ExtTypeInfoRow other)
        {
            return false;
        }

        if (fields.Count != other.fields.Count)
        {
            return false;
        }

        using IEnumerator<KeyValuePair<string, TypeInfo>> i1 = fields.Entries.GetEnumerator();
        using IEnumerator<KeyValuePair<string, TypeInfo>> i2 = other.fields.Entries.GetEnumerator();
        while (i1.MoveNext() && i2.MoveNext())
        {
            KeyValuePair<string, TypeInfo> e1 = i1.Current;
            KeyValuePair<string, TypeInfo> e2 = i2.Current;
            if (!string.Equals(e1.Key, e2.Key, StringComparison.Ordinal)
                || !e1.Value.Equals(e2.Value))
            {
                return false;
            }
        }

        return true;
    }
}
