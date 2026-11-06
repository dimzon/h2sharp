/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * MergedResult against the reference: columns matched by alias and name in first-appearance
 * order, rows from all appended results padded with nulls, the single-column fast path, and a
 * zero-column result ignored.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class MergedResultOracleTests
{
    [JavaOracleFact]
    public void MergedResultAgreesWithTheReference()
    {
        List<string> java = OracleRunner.Run([], Body(), null, null, null, null).ToList();
        List<string> csharp = [.. Lines()];
        Assert.True(
            java.Count == csharp.Count && java.SequenceEqual(csharp),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", csharp));
    }

    private static string Body() =>
        """
        {
        org.h2.result.SimpleResult r1 = new org.h2.result.SimpleResult();
        r1.addColumn("A", "A", org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.INTEGER));
        r1.addColumn("ALIAS_B", "B", org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.VARCHAR));
        r1.addRow(org.h2.value.ValueInteger.get(1), org.h2.value.ValueVarchar.get("x"));
        r1.addRow(org.h2.value.ValueInteger.get(2), null);

        org.h2.result.SimpleResult r2 = new org.h2.result.SimpleResult();
        r2.addColumn("ALIAS_B", "B", org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.VARCHAR));
        r2.addColumn("C", "C", org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.INTEGER));
        r2.addRow(org.h2.value.ValueVarchar.get("y"), org.h2.value.ValueInteger.get(3));

        org.h2.result.MergedResult m = new org.h2.result.MergedResult();
        m.add(r1);
        m.add(r2);
        org.h2.result.SimpleResult merged = m.getResult();
        System.out.println("cols|" + merged.getVisibleColumnCount());
        for (int i = 0; i < merged.getVisibleColumnCount(); i++) {
          System.out.println("col" + i + "|" + merged.getAlias(i) + "|" + merged.getColumnName(i) + "|"
              + merged.getColumnType(i).getValueType());
        }
        System.out.println("rows|" + merged.getRowCount());
        while (merged.next()) {
          org.h2.value.Value[] row = merged.currentRow();
          StringBuilder b = new StringBuilder("row|");
          for (int i = 0; i < row.length; i++) {
            if (i > 0) b.append(',');
            org.h2.value.Value v = row[i];
            b.append(v == null || v == org.h2.value.ValueNull.INSTANCE ? "-" : v.getString());
          }
          System.out.println(b);
        }

        org.h2.result.SimpleResult s1 = new org.h2.result.SimpleResult();
        s1.addColumn("A", "A", org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.INTEGER));
        s1.addRow(org.h2.value.ValueInteger.get(1));
        org.h2.result.SimpleResult s2 = new org.h2.result.SimpleResult();
        s2.addColumn("A", "A", org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.INTEGER));
        s2.addRow(org.h2.value.ValueInteger.get(2));
        org.h2.result.MergedResult m2 = new org.h2.result.MergedResult();
        m2.add(s1);
        m2.add(s2);
        org.h2.result.SimpleResult merged2 = m2.getResult();
        System.out.println("single|" + merged2.getVisibleColumnCount() + "|" + merged2.getRowCount());
        while (merged2.next()) {
          System.out.println("srow|" + merged2.currentRow()[0].getInt());
        }

        org.h2.result.MergedResult m3 = new org.h2.result.MergedResult();
        m3.add(new org.h2.result.SimpleResult());
        org.h2.result.SimpleResult merged3 = m3.getResult();
        System.out.println("empty|" + merged3.getVisibleColumnCount() + "|" + merged3.getRowCount());
        }
        """;

    private static IEnumerable<string> Lines()
    {
        SimpleResult r1 = new();
        r1.AddColumn("A", "A", TypeInfo.GetTypeInfo(Value.Integer));
        r1.AddColumn("ALIAS_B", "B", TypeInfo.GetTypeInfo(Value.Varchar));
        r1.AddRow(ValueInteger.Get(1), ValueVarchar.Get("x"));
        r1.AddRow(ValueInteger.Get(2), null);

        SimpleResult r2 = new();
        r2.AddColumn("ALIAS_B", "B", TypeInfo.GetTypeInfo(Value.Varchar));
        r2.AddColumn("C", "C", TypeInfo.GetTypeInfo(Value.Integer));
        r2.AddRow(ValueVarchar.Get("y"), ValueInteger.Get(3));

        MergedResult m = new();
        m.Add(r1);
        m.Add(r2);
        SimpleResult merged = m.GetResult();
        yield return $"cols|{merged.GetVisibleColumnCount()}";
        for (int i = 0; i < merged.GetVisibleColumnCount(); i++)
        {
            yield return "col" + i + "|" + merged.GetAlias(i) + "|" + merged.GetColumnName(i) + "|"
                + merged.GetColumnType(i).GetValueType();
        }

        yield return $"rows|{merged.GetRowCount()}";
        while (merged.Next())
        {
            Value?[] row = merged.CurrentRow();
            List<string> cells = [];
            for (int i = 0; i < row.Length; i++)
            {
                Value? v = row[i];
                cells.Add(v is null or ValueNull ? "-" : v.GetString()!);
            }

            yield return "row|" + string.Join(",", cells);
        }

        SimpleResult s1 = new();
        s1.AddColumn("A", "A", TypeInfo.GetTypeInfo(Value.Integer));
        s1.AddRow(ValueInteger.Get(1));
        SimpleResult s2 = new();
        s2.AddColumn("A", "A", TypeInfo.GetTypeInfo(Value.Integer));
        s2.AddRow(ValueInteger.Get(2));
        MergedResult m2 = new();
        m2.Add(s1);
        m2.Add(s2);
        SimpleResult merged2 = m2.GetResult();
        yield return $"single|{merged2.GetVisibleColumnCount()}|{merged2.GetRowCount()}";
        while (merged2.Next())
        {
            yield return "srow|" + merged2.CurrentRow()[0]!.GetInt();
        }

        MergedResult m3 = new();
        m3.Add(new SimpleResult());
        SimpleResult merged3 = m3.GetResult();
        yield return $"empty|{merged3.GetVisibleColumnCount()}|{merged3.GetRowCount()}";
    }
}
