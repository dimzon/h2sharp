/*
 * Ported from org.h2.util.geometry.EWKBUtils.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Util;

namespace H2Sharp.Util.Geometry;

/// <summary>
/// The EWKB format: OGC well-known binary with the PostGIS flags on the type word.
/// </summary>
/// <remarks>
/// Both marks of the dimension system are readable, the OGC one in the type itself and the PostGIS
/// one as a bit on the type, and both are always written the PostGIS way. Only the SRID of the top
/// object is used: an SRID inside a collection is read and dropped, because the geometry has one.
/// A POINT EMPTY is stored as a row of NaNs, which is what OGC 12-128r15 asks for.
/// </remarks>
public static class EWKBUtils
{
    /// <summary>
    /// A target that writes an EWKB.
    /// </summary>
    public sealed class EWKBTarget : GeometryUtils.Target
    {
        private readonly List<byte> output = [];

        private readonly int dimensionSystem;

        private readonly byte[] buffer = new byte[8];

        private int type;

        private int srid;

        /// <summary>
        /// A new writer.
        /// </summary>
        /// <param name="dimensionSystem">the dimension system to write</param>
        public EWKBTarget(int dimensionSystem)
        {
            this.dimensionSystem = dimensionSystem;
        }

        /// <summary>
        /// The bytes written so far.
        /// </summary>
        /// <returns>the EWKB</returns>
        public byte[] ToArray() => [.. output];

        /// <inheritdoc/>
        protected internal override void Init(int srid) => this.srid = srid;

        /// <inheritdoc/>
        protected internal override void StartPoint() => WriteHeader(GeometryUtils.Point);

        /// <inheritdoc/>
        protected internal override void StartLineString(int numPoints)
        {
            WriteHeader(GeometryUtils.LineString);
            WriteInt(numPoints);
        }

        /// <inheritdoc/>
        protected internal override void StartPolygon(int numInner, int numPoints)
        {
            WriteHeader(GeometryUtils.Polygon);
            if (numInner == 0 && numPoints == 0)
            {
                // The specification does not say how an empty polygon looks, so it is stored as a
                // polygon with no rings, which is what PostGIS does.
                WriteInt(0);
            }
            else
            {
                WriteInt(numInner + 1);
                WriteInt(numPoints);
            }
        }

        /// <inheritdoc/>
        protected internal override void StartPolygonInner(int numPoints) => WriteInt(numPoints);

        /// <inheritdoc/>
        protected internal override void StartCollection(int type, int numItems)
        {
            WriteHeader(type);
            WriteInt(numItems);
        }

        private void WriteHeader(int type)
        {
            this.type = type;
            switch (dimensionSystem)
            {
                case GeometryUtils.DimensionSystemXyz:
                    type |= EwkbZ;
                    break;

                case GeometryUtils.DimensionSystemXyzm:
                    type |= EwkbZ;

                    // Falls into the next case, which adds the M.
                    goto case GeometryUtils.DimensionSystemXym;

                case GeometryUtils.DimensionSystemXym:
                    type |= EwkbM;
                    break;
            }

            if (srid != 0)
            {
                type |= EwkbSrid;
            }

            output.Add(0);
            WriteInt(type);
            if (srid != 0)
            {
                WriteInt(srid);

                // An object inside a collection never carries an SRID of its own.
                srid = 0;
            }
        }

        /// <inheritdoc/>
        protected internal override void AddCoordinate(
            double x, double y, double z, double m, int index, int total)
        {
            // A POINT EMPTY is a row of NaNs and must not be checked, while any other geometry that
            // carries one is not a geometry at all.
            bool check = type != GeometryUtils.Point
                || !double.IsNaN(x)
                || !double.IsNaN(y)
                || !double.IsNaN(z)
                || !double.IsNaN(m);
            if (check)
            {
                GeometryUtils.CheckFinite(x);
                GeometryUtils.CheckFinite(y);
            }

            WriteDouble(x);
            WriteDouble(y);
            if ((dimensionSystem & GeometryUtils.DimensionSystemXyz) != 0)
            {
                WriteDouble(check ? GeometryUtils.CheckFinite(z) : z);
            }
            else if (check && !double.IsNaN(z))
            {
                throw new ArgumentException();
            }

            if ((dimensionSystem & GeometryUtils.DimensionSystemXym) != 0)
            {
                WriteDouble(check ? GeometryUtils.CheckFinite(m) : m);
            }
            else if (check && !double.IsNaN(m))
            {
                throw new ArgumentException();
            }
        }

        private void WriteInt(int value)
        {
            Bits.IntToBigEndian(buffer, 0, value);
            for (int i = 0; i < 4; i++)
            {
                output.Add(buffer[i]);
            }
        }

        private void WriteDouble(double value)
        {
            value = GeometryUtils.ToCanonicalDouble(value);
            Bits.LongToBigEndian(buffer, 0, BitConverter.DoubleToInt64Bits(value));
            for (int i = 0; i < 8; i++)
            {
                output.Add(buffer[i]);
            }
        }
    }

    /// <summary>
    /// Where the binary reader stands in the bytes.
    /// </summary>
    private sealed class EWKBSource
    {
        private readonly byte[] ewkb;

        private int offset;

        /// <summary>
        /// Whether the numbers that follow are big-endian.
        /// </summary>
        internal bool BigEndian;

        internal EWKBSource(byte[] ewkb)
        {
            this.ewkb = ewkb;
        }

        internal byte ReadByte() => ewkb[offset++];

        internal int ReadInt()
        {
            int result = BigEndian
                ? Bits.IntFromBigEndian(ewkb, offset)
                : BitConverter.ToInt32(ewkb, offset);
            offset += 4;
            return result;
        }

        internal double ReadCoordinate()
        {
            long bits = BigEndian
                ? Bits.LongFromBigEndian(ewkb, offset)
                : BitConverter.ToInt64(ewkb, offset);
            offset += 8;
            return GeometryUtils.ToCanonicalDouble(BitConverter.Int64BitsToDouble(bits));
        }

        public override string ToString()
        {
            string hex = Hex.BytesToHex(ewkb);
            int at = offset * 2;

            // The reference passes the end of the range, and this counts characters from the start.
            // Same three arguments, different meaning, and the difference is an exception instead of
            // a message at the exact moment something else is already wrong.
            return new StringBuilder(hex.Length + 3)
                .Append(hex, 0, at).Append("<*>").Append(hex, at, hex.Length - at)
                .ToString();
        }
    }

    /// <summary>
    /// The bit on the type word that says the geometry has a Z.
    /// </summary>
    public const int EwkbZ = unchecked((int)0x8000_0000);

    /// <summary>
    /// The bit on the type word that says the geometry has an M.
    /// </summary>
    public const int EwkbM = 0x4000_0000;

    /// <summary>
    /// The bit on the type word that says an SRID follows it.
    /// </summary>
    public const int EwkbSrid = 0x2000_0000;

    /// <summary>
    /// Rewrites any readable EWKB as the canonical one: the smallest dimension system, the flags of
    /// EWKB for it, and the rest of the differences the same way.
    /// </summary>
    /// <param name="ewkb">the source</param>
    /// <returns>the canonical EWKB, which may be the source itself</returns>
    public static byte[] EWKB2EWKB(byte[] ewkb) => EWKB2EWKB(ewkb, GetDimensionSystem(ewkb));

    /// <summary>
    /// Rewrites any readable EWKB as the canonical one, with the dimension system given.
    /// </summary>
    /// <param name="ewkb">the source</param>
    /// <param name="dimensionSystem">the dimension system</param>
    /// <returns>the canonical EWKB, which may be the source itself</returns>
    public static byte[] EWKB2EWKB(byte[] ewkb, int dimensionSystem)
    {
        EWKBTarget target = new(dimensionSystem);
        ParseEWKB(ewkb, target);
        return target.ToArray();
    }

    /// <summary>
    /// Reads an EWKB.
    /// </summary>
    /// <param name="ewkb">the geometry</param>
    /// <param name="target">where to report it</param>
    /// <exception cref="ArgumentException">if the bytes are not an EWKB</exception>
    public static void ParseEWKB(byte[] ewkb, GeometryUtils.Target target)
    {
        try
        {
            ParseEWKB(new EWKBSource(ewkb), target, 0);
        }
        catch (IndexOutOfRangeException)
        {
            // The reference catches the same thing: bytes that end in the middle of a number are
            // refused as an argument, not as a crash.
            throw new ArgumentException();
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentException();
        }
    }

    /// <summary>
    /// The dimension system a type word with its flags stands for.
    /// </summary>
    /// <param name="type">the type word</param>
    /// <returns>the dimension system</returns>
    public static int Type2DimensionSystem(int type)
    {
        // The PostGIS extensions.
        bool useZ = (type & EwkbZ) != 0;
        bool useM = (type & EwkbM) != 0;

        // OGC 06-103r4 writes the dimension system into the type itself.
        type &= 0xFFFF;
        switch (type / 1_000)
        {
            case GeometryUtils.DimensionSystemXyz:
                useZ = true;
                break;

            case GeometryUtils.DimensionSystemXyzm:
                useZ = true;

                // Falls into the next case, which adds the M.
                goto case GeometryUtils.DimensionSystemXym;

            case GeometryUtils.DimensionSystemXym:
                useM = true;
                break;
        }

        return (useZ ? GeometryUtils.DimensionSystemXyz : 0) | (useM ? GeometryUtils.DimensionSystemXym : 0);
    }

    private static void ParseEWKB(EWKBSource source, GeometryUtils.Target target, int parentType)
    {
        // The byte order of the geometry that follows.
        switch (source.ReadByte())
        {
            case 0:
                source.BigEndian = true;
                break;

            case 1:
                source.BigEndian = false;
                break;

            default:
                throw new ArgumentException();
        }

        // The type of the geometry, and the flags with it.
        int type = source.ReadInt();
        bool useZ = (type & EwkbZ) != 0;
        bool useM = (type & EwkbM) != 0;
        int srid = (type & EwkbSrid) != 0 ? source.ReadInt() : 0;

        // Only the SRID of the top object is the SRID of the geometry.
        if (parentType == 0)
        {
            target.Init(srid);
        }

        type &= 0xFFFF;
        switch (type / 1_000)
        {
            case GeometryUtils.DimensionSystemXyz:
                useZ = true;
                break;

            case GeometryUtils.DimensionSystemXyzm:
                useZ = true;

                // Falls into the next case, which adds the M.
                goto case GeometryUtils.DimensionSystemXym;

            case GeometryUtils.DimensionSystemXym:
                useM = true;
                break;
        }

        target.DimensionSystem(
            (useZ ? GeometryUtils.DimensionSystemXyz : 0) | (useM ? GeometryUtils.DimensionSystemXym : 0));

        type %= 1_000;
        switch (type)
        {
            case GeometryUtils.Point:
                if (parentType != 0
                    && parentType != GeometryUtils.MultiPoint
                    && parentType != GeometryUtils.GeometryCollection)
                {
                    throw new ArgumentException();
                }

                target.StartPoint();
                AddCoordinate(source, target, useZ, useM, 0, 1);
                break;

            case GeometryUtils.LineString:
                if (parentType != 0
                    && parentType != GeometryUtils.MultiLineString
                    && parentType != GeometryUtils.GeometryCollection)
                {
                    throw new ArgumentException();
                }

                int numPoints = source.ReadInt();
                if (numPoints < 0 || numPoints == 1)
                {
                    throw new ArgumentException();
                }

                target.StartLineString(numPoints);
                for (int i = 0; i < numPoints; i++)
                {
                    AddCoordinate(source, target, useZ, useM, i, numPoints);
                }

                break;

            case GeometryUtils.Polygon:
                if (parentType != 0
                    && parentType != GeometryUtils.MultiPolygon
                    && parentType != GeometryUtils.GeometryCollection)
                {
                    throw new ArgumentException();
                }

                int numRings = source.ReadInt();
                if (numRings == 0)
                {
                    target.StartPolygon(0, 0);
                    break;
                }

                if (numRings < 0)
                {
                    throw new ArgumentException();
                }

                numRings--;
                int size = source.ReadInt();

                // A ring is either empty or has four points and more.
                if (size < 0 || size is >= 1 and <= 3)
                {
                    throw new ArgumentException();
                }

                if (size == 0 && numRings > 0)
                {
                    throw new ArgumentException();
                }

                target.StartPolygon(numRings, size);
                if (size > 0)
                {
                    AddRing(source, target, useZ, useM, size);
                    for (int i = 0; i < numRings; i++)
                    {
                        size = source.ReadInt();
                        if (size < 0 || size is >= 1 and <= 3)
                        {
                            throw new ArgumentException();
                        }

                        target.StartPolygonInner(size);
                        AddRing(source, target, useZ, useM, size);
                    }

                    target.EndNonEmptyPolygon();
                }

                break;

            case GeometryUtils.MultiPoint:
            case GeometryUtils.MultiLineString:
            case GeometryUtils.MultiPolygon:
            case GeometryUtils.GeometryCollection:
                if (parentType != 0 && parentType != GeometryUtils.GeometryCollection)
                {
                    throw new ArgumentException();
                }

                int numItems = source.ReadInt();
                if (numItems < 0)
                {
                    throw new ArgumentException();
                }

                target.StartCollection(type, numItems);
                for (int i = 0; i < numItems; i++)
                {
                    GeometryUtils.Target innerTarget = target.StartCollectionItem(i, numItems);
                    ParseEWKB(source, innerTarget, type);
                    target.EndCollectionItem(innerTarget, type, i, numItems);
                }

                break;

            default:
                throw new ArgumentException();
        }

        target.EndObject(type);
    }

    private static void AddRing(EWKBSource source, GeometryUtils.Target target, bool useZ, bool useM, int size)
    {
        // Zero and four and more are the sizes a ring may have.
        if (size < 4)
        {
            return;
        }

        double startX = source.ReadCoordinate();
        double startY = source.ReadCoordinate();
        target.AddCoordinate(
            startX,
            startY,
            useZ ? source.ReadCoordinate() : double.NaN,
            useM ? source.ReadCoordinate() : double.NaN,
            0,
            size);

        for (int i = 1; i < size - 1; i++)
        {
            AddCoordinate(source, target, useZ, useM, i, size);
        }

        double endX = source.ReadCoordinate();
        double endY = source.ReadCoordinate();

        // OGC 06-103r4 calls two points equal when their X and Y agree, so a ring must come back to
        // where it started. The reference asks whether Z and M matter too and answers with a
        // question of its own; X and Y are what it compares.
        if (startX != endX || startY != endY)
        {
            throw new ArgumentException();
        }

        target.AddCoordinate(
            endX,
            endY,
            useZ ? source.ReadCoordinate() : double.NaN,
            useM ? source.ReadCoordinate() : double.NaN,
            size - 1,
            size);
    }

    private static void AddCoordinate(
        EWKBSource source, GeometryUtils.Target target, bool useZ, bool useM, int index, int total)
    {
        target.AddCoordinate(
            source.ReadCoordinate(),
            source.ReadCoordinate(),
            useZ ? source.ReadCoordinate() : double.NaN,
            useM ? source.ReadCoordinate() : double.NaN,
            index,
            total);
    }

    /// <summary>
    /// The dimension system an EWKB says it has.
    /// </summary>
    /// <param name="ewkb">the geometry</param>
    /// <returns>the dimension system</returns>
    public static int GetDimensionSystem(byte[] ewkb)
    {
        EWKBSource source = new(ewkb);
        switch (source.ReadByte())
        {
            case 0:
                source.BigEndian = true;
                break;

            case 1:
                source.BigEndian = false;
                break;

            default:
                throw new ArgumentException();
        }

        return Type2DimensionSystem(source.ReadInt());
    }

    /// <summary>
    /// An envelope as a WKB: a point when it is a point, a line when it is a segment, a polygon when
    /// it is an area.
    /// </summary>
    /// <param name="envelope">the envelope, or null</param>
    /// <returns>the WKB, or null</returns>
    public static byte[]? Envelope2WKB(double[]? envelope)
    {
        if (envelope is null)
        {
            return null;
        }

        double minX = envelope[GeometryUtils.MinX];
        double maxX = envelope[GeometryUtils.MaxX];
        double minY = envelope[GeometryUtils.MinY];
        double maxY = envelope[GeometryUtils.MaxY];

        byte[] result;
        if (minX == maxX && minY == maxY)
        {
            result = new byte[21];
            result[4] = (byte)GeometryUtils.Point;
            Bits.LongToBigEndian(result, 5, BitConverter.DoubleToInt64Bits(minX));
            Bits.LongToBigEndian(result, 13, BitConverter.DoubleToInt64Bits(minY));
        }
        else if (minX == maxX || minY == maxY)
        {
            result = new byte[41];
            result[4] = (byte)GeometryUtils.LineString;
            result[8] = 2;
            Bits.LongToBigEndian(result, 9, BitConverter.DoubleToInt64Bits(minX));
            Bits.LongToBigEndian(result, 17, BitConverter.DoubleToInt64Bits(minY));
            Bits.LongToBigEndian(result, 25, BitConverter.DoubleToInt64Bits(maxX));
            Bits.LongToBigEndian(result, 33, BitConverter.DoubleToInt64Bits(maxY));
        }
        else
        {
            result = new byte[93];
            result[4] = (byte)GeometryUtils.Polygon;
            result[8] = 1;
            result[12] = 5;
            Bits.LongToBigEndian(result, 13, BitConverter.DoubleToInt64Bits(minX));
            Bits.LongToBigEndian(result, 21, BitConverter.DoubleToInt64Bits(minY));
            Bits.LongToBigEndian(result, 29, BitConverter.DoubleToInt64Bits(minX));
            Bits.LongToBigEndian(result, 37, BitConverter.DoubleToInt64Bits(maxY));
            Bits.LongToBigEndian(result, 45, BitConverter.DoubleToInt64Bits(maxX));
            Bits.LongToBigEndian(result, 53, BitConverter.DoubleToInt64Bits(maxY));
            Bits.LongToBigEndian(result, 61, BitConverter.DoubleToInt64Bits(maxX));
            Bits.LongToBigEndian(result, 69, BitConverter.DoubleToInt64Bits(minY));
            Bits.LongToBigEndian(result, 77, BitConverter.DoubleToInt64Bits(minX));
            Bits.LongToBigEndian(result, 85, BitConverter.DoubleToInt64Bits(minY));
        }

        return result;
    }
}
