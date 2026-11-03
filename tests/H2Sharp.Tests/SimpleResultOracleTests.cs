/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * SimpleResult against the reference.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// The simple result against the reference.
/// </summary>
/// <remarks>
/// <see cref="SimpleResult"/> is both a result and a target: the same object is walked with
/// <c>next()</c> and filled with <c>addRow()</c>. The observable surface is small, and every part
/// of it is an honest field read — which is exactly why it is worth a differential test: a port
/// that "improved" one of the corner answers (a fresh result that reports itself after the last
/// row, a copy that deep-copies, a sort that does not stick) would pass every green test that
/// never walks a SimpleResult and still be wrong for <c>MergedResult</c>, which feeds
/// INFORMATION_SCHEMA through this class.
/// <para>
/// The sharpest edge is the shallow copy: the reference shares the column and row lists with the
/// copy, so a row added to the copy is visible in the original. A deep copy would look more
/// correct and would not match.
/// </para>
/// </remarks>
[Collection(OracleCollection.Name)]
public sealed class SimpleResultOracleTests
{
    [JavaOracleFact]
    public void SimpleResultAgreesWithTheReference()
    {
        List<string> java = OracleRunner.Run([], Body(), null, null, null, Members()).ToList();
        List<string> csharp = [.. Lines()];
        Compare(java, csharp);
    }

    private static string Body() =>
        """
        {
        org.h2.result.SimpleResult r = new org.h2.result.SimpleResult("PUBLIC", "T");
        r.addColumn("A", "A", org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.INTEGER));
        r.addColumn("ALIAS_B", "B", org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.NUMERIC, 10L, 2, null));
        r.addColumn("ALIAS_C", "C", org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.VARCHAR));

        System.out.println("cols|" + r.getVisibleColumnCount());
        System.out.println("rows|" + r.getRowCount());
        System.out.println("schema|" + r.getSchemaName(0));
        System.out.println("table|" + r.getTableName(0));
        for (int i = 0; i < 3; i++) {
          System.out.println("col" + i + "|" + r.getAlias(i) + "|" + r.getColumnName(i) + "|"
              + r.getColumnType(i).getValueType() + "|" + r.getColumnType(i).getPrecision() + "|"
              + r.getColumnType(i).getScale());
        }
        System.out.println("nullable|" + r.getNullable(0));
        System.out.println("identity|" + r.isIdentity(0));
        System.out.println("lazy|" + r.isLazy());
        System.out.println("closed|" + r.isClosed());
        System.out.println("needclose|" + r.needToClose());
        System.out.println("fetch|" + r.getFetchSize());
        r.setFetchSize(123);
        System.out.println("fetch|" + r.getFetchSize());

        r.addRow(org.h2.value.ValueInteger.get(3), org.h2.value.ValueNull.INSTANCE, null);
        r.addRow(org.h2.value.ValueInteger.get(1), null, org.h2.value.ValueVarchar.get("x"));
        r.addRow(org.h2.value.ValueInteger.get(2), null, null);
        System.out.println("rows|" + r.getRowCount());

        System.out.println("rowid|" + r.getRowId());
        System.out.println("afterlast|" + r.isAfterLast());
        System.out.println("next|" + r.next());
        System.out.println("rowid|" + r.getRowId());
        System.out.println("cur|" + row(r.currentRow()));
        System.out.println("hasnext|" + r.hasNext());
        System.out.println("afterlast|" + r.isAfterLast());
        System.out.println("next|" + r.next());
        System.out.println("cur|" + row(r.currentRow()));
        System.out.println("next|" + r.next());
        System.out.println("cur|" + row(r.currentRow()));
        System.out.println("hasnext|" + r.hasNext());
        System.out.println("afterlast|" + r.isAfterLast());
        System.out.println("next|" + r.next());
        System.out.println("rowid|" + r.getRowId());
        r.reset();
        System.out.println("rowid|" + r.getRowId());
        System.out.println("afterlast|" + r.isAfterLast());

        r.sortRows((a, b) -> Integer.compare(a[0] == null ? -1 : a[0].getInt(),
            b[0] == null ? -1 : b[0].getInt()));
        for (int i = 0; i < 3; i++) {
          r.next();
          System.out.println("sorted" + i + "|" + row(r.currentRow()));
        }

        r.close();
        System.out.println("closed|" + r.isClosed());

        org.h2.result.SimpleResult copy = r.createShallowCopy(null);
        System.out.println("copy|" + copy.getVisibleColumnCount() + "|" + copy.getRowCount() + "|"
            + copy.getSchemaName(0) + "|" + copy.getTableName(0));
        System.out.println("copyrowid|" + copy.getRowId());
        copy.addRow(org.h2.value.ValueInteger.get(9), null, null);
        System.out.println("shared|" + r.getRowCount() + "|" + copy.getRowCount());

        org.h2.result.SimpleResult m = new org.h2.result.SimpleResult("S", "M");
        m.addColumn("A2", "A2", org.h2.value.Value.INTEGER, -1L, 0);
        m.addColumn("C2", org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.VARCHAR));
        System.out.println("m|" + m.getVisibleColumnCount() + "|"
            + m.getColumnType(0).getValueType() + "|" + m.getColumnType(1).getValueType());
        m.addRow(org.h2.value.ValueInteger.get(7), null);
        System.out.println("mrows|" + m.getRowCount());
        System.out.println("mnext1|" + m.next());
        System.out.println("mnext2|" + m.next());
        System.out.println("mnext3|" + m.next());
        System.out.println("mrowid|" + m.getRowId());

        org.h2.result.SimpleResult empty = new org.h2.result.SimpleResult();
        System.out.println("empty|" + empty.getRowCount() + "|" + empty.getVisibleColumnCount() + "|"
            + empty.getSchemaName(0) + "|" + empty.getTableName(0));
        System.out.println("emptyrowid|" + empty.getRowId());
        System.out.println("emptynext|" + empty.next());
        System.out.println("emptyafterlast|" + empty.isAfterLast());
        System.out.println("emptyhasnext|" + empty.hasNext());
        empty.addRow();
        System.out.println("emptynext|" + empty.next());
        // No currentRow here: the reference's own quirk is that a failed next() on an empty
        // result still advances the row id to 0, so the row added after it sits behind the
        // cursor and currentRow() throws — on the JVM exactly as it would here.
        }
        """;
    private static string Members() =>
        """
        static String row(org.h2.value.Value[] r) {
          StringBuilder b = new StringBuilder();
          for (org.h2.value.Value v : r) {
            b.append(b.length() == 0 ? "" : ", ");
            b.append(v == null ? "null" : hex(v.getTraceSQL()));
          }
          return b.toString();
        }
        """;

    private static IEnumerable<string> Lines()
    {
        SimpleResult r = new("PUBLIC", "T");
        r.AddColumn("A", "A", TypeInfo.GetTypeInfo(Value.Integer));
        r.AddColumn("ALIAS_B", "B", TypeInfo.GetTypeInfo(Value.Numeric, 10L, 2, null));
        r.AddColumn("ALIAS_C", "C", TypeInfo.GetTypeInfo(Value.Varchar));

        yield return $"cols|{r.GetVisibleColumnCount()}";
        yield return $"rows|{r.GetRowCount()}";
        yield return $"schema|{r.GetSchemaName(0)}";
        yield return $"table|{r.GetTableName(0)}";
        for (int i = 0; i < 3; i++)
        {
            TypeInfo t = r.GetColumnType(i);
            yield return "col" + i + "|" + r.GetAlias(i) + "|" + r.GetColumnName(i) + "|"
                + t.GetValueType() + "|" + t.GetPrecision() + "|" + t.GetScale();
        }

        yield return $"nullable|{r.GetNullable(0)}";
        yield return $"identity|{J(r.IsIdentity(0))}";
        yield return $"lazy|{J(r.IsLazy())}";
        yield return $"closed|{J(r.IsClosed())}";
        yield return $"needclose|{J(r.NeedToClose())}";
        yield return $"fetch|{r.GetFetchSize()}";
        r.SetFetchSize(123);
        yield return $"fetch|{r.GetFetchSize()}";

        r.AddRow(ValueInteger.Get(3), ValueNull.Instance, null);
        r.AddRow(ValueInteger.Get(1), null, ValueVarchar.Get("x"));
        r.AddRow(ValueInteger.Get(2), null, null);
        yield return $"rows|{r.GetRowCount()}";

        yield return $"rowid|{r.GetRowId()}";
        yield return $"afterlast|{J(r.IsAfterLast())}";
        yield return $"next|{J(r.Next())}";
        yield return $"rowid|{r.GetRowId()}";
        yield return $"cur|{Row(r.CurrentRow())}";
        yield return $"hasnext|{J(r.HasNext())}";
        yield return $"afterlast|{J(r.IsAfterLast())}";
        yield return $"next|{J(r.Next())}";
        yield return $"cur|{Row(r.CurrentRow())}";
        yield return $"next|{J(r.Next())}";
        yield return $"cur|{Row(r.CurrentRow())}";
        yield return $"hasnext|{J(r.HasNext())}";
        yield return $"afterlast|{J(r.IsAfterLast())}";
        yield return $"next|{J(r.Next())}";
        yield return $"rowid|{r.GetRowId()}";
        r.Reset();
        yield return $"rowid|{r.GetRowId()}";
        yield return $"afterlast|{J(r.IsAfterLast())}";

        r.SortRows((a, b) => (a[0] is null ? -1 : a[0]!.GetInt())
            .CompareTo(b[0] is null ? -1 : b[0]!.GetInt()));
        for (int i = 0; i < 3; i++)
        {
            _ = r.Next();
            yield return "sorted" + i + "|" + Row(r.CurrentRow());
        }

        r.Close();
        yield return $"closed|{J(r.IsClosed())}";

        SimpleResult copy = r.CreateShallowCopy(null!);
        yield return "copy|" + copy.GetVisibleColumnCount() + "|" + copy.GetRowCount() + "|"
            + copy.GetSchemaName(0) + "|" + copy.GetTableName(0);
        yield return $"copyrowid|{copy.GetRowId()}";
        copy.AddRow(ValueInteger.Get(9), null, null);
        yield return "shared|" + r.GetRowCount() + "|" + copy.GetRowCount();

        SimpleResult m = new("S", "M");
        m.AddColumn("A2", "A2", Value.Integer, -1L, 0);
        m.AddColumn("C2", TypeInfo.GetTypeInfo(Value.Varchar));
        yield return "m|" + m.GetVisibleColumnCount() + "|"
            + m.GetColumnType(0).GetValueType() + "|" + m.GetColumnType(1).GetValueType();
        m.AddRow(ValueInteger.Get(7), null);
        yield return $"mrows|{m.GetRowCount()}";
        yield return $"mnext1|{J(m.Next())}";
        yield return $"mnext2|{J(m.Next())}";
        yield return $"mnext3|{J(m.Next())}";
        yield return $"mrowid|{m.GetRowId()}";

        SimpleResult empty = new();
        yield return "empty|" + empty.GetRowCount() + "|" + empty.GetVisibleColumnCount() + "|"
            + empty.GetSchemaName(0) + "|" + empty.GetTableName(0);
        yield return $"emptyrowid|{empty.GetRowId()}";
        yield return $"emptynext|{J(empty.Next())}";
        yield return $"emptyafterlast|{J(empty.IsAfterLast())}";
        yield return $"emptyhasnext|{J(empty.HasNext())}";
        empty.AddRow();
        yield return $"emptynext|{J(empty.Next())}";
        // No CurrentRow: a failed Next() on the empty result already moved the row id to 0,
        // so the row added after it is behind the cursor and CurrentRow() throws — the
        // reference behaves the same way.

        // The .NET-side surface the reference expresses differently: the copy through the
        // interface it also implements, and the empty close and limits callbacks. None of
        // them print; they are here so the interface adapter and the no-ops are walked.
        ResultInterface viaInterface = r;
        _ = viaInterface.CreateShallowCopy(null!);
        r.LimitsWereApplied();
        r.Dispose();
    }

    /// <summary>
    /// A row as the reference prints it: values separated, each traced, a missing entry as null.
    /// </summary>
    private static string Row(Value?[] row)
    {
        List<string> parts = [];
        foreach (Value? v in row)
        {
            parts.Add(v is null ? "null" : OracleRunner.Hex(v.GetTraceSql()));
        }

        return string.Join(", ", parts);
    }

    private static string J(bool value) => value ? "true" : "false";

    private static void Compare(List<string> java, List<string> csharp)
    {
        int common = Math.Min(java.Count, csharp.Count);
        for (int i = 0; i < common; i++)
        {
            Assert.True(java[i] == csharp[i],
                $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }

        Assert.True(java.Count == csharp.Count,
            "строк: java=" + java.Count.ToString(CultureInfo.InvariantCulture)
            + " csharp=" + csharp.Count.ToString(CultureInfo.InvariantCulture)
            + "\n  последнее совпадение: " + (common - 1).ToString(CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// The pieces of <see cref="SimpleResult"/> and its column that the reference keeps package
/// private: the column's identity, its text, and the constructor refusals. The semantics are the
/// reference's own; the JVM side cannot be driven there through the public API, so these are
/// written directly against the reference source.
/// </summary>
public sealed class SimpleResultDirectTests
{
    [Fact]
    public void ColumnRefusesNulls()
    {
        TypeInfo type = TypeInfo.GetTypeInfo(Value.Integer);
        Assert.Throws<ArgumentNullException>(() => new SimpleResult.Column(null!, "N", type));
        Assert.Throws<ArgumentNullException>(() => new SimpleResult.Column("A", null!, type));
        Assert.Throws<ArgumentNullException>(() => new SimpleResult.Column("A", "N", null!));
    }

    [Fact]
    public void ColumnEqualityUsesAliasAndNameOnly()
    {
        SimpleResult.Column a = new("A", "N", TypeInfo.GetTypeInfo(Value.Integer));
        SimpleResult.Column same = new("A", "N", TypeInfo.GetTypeInfo(Value.Varchar));
        SimpleResult.Column otherAlias = new("B", "N", TypeInfo.GetTypeInfo(Value.Integer));
        SimpleResult.Column otherName = new("A", "M", TypeInfo.GetTypeInfo(Value.Integer));

        Assert.True(a.Equals(a));
        Assert.True(a.Equals(same));
        Assert.False(a.Equals(otherAlias));
        Assert.False(a.Equals(otherName));
        Assert.False(a.Equals("A"));
        Assert.False(a.Equals(null));
        Assert.True(a.GetHashCode() == same.GetHashCode());
        Assert.NotEqual(0, a.GetHashCode());
    }

    [Fact]
    public void ColumnToStringIsNameOrNameWithAlias()
    {
        Assert.Equal("N", new SimpleResult.Column("N", "N", TypeInfo.GetTypeInfo(Value.Integer)).ToString());
        Assert.Equal("N A", new SimpleResult.Column("A", "N", TypeInfo.GetTypeInfo(Value.Integer)).ToString());
    }
}
