/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for rendering a type as SQL.
 *
 * The rendering is pure output: the same type always produces the same string, and the
 * string is what ends up in stored metadata and in every message that mentions a column.
 * So it is compared directly, over the singletons and over a grid of parameterised types,
 * because the interesting behaviour is in the parameters rather than in the names.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class TypeInfoSqlOracleTests
{
    [JavaOracleFact]
    public void SingletonRendering_MatchesReference()
    {
        // Keyed by value type rather than compared positionally: several singletons share a
        // value type (the DECFLOAT and NUMERIC variants), so comparing two differently
        // ordered lists positionally says nothing about correctness.
        string body = """
                      java.lang.reflect.Field[] fields = org.h2.value.TypeInfo.class.getFields();
                      java.util.TreeMap<String, org.h2.value.TypeInfo> sorted =
                          new java.util.TreeMap<>();
                      for (java.lang.reflect.Field f : fields) {
                        if (org.h2.value.TypeInfo.class.isAssignableFrom(f.getType())
                                && java.lang.reflect.Modifier.isStatic(f.getModifiers())) {
                          org.h2.value.TypeInfo t = (org.h2.value.TypeInfo) f.get(null);
                          if (t.getValueType() != org.h2.value.Value.ROW) {
                            sorted.put(f.getName(), t);
                          }
                        }
                      }
                      for (java.util.Map.Entry<String, org.h2.value.TypeInfo> e : sorted.entrySet()) {
                        org.h2.value.TypeInfo t = e.getValue();
                        String result;
                        try {
                          result = t.getSQL(0);
                        } catch (RuntimeException ex) {
                          result = "THROWS";
                        }
                        // Keyed by the type and its parameters, not by the name: NUMERIC has
                        // three singletons and DECFLOAT two, so a value type alone cannot
                        // identify one. The key and the rendering go on separate lines so the
                        // key never has to be recovered from a string that contains spaces.
                        System.out.println(hex(t.getValueType() + " " + t.getPrecision()
                            + " " + t.getScale()));
                        System.out.println(hex(result));
                      }
            """;

        List<string> java = OracleRunner.Run([], body);
        Assert.Equal(0, java.Count % 2);

        Dictionary<string, string> fromJava = [];
        for (int i = 0; i < java.Count; i += 2)
        {
            fromJava[Decode(java[i])] = Decode(java[i + 1]);
        }

        Dictionary<string, string> fromCsharp = [];
        foreach (TypeInfo t in RenderableSingletons())
        {
            string key = $"{t.GetValueType()} {t.GetPrecision()} {t.GetScale()}";
            fromCsharp[key] = RenderSingleton(t);
        }

        Assert.Equal(fromJava.Count, fromCsharp.Count);
        foreach ((string key, string expected) in fromJava)
        {
            Assert.True(
                fromCsharp.TryGetValue(key, out string? actual),
                $"the reference has a singleton {key} and we do not");
            Assert.True(
                expected == actual,
                $"{key}:\n  java:   {expected}\n  csharp: {actual}");
        }
    }

    [JavaOracleFact]
    public void ParameterisedRendering_MatchesReference()
    {
        // A grid over the types that take a precision or a scale, with values on both sides
        // of their interesting boundaries: below zero, exactly zero, and large.
        //
        // nums carries the type numbers first and the precisions after them, with the split
        // stated here and in the snippet. Scales are a literal in both, since they are few
        // and fixed; packing them into the same file with an implicit split is how this test
        // first read half the grid as types.
        long[] precisions = [-1L, 0L, 1L, 10L, 53L, 100_000L];
        int[] scales = [-1, 0, 1, 6, 9];

        List<int> typeNumbers = [];
        foreach (TypeInfo singleton in Singletons())
        {
            typeNumbers.Add(singleton.GetValueType());
        }

        string body = """
                      int typeCount = 37;
                      int scaleCount = 5;
                      int precisionCount = nums.length - typeCount;
                      int[] scales = new int[] { -1, 0, 1, 6, 9 };
                      for (int i = 0; i < typeCount; i++) {
                        int type = nums[i];
                        if (type == org.h2.value.Value.ROW) {
                          continue;
                        }
                        for (int p = 0; p < precisionCount; p++) {
                          int precision = nums[typeCount + p];
                          for (int s = 0; s < scaleCount; s++) {
                            int scale = scales[s];
                            String result;
                            try {
                              result = org.h2.value.TypeInfo.getTypeInfo(
                                  type, precision, scale, null).getSQL(0);
                            } catch (RuntimeException e) {
                              result = "THROWS";
                            }
                            System.out.println(hex(result));
                          }
                        }
                      }
            """;

        // The snippet hardcodes the sizes rather than interpolating them, because an
        // interpolated raw string full of Java braces is its own readability problem. This
        // keeps the two honest with each other. The count is over every singleton, including
        // the four interval ones the snippet skips: nums packs the full list so the type
        // numbers and the precisions stay in the layout the snippet assumes.
        Assert.Equal(37, typeNumbers.Count);
        Assert.Equal(5, scales.Length);

        List<int> numbers = [.. typeNumbers];
        foreach (long precision in precisions)
        {
            numbers.Add((int)precision);
        }

        List<string> java = OracleRunner.Run([], body, numbers);

        List<string> csharp = [];
        foreach (int type in RenderableTypeNumbers())
        {
            foreach (long precision in precisions)
            {
                foreach (int scale in scales)
                {
                    csharp.Add(Render(type, precision, scale));
                }
            }
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"index {i}:\n  java:   {Decode(java[i])}\n  csharp: {Decode(csharp[i])}");
        }
    }

    [Fact]
    public void RenderingCoversTheShapesThatMatter()
    {
        // A few by hand, so the intent survives even if the oracle is weakened: precision in
        // parentheses, DECIMAL through the extended type, FLOAT for a sized approximate
        // type, and the " WITH TIME ZONE" suffix.
        Assert.Equal("INTEGER", TypeInfo.TypeInteger.GetSql(0));
        Assert.Equal("CHARACTER VARYING(10)", new TypeInfo(Value.Varchar, 10, -1, null).GetSql(0));
        Assert.Equal("NUMERIC(10, 2)", new TypeInfo(Value.Numeric, 10, 2, null).GetSql(0));
        Assert.Equal("DECIMAL(10, 2)", new TypeInfo(Value.Numeric, 10, 2, ExtTypeInfoNumeric.Decimal).GetSql(0));
        Assert.Equal("FLOAT(24)", new TypeInfo(Value.Real, 24, -1, null).GetSql(0));
        Assert.Equal("REAL", new TypeInfo(Value.Real, -1, -1, null).GetSql(0));

        // TIME and TIMESTAMP are built from TIME WITH TIME ZONE and TIMESTAMP WITH TIME
        // ZONE respectively, so the suffix is the difference between the two singletons.
        StringBuilder timeTz = new();
        _ = TypeInfo.TypeTimeTz.GetSql(timeTz, 0);
        Assert.Equal("TIME(9) WITH TIME ZONE", timeTz.ToString());

        StringBuilder timestampTz = new();
        _ = TypeInfo.TypeTimestampTz.GetSql(timestampTz, 0);
        Assert.Equal("TIMESTAMP(9) WITH TIME ZONE", timestampTz.ToString());
    }

    /// <summary>
    /// The singletons, in the order the reference reflection dump produces them.
    /// </summary>
    private static TypeInfo[] Singletons() =>
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

    /// <summary>
    /// The value types excluded from the rendering comparison, with the reason.
    /// </summary>
    /// <remarks>
    /// ROW renders through its <c>ExtTypeInfoRow</c>, which renders an empty field list as
    /// <c>()</c>, so the reference prints <c>ROW()</c> where a port without that class prints
    /// <c>ROW</c>. ExtTypeInfoRow needs an ordered field map and is not ported. It is
    /// excluded explicitly rather than quietly tolerated, and
    /// <c>RenderingExcludesExactlyTheUnrenderedTypes</c> keeps the exclusion from growing.
    /// The interval types used to be excluded here for the same reason and no longer are:
    /// they render through <c>IntervalQualifier</c>, which is ported.
    /// </remarks>
    private static bool IsExcluded(int valueType) => valueType is Value.Row;

    [Fact]
    public void RenderingExcludesExactlyTheUnrenderedTypes()
    {
        // The intervals are back in the comparison, so only ROW is left out: it has exactly
        // one singleton, the empty row.
        int excluded = Singletons().Count(static t => IsExcluded(t.GetValueType()));
        Assert.Equal(1, excluded);

        // And the exclusion is visible rather than silent: without the extended type the row
        // renders as a plain name, which is what the reference does not print.
        Assert.Equal("ROW", TypeInfo.TypeRowEmpty.GetSql(0));
        Assert.NotEqual("ROW()", TypeInfo.TypeRowEmpty.GetSql(0));

        // The qualifier reached the switch, so an interval now carries its precision.
        Assert.Equal("INTERVAL DAY(18)", TypeInfo.TypeIntervalDay.GetSql(0));
    }

    private static TypeInfo[] RenderableSingletons()
        => [.. Singletons().Where(static t => !IsExcluded(t.GetValueType()))];

    private static List<int> RenderableTypeNumbers()
    {
        List<int> typeNumbers = [];
        foreach (TypeInfo singleton in Singletons())
        {
            if (!IsExcluded(singleton.GetValueType()))
            {
                typeNumbers.Add(singleton.GetValueType());
            }
        }

        return typeNumbers;
    }

    [JavaOracleFact]
    public void IntervalQualifiers_MatchReference()
    {
        // The rendering of an interval goes through this enum, and the predicates below say
        // which fields a value carries. The rendering reached through TypeInfo only covers
        // the "INTERVAL " prefix form, so the predicates and the bare qualifier form are
        // compared here instead of being trusted.
        string body = """
                      int[][] pairs = new int[][] {
                          { -1, -1 }, { 0, -1 }, { 4, -1 }, { -1, 0 }, { -1, 3 }, { 9, 6 }
                      };
                      org.h2.api.IntervalQualifier[] all = org.h2.api.IntervalQualifier.values();
                      System.out.println(hex(String.valueOf(all.length)));
                      for (int i = 0; i < all.length; i++) {
                        org.h2.api.IntervalQualifier q = all[i];
                        String line = q.toString() + " "
                            + q.hasYears() + " " + q.hasMonths() + " " + q.hasDays()
                            + " " + q.hasHours() + " " + q.hasMinutes() + " " + q.hasSeconds()
                            + " " + q.hasMultipleFields() + " " + q.isYearMonth() + " "
                            + q.isDayTime();
                        for (int[] pair : pairs) {
                          line += " | " + q.getTypeName(pair[0], pair[1]) + " | "
                              + q.getTypeName(new StringBuilder(), pair[0], pair[1], true);
                        }
                        System.out.println(hex(line));
                      }
            """;

        List<string> java = OracleRunner.Run([], body).Select(Decode).ToList();

        // The first line is the reference's own member count, so a lost or invented
        // qualifier shows up as a count difference rather than as a shifted comparison.
        Assert.Equal("13", java[0]);
        java.RemoveAt(0);
        List<string> csharp = [];
        for (int ordinal = 0; ordinal < 13; ordinal++)
        {
            IntervalQualifier qualifier = IntervalQualifiers.ValueOf(ordinal);
            System.Text.StringBuilder line = new();
            // q.toString() on the reference side is the qualifier's name with underscores turned into
        // spaces, not the constant's own name, so that is what is compared. A .NET enum
        // member cannot print that without an explicit mapping, which is what Name() is.
        // Java concatenates a boolean as "true"/"false" and .NET as "True"/"False", so the
        // C# side is formatted rather than appended.
        _ = line.Append(IntervalQualifiers.Name(qualifier))
                .Append(' ').Append(J(IntervalQualifiers.HasYears(qualifier)))
                .Append(' ').Append(J(IntervalQualifiers.HasMonths(qualifier)))
                .Append(' ').Append(J(IntervalQualifiers.HasDays(qualifier)))
                .Append(' ').Append(J(IntervalQualifiers.HasHours(qualifier)))
                .Append(' ').Append(J(IntervalQualifiers.HasMinutes(qualifier)))
                .Append(' ').Append(J(IntervalQualifiers.HasSeconds(qualifier)))
                .Append(' ').Append(J(IntervalQualifiers.HasMultipleFields(qualifier)))
                .Append(' ').Append(J(IntervalQualifiers.IsYearMonth(qualifier)))
                .Append(' ').Append(J(IntervalQualifiers.IsDayTime(qualifier)));
            foreach ((int precision, int scale) in new[] { (-1, -1), (0, -1), (4, -1), (-1, 0), (-1, 3), (9, 6) })
            {
                _ = line.Append(" | ")
                    .Append(IntervalQualifiers.GetTypeName(qualifier, precision, scale))
                    .Append(" | ")
                    .Append(IntervalQualifiers.AppendTypeName(
                        qualifier, new System.Text.StringBuilder(), precision, scale, true));
            }

            csharp.Add(line.ToString());
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"ordinal {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    private static string J(bool value) => value ? "true" : "false";

    private static string Decode(string hex)
    {
        string only = hex.TrimStart('=');
        System.Text.StringBuilder sb = new();
        for (int i = 0; i + 3 < only.Length; i += 4)
        {
            sb.Append((char)Convert.ToInt32(only.Substring(i, 4), 16));
        }

        return sb.ToString();
    }

    private static string RenderSingleton(TypeInfo type)
    {
        try
        {
            return type.GetSql(0);
        }
        catch (NullReferenceException)
        {
            // The undefined ENUM type renders through a null extended type and throws here
            // as it does in the reference.
            return "THROWS";
        }
    }

    private static string Render(int type, long precision, int scale)
    {
        try
        {
            return OracleRunner.Hex(TypeInfo.GetTypeInfo(type, precision, scale, null).GetSql(0));
        }
        catch (Exception e) when (e is NotSupportedException
            or NullReferenceException
            or H2SharpException
            or ArgumentException)
        {
            // The snippet above catches RuntimeException on the Java side, so any refusal
            // to build the type has to land on the same "THROWS" line rather than end the
            // run. ArgumentException is the counterpart of the reference's
            // IllegalArgumentException, which is a RuntimeException too.
            return OracleRunner.Hex("THROWS");
        }
    }
}
