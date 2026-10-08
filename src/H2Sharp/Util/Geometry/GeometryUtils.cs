/*
 * Ported from org.h2.util.geometry.GeometryUtils.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;

namespace H2Sharp.Util.Geometry;

/// <summary>
/// What the GEOMETRY readers hand a geometry to, and the geometry constants they agree on.
/// </summary>
/// <remarks>
/// The reference has no geometry objects at all: a geometry is a format, and both formats have a
/// reader that walks the text or the bytes and calls these methods. So there is no point, no line
/// string and no collection here, only the order in which a reader reports one, and the readers
/// themselves are the only places that know what an EWKT or an EWKB is.
/// </remarks>
public static class GeometryUtils
{
    /// <summary>
    /// Where a reader reports a geometry to.
    /// </summary>
    /// <remarks>
    /// The order of the calls is the shape of the geometry: a type is announced, then its
    /// coordinates, then it is closed. A collection announces a sub-target per item, because a
    /// reader may hand a MULTIPOINT to a different target than the one it was handed.
    /// </remarks>
    public abstract class Target
    {
        /// <summary>
        /// Announces the top level and its SRID.
        /// </summary>
        /// <param name="srid">the SRID</param>
        protected internal virtual void Init(int srid)
        {
        }

        /// <summary>
        /// Announces a requirement of the geometry on dimensions.
        /// </summary>
        /// <param name="dimensionSystem">the dimension system</param>
        protected internal virtual void DimensionSystem(int dimensionSystem)
        {
        }

        /// <summary>
        /// Announces a POINT.
        /// </summary>
        protected internal virtual void StartPoint()
        {
        }

        /// <summary>
        /// Announces a LINESTRING.
        /// </summary>
        /// <param name="numPoints">how many points it has</param>
        protected internal virtual void StartLineString(int numPoints)
        {
        }

        /// <summary>
        /// Announces a POLYGON, whose inner rings are announced after it.
        /// </summary>
        /// <param name="numInner">how many inner rings it has, which is 0 for an empty polygon</param>
        /// <param name="numPoints">how many points its outer ring has</param>
        protected internal virtual void StartPolygon(int numInner, int numPoints)
        {
        }

        /// <summary>
        /// Announces an inner ring of a POLYGON.
        /// </summary>
        /// <param name="numPoints">how many points it has</param>
        protected internal virtual void StartPolygonInner(int numPoints)
        {
        }

        /// <summary>
        /// Announces that a POLYGON which is not empty has been written out.
        /// </summary>
        protected internal virtual void EndNonEmptyPolygon()
        {
        }

        /// <summary>
        /// Announces a collection.
        /// </summary>
        /// <param name="type">the kind: a multi point, a multi line string, a multi polygon or a
        /// geometry collection</param>
        /// <param name="numItems">how many items it has</param>
        protected internal virtual void StartCollection(int type, int numItems)
        {
        }

        /// <summary>
        /// Announces an item of a collection, and asks where to report it.
        /// </summary>
        /// <param name="index">the place of the item, counting from 0</param>
        /// <param name="total">how many items there are altogether</param>
        /// <returns>the target to report the item to, which may be another one</returns>
        protected internal virtual Target StartCollectionItem(int index, int total) => this;

        /// <summary>
        /// Announces that an item of a collection has been written out.
        /// </summary>
        /// <param name="target">the target the item was reported to</param>
        /// <param name="type">the kind of the collection</param>
        /// <param name="index">the place of the item, counting from 0</param>
        /// <param name="total">how many items there are altogether</param>
        protected internal virtual void EndCollectionItem(Target target, int type, int index, int total)
        {
        }

        /// <summary>
        /// Announces that the object has been written out.
        /// </summary>
        /// <param name="type">the kind of the object</param>
        protected internal virtual void EndObject(int type)
        {
        }

        /// <summary>
        /// Adds one coordinate.
        /// </summary>
        /// <param name="x">the X coordinate</param>
        /// <param name="y">the Y coordinate</param>
        /// <param name="z">the Z coordinate, which is NaN when there is none</param>
        /// <param name="m">the M coordinate, which is NaN when there is none</param>
        /// <param name="index">the place of the coordinate in the sequence, counting from 0</param>
        /// <param name="total">how many coordinates the sequence has</param>
        protected internal abstract void AddCoordinate(double x, double y, double z, double m, int index, int total);
    }

    /// <summary>
    /// A target that finds the envelope, and skips the inner rings of a polygon because they cannot
    /// make it any wider.
    /// </summary>
    public sealed class EnvelopeTarget : Target
    {
        private bool enabled;

        private bool set;

        private double minX;
        private double maxX;
        private double minY;
        private double maxY;

        /// <inheritdoc/>
        protected internal override void StartPoint() => enabled = true;

        /// <inheritdoc/>
        protected internal override void StartLineString(int numPoints) => enabled = true;

        /// <inheritdoc/>
        protected internal override void StartPolygon(int numInner, int numPoints) => enabled = true;

        /// <inheritdoc/>
        protected internal override void StartPolygonInner(int numPoints) => enabled = false;

        /// <inheritdoc/>
        protected internal override void AddCoordinate(
            double x, double y, double z, double m, int index, int total)
        {
            // POINT EMPTY is a row of NaNs, and there is nothing to enclose.
            if (!enabled || double.IsNaN(x) || double.IsNaN(y))
            {
                return;
            }

            if (!set)
            {
                minX = maxX = x;
                minY = maxY = y;
                set = true;
                return;
            }

            if (minX > x)
            {
                minX = x;
            }

            if (maxX < x)
            {
                maxX = x;
            }

            if (minY > y)
            {
                minY = y;
            }

            if (maxY < y)
            {
                maxY = y;
            }
        }

        /// <summary>
        /// The envelope, as X min, X max, Y min, Y max, or <see langword="null"/> when there was
        /// nothing to enclose.
        /// </summary>
        /// <returns>the envelope, or null</returns>
        public double[]? GetEnvelope() => set ? [minX, maxX, minY, maxY] : null;
    }

    /// <summary>
    /// A target that finds the smallest dimension system a geometry fits in, that is whether any of
    /// its coordinates carry a Z or an M.
    /// </summary>
    public sealed class DimensionSystemTarget : Target
    {
        private bool hasZ;

        private bool hasM;

        /// <inheritdoc/>
        protected internal override void DimensionSystem(int dimensionSystem)
        {
            if ((dimensionSystem & DimensionSystemXyz) != 0)
            {
                hasZ = true;
            }

            if ((dimensionSystem & DimensionSystemXym) != 0)
            {
                hasM = true;
            }
        }

        /// <inheritdoc/>
        protected internal override void AddCoordinate(
            double x, double y, double z, double m, int index, int total)
        {
            if (!hasZ && !double.IsNaN(z))
            {
                hasZ = true;
            }

            if (!hasM && !double.IsNaN(m))
            {
                hasM = true;
            }
        }

        /// <summary>
        /// The smallest dimension system that holds everything the geometry was read with.
        /// </summary>
        /// <returns>the dimension system</returns>
        public int GetDimensionSystem()
            => (hasZ ? DimensionSystemXyz : 0) | (hasM ? DimensionSystemXym : 0);
    }

    /// <summary>
    /// The kind POINT.
    /// </summary>
    public const int Point = 1;

    /// <summary>
    /// The kind LINESTRING.
    /// </summary>
    public const int LineString = 2;

    /// <summary>
    /// The kind POLYGON.
    /// </summary>
    public const int Polygon = 3;

    /// <summary>
    /// The kind MULTIPOINT.
    /// </summary>
    public const int MultiPoint = 4;

    /// <summary>
    /// The kind MULTILINESTRING.
    /// </summary>
    public const int MultiLineString = 5;

    /// <summary>
    /// The kind MULTIPOLYGON.
    /// </summary>
    public const int MultiPolygon = 6;

    /// <summary>
    /// The kind GEOMETRYCOLLECTION.
    /// </summary>
    public const int GeometryCollection = 7;

    /// <summary>
    /// The place of the X coordinate.
    /// </summary>
    public const int X = 0;

    /// <summary>
    /// The place of the Y coordinate.
    /// </summary>
    public const int Y = 1;

    /// <summary>
    /// The place of the Z coordinate.
    /// </summary>
    public const int Z = 2;

    /// <summary>
    /// The place of the M coordinate.
    /// </summary>
    public const int M = 3;

    /// <summary>
    /// Two dimensions, X and Y.
    /// </summary>
    public const int DimensionSystemXy = 0;

    /// <summary>
    /// Three dimensions, with a Z. Also a bit, so a mask of a dimension system says whether it has
    /// a Z.
    /// </summary>
    public const int DimensionSystemXyz = 1;

    /// <summary>
    /// Three dimensions, with an M. Also a bit, so a mask of a dimension system says whether it has
    /// an M.
    /// </summary>
    public const int DimensionSystemXym = 2;

    /// <summary>
    /// Four dimensions, with a Z and an M, which is the two bits above together.
    /// </summary>
    public const int DimensionSystemXyzm = DimensionSystemXyz | DimensionSystemXym;

    /// <summary>
    /// The place of the smallest X in an envelope.
    /// </summary>
    public const int MinX = 0;

    /// <summary>
    /// The place of the largest X in an envelope.
    /// </summary>
    public const int MaxX = 1;

    /// <summary>
    /// The place of the smallest Y in an envelope.
    /// </summary>
    public const int MinY = 2;

    /// <summary>
    /// The place of the largest Y in an envelope.
    /// </summary>
    public const int MaxY = 3;

    /// <summary>
    /// The envelope of a geometry in EWKB.
    /// </summary>
    /// <param name="ewkb">the geometry</param>
    /// <returns>the envelope as X min, X max, Y min, Y max, or null when there is none</returns>
    public static double[]? GetEnvelope(byte[] ewkb)
    {
        EnvelopeTarget target = new();
        EWKBUtils.ParseEWKB(ewkb, target);
        return target.GetEnvelope();
    }

    /// <summary>
    /// Whether two envelopes touch or cross.
    /// </summary>
    /// <param name="envelope1">the first envelope, or null</param>
    /// <param name="envelope2">the second envelope, or null</param>
    /// <returns>true if they intersect</returns>
    public static bool Intersects(double[]? envelope1, double[]? envelope2)
        => envelope1 is not null
            && envelope2 is not null
            && envelope1[MaxX] >= envelope2[MinX]
            && envelope1[MinX] <= envelope2[MaxX]
            && envelope1[MaxY] >= envelope2[MinY]
            && envelope1[MinY] <= envelope2[MaxY];

    /// <summary>
    /// The smallest envelope that holds both of two.
    /// </summary>
    /// <remarks>
    /// Neither argument is changed, and one of them is the answer when it already holds the other.
    /// </remarks>
    /// <param name="envelope1">the first envelope, or null</param>
    /// <param name="envelope2">the second envelope, or null</param>
    /// <returns>the union</returns>
    public static double[]? Union(double[]? envelope1, double[]? envelope2)
    {
        if (envelope1 is null)
        {
            return envelope2;
        }

        if (envelope2 is null)
        {
            return envelope1;
        }

        double minX1 = envelope1[MinX];
        double maxX1 = envelope1[MaxX];
        double minY1 = envelope1[MinY];
        double maxY1 = envelope1[MaxY];
        double minX2 = envelope2[MinX];
        double maxX2 = envelope2[MaxX];
        double minY2 = envelope2[MinY];
        double maxY2 = envelope2[MaxY];

        bool modified = false;
        if (minX1 > minX2)
        {
            minX1 = minX2;
            modified = true;
        }

        if (maxX1 < maxX2)
        {
            maxX1 = maxX2;
            modified = true;
        }

        if (minY1 > minY2)
        {
            minY1 = minY2;
            modified = true;
        }

        if (maxY1 < maxY2)
        {
            maxY1 = maxY2;
            modified = true;
        }

        return modified ? [minX1, maxX1, minY1, maxY1] : envelope1;
    }

    /// <summary>
    /// Every NaN as the one NaN, and every negative zero as a positive one.
    /// </summary>
    /// <remarks>
    /// The two runtimes do not agree on the bits of a NaN: .NET spells its NaN with the sign bit set
    /// and the JDK does not, and these formats are compared byte by byte, so the bits are settled
    /// once here rather than at every reader. It is visible in the very first geometry anyone
    /// stores, because a POINT EMPTY is written as a row of NaNs.
    /// </remarks>
    /// <param name="d">the value</param>
    /// <returns>the canonical value</returns>
    internal static double ToCanonicalDouble(double d)
        => double.IsNaN(d) ? BitConverter.Int64BitsToDouble(JavaNanBits) : d == 0d ? 0d : d;

    /// <summary>
    /// The bits of the JDK's NaN.
    /// </summary>
    private const long JavaNanBits = 0x7FF8_0000_0000_0000L;

    /// <summary>
    /// Refuses anything that is not a finite number, that is a NaN or an infinity.
    /// </summary>
    /// <param name="d">the value</param>
    /// <returns>the same value</returns>
    /// <exception cref="ArgumentException">if the value is not finite</exception>
    internal static double CheckFinite(double d)
    {
        if (!double.IsFinite(d))
        {
            throw new ArgumentException();
        }

        return d;
    }
}
