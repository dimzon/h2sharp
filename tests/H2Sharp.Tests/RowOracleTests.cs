using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// Rows against the reference: what a row reports about itself, and how a row is keyed, copied and
/// compared.
/// </summary>
/// <remarks>
/// A row is a value that knows its own columns. That is worth pinning from both ends, because the
/// wrong answer here is not a wrong query result but a wrong row identity: two rows holding equal
/// values are equal for a comparison and are not the same row, and only one of those two answers is
/// allowed to be the interesting one.
/// <para>
/// The memory figure is compared as a number, not as a guess. It is the one place where a row's own
/// bookkeeping, the constants it sums up and the sizes each value reports all have to agree.
/// </para>
/// <para>
/// The members the reference refuses are compared too. A row that quietly implemented
/// <c>equals</c> would be usable in a hash table, and a hash table is somewhere a row must not go.
/// </para>
/// </remarks>
[Collection(OracleCollection.Name)]
public sealed class RowOracleTests
{
    [Fact]
    public void RowsAgreeWithTheReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                Program(),
                numbers: null,
                extraJavaArgs: ["-Duser.language=en"])
            .ToList();

        List<string> csharp = [];
        csharp.AddRange(
        [
            Describe(Row.Get([ValueInteger.Get(1), ValueVarchar.Get("x"), ValueNull.Instance], SearchRow.MemoryCalculate)),
            Describe(Row.Get([ValueInteger.Get(1), ValueVarchar.Get("x"), ValueNull.Instance], 64)),
            Describe(Row.Get([ValueInteger.Get(1), ValueVarchar.Get("x"), ValueNull.Instance], SearchRow.MemoryCalculate, 42)),
            Describe(Row.Get([], 32)),
            Describe(Row.Get([null!], SearchRow.MemoryCalculate)),
            Describe(Row.Get([ValueVarchar.Get("привет")], SearchRow.MemoryCalculate)),
        ]);

        csharp.Add(Identity());
        csharp.Add(KeysAndCopying());

        SimpleRowValue simple0 = new(3);
        SimpleRowValue simple1 = new(3, 1);
        simple1.SetValue(1, ValueVarchar.Get("only"));
        foreach (SearchRow r in new SearchRow[] { simple0, simple1 })
        {
            csharp.Add(Simple(r));
        }

        SearchRow refused = Row.Get([ValueInteger.Get(1)], SearchRow.MemoryCalculate);
        csharp.Add(Attempt("hashCode", () => refused.GetHashCode().ToString(CultureInfo.InvariantCulture)));
        csharp.Add(Attempt("equals", () => (refused.Equals(null) ? "true" : "false")));
        csharp.Add(Attempt("compareTypeSafe", () =>
            refused.CompareTypeSafe(refused, CompareMode.GetInstance(null, 0), null).ToString(CultureInfo.InvariantCulture)));

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"case {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    private static string Program() => ProgramBody();

    private static string ProgramBody()
    {
        // The reference's helpers, spelled here rather than in the shared preamble: they exist to
        // describe a row, and nothing outside this test describes one.
        return """
        {
        class R {
        static String show(org.h2.value.Value v) {
          return v == null ? "null" : v.getTraceSQL();
        }

        static String describe(org.h2.result.Row r) {
          try {
          return body(r);
          } catch (RuntimeException e) {
          return "THROWS";
          }
        }

        static String body(org.h2.result.Row r) {
          StringBuilder b = new StringBuilder();
          b.append(r.getColumnCount()).append('|');
          b.append(r.getMemory()).append('|');
          b.append(r.getValueType()).append('|');
          b.append(r.getKey()).append('|');
          b.append(r.getType() == org.h2.value.TypeInfo.TYPE_ROW_EMPTY).append('|');
          for (int i = 0; i < r.getColumnCount(); i++) {
            b.append(show(r.getValue(i))).append('|');
            b.append(r.isNull(i)).append('|');
          }
          b.append(r.getSQL(new StringBuilder(), 0)).append('|');
          b.append(r.getString()).append('|');
          b.append(r.toString()).append('|');
          b.append(show(r.getValue(org.h2.result.SearchRow.ROWID_INDEX)));
          return b.toString();
        }

        static String simple(org.h2.result.SearchRow r) {
          try {
          return simpleBody(r);
          } catch (RuntimeException e) {
          return "THROWS";
          }
        }

        static String simpleBody(org.h2.result.SearchRow r) {
          StringBuilder b = new StringBuilder();
          b.append(r.getColumnCount()).append('|');
          b.append(show(r.getValue(0))).append('|').append(show(r.getValue(1))).append('|');
          b.append(show(r.getValue(2))).append('|');
          b.append(r.isNull(0)).append('|').append(r.isNull(1)).append('|').append(r.isNull(2)).append('|');
          b.append(r.getMemory()).append('|');
          b.append(r.getSQL(new StringBuilder(), 0)).append('|');
          b.append(r.toString());
          return b.toString();
        }

        static String attempt(String what, java.util.function.Supplier<String> body) {
          try {
            return what + ":" + body.get();
          } catch (RuntimeException e) {
            return what + ":THROWS";
          }
        }
        }

        org.h2.result.Row[] rows = {
          org.h2.result.Row.get(new org.h2.value.Value[] {
              org.h2.value.ValueInteger.get(1), org.h2.value.ValueVarchar.get("x"),
              org.h2.value.ValueNull.INSTANCE }, org.h2.result.SearchRow.MEMORY_CALCULATE),
          org.h2.result.Row.get(new org.h2.value.Value[] {
              org.h2.value.ValueInteger.get(1), org.h2.value.ValueVarchar.get("x"),
              org.h2.value.ValueNull.INSTANCE }, 64),
          org.h2.result.Row.get(new org.h2.value.Value[] {
              org.h2.value.ValueInteger.get(1), org.h2.value.ValueVarchar.get("x"),
              org.h2.value.ValueNull.INSTANCE }, org.h2.result.SearchRow.MEMORY_CALCULATE, 42),
          org.h2.result.Row.get(new org.h2.value.Value[0], 32),
          org.h2.result.Row.get(new org.h2.value.Value[] { null }, org.h2.result.SearchRow.MEMORY_CALCULATE),
          org.h2.result.Row.get(new org.h2.value.Value[] {
              org.h2.value.ValueVarchar.get("привет") }, org.h2.result.SearchRow.MEMORY_CALCULATE),
        };
        for (int i = 0; i < rows.length; i++) {
          System.out.println(R.describe(rows[i]));
        }

        org.h2.value.Value[] a = {
            org.h2.value.ValueInteger.get(1), org.h2.value.ValueVarchar.get("x") };
        org.h2.value.Value[] b = {
            org.h2.value.ValueInteger.get(1), org.h2.value.ValueVarchar.get("x") };
        org.h2.result.Row r1 = org.h2.result.Row.get(a, org.h2.result.SearchRow.MEMORY_CALCULATE);
        org.h2.result.Row r2 = org.h2.result.Row.get(a, org.h2.result.SearchRow.MEMORY_CALCULATE);
        org.h2.result.Row r3 = org.h2.result.Row.get(b, org.h2.result.SearchRow.MEMORY_CALCULATE);
        System.out.println(r1.hasSameValues(r2) + "|" + r1.hasSameValues(r3) + "|"
            + r1.hasSharedData(r2) + "|" + r1.hasSharedData(r3) + "|" + r2.hasSharedData(r3));

        org.h2.result.Row kr = org.h2.result.Row.get(new org.h2.value.Value[] {
            org.h2.value.ValueInteger.get(1), org.h2.value.ValueVarchar.get("x") },
            org.h2.result.SearchRow.MEMORY_CALCULATE);
        StringBuilder kb = new StringBuilder();
        kb.append(kr.getValue(org.h2.result.SearchRow.ROWID_INDEX).getString()).append('|');
        kr.setKey(77);
        kb.append(kr.getKey()).append('|');
        kb.append(kr.getValue(org.h2.result.SearchRow.ROWID_INDEX).getString()).append('|');
        kr.setValue(org.h2.result.SearchRow.ROWID_INDEX, org.h2.value.ValueBigint.get(88));
        kb.append(kr.getKey()).append('|');
        kb.append(kr.isNull(0)).append('|').append(kr.isNull(1)).append('|');
        org.h2.result.Row target = org.h2.result.Row.get(
            new org.h2.value.Value[2], org.h2.result.SearchRow.MEMORY_CALCULATE);
        target.copyFrom(kr);
        kb.append(R.describe(target));
        System.out.println(kb.toString());

        org.h2.result.SimpleRowValue[] simples = {
          new org.h2.result.SimpleRowValue(3),
          new org.h2.result.SimpleRowValue(3, 1),
        };
        simples[1].setValue(1, org.h2.value.ValueVarchar.get("only"));
        for (int i = 0; i < simples.length; i++) {
          System.out.println(R.simple(simples[i]));
        }

        org.h2.result.Row refused = org.h2.result.Row.get(
            new org.h2.value.Value[] { org.h2.value.ValueInteger.get(1) },
            org.h2.result.SearchRow.MEMORY_CALCULATE);
        System.out.println(R.attempt("hashCode", () -> "" + refused.hashCode()));
        System.out.println(R.attempt("equals", () -> "" + refused.equals(null)));
        System.out.println(R.attempt("compareTypeSafe", () ->
            "" + refused.compareTypeSafe(refused, org.h2.value.CompareMode.getInstance(null, 0), null)));
        }
        """;
    }

    private static string Describe(Row r)
    {
        try
        {
            return DescribeBody(r);
        }
        catch (Exception e) when (e is NullReferenceException)
        {
            // A row holding a null slot throws when it is written out, on both sides. The type of the
            // throw differs between the two languages and is not what is being compared here.
            return "THROWS";
        }
    }

    private static string DescribeBody(Row r)
    {
        StringBuilder b = new();
        _ = b.Append(r.GetColumnCount().ToString(CultureInfo.InvariantCulture)).Append('|');
        _ = b.Append(r.GetMemory().ToString(CultureInfo.InvariantCulture)).Append('|');
        _ = b.Append(r.GetValueType().ToString(CultureInfo.InvariantCulture)).Append('|');
        _ = b.Append(r.GetKey().ToString(CultureInfo.InvariantCulture)).Append('|');
        _ = b.Append(J(r.GetTypeInfo() == TypeInfo.TypeRowEmpty)).Append('|');
        for (int i = 0; i < r.GetColumnCount(); i++)
        {
            _ = b.Append(Show(r.GetValue(i))).Append('|');
            _ = b.Append(J(r.IsNull(i))).Append('|');
        }

        _ = b.Append(r.GetSql(new StringBuilder(), 0)).Append('|');
        _ = b.Append(r.GetString()).Append('|');
        _ = b.Append(r.ToString()).Append('|');
        _ = b.Append(Show(r.GetValue(SearchRow.RowidIndex)));
        return b.ToString();
    }

    private static string Simple(SearchRow r)
    {
        try
        {
            return SimpleBody(r);
        }
        catch (Exception e) when (e is NullReferenceException)
        {
            return "THROWS";
        }
    }

    private static string SimpleBody(SearchRow r)
    {
        StringBuilder b = new();
        _ = b.Append(r.GetColumnCount().ToString(CultureInfo.InvariantCulture)).Append('|');
        _ = b.Append(Show(r.GetValue(0))).Append('|').Append(Show(r.GetValue(1))).Append('|');
        _ = b.Append(Show(r.GetValue(2))).Append('|');
        _ = b.Append(J(r.IsNull(0))).Append('|').Append(J(r.IsNull(1))).Append('|').Append(J(r.IsNull(2))).Append('|');
        _ = b.Append(r.GetMemory().ToString(CultureInfo.InvariantCulture)).Append('|');
        _ = b.Append(r.GetSql(new StringBuilder(), 0)).Append('|');
        _ = b.Append(r.ToString());
        return b.ToString();
    }

    private static string Identity()
    {
        Value[] a = [ValueInteger.Get(1), ValueVarchar.Get("x")];
        Value[] b = [ValueInteger.Get(1), ValueVarchar.Get("x")];
        Row r1 = Row.Get(a, SearchRow.MemoryCalculate);
        Row r2 = Row.Get(a, SearchRow.MemoryCalculate);
        Row r3 = Row.Get(b, SearchRow.MemoryCalculate);
        return J(r1.HasSameValues(r2)) + "|" + J(r1.HasSameValues(r3)) + "|"
            + J(r1.HasSharedData(r2)) + "|" + J(r1.HasSharedData(r3)) + "|" + J(r2.HasSharedData(r3));
    }

    private static string KeysAndCopying()
    {
        Row r = Row.Get([ValueInteger.Get(1), ValueVarchar.Get("x")], SearchRow.MemoryCalculate);
        StringBuilder b = new();
        _ = b.Append(r.GetValue(SearchRow.RowidIndex)!.GetString()).Append('|');
        r.SetKey(77);
        _ = b.Append(r.GetKey().ToString(CultureInfo.InvariantCulture)).Append('|');
        _ = b.Append(r.GetValue(SearchRow.RowidIndex)!.GetString()).Append('|');
        r.SetValue(SearchRow.RowidIndex, ValueBigint.Get(88));
        _ = b.Append(r.GetKey().ToString(CultureInfo.InvariantCulture)).Append('|');
        _ = b.Append(J(r.IsNull(0))).Append('|').Append(J(r.IsNull(1))).Append('|');
        Row target = Row.Get(new Value[2], SearchRow.MemoryCalculate);
        target.CopyFrom(r);
        _ = b.Append(Describe(target));
        return b.ToString();
    }

    private static string Show(Value? v) => v is null ? "null" : v.GetTraceSql();

    private static string J(bool value) => value ? "true" : "false";

    private static string Attempt(string what, Func<string> body)
    {
        try
        {
            return what + ":" + body();
        }
        catch (Exception e) when (e is NotSupportedException)
        {
            // The reference throws UnsupportedOperationException and this throws
            // NotSupportedException. The names differ and the refusal does not, and the refusal is
            // what is being compared.
            return what + ":THROWS";
        }
    }
}
