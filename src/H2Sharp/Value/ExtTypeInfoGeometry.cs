/*
 * Ported from org.h2.value.ExtTypeInfoGeometry.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;
using H2Sharp.Util.Geometry;

namespace H2Sharp.Values;

/// <summary>
/// Extended parameters of the GEOMETRY data type, which is the only kind and the only SRID a column
/// may be narrowed to.
/// </summary>
/// <remarks>
/// A GEOMETRY column is written with these in parentheses after the type, so they have to render
/// themselves, and they are part of the type: two columns of the same geometry kind but of different
/// reference systems are not the same type.
/// </remarks>
public sealed class ExtTypeInfoGeometry : ExtTypeInfo
{
    private readonly int type;

    private readonly int? srid;

    private static StringBuilder ToSql(StringBuilder builder, int type, int? srid)
    {
        if (type == 0 && srid is null)
        {
            return builder;
        }

        _ = builder.Append('(');
        if (type == 0)
        {
            _ = builder.Append("GEOMETRY");
        }
        else
        {
            EWKTUtils.FormatGeometryTypeAndDimensionSystem(builder, type);
        }

        if (srid is not null)
        {
            _ = builder.Append(", ").Append(srid.Value);
        }

        return builder.Append(')');
    }

    /// <summary>
    /// Creates new instance of extended parameters of the GEOMETRY data type.
    /// </summary>
    /// <param name="type">the kind and the dimension system, or 0 when not narrowed to any</param>
    /// <param name="srid">the SRID, or null when not narrowed to one</param>
    public ExtTypeInfoGeometry(int type, int? srid)
    {
        this.type = type;
        this.srid = srid;
    }

    /// <summary>
    /// The kind and the dimension system, or 0 when not narrowed to any.
    /// </summary>
    public int GetTypeAndDimensionSystem() => type;

    /// <summary>
    /// The SRID, or null when not narrowed to one.
    /// </summary>
    public int? GetSrid() => srid;

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        unchecked
        {
            return (31 * (srid?.GetHashCode() ?? 0)) + type;
        }
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => ReferenceEquals(this, obj)
            || (obj is ExtTypeInfoGeometry other && type == other.type && srid == other.srid);

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags) => ToSql(builder, type, srid);
}
