/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the promotion rules.
 *
 * getHigherType is the rule every arithmetic operator and every INSERT goes through, so it is
 * compared over every ordered pair of a type grid rather than over hand picked pairs. The grid
 * carries the parameters that move: a decimal with a scale, a string with a length, a timestamp
 * with a fractional seconds precision, an array, and rows of two degrees, because those are
 * the types whose promoted form is not simply the "wider" operand.
 *
 * Comparable and HaveSameOrdering are compared as answers and as the exceptions they throw,
 * since a promotion rule that returns the wrong type and one that refuses outright are
 * different bugs and the grid exercises both.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]

public sealed class TypeInfoPromotionOracleTests
{
    /// <summary>
    /// The types put through every pair, and the name each is reported under.
    /// </summary>
    /// <remarks>
    /// Chosen to cover each branch of the reference's switch rather than to be exhaustive over
    /// all 49 value types: NULL and UNKNOWN, the numeric family with its moving scale, the
    /// approximate numbers that reset precision, the strings with and without a length, the
    /// date and time family where a DATE is not comparable with a TIME, the intervals, the
    /// case insensitive string, the collection types, and rows of two degrees.
    /// <para>
    /// The names live here rather than in a second table next to the Java literal, so a failure
    /// names the pair that broke without a lookup. The Java side repeats the same list, which
    /// is why the count is asserted below rather than trusted.
    /// </para>
    /// </remarks>
    private static readonly (string Spec, TypeInfo Type)[] Grid =
    [
        ("null", TypeInfo.TypeNull),
        ("unknown", TypeInfo.TypeUnknown),
        ("boolean", TypeInfo.TypeBoolean),
        ("tinyint", TypeInfo.TypeTinyint),
        ("smallint", TypeInfo.TypeSmallint),
        ("integer", TypeInfo.TypeInteger),
        ("bigint", TypeInfo.TypeBigint),
        ("n10.2", TypeInfo.GetTypeInfo(Value.Numeric, 10, 2, null)),
        ("n0", TypeInfo.TypeNumericScale0),
        ("n30.0", TypeInfo.GetTypeInfo(Value.Numeric, 30, 0, null)),
        ("decfloat", TypeInfo.TypeDecfloat),
        ("decfloat20", TypeInfo.GetTypeInfo(Value.Decfloat, 20, 0, null)),
        ("real", TypeInfo.TypeReal),
        ("double", TypeInfo.TypeDouble),
        ("date", TypeInfo.TypeDate),
        ("time", TypeInfo.TypeTime),
        ("timetz", TypeInfo.TypeTimeTz),
        ("timestamp", TypeInfo.TypeTimestamp),
        ("ts3", TypeInfo.GetTypeInfo(Value.Timestamp, 3, 0, null)),
        ("timestamptz", TypeInfo.TypeTimestampTz),
        ("iday", TypeInfo.TypeIntervalDay),
        ("idays", TypeInfo.TypeIntervalDayToSecond),
        ("char", TypeInfo.TypeChar),
        ("varchar5", TypeInfo.GetTypeInfo(Value.Varchar, 5, 0, null)),
        ("varchar", TypeInfo.TypeVarchar),
        ("varcharic", TypeInfo.TypeVarcharIgnorecase),
        ("clob", TypeInfo.TypeClob),
        ("binary", TypeInfo.TypeBinary),
        ("varbinary", TypeInfo.TypeVarbinary),
        ("blob", TypeInfo.TypeBlob),
        ("javaobject", TypeInfo.TypeJavaObject),
        ("uuid", TypeInfo.TypeUuid),
        ("json", TypeInfo.TypeJson),
        ("geometry", TypeInfo.TypeGeometry),
        ("enum", TypeInfo.GetTypeInfo(Value.Enum, -1, -1, new ExtTypeInfoEnum(["P", "Q"]))),
        ("arr2", TypeInfo.GetTypeInfo(
            Value.Array, 4, 0,
            TypeInfo.GetTypeInfo(Value.Array, 0, 0, TypeInfo.TypeInteger))),
        ("row2", Row([("a", TypeInfo.TypeInteger), ("b", TypeInfo.TypeVarchar)])),
        ("row1", Row([("a", TypeInfo.TypeInteger)])),
        ("row0", TypeInfo.TypeRowEmpty),
    ];

    [JavaOracleFact]
    public void GetHigherType_MatchesReference()
    {
        string body = """
                      for (int i = 0; i < specs.length; i++) {
                        for (int j = 0; j < specs.length; j++) {
                          String line;
                          try {
                            // 1 is QUOTE_ONLY_WHEN_REQUIRED, so the promoted type is rendered
                            // the way it would appear in SQL rather than as a bare name.
                            line = "H " + org.h2.value.TypeInfo.getHigherType(
                                grid[i], grid[j]).getSQL(1);
                          } catch (org.h2.message.DbException ex) {
                            line = "EX " + ex.getErrorCode() + " " + ex.getMessage();
                          } catch (RuntimeException ex) {
                            line = "EX -1 " + ex.getMessage();
                          }
                          System.out.println(hex(specs[i] + " | " + specs[j] + " | " + line));
                        }
                      }
            """;

        List<string> java = Run(body);

        List<string> csharp = [];
        foreach ((string spec, TypeInfo left) in Grid)
        {
            foreach ((string other, TypeInfo right) in Grid)
            {
                csharp.Add($"{spec} | {other} | {Higher(left, right)}");
            }
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], Failure(java[i], csharp[i]));
        }
    }

    [JavaOracleFact]
    public void Comparability_MatchesReference()
    {
        string body = """
                      for (int i = 0; i < specs.length; i++) {
                        for (int j = 0; j < specs.length; j++) {
                          StringBuilder line = new StringBuilder();
                          line.append(specs[i]).append(' ').append(specs[j]);
                          try {
                            org.h2.value.TypeInfo.checkComparable(grid[i], grid[j]);
                            line.append(" C-");
                          } catch (org.h2.message.DbException ex) {
                            line.append(" EX ").append(ex.getErrorCode());
                          }
                          line.append(' ').append(
                              org.h2.value.TypeInfo.haveSameOrdering(grid[i], grid[j]));
                          System.out.println(hex(line.toString()));
                        }
                      }
            """;

        List<string> java = Run(body);

        List<string> csharp = [];
        foreach ((string spec, TypeInfo left) in Grid)
        {
            foreach ((string other, TypeInfo right) in Grid)
            {
                csharp.Add(Comparability(spec, left, other, right));
            }
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], Failure(java[i], csharp[i]));
        }
    }

    /// <summary>
    /// Unwrapping a single field row is recursive, and it is what makes a row of one column
    /// compare as that column.
    /// </summary>
    [JavaOracleFact]
    public void UnwrapRow_MatchesReference()
    {
        string body = """
                      java.util.LinkedHashMap<String, org.h2.value.TypeInfo> one =
                          new java.util.LinkedHashMap<>();
                      one.put("a", org.h2.value.TypeInfo.TYPE_INTEGER);
                      org.h2.value.TypeInfo rowOne = org.h2.value.TypeInfo.getTypeInfo(
                          org.h2.value.Value.ROW, 0L, 0, new org.h2.value.ExtTypeInfoRow(one));
                      java.util.LinkedHashMap<String, org.h2.value.TypeInfo> nested =
                          new java.util.LinkedHashMap<>();
                      nested.put("a", rowOne);
                      org.h2.value.TypeInfo rowNested = org.h2.value.TypeInfo.getTypeInfo(
                          org.h2.value.Value.ROW, 0L, 0, new org.h2.value.ExtTypeInfoRow(nested));
                      System.out.println(hex(rowOne.unwrapRow().getSQL(0)));
                      System.out.println(hex(rowNested.unwrapRow().getSQL(0)));
                      System.out.println(hex(
                          org.h2.value.TypeInfo.TYPE_ROW_EMPTY.unwrapRow().getSQL(0)));
            """;

        List<string> java = OracleRunner.Run([], body).Select(Decode).ToList();

        TypeInfo rowOne = Row([("a", TypeInfo.TypeInteger)]);
        TypeInfo rowNested = Row([("a", rowOne)]);

        Assert.Equal(3, java.Count);
        Assert.Equal(java[0], rowOne.UnwrapRow().GetSql(0));
        Assert.Equal(java[1], rowNested.UnwrapRow().GetSql(0));
        Assert.Equal(java[2], TypeInfo.TypeRowEmpty.UnwrapRow().GetSql(0));
    }

    /// <summary>
    /// Runs a body with the grid already built on the Java side.
    /// </summary>
    /// <remarks>
    /// The grid is statements rather than a helper method on purpose: the harness places the
    /// body inside main, where a method cannot be declared. The language pin matters too, since
    /// the rejects carry translated messages, and the trace suffix that the reference appends
    /// to every exception is a counter this port does not have.
    /// </remarks>
    private static List<string> Run(string body) => OracleRunner.Run(
            [],
            GridJava + body,
            extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
        .Select(Decode)
        .Select(StripTrace)
        .ToList();

    private static string Higher(TypeInfo left, TypeInfo right)
    {
        try
        {
            return "H " + TypeInfo.GetHigherType(left, right).GetSql(HasSql.QuoteOnlyWhenRequired);
        }
        catch (H2SharpException ex)
        {
            return $"EX {ex.H2ErrorCode} {TraceSuffix.Replace(ex.Message, string.Empty)}";
        }
    }

    private static string Comparability(string spec, TypeInfo left, string other, TypeInfo right)
    {
        StringBuilder line = new();
        _ = line.Append(spec).Append(' ').Append(other);
        try
        {
            TypeInfo.CheckComparable(left, right);
            _ = line.Append(" C-");
        }
        catch (H2SharpException ex)
        {
            _ = line.Append(" EX ").Append(ex.H2ErrorCode);
        }

        return line.Append(' ').Append(J(TypeInfo.HaveSameOrdering(left, right))).ToString();
    }

    private static TypeInfo Row((string Name, TypeInfo Type)[] fields)
    {
        OrderedStringMap<TypeInfo> map = new(fields.Length);
        foreach ((string name, TypeInfo type) in fields)
        {
            map.Put(name, type);
        }

        return TypeInfo.GetTypeInfo(Value.Row, 0, 0, new ExtTypeInfoRow(map));
    }

    /// <summary>
    /// The same grid, built in Java.
    /// </summary>
    /// <remarks>
    /// Written out because it cannot be shared with the C# side without an intermediate format
    /// between the two languages, which is the one thing the tooling deliberately avoids. The
    /// pair count is asserted against the C# grid, so a name that drifts on one side fails
    /// rather than quietly comparing the wrong rows.
    /// </remarks>
    private const string GridJava = """
                      String[] specs = new String[] {
                          "null", "unknown", "boolean", "tinyint", "smallint", "integer",
                          "bigint", "n10.2", "n0", "n30.0", "decfloat", "decfloat20",
                          "real", "double", "date", "time", "timetz", "timestamp", "ts3",
                          "timestamptz", "iday", "idays", "char", "varchar5", "varchar",
                          "varcharic", "clob", "binary", "varbinary", "blob", "javaobject",
                          "uuid", "json", "geometry", "enum", "arr2", "row2", "row1", "row0"
                      };
                      java.util.LinkedHashMap<String, org.h2.value.TypeInfo> m2 =
                          new java.util.LinkedHashMap<>();
                      m2.put("a", org.h2.value.TypeInfo.TYPE_INTEGER);
                      m2.put("b", org.h2.value.TypeInfo.TYPE_VARCHAR);
                      java.util.LinkedHashMap<String, org.h2.value.TypeInfo> m1 =
                          new java.util.LinkedHashMap<>();
                      m1.put("a", org.h2.value.TypeInfo.TYPE_INTEGER);
                      java.util.LinkedHashMap<String, org.h2.value.TypeInfo> m0 =
                          new java.util.LinkedHashMap<>();
                      org.h2.value.TypeInfo[] grid = new org.h2.value.TypeInfo[] {
                          org.h2.value.TypeInfo.TYPE_NULL,
                          org.h2.value.TypeInfo.TYPE_UNKNOWN,
                          org.h2.value.TypeInfo.TYPE_BOOLEAN,
                          org.h2.value.TypeInfo.TYPE_TINYINT,
                          org.h2.value.TypeInfo.TYPE_SMALLINT,
                          org.h2.value.TypeInfo.TYPE_INTEGER,
                          org.h2.value.TypeInfo.TYPE_BIGINT,
                          org.h2.value.TypeInfo.getTypeInfo(
                              org.h2.value.Value.NUMERIC, 10L, 2, null),
                          org.h2.value.TypeInfo.TYPE_NUMERIC_SCALE_0,
                          org.h2.value.TypeInfo.getTypeInfo(
                              org.h2.value.Value.NUMERIC, 30L, 0, null),
                          org.h2.value.TypeInfo.TYPE_DECFLOAT,
                          org.h2.value.TypeInfo.getTypeInfo(
                              org.h2.value.Value.DECFLOAT, 20L, 0, null),
                          org.h2.value.TypeInfo.TYPE_REAL,
                          org.h2.value.TypeInfo.TYPE_DOUBLE,
                          org.h2.value.TypeInfo.TYPE_DATE,
                          org.h2.value.TypeInfo.TYPE_TIME,
                          org.h2.value.TypeInfo.TYPE_TIME_TZ,
                          org.h2.value.TypeInfo.TYPE_TIMESTAMP,
                          org.h2.value.TypeInfo.getTypeInfo(
                              org.h2.value.Value.TIMESTAMP, 3L, 0, null),
                          org.h2.value.TypeInfo.TYPE_TIMESTAMP_TZ,
                          org.h2.value.TypeInfo.TYPE_INTERVAL_DAY,
                          org.h2.value.TypeInfo.TYPE_INTERVAL_DAY_TO_SECOND,
                          org.h2.value.TypeInfo.TYPE_CHAR,
                          org.h2.value.TypeInfo.getTypeInfo(
                              org.h2.value.Value.VARCHAR, 5L, 0, null),
                          org.h2.value.TypeInfo.TYPE_VARCHAR,
                          org.h2.value.TypeInfo.TYPE_VARCHAR_IGNORECASE,
                          org.h2.value.TypeInfo.TYPE_CLOB,
                          org.h2.value.TypeInfo.TYPE_BINARY,
                          org.h2.value.TypeInfo.TYPE_VARBINARY,
                          org.h2.value.TypeInfo.TYPE_BLOB,
                          org.h2.value.TypeInfo.TYPE_JAVA_OBJECT,
                          org.h2.value.TypeInfo.TYPE_UUID,
                          org.h2.value.TypeInfo.TYPE_JSON,
                          org.h2.value.TypeInfo.TYPE_GEOMETRY,
                          org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.ENUM, -1L, -1,
                              new org.h2.value.ExtTypeInfoEnum(new String[] { "P", "Q" })),
                          org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.ARRAY, 4L, 0,
                              org.h2.value.TypeInfo.getTypeInfo(
                                  org.h2.value.Value.ARRAY, 0L, 0,
                                  org.h2.value.TypeInfo.TYPE_INTEGER)),
                          org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.ROW, 0L, 0,
                              new org.h2.value.ExtTypeInfoRow(m2)),
                          org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.ROW, 0L, 0,
                              new org.h2.value.ExtTypeInfoRow(m1)),
                          org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.ROW, -1L, -1,
                              new org.h2.value.ExtTypeInfoRow(m0))
                      };

            """;

    /// <remarks>
    /// Java concatenates a boolean as "true" and .NET as "True", so the answer is
    /// formatted rather than appended.
    /// </remarks>
    private static string J(bool value) => value ? "true" : "false";

    private static string Failure(string java, string csharp) =>
        $"{PairOf(java)}\n  java:   {java}\n  csharp: {csharp}";

    private static string PairOf(string java)
    {
        string[] parts = java.Split('|');
        return parts.Length == 3
            ? $"{parts[0].Trim()} against {parts[1].Trim()}"
            : $"{java.Split(' ')[0]} against {java.Split(' ')[1]}";
    }

    private static readonly System.Text.RegularExpressions.Regex TraceSuffix =
        new(@"\s\[\d+-\d+\]$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static string StripTrace(string line) => TraceSuffix.Replace(line, string.Empty);

    private static string Decode(string hex)
    {
        string only = hex.TrimStart('=');
        StringBuilder sb = new();
        for (int i = 0; i + 3 < only.Length; i += 4)
        {
            sb.Append((char)Convert.ToInt32(only.Substring(i, 4), 16));
        }

        return sb.ToString();
    }
}
