/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the GEOMETRY formats and the value built on them.
 *
 * A geometry in the reference is not an object, it is a format: EWKT in the text a person writes and
 * EWKB in what is stored. So there is nothing here to compare but strings and bytes, which is the
 * easiest thing in the port to compare exactly and the easiest thing to get wrong quietly, since
 * both formats are self-consistent whatever they decide a coordinate is.
 *
 * The corpus is where the formats are difficult rather than where they are easy: every kind, empty
 * of everything, the four dimension systems, an SRID, the alternative spelling of a MULTIPOINT, a
 * polygon with a hole and with an empty hole, a ring that does not close, a coordinate written in
 * exponents, a negative zero, a blank before and after everything, and the words that differ only
 * by case, including the ones where the JDK and .NET disagree about what case means.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Util.Geometry;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]

public sealed class GeometryOracleTests
{
    /// <summary>
    /// The texts of the corpus, where every one of them is either a geometry or a refusal.
    /// </summary>
    private static readonly string[] Texts =
    [
        "POINT(1 2)",
        "POINT (1 2)",
        "POINT EMPTY",
        "point empty",
        "POINT Z(1 2 3)",
        "POINT ZM(1 2 3 4)",
        "POINT M(1 2 3)",

        // A coordinate large or small enough to be written by Double.toString in the exponent form,
        // which the EWKT writer cuts apart. Without these the cut is never taken and the bug it
        // hides is never reached.
        "POINT(1e300 -1e-300)",
        "POINT(1e21 1e-7)",
        "POINT(1e100 1e-100)",
        "LINESTRING(1e300 1e-300, 1 2)",
        "POLYGON((1e300 1e-300, 1e300 1, 1 1, 1e300 1e-300))",
        "SRID=4326;POINT(1 2)",
        "SRID=4326;POINT Z(1 2 3)",
        "SRID=-1;POINT(0 0)",
        "  POINT ( 1   2 )  ",
        "POINTZ(1 2)",
        "POINTZ (1 2)",
        "LINESTRING(1 2, 3 4)",
        "LINESTRING EMPTY",
        "LINESTRING(1 2)",
        "LINESTRING(1 2, 3 4, 5 6, 1 2)",
        "LINESTRING Z(1 2 3, 4 5 6)",
        "LINESTRING M(1 2 3, 4 5 6)",
        "LINESTRING ZM(1 2 3 4, 5 6 7 8)",
        "POLYGON((0 0, 4 0, 4 4, 0 4, 0 0))",
        "POLYGON EMPTY",
        "POLYGON((0 0, 4 0, 4 4, 0 4, 0 0), (1 1, 2 1, 2 2, 1 1))",
        "POLYGON((0 0, 4 0, 4 4, 0 4, 0 0), EMPTY)",
        "POLYGON Z((0 0 1, 4 0 2, 4 4 3, 0 0 1))",
        "MULTIPOINT(1 2, 3 4)",
        "MULTIPOINT((1 2), (3 4))",
        "MULTIPOINT EMPTY",
        "MULTIPOINT Z((1 2 3), (4 5 6))",
        "MULTILINESTRING((1 2, 3 4), (5 6, 7 8))",
        "MULTILINESTRING EMPTY",
        "MULTIPOLYGON(((0 0, 1 0, 1 1, 0 0)), ((2 2, 3 2, 3 3, 2 2)))",
        "MULTIPOLYGON EMPTY",
        "GEOMETRYCOLLECTION(POINT(1 2), LINESTRING(3 4, 5 6))",
        "GEOMETRYCOLLECTION EMPTY",
        "GEOMETRYCOLLECTION(GEOMETRYCOLLECTION(POINT EMPTY))",
        "GEOMETRYCOLLECTION(MULTIPOINT((1 2)), POLYGON EMPTY)",
        "SRID=4326;MULTIPOINT((1 2))",
        "SRID=4326;GEOMETRYCOLLECTION(POINT(1 2))",
        "POINT(-0 0)",
        "POINT(1e2 -1.5e-3)",
        "POINT(1E+2 2)",
        "POINT(.5 .5)",
        "POINT(1. 2.)",
        "POINT(1 2 3)",
        "POINT(1 2))",
        "POINT(1 2",
        "POINT Z POINT(1 2)",
        "POINT(1 2, 3 4)",
        "LINESTRING(1 2, 3 4, 5 6)",
        "POLYGON((0 0, 1 0, 1 1))",
        "POLYGON((0 0, 1 0, 1 1, 2 2))",
        "POLYGON(EMPTY, (0 0, 1 0, 1 1, 0 0))",
        "SRID=4326 POINT(1 2)",
        "SRID=abc;POINT(1 2)",
        "SRID=;POINT(1 2)",
        "SRID=4326;",
        "POINT(1 2)extra",
        "GEOMETRYCOLLECTION(LINESTRING(1 2))",
        "MULTIPOINT((1 2), POINT(3 4))",
        "",
        " ",
    ];

    /// <summary>
    /// The words a GEOMETRY column is declared with.
    /// </summary>
    private static readonly string[] TypeWords =
    [
        "POINT",
        "point",
        "PoInT",
        "POINT Z",
        "POINT ZM",
        "POINT M",
        "POINT  Z  ",
        "POINTZ",
        "LINESTRING",
        "linestring",
        "POLYGON",
        "MULTIPOINT",
        "MULTILINESTRING",
        "MULTIPOLYGON",
        "GEOMETRYCOLLECTION",
        "geometrycollection",
        "GEOMETRY",
        "MULTI",
        "MULTIPOIN",
        "POIN",
        "POINT Z extra",
        "POINT(1 2)",
        "POINT ZM extra",
        "",
        " ",
        "Z",
        "ZM",
        "M",
        "z",
    ];

    /// <summary>
    /// The words that differ from the real ones only by a character whose case .NET and the JDK
    /// disagree about: the dotless i of the Turkish, and the Kelvin sign.
    /// </summary>
    private static readonly string[] FoldWords =
    [
        "poİnt",
        "poınt",
        "POİNT",
        "LINESTRİNG",
        "LINESTRıNG",
        "GEOMETRYCOLLECTİON",
        "KPOINT",
        "MULTİPOINT",
    ];

    [JavaOracleFact]
    public void TheTypeWordsMatchTheReference()
    {
        List<string> java = OracleRunner.Run(
                [.. TypeWords, .. FoldWords],
                """
                java.util.function.Function<String, String> parseType = s -> {
                  try {
                    return "" + org.h2.util.geometry.EWKTUtils.parseGeometryType(s);
                  } catch (RuntimeException e) {
                    return "REFUSED";
                  }
                };
                java.util.function.Function<String, String> parseDimensions = s -> {
                  try {
                    return "" + org.h2.util.geometry.EWKTUtils.parseDimensionSystem(s);
                  } catch (RuntimeException e) {
                    return "REFUSED";
                  }
                };
                java.util.function.Function<String, String> formatCode = s -> {
                  try {
                    return org.h2.util.geometry.EWKTUtils.formatGeometryTypeAndDimensionSystem(
                        new StringBuilder(), Integer.parseInt(s)).toString();
                  } catch (RuntimeException e) {
                    return "REFUSED";
                  }
                };
                for (int i = 0; i < inputs.length; i++) {
                  String s = inputs[i];
                  System.out.println(hex(parseType.apply(s) + "|"
                      + parseDimensions.apply(s) + "|"
                      + formatCode.apply(s)));
                }
                """,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(Decode)
            .ToList();

        List<string> csharp = [];
        foreach (string word in TypeWords)
        {
            csharp.Add($"{GeometryType(word)}|{DimensionSystemWord(word)}|{FormatCode(word)}");
        }

        foreach (string word in FoldWords)
        {
            csharp.Add($"{GeometryType(word)}|{DimensionSystemWord(word)}|{FormatCode(word)}");
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(
                java[i] == csharp[i],
                $"\"{(i < TypeWords.Length ? TypeWords : FoldWords)[i % TypeWords.Length]}\":\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [JavaOracleFact]
    public void TheTypesFormatAsTheReferenceFormatsThem()
    {
        List<int> codes = [];
        for (int type = 1; type <= 7; type++)
        {
            for (int dimension = 0; dimension <= 3; dimension++)
            {
                codes.Add((dimension * 1_000) + type);
            }
        }

        codes.AddRange([0, 8, -1, 1_000, 3_001, 7_004, -2_001, 2_999]);

        List<string> java = OracleRunner.Run(
                [],
                """
                for (int i = 0; i < nums.length; i++) {
                  try {
                    System.out.println(hex(org.h2.util.geometry.EWKTUtils
                        .formatGeometryTypeAndDimensionSystem(new StringBuilder(), nums[i]).toString()));
                  } catch (RuntimeException e) {
                    System.out.println(hex("REFUSED"));
                  }
                }
                """,
                numbers: [.. codes],
                extraJavaArgs: ["-Duser.language=en"])
            .Select(Decode)
            .ToList();

        List<string> csharp = [.. codes.Select(FormatCodeNumber)];

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"{codes[i]}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [JavaOracleFact]
    public void TheTextsAndBytesMatchTheReference()
    {
        List<string> java = OracleRunner.Run(
                Texts,
                """
                java.util.function.Function<double[], String> envelope = result -> {
                  if (result == null) {
                    return "none";
                  }
                  StringBuilder b = new StringBuilder();
                  for (int i = 0; i < result.length; i++) {
                    if (i > 0) {
                      b.append(' ');
                    }
                    b.append(Long.toHexString(Double.doubleToLongBits(result[i])));
                  }
                  return b.toString();
                };
                java.util.function.Function<String, String> dimension = s -> {
                  try {
                    return "" + org.h2.util.geometry.EWKTUtils.getDimensionSystem(s);
                  } catch (RuntimeException e) {
                    return "REFUSED";
                  }
                };
                java.util.function.Function<String, byte[]> toBytes = s -> {
                  try {
                    return org.h2.util.geometry.EWKTUtils.ewkt2ewkb(s);
                  } catch (RuntimeException e) {
                    return null;
                  }
                };
                java.util.function.Function<byte[], String> text = b -> {
                  try {
                    return org.h2.util.geometry.EWKTUtils.ewkb2ewkt(b);
                  } catch (RuntimeException e) {
                    return "REFUSED";
                  }
                };
                java.util.function.Function<byte[], String> canonical = b -> {
                  try {
                    return org.h2.util.StringUtils.convertBytesToHex(org.h2.util.geometry.EWKBUtils.ewkb2ewkb(b));
                  } catch (RuntimeException e) {
                    return "REFUSED";
                  }
                };
                java.util.function.Function<byte[], String> dimensionsOfBytes = b -> {
                  try {
                    return "" + org.h2.util.geometry.EWKBUtils.getDimensionSystem(b);
                  } catch (RuntimeException e) {
                    return "REFUSED";
                  }
                };
                java.util.function.Function<byte[], String> value = b -> {
                  try {
                    org.h2.value.ValueGeometry v = org.h2.value.ValueGeometry.getFromEWKB(b);
                    StringBuilder s = new StringBuilder();
                    s.append(v.getTypeAndDimensionSystem()).append(',');
                    s.append(v.getGeometryType()).append(',');
                    s.append(v.getDimensionSystem()).append(',');
                    s.append(v.getSRID()).append(',');
                    s.append(v.getMemory()).append(',');
                    s.append(v.getString()).append(',');
                    s.append(v.getSQL(new StringBuilder(), 8)).append(',');
                    s.append(envelope.apply(v.getEnvelopeNoCopy()));
                    return s.toString();
                  } catch (RuntimeException e) {
                    return "REFUSED";
                  }
                };
                for (int i = 0; i < inputs.length; i++) {
                  String s = inputs[i];
                  StringBuilder b = new StringBuilder();
                  b.append(dimension.apply(s)).append('|');
                  byte[] bytes = toBytes.apply(s);
                  if (bytes == null) {
                    b.append("REFUSED");
                  } else {
                    b.append(org.h2.util.StringUtils.convertBytesToHex(bytes));
                    b.append('|').append(text.apply(bytes));
                    b.append('|').append(canonical.apply(bytes));
                    b.append('|').append(dimensionsOfBytes.apply(bytes));
                    b.append('|').append(value.apply(bytes));
                  }
                  System.out.println(hex(b.toString()));
                }
                """,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(Decode)
            .ToList();

        List<string> csharp = [.. Texts.Select(Describe)];

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"\"{Texts[i]}\":\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [JavaOracleFact]
    public void TheEnvelopeArithmeticMatchesTheReference()
    {
        // Every pair of a small set of envelopes, with a missing one on both sides, because an
        // envelope that is not there is an answer of its own rather than an empty one.
        double[]?[][] envelopes =
                [
                    [null, [1d, 2d, 3d, 4d], [0d, 0d, 1d, 1d], [-5d, 5d, 0d, 10d], [1d, 1d, 1d, 1d], [2d, 2d, 2d, 2d]],
            [null, [1d, 2d, 3d, 4d], [0d, 0d, 1d, 1d], [-5d, 5d, 0d, 10d], [1d, 1d, 1d, 1d], [2d, 2d, 2d, 2d]],
        ];

        // Every envelope is an input of its own, because a number cannot travel through the oracle as a
        // number: the four doubles of an envelope go as their bits, in hex, and one that is not there
        // is the word "null" rather than four zeros.
        List<string> envelopeInputs = [];
        foreach (double[]?[] row in envelopes)
        {
            foreach (double[]? value in row)
            {
                envelopeInputs.Add(value is null ? "null" : string.Join(' ', value.Select(D)));
            }
        }

        List<string> java = OracleRunner.Run(
                envelopeInputs,
                """
                java.util.function.Function<String, double[]> box = s -> {
                  if (s.equals("null")) {
                    return null;
                  }
                  String[] parts = s.split(" ");
                  double[] result = new double[parts.length];
                  for (int i = 0; i < parts.length; i++) {
                    result[i] = Double.longBitsToDouble(Long.parseUnsignedLong(parts[i], 16));
                  }
                  return result;
                };
                java.util.function.Function<double[], String> envelope = result -> {
                  if (result == null) {
                    return "none";
                  }
                  StringBuilder s = new StringBuilder();
                  for (int i = 0; i < result.length; i++) {
                    if (i > 0) {
                      s.append(' ');
                    }
                    s.append(Long.toHexString(Double.doubleToLongBits(result[i])));
                  }
                  return s.toString();
                };
                int perRow = inputs.length / 2;
                for (int row = 0; row < 2; row++) {
                  double[] left = box.apply(inputs[row * perRow]);
                  for (int i = 0; i < perRow; i++) {
                    double[] right = box.apply(inputs[row * perRow + i]);
                    System.out.println(hex(org.h2.util.geometry.GeometryUtils.intersects(left, right) + "|"
                        + envelope.apply(org.h2.util.geometry.GeometryUtils.union(left, right)) + "|"
                        + envelope.apply(org.h2.util.geometry.GeometryUtils.union(right, left))));
                  }
                }
                """,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(Decode)
            .ToList();

        List<string> csharp = [];
        foreach (double[]?[] row in envelopes)
        {
            foreach (double[]? other in row)
            {
                csharp.Add(string.Join(
                    '|',
                    J(GeometryUtils.Intersects(row[0], other)),
                    BitsOf(GeometryUtils.Union(row[0], other)),
                    BitsOf(GeometryUtils.Union(other, row[0]))));
            }
        }

        if (java.Count != csharp.Count)
        {
            Assert.Fail($"java {java.Count} строк, csharp {csharp.Count}; java: {string.Join(" ;; ", java)}");
        }

        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"row {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [JavaOracleFact]
    public void TheExtendedTypeRendersAsTheReferenceRendersIt()
    {
        int[] codes = [0, 1, 2, 3, 4, 5, 6, 7, 1_001, 1_002, 1_003, 7_003, 0 + 0];

        // Two numbers per row, because the SRID is there only for every other row and a null cannot
        // travel as a number.
        int[] pairs = [.. codes.SelectMany(c => new[] { c, 4326 })];

        List<string> java = OracleRunner.Run(
                [],
                """
                for (int i = 0; i + 1 < nums.length; i += 2) {
                  int type = nums[i];
                  Integer srid = Integer.valueOf(nums[i + 1]);
                  StringBuilder b = new StringBuilder();
                  try {
                    org.h2.value.ExtTypeInfoGeometry ext = new org.h2.value.ExtTypeInfoGeometry(type, srid);
                    b.append(ext.getSQL(new StringBuilder(), 0)).append('|');
                    b.append(ext.getType()).append('|').append(ext.getSrid()).append('|');
                    b.append(ext.hashCode()).append('|');
                    b.append(ext.equals(new org.h2.value.ExtTypeInfoGeometry(type, srid))).append('|');
                    b.append(ext.equals(new org.h2.value.ExtTypeInfoGeometry(type + 1, srid))).append('|');
                    b.append(ext.equals(null));
                  } catch (RuntimeException e) {
                    b.setLength(0);
                    b.append("REFUSED");
                  }
                  System.out.println(hex(b.toString()));
                }
                """,
                numbers: pairs,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(Decode)
            .ToList();

        List<string> csharp = [];
        for (int i = 0; i + 1 < pairs.Length; i += 2)
        {
            int code = pairs[i];
            foreach (int? srid in new int?[] { pairs[i + 1] })
            {
                ExtTypeInfoGeometry ext = new(code, srid);
                csharp.Add(AsText(() => string.Join(
                    '|',
                    ext.GetSql(new StringBuilder(), 0),
                    ext.GetTypeAndDimensionSystem(),
                    ext.GetSrid()?.ToString(CultureInfo.InvariantCulture) ?? "null",
                    ext.GetHashCode(),
                    J(ext.Equals(new ExtTypeInfoGeometry(code, srid))),
                    J(ext.Equals(new ExtTypeInfoGeometry(code + 1, srid))),
                    J(ext.Equals(null)))));
            }
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"code {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    private static string Describe(string text)
    {
        StringBuilder b = new();
        _ = b.Append(Dimension(text)).Append('|');

        byte[]? bytes;
        try
        {
            bytes = EWKTUtils.EWKT2EWKB(text);
        }
        catch (Exception e) when (IsRefusal(e))
        {
            return b.Append("REFUSED").ToString();
        }

        _ = b.Append(Hex.BytesToHex(bytes)).Append('|')
            .Append(AsText(() => EWKTUtils.EWKB2EWKT(bytes))).Append('|')
            .Append(AsText(() => Hex.BytesToHex(EWKBUtils.EWKB2EWKB(bytes)))).Append('|')
            .Append(AsText(() => EWKBUtils.GetDimensionSystem(bytes).ToString(CultureInfo.InvariantCulture)))
            .Append('|')
            .Append(AsValue(bytes));
        return b.ToString();
    }

    private static string AsValue(byte[] bytes)
    {
        try
        {
            ValueGeometry value = (ValueGeometry)ValueGeometry.GetFromEWKB(bytes);
            StringBuilder b = new();
            return string.Join(
                ',',
                value.GetTypeAndDimensionSystem(),
                value.GetGeometryType(),
                value.GetDimensionSystem(),
                value.GetSrid(),
                value.GetMemory(),
                value.GetString(),
                value.GetSql(new StringBuilder(), 8),
                BitsOf(value.GetEnvelopeNoCopy()));
        }
        catch (Exception e) when (IsRefusal(e))
        {
            return "REFUSED";
        }
    }

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

    private static string Dimension(string text) => AsText(
        () => EWKTUtils.GetDimensionSystem(text).ToString(CultureInfo.InvariantCulture));

    private static string GeometryType(string word) => AsText(
        () => EWKTUtils.ParseGeometryType(word).ToString(CultureInfo.InvariantCulture));

    private static string DimensionSystemWord(string word) => AsText(
        () => EWKTUtils.ParseDimensionSystem(word).ToString(CultureInfo.InvariantCulture));

    private static string FormatCode(string word) => FormatCodeNumber(
        int.TryParse(word, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code) ? code : -1);

    private static string FormatCodeNumber(int code)
    {
        try
        {
            return EWKTUtils.FormatGeometryTypeAndDimensionSystem(new StringBuilder(), code).ToString();
        }
        catch (ArgumentException)
        {
            return "REFUSED";
        }
    }

    private static string BitsOf(double[]? envelope)
        => envelope is null
            ? "none"
            : string.Join(' ', envelope.Select(D));

    private static string D(double value)
        => BitConverter.DoubleToInt64Bits(value).ToString("x", CultureInfo.InvariantCulture);

    private static string J(bool value) => value ? "true" : "false";

    private static bool IsRefusal(Exception e)
        => e is ArgumentException
            or FormatException
            or IndexOutOfRangeException
            or ArithmeticException
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
