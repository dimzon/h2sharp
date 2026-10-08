/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the type model.
 *
 * The singletons are compared field by field against the reference, which is what makes
 * them trustworthy: precision and scale are the kind of numbers that look right and are
 * not, and every one of them was read from the reference rather than remembered.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]

public sealed class TypeInfoOracleTests
{
    /// <summary>
    /// One line per singleton: name, value type, precision, scale.
    /// </summary>
    private const string DumpSingletons = """
        java.lang.reflect.Field[] fields = org.h2.value.TypeInfo.class.getFields();
        java.util.TreeMap<String, org.h2.value.TypeInfo> sorted = new java.util.TreeMap<>();
        for (java.lang.reflect.Field f : fields) {
          if (org.h2.value.TypeInfo.class.isAssignableFrom(f.getType())
                  && java.lang.reflect.Modifier.isStatic(f.getModifiers())) {
            sorted.put(f.getName(), (org.h2.value.TypeInfo) f.get(null));
          }
        }
        for (java.util.Map.Entry<String, org.h2.value.TypeInfo> e : sorted.entrySet()) {
          org.h2.value.TypeInfo t = e.getValue();
          System.out.println(e.getKey() + " " + t.getValueType()
              + " " + t.getPrecision() + " " + t.getScale());
        }
        """;

    private static List<string> JavaSingletons() => [.. OracleRunner.Run([], DumpSingletons)];

    [JavaOracleFact]
    public void SingletonTypes_MatchReference()
    {
        List<string> java = JavaSingletons();

        List<string> csharp =
        [
            .. new (string Name, TypeInfo Type)[]
            {
                ("TYPE_UNKNOWN", TypeInfo.TypeUnknown),
                ("TYPE_NULL", TypeInfo.TypeNull),
                ("TYPE_CHAR", TypeInfo.TypeChar),
                ("TYPE_VARCHAR", TypeInfo.TypeVarchar),
                ("TYPE_CLOB", TypeInfo.TypeClob),
                ("TYPE_VARCHAR_IGNORECASE", TypeInfo.TypeVarcharIgnorecase),
                ("TYPE_BINARY", TypeInfo.TypeBinary),
                ("TYPE_VARBINARY", TypeInfo.TypeVarbinary),
                ("TYPE_BLOB", TypeInfo.TypeBlob),
                ("TYPE_BOOLEAN", TypeInfo.TypeBoolean),
                ("TYPE_TINYINT", TypeInfo.TypeTinyint),
                ("TYPE_SMALLINT", TypeInfo.TypeSmallint),
                ("TYPE_INTEGER", TypeInfo.TypeInteger),
                ("TYPE_BIGINT", TypeInfo.TypeBigint),
                ("TYPE_NUMERIC_SCALE_0", TypeInfo.TypeNumericScale0),
                ("TYPE_NUMERIC_BIGINT", TypeInfo.TypeNumericBigint),
                ("TYPE_NUMERIC_FLOATING_POINT", TypeInfo.TypeNumericFloatingPoint),
                ("TYPE_REAL", TypeInfo.TypeReal),
                ("TYPE_DOUBLE", TypeInfo.TypeDouble),
                ("TYPE_DECFLOAT", TypeInfo.TypeDecfloat),
                ("TYPE_DECFLOAT_BIGINT", TypeInfo.TypeDecfloatBigint),
                ("TYPE_DATE", TypeInfo.TypeDate),
                ("TYPE_TIME", TypeInfo.TypeTime),
                ("TYPE_TIME_TZ", TypeInfo.TypeTimeTz),
                ("TYPE_TIMESTAMP", TypeInfo.TypeTimestamp),
                ("TYPE_TIMESTAMP_TZ", TypeInfo.TypeTimestampTz),
                ("TYPE_INTERVAL_DAY", TypeInfo.TypeIntervalDay),
                ("TYPE_INTERVAL_YEAR_TO_MONTH", TypeInfo.TypeIntervalYearToMonth),
                ("TYPE_INTERVAL_DAY_TO_SECOND", TypeInfo.TypeIntervalDayToSecond),
                ("TYPE_INTERVAL_HOUR_TO_SECOND", TypeInfo.TypeIntervalHourToSecond),
                ("TYPE_JAVA_OBJECT", TypeInfo.TypeJavaObject),
                ("TYPE_ENUM_UNDEFINED", TypeInfo.TypeEnumUndefined),
                ("TYPE_GEOMETRY", TypeInfo.TypeGeometry),
                ("TYPE_JSON", TypeInfo.TypeJson),
                ("TYPE_UUID", TypeInfo.TypeUuid),
                ("TYPE_ARRAY_UNKNOWN", TypeInfo.TypeArrayUnknown),
                ("TYPE_ROW_EMPTY", TypeInfo.TypeRowEmpty),
            }
                .Select(static e => $"{e.Name} {e.Type.GetValueType()} "
                    + $"{e.Type.GetPrecision()} {e.Type.GetScale()}"),
        ];

        csharp.Sort(StringComparer.Ordinal);
        List<string> sortedJava = [.. java];
        sortedJava.Sort(StringComparer.Ordinal);

        Assert.Equal(sortedJava, csharp);
    }

    [JavaOracleFact]
    public void GetTypeInfo_MatchesReferenceForEveryTypeNumber()
    {
        string body = """
                      for (int i = -1; i < org.h2.value.Value.TYPE_COUNT; i++) {
                        String result;
                        try {
                          org.h2.value.TypeInfo t = org.h2.value.TypeInfo.getTypeInfo(i);
                          result = t.getValueType() + " " + t.getPrecision() + " " + t.getScale();
                        } catch (RuntimeException e) {
                          result = "THROWS";
                        }
                        System.out.println(hex(result));
                      }
            """;

        List<string> java = OracleRunner.Run([], body);

        List<string> csharp = [];
        for (int type = Value.Unknown; type < Value.TypeCount; type++)
        {
            csharp.Add(Report(type));
        }

        Assert.Equal(java, csharp);
    }

    [JavaOracleFact]
    public void AreSameTypes_MatchesReferenceOverTheSingletons()
    {
        string body = """
                      java.lang.reflect.Field[] fields = org.h2.value.TypeInfo.class.getFields();
                      java.util.List<org.h2.value.TypeInfo> types = new java.util.ArrayList<>();
                      for (java.lang.reflect.Field f : fields) {
                        if (org.h2.value.TypeInfo.class.isAssignableFrom(f.getType())
                                && java.lang.reflect.Modifier.isStatic(f.getModifiers())) {
                          types.add((org.h2.value.TypeInfo) f.get(null));
                        }
                      }
                      for (org.h2.value.TypeInfo t1 : types) {
                        for (org.h2.value.TypeInfo t2 : types) {
                          // ARRAY without an element type makes areSameTypes cast a null
                          // extended type and throw, on both sides. Modelled rather than
                          // left to abort the run half way through.
                          String result;
                          try {
                            result = Boolean.toString(
                                org.h2.value.TypeInfo.areSameTypes(t1, t2));
                          } catch (RuntimeException e) {
                            result = "THROWS";
                          }
                          System.out.println(hex(result));
                        }
                      }
            """;

        List<string> java = OracleRunner.Run([], body);

        // The same singleton set on our side, in the same order as the reflection dump.
        TypeInfo[] types =
        [
            TypeInfo.TypeUnknown, TypeInfo.TypeNull, TypeInfo.TypeChar, TypeInfo.TypeVarchar,
            TypeInfo.TypeClob, TypeInfo.TypeVarcharIgnorecase, TypeInfo.TypeBinary,
            TypeInfo.TypeVarbinary, TypeInfo.TypeBlob, TypeInfo.TypeBoolean, TypeInfo.TypeTinyint,
            TypeInfo.TypeSmallint, TypeInfo.TypeInteger, TypeInfo.TypeBigint,
            TypeInfo.TypeNumericScale0, TypeInfo.TypeNumericBigint,
            TypeInfo.TypeNumericFloatingPoint, TypeInfo.TypeReal, TypeInfo.TypeDouble,
            TypeInfo.TypeDecfloat, TypeInfo.TypeDecfloatBigint, TypeInfo.TypeDate,
            TypeInfo.TypeTime, TypeInfo.TypeTimeTz, TypeInfo.TypeTimestamp,
            TypeInfo.TypeTimestampTz, TypeInfo.TypeIntervalDay,
            TypeInfo.TypeIntervalYearToMonth, TypeInfo.TypeIntervalDayToSecond,
            TypeInfo.TypeIntervalHourToSecond, TypeInfo.TypeJavaObject,
            TypeInfo.TypeEnumUndefined, TypeInfo.TypeGeometry, TypeInfo.TypeJson,
            TypeInfo.TypeUuid, TypeInfo.TypeArrayUnknown, TypeInfo.TypeRowEmpty,
        ];

        Assert.Equal(types.Length * types.Length, java.Count);

        List<string> csharp = [];
        foreach (TypeInfo t1 in types)
        {
            foreach (TypeInfo t2 in types)
            {
                csharp.Add(OracleRunner.Hex(Report(t1, t2)));
            }
        }

        Assert.Equal(java, csharp);
    }

    private static string Report(TypeInfo t1, TypeInfo t2)
    {
        try
        {
            return TypeInfo.AreSameTypes(t1, t2)
                ? "true"
                : "false";
        }
        catch (NullReferenceException)
        {
            // An ARRAY without an element type has no extended type to compare, which the
            // reference also treats as a failure rather than as "not the same".
            return "THROWS";
        }
    }

    [JavaOracleFact]
    public void DecimalPrecisions_MatchReference()
    {
        // Read through the public getDecimalPrecision rather than the DECIMAL_PRECISION
        // constants, which are package private in the reference and would need the snippet
        // compiled into org.h2.value. Going through the public accessor also tests the
        // switch itself, which is where the derivation actually lives.
        string body = """
                      int[] types = {
                          org.h2.value.Value.TINYINT, org.h2.value.Value.SMALLINT,
                          org.h2.value.Value.INTEGER, org.h2.value.Value.BIGINT,
                          org.h2.value.Value.REAL, org.h2.value.Value.DOUBLE,
                          org.h2.value.Value.BOOLEAN, org.h2.value.Value.NUMERIC,
                          org.h2.value.Value.DECFLOAT, org.h2.value.Value.DATE,
                          org.h2.value.Value.TIME, org.h2.value.Value.TIMESTAMP,
                          org.h2.value.Value.VARCHAR, org.h2.value.Value.CLOB,
                          org.h2.value.Value.INTERVAL_DAY,
                      };
                      for (int t : types) {
                          System.out.println(hex(Long.toString(
                              org.h2.value.TypeInfo.getTypeInfo(t).getDecimalPrecision())));
                      }
            """;

        List<string> java = OracleRunner.Run([], body);

        List<string> csharp =
        [
            .. new[]
            {
                TypeInfo.TypeTinyint, TypeInfo.TypeSmallint, TypeInfo.TypeInteger,
                TypeInfo.TypeBigint, TypeInfo.TypeReal, TypeInfo.TypeDouble,
                TypeInfo.TypeBoolean, TypeInfo.TypeNumericFloatingPoint, TypeInfo.TypeDecfloat,
                TypeInfo.TypeDate, TypeInfo.TypeTime, TypeInfo.TypeTimestamp,
                TypeInfo.TypeVarchar, TypeInfo.TypeClob, TypeInfo.TypeIntervalDay,
            }
                .Select(static t => OracleRunner.Hex(
                    t.GetDecimalPrecision().ToString(CultureInfo.InvariantCulture))),
        ];

        Assert.Equal(java, csharp);
    }

    [Fact]
    public void GetDecimalPrecision_UsesTheFixedWidths()
    {
        Assert.Equal(3, TypeInfo.TypeTinyint.GetDecimalPrecision());
        Assert.Equal(5, TypeInfo.TypeSmallint.GetDecimalPrecision());
        Assert.Equal(10, TypeInfo.TypeInteger.GetDecimalPrecision());
        Assert.Equal(19, TypeInfo.TypeBigint.GetDecimalPrecision());

        // Real and Double are the surprising ones, which is why they are checked here rather
        // than left to the differential test: 7 and 17, not the 24 and 53 of their bit
        // widths.
        Assert.Equal(7, TypeInfo.TypeReal.GetDecimalPrecision());
        Assert.Equal(17, TypeInfo.TypeDouble.GetDecimalPrecision());

        // A type with no fixed width reports its declared precision.
        Assert.Equal(-1, TypeInfo.TypeVarchar.GetDecimalPrecision());
    }

    [Fact]
    public void IntervalScalesFollowWhetherTheTypeCarriesSeconds()
    {
        // The four second-bearing intervals keep the maximum scale; the others report the derived
        // zero, because their stored scale is the "unspecified" sentinel.
        Assert.Equal(TypeInfo.IntervalMaximumScale, TypeInfo.TypeIntervalDayToSecond.GetScale());
        Assert.Equal(TypeInfo.IntervalMaximumScale, TypeInfo.TypeIntervalHourToSecond.GetScale());
        Assert.Equal(0, TypeInfo.TypeIntervalDay.GetScale());
        Assert.Equal(0, TypeInfo.TypeIntervalYearToMonth.GetScale());
    }

    private static string Report(int type)
    {
        try
        {
            TypeInfo t = TypeInfo.GetTypeInfo(type);
            return OracleRunner.Hex(string.Create(
                CultureInfo.InvariantCulture,
                $"{t.GetValueType()} {t.GetPrecision()} {t.GetScale()}"));
        }
        catch (H2SharpException)
        {
            return OracleRunner.Hex("THROWS");
        }
    }
}
