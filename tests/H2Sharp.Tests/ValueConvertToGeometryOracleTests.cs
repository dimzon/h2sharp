using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Util.Geometry;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// Converting a value to GEOMETRY against the reference.
/// </summary>
/// <remarks>
/// The reference converts more source families than it looks like it should: a JSON document goes
/// through the GeoJSON reader, and a binary value is read as EWKB rather than refused on the grounds
/// that a geometry is text. The SRID comes from the target type, which is the only way a JSON
/// document can arrive with one, so the target type is part of the comparison and not decoration.
/// <para>
/// A refusal is a result. The reference turns a JSON document it cannot read into a data conversion
/// error naming the value that failed, and an exception that is merely a different exception is just
/// as much a mismatch as a different answer.
/// </para>
/// </remarks>
[Collection(OracleCollection.Name)]
public sealed class ValueConvertToGeometryOracleTests
{
    private static readonly System.Text.RegularExpressions.Regex TraceSuffix =
        new(@"\s\[\d+-\d+\]$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);


    private static readonly (string Name, Func<Value> CSharp, string Java, int Srid)[] Cases =
    [
        ("geometry itself", () => Geom("POINT(1 2)"), "org.h2.value.ValueGeometry.getFromEWKB(" + Ewb("POINT(1 2)") + ")", 0),
        ("geometry empty", () => Geom("POINT EMPTY"), "org.h2.value.ValueGeometry.getFromEWKB(" + Ewb("POINT EMPTY") + ")", 0),
        ("geometry z", () => Geom("POINT Z(1 2 3)"), "org.h2.value.ValueGeometry.getFromEWKB(" + Ewb("POINT Z(1 2 3)") + ")", 0),
        ("geometry with srid", () => Geom("SRID=4326;POINT(1 2)"), "org.h2.value.ValueGeometry.getFromEWKB(" + Ewb("SRID=4326;POINT(1 2)") + ")", 0),

        ("json point", () => Json(Point), Js(Point), 0),
        ("json point z", () => Json(PointZ), Js(PointZ), 0),
        ("json point with srid", () => Json(Point), Js(Point), 4326),
        ("json linestring", () => Json(LineString), Js(LineString), 0),
        ("json polygon", () => Json(Polygon), Js(Polygon), 0),
        ("json multipoint", () => Json(MultiPoint), Js(MultiPoint), 0),
        ("json collection", () => Json(Collection), Js(Collection), 0),
        ("json null", () => Json("null"), Js("null"), 0),
        ("json collection srid", () => Json(Point), Js(Point), 3857),
        ("json wrong type", () => Json("{\"type\":\"Circle\",\"coordinates\":[1,2]}"), Js("{\"type\":\"Circle\",\"coordinates\":[1,2]}"), 0),
        ("json not an object", () => Json("[1,2]"), Js("[1,2]"), 0),
        ("json number", () => Json("42"), Js("42"), 0),
        ("json broken", () => Json("{oops"), Js("{oops"), 0),

        ("binary ewkb", () => ValueBinary.Get(Ewkb("POINT(1 2)")), "org.h2.value.ValueBinary.get(" + Ewb("POINT(1 2)") + ")", 0),
        ("varbinary ewkb", () => ValueVarbinary.Get(Ewkb("POINT(1 2)")), "org.h2.value.ValueVarbinary.get(" + Ewb("POINT(1 2)") + ")", 0),
        ("blob ewkb", () => ValueBlob.CreateSmall(Ewkb("POINT(1 2)")), "org.h2.value.ValueBlob.createSmall(" + Ewb("POINT(1 2)") + ")", 0),
        ("varbinary ewkb with srid", () => ValueVarbinary.Get(Ewkb("POINT(1 2)")), "org.h2.value.ValueVarbinary.get(" + Ewb("POINT(1 2)") + ")", 4326),
        ("varbinary not ewkb", () => ValueVarbinary.Get(Utf8("nonsense")), "org.h2.value.ValueVarbinary.get(\"nonsense\".getBytes(java.nio.charset.StandardCharsets.UTF_8))", 0),
        ("varbinary empty", () => ValueVarbinary.Get([]), "org.h2.value.ValueVarbinary.get(new byte[0])", 0),

        ("varchar ewkt", () => ValueVarchar.Get("POINT(1 2)"), "org.h2.value.ValueVarchar.get(\"POINT(1 2)\")", 0),
        ("varchar ewkt srid", () => ValueVarchar.Get("POINT(1 2)"), "org.h2.value.ValueVarchar.get(\"POINT(1 2)\")", 4326),
        ("varchar ewkt empty", () => ValueVarchar.Get("POINT EMPTY"), "org.h2.value.ValueVarchar.get(\"POINT EMPTY\")", 0),
        ("varchar not ewkt", () => ValueVarchar.Get("nonsense"), "org.h2.value.ValueVarchar.get(\"nonsense\")", 0),
        ("varchar empty", () => ValueVarchar.Get(""), "org.h2.value.ValueVarchar.get(\"\")", 0),
        ("char ewkt", () => ValueChar.Get("POINT(1 2)"), "org.h2.value.ValueChar.get(\"POINT(1 2)\")", 0),
        ("clob ewkt", () => ValueClob.CreateSmall(Utf8("POINT(1 2)")), "org.h2.value.ValueClob.createSmall(\"POINT(1 2)\".getBytes(java.nio.charset.StandardCharsets.UTF_8))", 0),
        ("varchar ignoring case", () => ValueVarcharIgnoreCase.Get("POINT(1 2)"), "org.h2.value.ValueVarcharIgnoreCase.get(\"POINT(1 2)\")", 0),

        ("integer", () => ValueInteger.Get(1), "org.h2.value.ValueInteger.get(1)", 0),
        ("boolean", () => ValueBoolean.True, "org.h2.value.ValueBoolean.TRUE", 0),
        ("uuid", () => ValueUuid.Get(0L, 1L), "org.h2.value.ValueUuid.get(new java.util.UUID(0L, 1L))", 0),
    ];

    private const string Point = """{"type":"Point","coordinates":[1,2]}""";

    private const string PointZ = """{"type":"Point","coordinates":[1,2,3]}""";

    private const string LineString = """{"type":"LineString","coordinates":[[1,2],[3,4]]}""";

    private const string Polygon = """{"type":"Polygon","coordinates":[[[1,2],[3,4],[5,6],[1,2]]]}""";

    private const string MultiPoint = """{"type":"MultiPoint","coordinates":[[1,2],[3,4]]}""";

    private const string Collection = """{"type":"GeometryCollection","geometries":[{"type":"Point","coordinates":[1,2]}]}""";

    [JavaOracleFact]
    public void ConversionsToGeometryAgreeWithTheReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                Program(),
                numbers: null,
                extraJavaArgs: ["-Duser.language=en"])
            .ToList();

        List<string> csharp = [.. Cases.Select(Describe)];

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"{Cases[i].Name}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>The reference's EWKB from text, spelled the way a Java expression has to spell it.</summary>
    private static string Ewb(string ewkt) => "Runner.ewkb(" + JavaString(ewkt) + ")";

    /// <summary>The reference's way of holding a JSON document, as a Java expression.</summary>
    private static string Js(string document) => "org.h2.value.ValueJson.fromJson(" + JavaString(document) + ")";

    private static string Program()
    {
        StringBuilder calls = new();
        foreach ((string name, Func<Value> _, string java, int srid) in Cases)
        {
            _ = calls.Append("      Runner.out(").Append(JavaString(name)).Append(", ").Append(srid)
                .Append(", () -> ").Append(java).Append(");\n");
        }

        return $$"""
{
class Runner {
static String hex(String text) {
StringBuilder b = new StringBuilder();
b.append('=');
for (int i = 0; i < text.length(); i++) {
b.append(String.format("%04x", (int) text.charAt(i)));
}
return b.toString();
}

static byte[] ewkb(String ewkt) {
  return org.h2.util.geometry.EWKTUtils.ewkt2ewkb(ewkt);
}

static org.h2.value.ExtTypeInfoGeometry type(int srid) {
  // Only the SRID is read by the conversion, but the type still has to be something. The same
  // value is handed to the C# side so that the two calls are the same call.
  return srid == 0 ? null : new org.h2.value.ExtTypeInfoGeometry(1, srid);
}

static void out(String name, int srid, java.util.function.Supplier<org.h2.value.Value> factory) {
String result;
try {
org.h2.value.Value value = factory.get();
org.h2.value.ValueGeometry g = value.convertToGeometry(type(srid));
result = hex(g.getString()) + " / " + g.getSRID() + " / " + g.getTypeAndDimensionSystem();
} catch (org.h2.message.DbException e) {
result = e.getErrorCode() + ":" + e.getMessage();
} catch (RuntimeException e) {
result = "THROWS " + e.getClass().getSimpleName();
}
System.out.println(result);
}
}

{{calls}}
}
""";
    }

    private static string Describe((string Name, Func<Value> CSharp, string Java, int Srid) c)
    {
        TypeInfo target = c.Srid == 0
            ? TypeInfo.TypeGeometry
            : TypeInfo.GetTypeInfo(Value.Geometry, -1, -1, new ExtTypeInfoGeometry(GeometryUtils.Point, c.Srid));
        try
        {
            // The value is built inside the try on purpose: the reference evaluates its factory
            // inside its own try, so a source that cannot even be built is a result and not a
            // failure of the test.
            ValueGeometry geometry = (ValueGeometry)c.CSharp().ConvertToType(target);
            return string.Join(
                " / ",
                Hex(geometry.GetString()),
                geometry.GetSrid().ToString(CultureInfo.InvariantCulture),
                geometry.GetTypeAndDimensionSystem().ToString(CultureInfo.InvariantCulture));
        }
        catch (H2SharpException e)
        {
            // The trailing "[code-build]" is how the engine decorates a message when it is shown,
            // and the reference shows it here too.
            StringBuilder message = new(TraceSuffix.Replace(e.Message, string.Empty));
            _ = message.Append(" [").Append(e.H2ErrorCode.ToString(CultureInfo.InvariantCulture))
                .Append('-').Append(Constants.BuildId.ToString(CultureInfo.InvariantCulture)).Append(']');
            return e.H2ErrorCode + ":" + message;
        }
        catch (Exception e) when (e is not H2SharpException)
        {
            return "THROWS " + e.GetType().Name;
        }
    }

    private static Value Geom(string ewkt) => ValueGeometry.GetFromEWKB(EWKTUtils.EWKT2EWKB(ewkt));

    private static Value Json(string text) => ValueJson.FromJson(text);

    private static byte[] Ewkb(string ewkt) => EWKTUtils.EWKT2EWKB(ewkt);

    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    /// <summary>
    /// The same hex the reference side prints, so that a geometry with non-ASCII or control characters
    /// in it travels as text and comes back as text on both sides.
    /// </summary>
    private static string Hex(string text)
    {
        StringBuilder hex = new((text.Length * 4) + 1);
        _ = hex.Append('=');
        foreach (char ch in text)
        {
            _ = hex.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
        }

        return hex.ToString();
    }

    private static string JavaString(string text)
    {
        StringBuilder b = new();
        _ = b.Append('"');
        foreach (char c in text)
        {
            switch (c)
            {
                case '"':
                    _ = b.Append("\\\"");
                    break;
                case '\\':
                    _ = b.Append("\\\\");
                    break;
                default:
                    _ = b.Append(c);
                    break;
            }
        }

        return _ = b.Append('"').ToString();
    }
}
