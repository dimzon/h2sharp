using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Util.Geometry;
using H2Sharp.Util.Json;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// GeoJSON against the reference: EWKT in, GeoJSON out, and back into EWKT to catch a converter
/// that writes something the other direction cannot read.
/// </summary>
/// <remarks>
/// The corpus covers every geometry type with and without Z, the empty shapes of each, polygons with
/// and without inner rings, collections nested in collections, and the shapes that mix X and Y.
/// <para>
/// A refusal counts as a result and not as a failure: the reference turns down an XYM dimension system
/// with a conversion error, and a C# side that accepts it is just as wrong as one that returns
/// something else.
/// </para>
/// </remarks>
[Collection(OracleCollection.Name)]
public sealed class GeoJsonOracleTests
{
    private static readonly string[] Texts =
    [
        "POINT(1 2)",
        "POINT EMPTY",
        "POINT(1 2 3)",
        "POINT Z EMPTY",
        "POINT Z(1 2 3)",
        "POINT ZM(1 2 3 4)",
        "POINT ZM EMPTY",
        "POINT(1.5 -2.25)",
        "POINT(1e300 -1e-300)",
        "POINT M(1 2 3)",
        "POINT M EMPTY",

        "LINESTRING(1 2,3 4)",
        "LINESTRING(1 2)",
        "LINESTRING EMPTY",
        "LINESTRING(1 2,3 4,5 6)",
        "LINESTRING(1 2,3 4,5 6,7 8)",
        "LINESTRING(1 2,3 4,5 6,7 8,9 10)",
        "LINESTRING(1 2,3 4,5 6,7 8,9 10,11 12)",
        "LINESTRING Z(1 2 3,4 5 6)",

        "POLYGON((1 2,3 4,5 6,1 2))",
        "POLYGON((1 2,3 4,5 6,1 2),(0 0,1 1,2 2,0 0))",
        "POLYGON EMPTY",
        "POLYGON((1 2,3 4,5 6,1 2),(0 0,1 1,2 2,0 0),(9 9,8 8,7 7,9 9))",
        "POLYGON Z((1 2 3,4 5 6,7 8 9,1 2 3))",

        "MULTIPOINT(1 2)",
        "MULTIPOINT((1 2),(3 4))",
        "MULTIPOINT EMPTY",
        "MULTIPOINT(EMPTY)",
        "MULTIPOINT((1 2),EMPTY,(3 4))",
        "MULTILINESTRING((1 2,3 4))",
        "MULTILINESTRING((1 2,3 4),(5 6,7 8))",
        "MULTILINESTRING EMPTY",
        "MULTILINESTRING(EMPTY)",
        "MULTILINESTRING((1 2,3 4),EMPTY)",
        "MULTIPOLYGON(((1 2,3 4,5 6,1 2)))",
        "MULTIPOLYGON(((1 2,3 4,5 6,1 2)),((7 8,9 10,11 12,7 8)))",
        "MULTIPOLYGON EMPTY",
        "MULTIPOLYGON(EMPTY)",
        "MULTIPOLYGON(EMPTY,((1 2,3 4,5 6,1 2)))",

        "GEOMETRYCOLLECTION(POINT(1 2))",
        "GEOMETRYCOLLECTION EMPTY",
        "GEOMETRYCOLLECTION(POINT(1 2),LINESTRING(3 4,5 6))",
        "GEOMETRYCOLLECTION(POLYGON((1 2,3 4,5 6,1 2)))",
        "GEOMETRYCOLLECTION(GEOMETRYCOLLECTION(POINT(1 2)))",
        "GEOMETRYCOLLECTION(GEOMETRYCOLLECTION(POINT EMPTY))",
        "GEOMETRYCOLLECTION(MULTIPOINT((1 2)),POLYGON EMPTY)",
        "GEOMETRYCOLLECTION(GEOMETRYCOLLECTION(GEOMETRYCOLLECTION(POINT(1 2),LINESTRING EMPTY)))",

        "SRID=4326;POINT(1 2)",
        "SRID=4326;GEOMETRYCOLLECTION(POINT(1 2))",
        "SRID=4326;POLYGON((1 2,3 4,5 6,1 2))",

        "LINESTRING(1 2,3 4,5 6,7 8,9 10,11 12,13 14)",
        "POLYGON((1 2,3 4,5 6,1 2),(0 0,1 1,2 2,0 0),(9 9,8 8,7 7,9 9),(5 5,6 6,4 4,5 5))",
        "MULTILINESTRING((1 2,3 4),(5 6,7 8),(9 10,11 12),(13 14,15 16))",
    ];

    [JavaOracleFact]
    public void GeoJsonMatchesReference()
    {
        List<string> java = OracleRunner.Run(
                Texts,
                """
                for (String s : inputs) {
                  StringBuilder b = new StringBuilder();
                  try {
                    byte[] ewkb = org.h2.util.geometry.EWKTUtils.ewkt2ewkb(s);
                    int ds = org.h2.util.geometry.EWKBUtils.getDimensionSystem(ewkb);
                    byte[] json = org.h2.util.geometry.GeoJsonUtils.ewkbToGeoJson(ewkb, ds);
                    b.append(new String(json, java.nio.charset.StandardCharsets.UTF_8)).append('|');
                    byte[] back = org.h2.util.geometry.GeoJsonUtils.geoJsonToEwkb(json, 4326);
                    b.append(org.h2.util.geometry.EWKTUtils.ewkb2ewkt(back)).append('|');
                    byte[] back0 = org.h2.util.geometry.GeoJsonUtils.geoJsonToEwkb(json, 0);
                    b.append(org.h2.util.geometry.EWKTUtils.ewkb2ewkt(back0));
                  } catch (RuntimeException e) {
                    b.append("REFUSED");
                  }
                  System.out.println(hex(b.toString()));
                }
                """,
                numbers: null,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(Decode)
            .ToList();

        List<string> csharp = [.. Texts.Select(Describe)];

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"{Texts[i]}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [JavaOracleFact]
    public void XymIsRefusedByBothSides()
    {
        // The reference turns the XYM dimension system down in the constructor of its GeoJson target,
        // before anything is read. The corpus above covers it through the shapes that carry an M, and
        // this covers the calling convention: the dimension system is the second argument and it is
        // checked no matter what the geometry is.
        int[] systems = [0, 1, 2, 3];
        List<string> java = OracleRunner.Run(
                [],
                """
                byte[] ewkb = org.h2.util.geometry.EWKTUtils.ewkt2ewkb("POINT Z(1 2 3)");
                for (int ds : nums) {
                  StringBuilder b = new StringBuilder();
                  try {
                    byte[] json = org.h2.util.geometry.GeoJsonUtils
                        .ewkbToGeoJson(ewkb, ds);
                    b.append(new String(json, java.nio.charset.StandardCharsets.UTF_8));
                  } catch (RuntimeException e) {
                    b.append("REFUSED");
                  }
                  System.out.println(hex(b.toString()));
                }
                """,
                numbers: systems,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(Decode)
            .ToList();

        byte[] point = EWKTUtils.EWKT2EWKB("POINT Z(1 2 3)");
        List<string> csharp = [.. systems.Select(ds => AsText(() => Encoding.UTF8.GetString(
            GeoJsonUtils.EwkbToGeoJson(point, ds))))];

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"ds {systems[i]}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [JavaOracleFact]
    public void GeoJsonToEwkbMatchesReference()
    {
        // The other direction, on its own terms: a GeoJSON document in, the EWKT of what it became.
        // The corpus holds the shapes a GeoJSON document is actually written in, plus the ones a
        // writer gets wrong, because reading them is where a converter shows its true colours.
        string[] documents =
        [
            """{"type":"Point","coordinates":[1,2]}""",
            """{"type":"Point","coordinates":[1,2,3]}""",
            """{"type":"Point","coordinates":[]}""",
            "null",
            """{"type":"Point"}""",
            """{"type":"Point","coordinates":{}}""",
            """{"type":"Point","coordinates":[1]}""",
            """{"type":"Point","coordinates":["1","2"]}""",
            """{"type":"LineString","coordinates":[[1,2],[3,4]]}""",
            """{"type":"LineString","coordinates":[]}""",
            """{"type":"LineString","coordinates":[1,2]}""",
            """{"type":"Polygon","coordinates":[[[1,2],[3,4],[5,6],[1,2]]]}""",
            """{"type":"Polygon","coordinates":[]}""",
            """{"type":"Polygon","coordinates":[[]]}""",
            """{"type":"Polygon","coordinates":[[[1,2],[3,4],[5,6],[1,2]],[[0,0],[1,1],[2,2],[0,0]]]}""",
            """{"type":"MultiPoint","coordinates":[[1,2],[3,4]]}""",
            """{"type":"MultiPoint","coordinates":[]}""",
            """{"type":"MultiPoint","coordinates":[null]}""",
            """{"type":"MultiLineString","coordinates":[[[1,2],[3,4]],[[5,6],[7,8]]]}""",
            """{"type":"MultiLineString","coordinates":[]}""",
            """{"type":"MultiLineString","coordinates":[1,2]}""",
            """{"type":"MultiPolygon","coordinates":[[[[1,2],[3,4],[5,6],[1,2]]]]}""",
            """{"type":"MultiPolygon","coordinates":[]}""",
            """{"type":"GeometryCollection","geometries":[{"type":"Point","coordinates":[1,2]}]}""",
            """{"type":"GeometryCollection","geometries":[]}""",
            """{"type":"GeometryCollection"}""",
            """{"type":"GeometryCollection","geometries":[{"type":"GeometryCollection","geometries":[{"type":"Point","coordinates":[1,2]}]}]}""",
            """{"type":"Circle","coordinates":[1,2]}""",
            """{"coordinates":[1,2]}""",
            """[1,2]""",
            "1",
            """{"type":"Point","coordinates":[1.5,-2.25]}""",
            """{"type":"Point","coordinates":[1e300,-1e-300]}""",
            """{"type":"Point","coordinates":[null,null]}""",
        ];

        List<string> java = OracleRunner.Run(
                documents,
                """
                for (String s : inputs) {
                  StringBuilder b = new StringBuilder();
                  try {
                    byte[] json = s.getBytes(java.nio.charset.StandardCharsets.UTF_8);
                    byte[] ewkb = org.h2.util.geometry.GeoJsonUtils.geoJsonToEwkb(json, 4326);
                    b.append(org.h2.util.geometry.EWKTUtils.ewkb2ewkt(ewkb));
                  } catch (RuntimeException e) {
                    b.append("REFUSED");
                  }
                  System.out.println(hex(b.toString()));
                }
                """,
                numbers: null,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(Decode)
            .ToList();

        List<string> csharp = [.. documents.Select(Read)];

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"{documents[i]}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// EWKT in, GeoJSON out, and the EWKT both round trips came back as. The two round trips differ
    /// only in the SRID, which is how a converter that keeps the SRID apart from the geometry shows
    /// up.
    /// </summary>
    private static string Describe(string text) => AsText(() =>
    {
        byte[] ewkb = EWKTUtils.EWKT2EWKB(text);
        int dimensionSystem = EWKBUtils.GetDimensionSystem(ewkb);
        byte[] json = GeoJsonUtils.EwkbToGeoJson(ewkb, dimensionSystem);
        return string.Join(
            '|',
            Encoding.UTF8.GetString(json),
            EWKTUtils.EWKB2EWKT(GeoJsonUtils.GeoJsonToEwkb(json, 4326)),
            EWKTUtils.EWKB2EWKT(GeoJsonUtils.GeoJsonToEwkb(json, 0)));
    });

    /// <summary>A GeoJSON document in, the EWKT it became.</summary>
    private static string Read(string document) => AsText(
        () => EWKTUtils.EWKB2EWKT(GeoJsonUtils.GeoJsonToEwkb(Encoding.UTF8.GetBytes(document), 4326)));

    private static string AsText(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (IsRefusal(e))
        {
            return "REFUSED";
        }
    }

    /// <remarks>
    /// <see cref="InvalidOperationException"/> is in the list because the reference throws
    /// <c>IllegalStateException</c> from its byte array target when the document it was handed to
    /// write does not close, and a shape like a multi-point holding one empty point really does leave
    /// an object unclosed. Both sides refuse it, and a refusal is the answer.
    /// </remarks>
    private static bool IsRefusal(Exception e)
        => e is ArgumentException
            or FormatException
            or IndexOutOfRangeException
            or ArithmeticException
            or InvalidOperationException
            or H2SharpException;

    private static string Decode(string hex)
    {
        string only = hex.TrimStart('=');
        if (only.Length == 0 || !only.All(static c => char.IsAsciiHexDigit(c)))
        {
            return hex;
        }

        StringBuilder b = new(only.Length / 4);
        for (int i = 0; i + 3 < only.Length; i += 4)
        {
            _ = b.Append((char)Convert.ToInt32(only.Substring(i, 4), 16));
        }

        return b.ToString();
    }
}
