/*
 * Ported from org.h2.value.ValueGeometry.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Util.Geometry;

namespace H2Sharp.Values;

/// <summary>
/// The GEOMETRY value: canonical EWKB in the bytes, and everything else read out of it.
/// </summary>
/// <remarks>
/// <para>
/// The bytes are always the canonical EWKB, because that is the only form H2 stores: an EWKT read
/// in and an EWKB read in both go through the writers, which reduce the dimension system to the
/// smallest one that holds the geometry and write it with the flags of EWKB.
/// </para>
/// <para>
/// The envelope is not in the bytes and is not computed until it is asked for, because walking the
/// bytes of a large geometry to find its bounding box is not worth it for a query that never asks.
/// An envelope of no values is how "not computed yet" is told from "there is none".
/// </para>
/// <para>
/// The reference can also hand out a geometry object of JTS, which this port does not have: there is
/// no geometry object model in the reference either, only the two formats, and JTS is the only thing
/// that ever built one.
/// </para>
/// </remarks>
public sealed class ValueGeometry : ValueBytesBase
{
    private static readonly double[] UnknownEnvelope = [];

    private readonly int typeAndDimensionSystem;

    private readonly int srid;

    private double[]? envelope;

    private ValueGeometry(byte[] bytes, double[]? envelope)
        : base(bytes)
    {
        if (bytes.Length < 9 || bytes[0] != 0)
        {
            throw DbException.Get(ErrorCode.DataConversionError1, StringUtils.ConvertBytesToHex(bytes));
        }

        value = bytes;
        this.envelope = envelope;
        int t = Bits.IntFromBigEndian(bytes, 1);
        srid = (t & EWKBUtils.EwkbSrid) != 0 ? Bits.IntFromBigEndian(bytes, 5) : 0;
        typeAndDimensionSystem = (t & 0xFFFF) % 1_000 + (EWKBUtils.Type2DimensionSystem(t) * 1_000);
    }

    /// <summary>
    /// A geometry value for the text.
    /// </summary>
    /// <param name="s">the WKT or EWKT</param>
    /// <returns>the value</returns>
    public static Value Get(string s)
    {
        try
        {
            return Cache(new ValueGeometry(EWKTUtils.EWKT2EWKB(s), UnknownEnvelope));
        }
        catch (Exception e) when (IsRefusal(e))
        {
            throw DbException.Get(ErrorCode.DataConversionError1, e, s);
        }
    }

    /// <summary>
    /// A geometry value for the bytes, which are taken as they are.
    /// </summary>
    /// <param name="bytes">the EWKB, which is not modified</param>
    /// <returns>the value</returns>
    public static Value Get(byte[] bytes) => Cache(new ValueGeometry(bytes, UnknownEnvelope));

    /// <summary>
    /// A geometry value for bytes that are any supported EWKB, canonical or not.
    /// </summary>
    /// <param name="bytes">the EWKB</param>
    /// <returns>the value</returns>
    public static Value GetFromEWKB(byte[] bytes)
    {
        try
        {
            return Cache(new ValueGeometry(EWKBUtils.EWKB2EWKB(bytes), UnknownEnvelope));
        }
        catch (Exception e) when (IsRefusal(e))
        {
            throw DbException.Get(
                ErrorCode.DataConversionError1, e, StringUtils.ConvertBytesToHex(bytes));
        }
    }

    /// <remarks>
    /// The reference catches every runtime exception here, which in .NET would also catch the
    /// argument exceptions of a bug in this file. Only the ones a reader raises for bad input are
    /// caught, so a mistake of ours is not dressed up as a data conversion error.
    /// </remarks>
    private static bool IsRefusal(Exception e)
        => e is ArgumentException
            or FormatException
            or IndexOutOfRangeException
            or ArithmeticException
            or H2SharpException;

    /// <summary>
    /// A geometry value for an envelope.
    /// </summary>
    /// <param name="envelope">the envelope, which is not modified</param>
    /// <returns>the value, or NULL</returns>
    public static Value FromEnvelope(double[]? envelope)
        => envelope is not null
            ? Cache(new ValueGeometry(EWKBUtils.Envelope2WKB(envelope)!, envelope))
            : ValueNull.Instance;

    /// <summary>
    /// The kind and the dimension system, in the OGC code form of the kind plus the dimension system
    /// times a thousand.
    /// </summary>
    /// <returns>the kind and the dimension system</returns>
    public int GetTypeAndDimensionSystem() => typeAndDimensionSystem;

    /// <summary>
    /// The kind of the geometry, without its dimensions.
    /// </summary>
    /// <returns>the kind</returns>
    public int GetGeometryType() => typeAndDimensionSystem % 1_000;

    /// <summary>
    /// The smallest dimension system this geometry fits in.
    /// </summary>
    /// <returns>the dimension system</returns>
    public int GetDimensionSystem() => typeAndDimensionSystem / 1_000;

    /// <summary>
    /// The reference system of the geometry.
    /// </summary>
    /// <returns>the SRID</returns>
    public int GetSrid() => srid;

    /// <summary>
    /// The bounding box, which is computed here the first time it is asked for and not modified.
    /// </summary>
    /// <returns>the envelope as X min, X max, Y min, Y max, or null when there is none</returns>
    public double[]? GetEnvelopeNoCopy()
    {
        if (ReferenceEquals(envelope, UnknownEnvelope))
        {
            GeometryUtils.EnvelopeTarget target = new();
            EWKBUtils.ParseEWKB(value, target);
            envelope = target.GetEnvelope();
        }

        return envelope;
    }

    /// <summary>
    /// Whether this geometry's bounding box touches another one's.
    /// </summary>
    /// <param name="r">the other geometry</param>
    /// <returns>true if the two overlap</returns>
    public bool IntersectsBoundingBox(ValueGeometry r)
        => GeometryUtils.Intersects(GetEnvelopeNoCopy(), r.GetEnvelopeNoCopy());

    /// <summary>
    /// The smallest bounding box that holds both.
    /// </summary>
    /// <param name="r">the other geometry</param>
    /// <returns>the union of the two envelopes</returns>
    public Value GetEnvelopeUnion(ValueGeometry r)
        => FromEnvelope(GeometryUtils.Union(GetEnvelopeNoCopy(), r.GetEnvelopeNoCopy()));

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => TypeInfo.TypeGeometry;

    /// <inheritdoc/>
    public override int GetValueType() => Value.Geometry;

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        _ = builder.Append("GEOMETRY ");
        if ((sqlFlags & HasSql.AddPlanInformation) != 0)
        {
            EWKBUtils.ParseEWKB(value, new EWKTUtils.EWKTTarget(_ = builder.Append('\''), GetDimensionSystem()));
            _ = builder.Append('\'');
        }
        else
        {
            _ = base.GetSql(builder, HasSql.DefaultSqlFlags);
        }

        return builder;
    }

    /// <inheritdoc/>
    public override string GetString() => EWKTUtils.EWKB2EWKT(value, GetDimensionSystem());

    /// <inheritdoc/>
    /// <remarks>
    /// A geometry is worth twenty bytes for every byte of it, which is the reference's own estimate
    /// of what a geometry object built out of these bytes costs.
    /// </remarks>
    public override int GetMemory() => MathUtils.ConvertLongToInt((value.Length * 20L) + 24);
}
