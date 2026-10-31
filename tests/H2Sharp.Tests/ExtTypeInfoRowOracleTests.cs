/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for a row's field list.
 *
 * A row is the only type whose parameters are other types, so it is where rendering,
 * equality and the hash all have to agree about order and about nesting. The hash matters
 * here for the same reason it matters for a type: it is folded into the hash of any type
 * that contains the row, and it has to be the same number in both languages.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]

public sealed class ExtTypeInfoRowOracleTests
{
    /// <summary>
    /// The field sets, each a sequence of (name, type) pairs.
    /// </summary>
    /// <remarks>
    /// Written out on both sides because the reference takes a LinkedHashMap and the port an
    /// ordered map, and passing either through the oracle harness is more trouble than
    /// spelling the cases out. The cases cover an empty row, the generated names the reference
    /// gives a positional constructor, names that need quoting, an enum and a nested row as
    /// field types, and equality against rows that differ only in order, name or type.
    /// </remarks>
    private static readonly (string Name, TypeInfo Type)[][] Cases =
    [
        [],
        [("a", TypeInfo.TypeInteger)],
        [("a", TypeInfo.TypeInteger), ("b", TypeInfo.TypeVarchar)],
        [("b", TypeInfo.TypeVarchar), ("a", TypeInfo.TypeInteger)],
        [("A", TypeInfo.TypeInteger)],
        [("select", TypeInfo.TypeInteger)],
        [("Mixed Case", TypeInfo.TypeInteger), ("x", TypeInfo.TypeDouble)],
        [("e", TypeInfo.GetTypeInfo(Value.Enum, -1, -1, new ExtTypeInfoEnum(["P", "Q"])))],
        [("nested", TypeInfo.TypeRowEmpty)],
        [("n", TypeInfo.TypeInteger), ("nested", TypeInfo.TypeRowEmpty), ("e", TypeInfo.TypeInteger)],
    ];

    [JavaOracleFact]
    public void RowFieldLists_MatchReference()
    {
        string body = """
                      org.h2.value.TypeInfo enumType = org.h2.value.TypeInfo.getTypeInfo(
                          org.h2.value.Value.ENUM, -1L, -1,
                          new org.h2.value.ExtTypeInfoEnum(new String[] { "P", "Q" }));
                      org.h2.value.TypeInfo[] types = new org.h2.value.TypeInfo[] {
                          org.h2.value.TypeInfo.TYPE_INTEGER,
                          org.h2.value.TypeInfo.TYPE_VARCHAR,
                          org.h2.value.TypeInfo.TYPE_DOUBLE,
                          enumType,
                          org.h2.value.TypeInfo.TYPE_ROW_EMPTY
                      };
                      String[][] cases = new String[][] {
                          new String[] {},
                          new String[] { "a:0" },
                          new String[] { "a:0", "b:1" },
                          new String[] { "b:1", "a:0" },
                          new String[] { "A:0" },
                          new String[] { "select:0" },
                          new String[] { "Mixed Case:0", "x:2" },
                          new String[] { "e:3" },
                          new String[] { "nested:4" },
                          new String[] { "n:0", "nested:4", "e:0" }
                      };
                      for (int i = 0; i < cases.length; i++) {
                        java.util.LinkedHashMap<String, org.h2.value.TypeInfo> map =
                            new java.util.LinkedHashMap<>();
                        for (String field : cases[i]) {
                          String[] parts = field.split(":", 2);
                          map.put(parts[0], types[Integer.parseInt(parts[1])]);
                        }
                        org.h2.value.ExtTypeInfoRow row = new org.h2.value.ExtTypeInfoRow(map);
                        org.h2.value.TypeInfo t = org.h2.value.TypeInfo.getTypeInfo(
                            org.h2.value.Value.ROW, -1L, -1, row);
                        StringBuilder line = new StringBuilder();
                        // Once without quoting and once with it, since the field names go
                        // through quoteIdentifier and that is where the flags matter.
                        line.append(t.getSQL(0)).append(' ').append(row.getTraceSQL());
                        for (java.util.Map.Entry<String, org.h2.value.TypeInfo> e
                                : row.getFields()) {
                          line.append(' ').append(e.getKey()).append('=')
                              .append(e.getValue().getSQL(0)).append('/')
                              .append(e.getValue().getPrecision()).append(',')
                              .append(e.getValue().getScale());
                        }
                        java.util.LinkedHashMap<String, org.h2.value.TypeInfo> reversed =
                            new java.util.LinkedHashMap<>();
                        java.util.List<String> keys = new java.util.ArrayList<>(map.keySet());
                        java.util.Collections.reverse(keys);
                        for (String key : keys) {
                          reversed.put(key, map.get(key));
                        }
                        java.util.LinkedHashMap<String, org.h2.value.TypeInfo> renamed =
                            new java.util.LinkedHashMap<>();
                        renamed.put("zzz", org.h2.value.TypeInfo.TYPE_INTEGER);
                        line.append(' ').append(row.equals(
                                new org.h2.value.ExtTypeInfoRow(reversed)))
                            .append(' ').append(row.equals(
                                new org.h2.value.ExtTypeInfoRow(renamed)))
                            .append(' ').append(row.hashCode()).append(' ').append(row);
                        System.out.println(hex(line.toString()));
                      }
            """;

        List<string> java = OracleRunner.Run(
                [],
                body,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .Select(Decode)
            .ToList();

        List<string> csharp = [];
        foreach ((string Name, TypeInfo Type)[] fields in Cases)
        {
            csharp.Add(Render(fields));
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(
                java[i] == csharp[i],
                $"case {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// The positional constructor names the fields C1..Cn, which is the only way the
    /// reference lets a caller skip the names.
    /// </summary>
    [JavaOracleFact]
    public void PositionalFields_AreNamedInOrder_MatchReference()
    {
        string body = """
                      org.h2.value.TypeInfo[] types = new org.h2.value.TypeInfo[] {
                          org.h2.value.TypeInfo.TYPE_INTEGER,
                          org.h2.value.TypeInfo.TYPE_VARCHAR,
                          org.h2.value.TypeInfo.TYPE_DOUBLE
                      };
                      for (int degree = 0; degree <= types.length; degree++) {
                        org.h2.value.ExtTypeInfoRow row =
                            new org.h2.value.ExtTypeInfoRow(types, degree);
                        StringBuilder line = new StringBuilder(row.getSQL(0));
                        for (java.util.Map.Entry<String, org.h2.value.TypeInfo> e
                                : row.getFields()) {
                          line.append(' ').append(e.getKey()).append('=')
                              .append(e.getValue().getSQL(0));
                        }
                        System.out.println(hex(line.toString()));
                      }
            """;

        List<string> java = OracleRunner.Run([], body).Select(Decode).ToList();

        TypeInfo[] types =
            [TypeInfo.TypeInteger, TypeInfo.TypeVarchar, TypeInfo.TypeDouble];
        List<string> csharp = [];
        for (int degree = 0; degree <= types.Length; degree++)
        {
            csharp.Add(Render(types, degree));
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(
                java[i] == csharp[i],
                $"degree {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// A row may not have more fields than a table may have columns.
    /// </summary>
    [JavaOracleFact]
    public void TooManyColumns_IsRejected_MatchReference()
    {
        string body = """
                      int max = org.h2.engine.Constants.MAX_COLUMNS;
                      int[] degrees = new int[] { max, max + 1 };
                      for (int i = 0; i < degrees.length; i++) {
                        String line;
                        try {
                          java.util.LinkedHashMap<String, org.h2.value.TypeInfo> map =
                              new java.util.LinkedHashMap<>();
                          for (int j = 0; j < degrees[i]; j++) {
                            map.put("C" + (j + 1), org.h2.value.TypeInfo.TYPE_INTEGER);
                          }
                          line = "OK " + new org.h2.value.ExtTypeInfoRow(map).getFields().size();
                        } catch (org.h2.message.DbException ex) {
                          line = "EX " + ex.getErrorCode() + " " + ex.getMessage();
                        }
                        System.out.println(hex(line));
                      }
            """;

        List<string> java = OracleRunner.Run(
                [],
                body,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .Select(Decode)
            .Select(StripTrace)
            .ToList();

        int max = Constants.MaxColumns;
        List<string> csharp = [];
        foreach (int degree in new[] { max, max + 1 })
        {
            csharp.Add(RenderMany(degree));
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(
                java[i] == csharp[i],
                $"degree {(i == 0 ? max : max + 1)}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// Past the first changed element a row converts against that element's type, not against
    /// each field's own: the iterator has moved on and the reference does not move it back.
    /// </summary>
    [JavaOracleFact]
    public void RowConversionUsesTheChangedFieldsTypeForTheRest_MatchReference()
    {
        const string body = """
            org.h2.value.ValueRow row = org.h2.value.ValueRow.get(new org.h2.value.Value[]{
                org.h2.value.ValueInteger.get(5), org.h2.value.ValueVarchar.get("x") });
            java.util.LinkedHashMap<String, org.h2.value.TypeInfo> m = new java.util.LinkedHashMap<>();
            m.put("C1", org.h2.value.TypeInfo.TYPE_BIGINT);
            m.put("C2", org.h2.value.TypeInfo.TYPE_VARCHAR);
            org.h2.value.TypeInfo target = org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.ROW,
                0, 0, new org.h2.value.ExtTypeInfoRow(m));
            try {
                org.h2.value.Value out = row.convertTo(target, null, null);
                System.out.println("=ok:" + out.getString());
            } catch (org.h2.message.DbException e) {
                System.out.println("=throws:" + e.getErrorCode());
            }
            """;

        string csharp;
        try
        {
            ValueRow row = ValueRow.Get([ValueInteger.Get(5), ValueVarchar.Get("x")]);
            OrderedStringMap<TypeInfo> map = new(2);
            map.Put("C1", TypeInfo.TypeBigint);
            map.Put("C2", TypeInfo.TypeVarchar);
            TypeInfo target = TypeInfo.GetTypeInfo(Value.Row, 0, 0, new ExtTypeInfoRow(map));
            csharp = "=ok:" + row.ConvertToType(target).GetString();
        }
        catch (H2SharpException e)
        {
            csharp = "=throws:" + e.H2ErrorCode.ToString(CultureInfo.InvariantCulture);
        }

        List<string> java = OracleRunner.Run([], body, null, null, null);
        Assert.Equal(java, [csharp]);
    }

    private static string Render((string Name, TypeInfo Type)[] fields)
    {
        OrderedStringMap<TypeInfo> map = new(fields.Length);
        foreach ((string name, TypeInfo type) in fields)
        {
            map.Put(name, type);
        }

        ExtTypeInfoRow row = new(map);
        TypeInfo t = TypeInfo.GetTypeInfo(Value.Row, -1L, -1, row);

        // Swapped keeps the same pairs in the opposite order, so equality must say no.
        OrderedStringMap<TypeInfo> swapped = new(fields.Length);
        for (int i = fields.Length - 1; i >= 0; i--)
        {
            swapped.Put(fields[i].Name, fields[i].Type);
        }

        StringBuilder line = new();
        _ = line.Append(t.GetSql(0)).Append(' ').Append(row.GetTraceSql());
        foreach (KeyValuePair<string, TypeInfo> e in row.GetFields())
        {
            _ = line.Append(' ').Append(e.Key).Append('=')
                .Append(e.Value.GetSql(0)).Append('/')
                .Append(e.Value.GetPrecision().ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(e.Value.GetScale().ToString(CultureInfo.InvariantCulture));
        }

        _ = line.Append(' ').Append(J(row.Equals(new ExtTypeInfoRow(swapped))))
            .Append(' ').Append(J(row.Equals(Named("zzz", TypeInfo.TypeInteger))))
            .Append(' ').Append(row.GetHashCode().ToString(CultureInfo.InvariantCulture))
            .Append(' ').Append(row.ToString());
        return line.ToString();
    }

    private static string Render(TypeInfo[] types, int degree)
    {
        ExtTypeInfoRow row = new(types, degree);
        StringBuilder line = new(row.GetSql(0));
        foreach (KeyValuePair<string, TypeInfo> e in row.GetFields())
        {
            _ = line.Append(' ').Append(e.Key).Append('=').Append(e.Value.GetSql(0));
        }

        return line.ToString();
    }

    private static string RenderMany(int degree)
    {
        try
        {
            OrderedStringMap<TypeInfo> map = new(degree);
            for (int j = 0; j < degree; j++)
            {
                map.Put(
                    "C" + (j + 1).ToString(CultureInfo.InvariantCulture),
                    TypeInfo.TypeInteger);
            }

            return "OK " + new ExtTypeInfoRow(map).GetFields().Count()
                .ToString(CultureInfo.InvariantCulture);
        }
        catch (H2SharpException ex)
        {
            return $"EX {ex.H2ErrorCode} {TraceSuffix.Replace(ex.Message, string.Empty)}";
        }
    }

    private static ExtTypeInfoRow Named(string name, TypeInfo type)
    {
        OrderedStringMap<TypeInfo> map = new(1);
        map.Put(name, type);
        return new ExtTypeInfoRow(map);
    }

    private static string J(bool value) => value ? "true" : "false";

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
