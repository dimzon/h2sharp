using System;
using H2Sharp.Api;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Util.Json;

namespace H2Sharp.Util.Geometry;

/// <summary>
/// Converts between EWKB and GeoJSON.
/// </summary>
/// <remarks>
/// The reference writes GeoJSON through the byte array JSON target rather than building a value tree,
/// which is the shape worth keeping: a geometry has already been read into a stream of callbacks, and
/// turning it into a tree first only to walk the tree afterwards would be two walks.
/// <para>
/// The XYM dimension system is not supported, and it is refused in the constructor rather than at
/// the first coordinate, so a caller that asked for it hears about it before anything is written.
/// </para>
/// </remarks>
public static class GeoJsonUtils
{
    private static readonly string[] Types =
    [
        "Point",
        "LineString",
        "Polygon",
        "MultiPoint",
        "MultiLineString",
        "MultiPolygon",
        "GeometryCollection",
    ];

    /// <summary>
    /// Converts EWKB with known dimension system to GeoJson.
    /// </summary>
    /// <param name="ewkb">geometry object in EWKB format</param>
    /// <param name="dimensionSystem">
    /// dimension system of the specified object, may be the same or smaller than its real dimension
    /// system. M dimension system is not supported
    /// </param>
    /// <returns>GeoJson representation of the specified geometry</returns>
    /// <exception cref="H2SharpException">on unsupported dimension system</exception>
    public static byte[] EwkbToGeoJson(byte[] ewkb, int dimensionSystem)
    {
        JsonByteArrayTarget output = new();
        EWKBUtils.ParseEWKB(ewkb, new GeoJsonTarget(output, dimensionSystem));
        return (byte[])output.GetResult()!;
    }

    /// <summary>
    /// Converts GeoJson with known dimension system to EWKB.
    /// </summary>
    /// <param name="json">geometry object in GeoJson format</param>
    /// <param name="srid">the SRID of geometry</param>
    /// <returns>EWKB representation of the specified geometry</returns>
    public static byte[] GeoJsonToEwkb(byte[] json, int srid)
    {
        JsonValue v = (JsonValue)JsonBytesSource.Parse(json, new JsonValueTarget())!;

        // The document is walked twice: once to learn what dimensions it uses and once to write it,
        // because the dimension system is a header of the EWKB and therefore has to be known before
        // the first byte of it is written.
        GeometryUtils.DimensionSystemTarget dimensions = new();
        Parse(v, dimensions);

        EWKBUtils.EWKBTarget target = new(dimensions.GetDimensionSystem());
        target.Init(srid);
        Parse(v, target);
        return target.ToArray();
    }

    /// <summary>
    /// Converter output target that writes a GeoJson.
    /// </summary>
    private sealed class GeoJsonTarget : GeometryUtils.Target
    {
        private readonly JsonByteArrayTarget output;

        private readonly int dimensionSystem;

        private int type;

        private bool inMulti, inMultiLine, wasEmpty;

        /// <summary>
        /// Creates new GeoJson output target.
        /// </summary>
        /// <param name="output">output JSON target</param>
        /// <param name="dimensionSystem">dimension system to use</param>
        /// <exception cref="H2SharpException">the XYM dimension system is not supported</exception>
        public GeoJsonTarget(JsonByteArrayTarget output, int dimensionSystem)
        {
            if (dimensionSystem == GeometryUtils.DimensionSystemXym)
            {
                throw DbException.Get(
                    ErrorCode.DataConversionError1,
                    "M (XYM) dimension system is not supported in GeoJson");
            }

            this.output = output;
            this.dimensionSystem = dimensionSystem;
        }

        /// <inheritdoc/>
        protected internal override void StartPoint()
        {
            type = GeometryUtils.Point;
            wasEmpty = false;
        }

        /// <inheritdoc/>
        protected internal override void StartLineString(int numPoints)
        {
            WriteHeader(GeometryUtils.LineString);
            if (numPoints == 0)
            {
                output.EndArray();
            }
        }

        /// <inheritdoc/>
        protected internal override void StartPolygon(int numInner, int numPoints)
        {
            WriteHeader(GeometryUtils.Polygon);
            if (numPoints == 0)
            {
                output.EndArray();
            }
            else
            {
                output.StartArray();
            }
        }

        /// <inheritdoc/>
        protected internal override void StartPolygonInner(int numPoints)
        {
            output.StartArray();
            if (numPoints == 0)
            {
                output.EndArray();
            }
        }

        /// <inheritdoc/>
        protected internal override void EndNonEmptyPolygon() => output.EndArray();

        /// <inheritdoc/>
        protected internal override void StartCollection(int type, int numItems)
        {
            WriteHeader(type);
            if (type != GeometryUtils.GeometryCollection)
            {
                inMulti = true;
                if (type == GeometryUtils.MultiLineString || type == GeometryUtils.MultiPolygon)
                {
                    inMultiLine = true;
                }
            }
        }

        /// <inheritdoc/>
        protected internal override GeometryUtils.Target StartCollectionItem(int index, int total)
        {
            if (inMultiLine)
            {
                output.StartArray();
            }

            return this;
        }

        /// <inheritdoc/>
        protected internal override void EndObject(int type)
        {
            switch (type)
            {
                case GeometryUtils.MultiPoint:
                case GeometryUtils.MultiLineString:
                case GeometryUtils.MultiPolygon:
                    inMultiLine = inMulti = false;
                    goto case GeometryUtils.GeometryCollection;

                case GeometryUtils.GeometryCollection:
                    output.EndArray();
                    break;
            }

            if (!inMulti && !wasEmpty)
            {
                output.EndObject();
            }
        }

        /// <inheritdoc/>
        protected internal override void AddCoordinate(double x, double y, double z, double m, int index, int total)
        {
            if (type == GeometryUtils.Point)
            {
                if (double.IsNaN(x) && double.IsNaN(y) && double.IsNaN(z) && double.IsNaN(m))
                {
                    // A point made only of NaN is an empty point, and GeoJson spells that as null
                    // rather than as an array of nulls.
                    wasEmpty = true;
                    output.ValueNull();
                    return;
                }

                if (!inMulti)
                {
                    WriteStartObject(GeometryUtils.Point);
                }
            }

            output.StartArray();
            WriteDouble(x);
            WriteDouble(y);
            if ((dimensionSystem & GeometryUtils.DimensionSystemXyz) != 0)
            {
                WriteDouble(z);
            }

            if ((dimensionSystem & GeometryUtils.DimensionSystemXym) != 0)
            {
                WriteDouble(m);
            }

            output.EndArray();
            if (type != GeometryUtils.Point && index + 1 == total)
            {
                output.EndArray();
            }
        }

        private void WriteHeader(int type)
        {
            this.type = type;
            wasEmpty = false;
            if (!inMulti)
            {
                WriteStartObject(type);
            }
        }

        private void WriteStartObject(int type)
        {
            output.StartObject();
            output.Member("type");
            output.ValueString(Types[type - 1]);
            output.Member(type != GeometryUtils.GeometryCollection ? "coordinates" : "geometries");
            if (type != GeometryUtils.Point)
            {
                output.StartArray();
            }
        }

        private void WriteDouble(double v)
            => output.ValueNumber(
                BigDecimal.Parse(JavaNumberFormat.DoubleToString(GeometryUtils.CheckFinite(v)))
                    .StripTrailingZeros());
    }

    private static void Parse(JsonValue v, GeometryUtils.Target target)
    {
        if (v is JsonNull)
        {
            target.StartPoint();
            target.AddCoordinate(double.NaN, double.NaN, double.NaN, double.NaN, 0, 1);
            target.EndObject(GeometryUtils.Point);
        }
        else if (v is JsonObject o)
        {
            if (o.GetFirst("type") is not JsonString t)
            {
                throw new ArgumentException();
            }

            switch (t.GetString())
            {
                case "Point":
                    Parse(o, target, GeometryUtils.Point);
                    break;
                case "LineString":
                    Parse(o, target, GeometryUtils.LineString);
                    break;
                case "Polygon":
                    Parse(o, target, GeometryUtils.Polygon);
                    break;
                case "MultiPoint":
                    Parse(o, target, GeometryUtils.MultiPoint);
                    break;
                case "MultiLineString":
                    Parse(o, target, GeometryUtils.MultiLineString);
                    break;
                case "MultiPolygon":
                    Parse(o, target, GeometryUtils.MultiPolygon);
                    break;
                case "GeometryCollection":
                    ParseGeometryCollection(o, target);
                    break;
                default:
                    throw new ArgumentException();
            }
        }
        else
        {
            throw new ArgumentException();
        }
    }

    private static void Parse(JsonObject o, GeometryUtils.Target target, int type)
    {
        if (o.GetFirst("coordinates") is not JsonArray a)
        {
            throw new ArgumentException();
        }

        switch (type)
        {
            case GeometryUtils.Point:
                target.StartPoint();
                ParseCoordinate(a, target, 0, 1);
                target.EndObject(GeometryUtils.Point);
                break;
            case GeometryUtils.LineString:
                ParseLineString(a, target);
                break;
            case GeometryUtils.Polygon:
                ParsePolygon(a, target);
                break;
            case GeometryUtils.MultiPoint:
                {
                    JsonValue[] points = a.GetArray();
                    int numPoints = points.Length;
                    target.StartCollection(GeometryUtils.MultiPoint, numPoints);
                    for (int i = 0; i < numPoints; i++)
                    {
                        target.StartPoint();
                        ParseCoordinate(points[i], target, 0, 1);
                        target.EndObject(GeometryUtils.Point);
                        target.EndCollectionItem(target, GeometryUtils.MultiPoint, i, numPoints);
                    }

                    target.EndObject(GeometryUtils.MultiPoint);
                    break;
                }

            case GeometryUtils.MultiLineString:
                {
                    JsonValue[] strings = a.GetArray();
                    int numStrings = strings.Length;
                    target.StartCollection(GeometryUtils.MultiLineString, numStrings);
                    for (int i = 0; i < numStrings; i++)
                    {
                        if (strings[i] is not JsonArray @string)
                        {
                            throw new ArgumentException();
                        }

                        ParseLineString(@string, target);
                        target.EndCollectionItem(target, GeometryUtils.MultiLineString, i, numStrings);
                    }

                    target.EndObject(GeometryUtils.MultiLineString);
                    break;
                }

            case GeometryUtils.MultiPolygon:
                {
                    JsonValue[] polygons = a.GetArray();
                    int numPolygons = polygons.Length;
                    target.StartCollection(GeometryUtils.MultiPolygon, numPolygons);
                    for (int i = 0; i < numPolygons; i++)
                    {
                        if (polygons[i] is not JsonArray @string)
                        {
                            throw new ArgumentException();
                        }

                        ParsePolygon(@string, target);
                        target.EndCollectionItem(target, GeometryUtils.MultiPolygon, i, numPolygons);
                    }

                    target.EndObject(GeometryUtils.MultiPolygon);
                    break;
                }

            default:
                throw new ArgumentException();
        }
    }

    private static void ParseGeometryCollection(JsonObject o, GeometryUtils.Target target)
    {
        if (o.GetFirst("geometries") is not JsonArray a)
        {
            throw new ArgumentException();
        }

        JsonValue[] geometries = a.GetArray();
        int numGeometries = geometries.Length;
        target.StartCollection(GeometryUtils.GeometryCollection, numGeometries);
        for (int i = 0; i < numGeometries; i++)
        {
            Parse(geometries[i], target);
            target.EndCollectionItem(target, GeometryUtils.GeometryCollection, i, numGeometries);
        }

        target.EndObject(GeometryUtils.GeometryCollection);
    }

    private static void ParseLineString(JsonArray a, GeometryUtils.Target target)
    {
        JsonValue[] points = a.GetArray();
        int numPoints = points.Length;
        target.StartLineString(numPoints);
        for (int i = 0; i < numPoints; i++)
        {
            ParseCoordinate(points[i], target, i, numPoints);
        }

        target.EndObject(GeometryUtils.LineString);
    }

    private static void ParsePolygon(JsonArray a, GeometryUtils.Target target)
    {
        JsonValue[] rings = a.GetArray();
        int numRings = rings.Length;
        if (numRings == 0)
        {
            target.StartPolygon(0, 0);
        }
        else
        {
            if (rings[0] is not JsonArray first)
            {
                throw new ArgumentException();
            }

            JsonValue[] points = first.GetArray();
            target.StartPolygon(numRings - 1, points.Length);
            ParseRing(points, target);
            for (int i = 1; i < numRings; i++)
            {
                if (rings[i] is not JsonArray ring)
                {
                    throw new ArgumentException();
                }

                points = ring.GetArray();
                target.StartPolygonInner(points.Length);
                ParseRing(points, target);
            }

            target.EndNonEmptyPolygon();
        }

        target.EndObject(GeometryUtils.Polygon);
    }

    private static void ParseRing(JsonValue[] points, GeometryUtils.Target target)
    {
        int numPoints = points.Length;
        for (int i = 0; i < numPoints; i++)
        {
            ParseCoordinate(points[i], target, i, numPoints);
        }
    }

    private static void ParseCoordinate(JsonValue v, GeometryUtils.Target target, int index, int total)
    {
        if (v is JsonNull)
        {
            target.AddCoordinate(double.NaN, double.NaN, double.NaN, double.NaN, 0, 1);
            return;
        }

        if (v is not JsonArray a)
        {
            throw new ArgumentException();
        }

        JsonValue[] values = a.GetArray();
        if (values.Length < 2)
        {
            throw new ArgumentException();
        }

        target.AddCoordinate(
            ReadCoordinate(values, GeometryUtils.X),
            ReadCoordinate(values, GeometryUtils.Y),
            ReadCoordinate(values, GeometryUtils.Z),
            ReadCoordinate(values, GeometryUtils.M),
            index,
            total);
    }

    private static double ReadCoordinate(JsonValue[] values, int index)
    {
        if (index >= values.Length)
        {
            return double.NaN;
        }

        if (values[index] is not JsonNumber number)
        {
            throw new ArgumentException();
        }

        return number.GetBigDecimal().ToDouble();
    }
}
