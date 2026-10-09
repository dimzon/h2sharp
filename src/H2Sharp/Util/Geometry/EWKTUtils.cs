/*
 * Ported from org.h2.util.geometry.EWKTUtils.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp.Util;

namespace H2Sharp.Util.Geometry;

/// <summary>
/// The EWKT format: OGC well-known text with the PostGIS SRID prefix and the dimension suffix.
/// </summary>
/// <remarks>
/// The support is deliberately limited, as the reference's is: the seven kinds, the four dimension
/// systems, an SRID at the front of the top object, and the alternative spelling of a MULTIPOINT
/// without parentheses. The numbers are read with the invariant culture parser and written with the
/// JDK's own <c>Double.toString</c> shape, because that shape is what the format is written in and
/// a coordinate of 1 is <c>1</c> here and not <c>1.0</c>.
/// </remarks>
public static class EWKTUtils
{
    /// <summary>
    /// The names of the kinds, where the type code is the place in this list plus one.
    /// </summary>
    internal static readonly string[] Types =
    [
        "POINT",
        "LINESTRING",
        "POLYGON",
        "MULTIPOINT",
        "MULTILINESTRING",
        "MULTIPOLYGON",
        "GEOMETRYCOLLECTION",
    ];

    /// <summary>
    /// The names of the dimension systems, where the code is the place in this list.
    /// </summary>
    private static readonly string[] DimensionSystems = ["XY", "Z", "M", "ZM"];

    /// <summary>
    /// A target that writes an EWKT.
    /// </summary>
    public sealed class EWKTTarget : GeometryUtils.Target
    {
        private readonly StringBuilder output;

        private readonly int dimensionSystem;

        private int type;

        private bool inMulti;

        /// <summary>
        /// A new writer.
        /// </summary>
        /// <param name="output">where to write</param>
        /// <param name="dimensionSystem">the dimension system to write</param>
        public EWKTTarget(StringBuilder output, int dimensionSystem)
        {
            this.output = output;
            this.dimensionSystem = dimensionSystem;
        }

        /// <inheritdoc/>
        protected internal override void Init(int srid)
        {
            if (srid != 0)
            {
                _ = output.Append("SRID=").Append(srid).Append(';');
            }
        }

        /// <inheritdoc/>
        protected internal override void StartPoint() => WriteHeader(GeometryUtils.Point);

        /// <inheritdoc/>
        protected internal override void StartLineString(int numPoints)
        {
            WriteHeader(GeometryUtils.LineString);
            if (numPoints == 0)
            {
                _ = output.Append("EMPTY");
            }
        }

        /// <inheritdoc/>
        protected internal override void StartPolygon(int numInner, int numPoints)
        {
            WriteHeader(GeometryUtils.Polygon);
            if (numPoints == 0)
            {
                _ = output.Append("EMPTY");
            }
            else
            {
                _ = output.Append('(');
            }
        }

        /// <inheritdoc/>
        protected internal override void StartPolygonInner(int numInner)
            => _ = output.Append(numInner > 0 ? ", " : ", EMPTY");

        /// <inheritdoc/>
        protected internal override void EndNonEmptyPolygon() => _ = output.Append(')');

        /// <inheritdoc/>
        protected internal override void StartCollection(int type, int numItems)
        {
            WriteHeader(type);
            if (numItems == 0)
            {
                _ = output.Append("EMPTY");
            }

            if (type != GeometryUtils.GeometryCollection)
            {
                inMulti = true;
            }
        }

        private void WriteHeader(int type)
        {
            this.type = type;
            if (inMulti)
            {
                return;
            }

            _ = output.Append(Types[type - 1]);
            switch (dimensionSystem)
            {
                case GeometryUtils.DimensionSystemXyz:
                    _ = output.Append(" Z");
                    break;

                case GeometryUtils.DimensionSystemXym:
                    _ = output.Append(" M");
                    break;

                case GeometryUtils.DimensionSystemXyzm:
                    _ = output.Append(" ZM");
                    break;
            }

            _ = output.Append(' ');
        }

        /// <inheritdoc/>
        protected internal override GeometryUtils.Target StartCollectionItem(int index, int total)
        {
            _ = index == 0 ? output.Append('(') : output.Append(", ");
            return this;
        }

        /// <inheritdoc/>
        protected internal override void EndCollectionItem(
            GeometryUtils.Target target, int type, int index, int total)
        {
            if (index + 1 == total)
            {
                _ = output.Append(')');
            }
        }

        /// <inheritdoc/>
        protected internal override void EndObject(int type)
        {
            switch (type)
            {
                case GeometryUtils.MultiPoint:
                case GeometryUtils.MultiLineString:
                case GeometryUtils.MultiPolygon:
                    inMulti = false;
                    break;
            }
        }

        /// <inheritdoc/>
        protected internal override void AddCoordinate(
            double x, double y, double z, double m, int index, int total)
        {
            if (type == GeometryUtils.Point
                && double.IsNaN(x)
                && double.IsNaN(y)
                && double.IsNaN(z)
                && double.IsNaN(m))
            {
                _ = output.Append("EMPTY");
                return;
            }

            if (index == 0)
            {
                _ = output.Append('(');
            }
            else
            {
                _ = output.Append(", ");
            }

            WriteDouble(x);
            _ = output.Append(' ');
            WriteDouble(y);
            if ((dimensionSystem & GeometryUtils.DimensionSystemXyz) != 0)
            {
                _ = output.Append(' ');
                WriteDouble(z);
            }

            if ((dimensionSystem & GeometryUtils.DimensionSystemXym) != 0)
            {
                _ = output.Append(' ');
                WriteDouble(m);
            }

            if (index + 1 == total)
            {
                _ = output.Append(')');
            }
        }

        /// <remarks>
        /// A coordinate is written the way the JDK writes it, less the <c>.0</c> the format does not
        /// want: <c>1</c> and not <c>1.0</c>, and <c>1E10</c> and not <c>1.0E10</c>.
        /// </remarks>
        private void WriteDouble(double value)
        {
            string s = JavaNumberFormat.DoubleToString(GeometryUtils.CheckFinite(value));
            if (s.EndsWith(".0", StringComparison.Ordinal))
            {
                _ = output.Append(s, 0, s.Length - 2);
                return;
            }

            int at = s.IndexOf(".0E", StringComparison.Ordinal);
            if (at < 0)
            {
                _ = output.Append(s);
            }
            else
            {
                // The reference ends the range at the length of the text. This counts characters
                // from the start, so the count is what is left after where the range begins.
                _ = output.Append(s, 0, at).Append(s, at + 2, s.Length - at - 2);
            }
        }
    }

    /// <summary>
    /// Where the text reader stands in the string.
    /// </summary>
    private sealed class EWKTSource
    {
        private readonly string ewkt;

        private int offset;

        internal EWKTSource(string ewkt)
        {
            this.ewkt = ewkt;
        }

        internal int ReadSRID()
        {
            SkipWhitespace();
            int srid;
            if (MatchesIgnoreCase(offset, "SRID="))
            {
                offset += 5;
                int at = ewkt.IndexOf(';', 5);
                if (at < 0)
                {
                    throw new ArgumentException();
                }

                int end = at;
                while (ewkt[end - 1] <= ' ')
                {
                    end--;
                }

                srid = int.Parse(ewkt[offset..end], CultureInfo.InvariantCulture);
                offset = at + 1;
            }
            else
            {
                srid = 0;
            }

            return srid;
        }

        internal void Read(char symbol)
        {
            SkipWhitespace();
            if (offset >= ewkt.Length || ewkt[offset] != symbol)
            {
                throw new ArgumentException();
            }

            offset++;
        }

        internal int ReadType()
        {
            SkipWhitespace();
            if (offset >= ewkt.Length)
            {
                throw new ArgumentException();
            }

            int result = 0;
            char ch = ewkt[offset];
            switch (ch)
            {
                case 'P':
                case 'p':
                    result = Match("POINT", GeometryUtils.Point);
                    if (result == 0)
                    {
                        result = Match("POLYGON", GeometryUtils.Polygon);
                    }

                    break;

                case 'L':
                case 'l':
                    result = Match("LINESTRING", GeometryUtils.LineString);
                    break;

                case 'M':
                case 'm':
                    if (Match("MULTI", 1) != 0)
                    {
                        result = Match("POINT", GeometryUtils.MultiPoint);
                        if (result == 0)
                        {
                            result = Match("POLYGON", GeometryUtils.MultiPolygon);
                            if (result == 0)
                            {
                                result = Match("LINESTRING", GeometryUtils.MultiLineString);
                            }
                        }
                    }

                    break;

                case 'G':
                case 'g':
                    result = Match("GEOMETRYCOLLECTION", GeometryUtils.GeometryCollection);
                    break;
            }

            return result == 0 ? throw new ArgumentException() : result;
        }

        internal int ReadDimensionSystem()
        {
            int before = offset;
            SkipWhitespace();
            int len = ewkt.Length;
            if (offset >= len)
            {
                throw new ArgumentException();
            }

            int result;
            char ch = ewkt[offset];
            switch (ch)
            {
                case 'M':
                case 'm':
                    result = GeometryUtils.DimensionSystemXym;
                    offset++;
                    break;

                case 'Z':
                case 'z':
                    offset++;
                    if (offset >= len)
                    {
                        result = GeometryUtils.DimensionSystemXyz;
                    }
                    else
                    {
                        ch = ewkt[offset];
                        if (ch is 'M' or 'm')
                        {
                            offset++;
                            result = GeometryUtils.DimensionSystemXyzm;
                        }
                        else
                        {
                            result = GeometryUtils.DimensionSystemXyz;
                        }
                    }

                    break;

                default:
                    result = GeometryUtils.DimensionSystemXy;
                    if (before != offset)
                    {
                        // A blank already ended the word, so the type is all there was.
                        return result;
                    }

                    break;
            }

            CheckStringEnd(len);
            return result;
        }

        internal bool ReadEmpty()
        {
            SkipWhitespace();
            if (offset >= ewkt.Length)
            {
                throw new ArgumentException();
            }

            if (ewkt[offset] == '(')
            {
                offset++;
                return false;
            }

            if (Match("EMPTY", 1) != 0)
            {
                CheckStringEnd(ewkt.Length);
                return true;
            }

            throw new ArgumentException();
        }

        /// <remarks>
        /// The words are matched without regard to case, as the reference's
        /// <c>regionMatches(true, ...)</c> does: the characters are made uppercase, and if they are
        /// still different then made lowercase, and only then are they different. That is not the
        /// same test as the ordinal comparison of .NET, which is built on simple case folding, and
        /// the two disagree in both directions: the ordinal test takes the Kelvin sign for a
        /// lowercase k and does not, while the reference does not take the dotless i for an i where
        /// this does.
        /// </remarks>
        private bool MatchesIgnoreCase(int start, string token)
        {
            if (start < 0 || start + token.Length > ewkt.Length)
            {
                return false;
            }

            for (int i = 0; i < token.Length; i++)
            {
                if (!EqualsIgnoreCase(ewkt[start + i], token[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool EqualsIgnoreCase(char c1, char c2)
        {
            if (c1 == c2)
            {
                return true;
            }

            char u1 = JavaCharacter.ToUpperCase(c1);
            char u2 = JavaCharacter.ToUpperCase(c2);
            return u1 == u2 || JavaCharacter.ToLowerCase(u1) == JavaCharacter.ToLowerCase(u2);
        }
        private int Match(string token, int code)
        {
            if (MatchesIgnoreCase(offset, token))
            {
                offset += token.Length;
                return code;
            }

            return 0;
        }

        private void CheckStringEnd(int len)
        {
            if (offset < len)
            {
                char ch = ewkt[offset];
                if (ch > ' ' && ch is not ('(' or ')' or ','))
                {
                    throw new ArgumentException();
                }
            }
        }

        internal bool HasCoordinate()
        {
            SkipWhitespace();
            if (offset >= ewkt.Length)
            {
                return false;
            }

            return IsNumberStart(ewkt[offset]);
        }

        internal double ReadCoordinate()
        {
            SkipWhitespace();
            int len = ewkt.Length;
            if (offset >= len || !IsNumberStart(ewkt[offset]))
            {
                throw new ArgumentException();
            }

            int start = offset++;
            char ch = '\0';
            while (offset < len && IsNumberPart(ch = ewkt[offset]))
            {
                offset++;
            }

            if (offset < len && ch > ' ' && ch is not (')' or ','))
            {
                throw new ArgumentException();
            }

            // A zero of either sign is one zero here, so that a geometry written from this one
            // writes the same text.
            double d = double.Parse(ewkt[start..offset], NumberStyles.Float, CultureInfo.InvariantCulture);
            return d == 0 ? 0 : d;
        }

        private static bool IsNumberStart(char ch)
            => ch is >= '0' and <= '9' or '+' or '-' or '.';

        private static bool IsNumberPart(char ch)
            => ch is >= '0' and <= '9' or '+' or '-' or '.' or 'E' or 'e';

        internal bool HasMoreCoordinates()
        {
            SkipWhitespace();
            if (offset >= ewkt.Length)
            {
                throw new ArgumentException();
            }

            switch (ewkt[offset])
            {
                case ',':
                    offset++;
                    return true;

                case ')':
                    offset++;
                    return false;

                default:
                    throw new ArgumentException();
            }
        }

        internal bool HasData()
        {
            SkipWhitespace();
            return offset < ewkt.Length;
        }

        internal int GetItemCount()
        {
            int result = 1;
            int at = offset;
            int level = 0;
            int len = ewkt.Length;
            while (at < len)
            {
                switch (ewkt[at++])
                {
                    case ',':
                        if (level == 0)
                        {
                            result++;
                        }

                        break;

                    case '(':
                        level++;
                        break;

                    case ')':
                        if (--level < 0)
                        {
                            return result;
                        }

                        break;
                }
            }

            throw new ArgumentException();
        }

        private void SkipWhitespace()
        {
            int len = ewkt.Length;
            while (offset < len && ewkt[offset] <= ' ')
            {
                offset++;
            }
        }

        public override string ToString()
            => new StringBuilder(ewkt.Length + 3)
                .Append(ewkt, 0, offset).Append("<*>").Append(ewkt, offset, ewkt.Length - offset)
                .ToString();
    }

    /// <summary>
    /// An EWKB as an EWKT, with the dimension system the bytes themselves say.
    /// </summary>
    /// <param name="ewkb">the geometry</param>
    /// <returns>the text</returns>
    public static string EWKB2EWKT(byte[] ewkb) => EWKB2EWKT(ewkb, EWKBUtils.GetDimensionSystem(ewkb));

    /// <summary>
    /// An EWKB as an EWKT.
    /// </summary>
    /// <param name="ewkb">the geometry</param>
    /// <param name="dimensionSystem">the dimension system</param>
    /// <returns>the text</returns>
    public static string EWKB2EWKT(byte[] ewkb, int dimensionSystem)
    {
        StringBuilder output = new();
        EWKBUtils.ParseEWKB(ewkb, new EWKTTarget(output, dimensionSystem));
        return output.ToString();
    }

    /// <summary>
    /// An EWKT as an EWKB, with the dimension system the text itself says.
    /// </summary>
    /// <param name="ewkt">the text</param>
    /// <returns>the bytes</returns>
    public static byte[] EWKT2EWKB(string ewkt) => EWKT2EWKB(ewkt, GetDimensionSystem(ewkt));

    /// <summary>
    /// An EWKT as an EWKB.
    /// </summary>
    /// <param name="ewkt">the text</param>
    /// <param name="dimensionSystem">the dimension system</param>
    /// <returns>the bytes</returns>
    public static byte[] EWKT2EWKB(string ewkt, int dimensionSystem)
    {
        EWKBUtils.EWKBTarget target = new(dimensionSystem);
        ParseEWKT(ewkt, target);
        return target.ToArray();
    }

    /// <summary>
    /// Reads an EWKT.
    /// </summary>
    /// <param name="ewkt">the text</param>
    /// <param name="target">where to report it</param>
    /// <exception cref="ArgumentException">if the text is not an EWKT</exception>
    public static void ParseEWKT(string ewkt, GeometryUtils.Target target)
        => ParseEWKT(new EWKTSource(ewkt), target, 0, 0);

    /// <summary>
    /// The kind and the dimension system a word says, which is what a GEOMETRY column is declared
    /// with.
    /// </summary>
    /// <param name="s">the word</param>
    /// <returns>the kind plus the dimension system times a thousand</returns>
    /// <exception cref="ArgumentException">if the word says neither</exception>
    public static int ParseGeometryType(string s)
    {
        EWKTSource source = new(s);
        int type = source.ReadType();
        int dimensionSystem = 0;
        if (source.HasData())
        {
            dimensionSystem = source.ReadDimensionSystem();
            if (source.HasData())
            {
                throw new ArgumentException();
            }
        }

        return (dimensionSystem * 1_000) + type;
    }

    /// <summary>
    /// The dimension system a word says, on its own.
    /// </summary>
    /// <param name="s">the word</param>
    /// <returns>the dimension system, which is never two dimensions</returns>
    /// <exception cref="ArgumentException">if the word says no dimension system</exception>
    public static int ParseDimensionSystem(string s)
    {
        EWKTSource source = new(s);
        int dimensionSystem = source.ReadDimensionSystem();
        if (source.HasData() || dimensionSystem == GeometryUtils.DimensionSystemXy)
        {
            throw new ArgumentException();
        }

        return dimensionSystem;
    }

    /// <summary>
    /// A kind and a dimension system as the word they are written as.
    /// </summary>
    /// <param name="builder">where to write</param>
    /// <param name="type">the kind plus the dimension system times a thousand</param>
    /// <returns>the same builder</returns>
    /// <exception cref="ArgumentException">if the code is not a kind with a dimension system</exception>
    public static StringBuilder FormatGeometryTypeAndDimensionSystem(StringBuilder builder, int type)
    {
        int t = type % 1_000;
        int d = type / 1_000;
        if (t < GeometryUtils.Point
            || t > GeometryUtils.GeometryCollection
            || d < GeometryUtils.DimensionSystemXy
            || d > GeometryUtils.DimensionSystemXyzm)
        {
            throw new ArgumentException();
        }

        _ = builder.Append(Types[t - 1]);
        if (d != GeometryUtils.DimensionSystemXy)
        {
            _ = builder.Append(' ').Append(DimensionSystems[d]);
        }

        return builder;
    }

    private static void ParseEWKT(EWKTSource source, GeometryUtils.Target target, int parentType, int dimensionSystem)
    {
        if (parentType == 0)
        {
            target.Init(source.ReadSRID());
        }

        int type;
        switch (parentType)
        {
            default:
                type = source.ReadType();
                dimensionSystem = source.ReadDimensionSystem();
                break;

            case GeometryUtils.MultiPoint:
                type = GeometryUtils.Point;
                break;

            case GeometryUtils.MultiLineString:
                type = GeometryUtils.LineString;
                break;

            case GeometryUtils.MultiPolygon:
                type = GeometryUtils.Polygon;
                break;
        }

        target.DimensionSystem(dimensionSystem);
        switch (type)
        {
            case GeometryUtils.Point:
                if (parentType != 0
                    && parentType != GeometryUtils.MultiPoint
                    && parentType != GeometryUtils.GeometryCollection)
                {
                    throw new ArgumentException();
                }

                bool emptyPoint = source.ReadEmpty();
                target.StartPoint();
                if (emptyPoint)
                {
                    target.AddCoordinate(double.NaN, double.NaN, double.NaN, double.NaN, 0, 1);
                }
                else
                {
                    AddCoordinate(source, target, dimensionSystem, 0, 1);
                    source.Read(')');
                }

                break;

            case GeometryUtils.LineString:
                if (parentType != 0
                    && parentType != GeometryUtils.MultiLineString
                    && parentType != GeometryUtils.GeometryCollection)
                {
                    throw new ArgumentException();
                }

                if (source.ReadEmpty())
                {
                    target.StartLineString(0);
                    break;
                }

                List<double[]> coordinates = [];
                do
                {
                    coordinates.Add(ReadCoordinate(source, dimensionSystem));
                }
                while (source.HasMoreCoordinates());

                int numPoints = coordinates.Count;
                if (numPoints == 1)
                {
                    throw new ArgumentException();
                }

                target.StartLineString(numPoints);
                for (int i = 0; i < numPoints; i++)
                {
                    double[] c = coordinates[i];
                    target.AddCoordinate(
                        c[GeometryUtils.X], c[GeometryUtils.Y], c[GeometryUtils.Z], c[GeometryUtils.M], i, numPoints);
                }

                break;

            case GeometryUtils.Polygon:
                if (parentType != 0
                    && parentType != GeometryUtils.MultiPolygon
                    && parentType != GeometryUtils.GeometryCollection)
                {
                    throw new ArgumentException();
                }

                if (source.ReadEmpty())
                {
                    target.StartPolygon(0, 0);
                    break;
                }

                List<double[]> outer = ReadRing(source, dimensionSystem);
                List<List<double[]>> inner = [];
                while (source.HasMoreCoordinates())
                {
                    inner.Add(ReadRing(source, dimensionSystem));
                }

                int numInner = inner.Count;
                int size = outer.Count;

                // A ring is either empty or has four points and more.
                if (size is >= 1 and <= 3)
                {
                    throw new ArgumentException();
                }

                if (size == 0 && numInner > 0)
                {
                    throw new ArgumentException();
                }

                target.StartPolygon(numInner, size);
                if (size > 0)
                {
                    AddRing(outer, target);
                    for (int i = 0; i < numInner; i++)
                    {
                        List<double[]> ring = inner[i];
                        size = ring.Count;
                        if (size is >= 1 and <= 3)
                        {
                            throw new ArgumentException();
                        }

                        target.StartPolygonInner(size);
                        AddRing(ring, target);
                    }

                    target.EndNonEmptyPolygon();
                }

                break;

            case GeometryUtils.MultiPoint:
            case GeometryUtils.MultiLineString:
            case GeometryUtils.MultiPolygon:
                ParseCollection(source, target, type, parentType, dimensionSystem);
                break;

            case GeometryUtils.GeometryCollection:
                ParseCollection(source, target, GeometryUtils.GeometryCollection, parentType, 0);
                break;

            default:
                throw new ArgumentException();
        }

        target.EndObject(type);
        if (parentType == 0 && source.HasData())
        {
            throw new ArgumentException();
        }
    }

    private static void ParseCollection(
        EWKTSource source, GeometryUtils.Target target, int type, int parentType, int dimensionSystem)
    {
        if (parentType != 0 && parentType != GeometryUtils.GeometryCollection)
        {
            throw new ArgumentException();
        }

        if (source.ReadEmpty())
        {
            target.StartCollection(type, 0);
            return;
        }

        if (type == GeometryUtils.MultiPoint && source.HasCoordinate())
        {
            ParseMultiPointAlternative(source, target, dimensionSystem);
            return;
        }

        int numItems = source.GetItemCount();
        target.StartCollection(type, numItems);
        for (int i = 0; i < numItems; i++)
        {
            if (i > 0)
            {
                source.Read(',');
            }

            GeometryUtils.Target innerTarget = target.StartCollectionItem(i, numItems);
            ParseEWKT(source, innerTarget, type, dimensionSystem);
            target.EndCollectionItem(innerTarget, type, i, numItems);
        }

        source.Read(')');
    }

    private static void ParseMultiPointAlternative(EWKTSource source, GeometryUtils.Target target, int dimensionSystem)
    {
        // The spelling of a MULTIPOINT without parentheses: (1 2, 3 4).
        List<double[]> points = [];
        do
        {
            points.Add(ReadCoordinate(source, dimensionSystem));
        }
        while (source.HasMoreCoordinates());

        int numItems = points.Count;
        target.StartCollection(GeometryUtils.MultiPoint, numItems);
        for (int i = 0; i < points.Count; i++)
        {
            GeometryUtils.Target innerTarget = target.StartCollectionItem(i, numItems);
            target.StartPoint();
            double[] c = points[i];
            target.AddCoordinate(
                c[GeometryUtils.X], c[GeometryUtils.Y], c[GeometryUtils.Z], c[GeometryUtils.M], 0, 1);
            target.EndCollectionItem(innerTarget, GeometryUtils.MultiPoint, i, numItems);
        }
    }

    private static List<double[]> ReadRing(EWKTSource source, int dimensionSystem)
    {
        if (source.ReadEmpty())
        {
            return [];
        }

        List<double[]> result = [];
        double[] c = ReadCoordinate(source, dimensionSystem);
        double startX = c[GeometryUtils.X];
        double startY = c[GeometryUtils.Y];
        result.Add(c);
        while (source.HasMoreCoordinates())
        {
            result.Add(ReadCoordinate(source, dimensionSystem));
        }

        int size = result.Count;
        if (size < 4)
        {
            throw new ArgumentException();
        }

        c = result[size - 1];
        double endX = c[GeometryUtils.X];
        double endY = c[GeometryUtils.Y];

        // OGC 06-103r4 calls two points equal when their X and Y agree, so a ring must come back to
        // where it started. The reference leaves a question of its own about Z and M here.
        if (startX != endX || startY != endY)
        {
            throw new ArgumentException();
        }

        return result;
    }

    private static void AddRing(List<double[]> ring, GeometryUtils.Target target)
    {
        int size = ring.Count;
        for (int i = 0; i < size; i++)
        {
            double[] coordinates = ring[i];
            target.AddCoordinate(
                coordinates[GeometryUtils.X],
                coordinates[GeometryUtils.Y],
                coordinates[GeometryUtils.Z],
                coordinates[GeometryUtils.M],
                i,
                size);
        }
    }

    private static void AddCoordinate(
        EWKTSource source, GeometryUtils.Target target, int dimensionSystem, int index, int total)
    {
        double x = source.ReadCoordinate();
        double y = source.ReadCoordinate();
        double z = (dimensionSystem & GeometryUtils.DimensionSystemXyz) != 0
            ? source.ReadCoordinate()
            : double.NaN;
        double m = (dimensionSystem & GeometryUtils.DimensionSystemXym) != 0
            ? source.ReadCoordinate()
            : double.NaN;
        target.AddCoordinate(x, y, z, m, index, total);
    }

    private static double[] ReadCoordinate(EWKTSource source, int dimensionSystem)
    {
        double x = source.ReadCoordinate();
        double y = source.ReadCoordinate();
        double z = (dimensionSystem & GeometryUtils.DimensionSystemXyz) != 0
            ? source.ReadCoordinate()
            : double.NaN;
        double m = (dimensionSystem & GeometryUtils.DimensionSystemXym) != 0
            ? source.ReadCoordinate()
            : double.NaN;
        return [x, y, z, m];
    }

    /// <summary>
    /// The dimension system an EWKT says, which is in the word after the kind.
    /// </summary>
    /// <param name="ewkt">the text</param>
    /// <returns>the dimension system</returns>
    public static int GetDimensionSystem(string ewkt)
    {
        EWKTSource source = new(ewkt);
        source.ReadSRID();
        source.ReadType();
        return source.ReadDimensionSystem();
    }
}
