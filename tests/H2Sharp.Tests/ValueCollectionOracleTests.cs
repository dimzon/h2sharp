/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for ARRAY and ROW.
 *
 * The corpus is pairs of values rather than single ones, because a collection is only ever
 * observed through a relationship: which of two is smaller, whether two of the same shape are
 * equal, what a type made of their elements looks like. A corpus of one value at a time would
 * test the printing and nothing else.
 *
 * The cases that matter are the asymmetric ones. A collection compares element by element, so
 * two of different lengths are ordered by length only after every element matched; an ARRAY
 * accepts a difference in length at all while a ROW refuses it; and an ARRAY casts its elements
 * to a component type in its constructor, which means an ARRAY of two values of different types
 * is not an array of either one.
 *
 * Not compared: the hash code, and the row's field names, both of which the reference derives
 * the same way here and neither of which anything observes.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]

public sealed class ValueCollectionOracleTests
{
    /// <summary>
    /// One row of the corpus: a left and a right, described the same way on both sides so that
    /// the Java side can build them and the C# side can build them from the same description.
    /// </summary>
    private sealed record Case(string Left, string Right);

    private static readonly Case[] Arrays =
    [
        new("1,2", "1,2"),
        new("1,2", "1,3"),
        new("1,2", "2,1"),
        new("1,2", "1,2,3"),
        new("1,2,3", "1,2"),
        new("1", "2"),
        new("1", "NULL"),
        new("NULL,1", "1,1"),
        new("NULL,NULL", "NULL,NULL"),
        new("1,NULL", "1,NULL"),
        new("NULL,1", "NULL,1"),
        new("", "1"),
        new("1", ""),
        new("", ""),
        new("'a','b'", "'a','b'"),
        new("'a','b'", "'a','c'"),
        new("'a'", "'a','b'"),
        new("'a','b','c'", "'a','c','b'"),
        new("1,2", "'2','b'"),
    ];

    private static readonly Case[] Rows =
    [
        new("1,2", "1,2"),
        new("1,2", "1,3"),
        new("1,2", "2,1"),
        new("1", "1"),
        new("1", "NULL"),
        new("NULL,1", "1,1"),
        new("NULL,NULL", "NULL,NULL"),
        new("1,NULL", "1,NULL"),
        new("'a','b'", "'a','b'"),
        new("'a','b'", "'a','c'"),
        new("1", "1,2"),
    ];

    [JavaOracleFact]
    public void ArraysMatchTheReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                DeclareArrays + Body,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .ToList();

        List<string> csharp = Arrays.Select(c => DescribeArray(c.Left, c.Right)).ToList();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [JavaOracleFact]
    public void RowsMatchTheReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                DeclareRows + Body,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .ToList();

        List<string> csharp = Rows.Select(c => DescribeRow(c.Left, c.Right)).ToList();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [Fact]
    public void AnArrayCastsItsElementsToOneType()
    {
        // This is the constructor's job in the reference, and it is observable: the two
        // elements come out the same type whatever they went in as, because a component type
        // was named.
        ValueArray array = ValueArray.Get(TypeInfo.TypeBigint, [ValueInteger.Get(1), ValueInteger.Get(2)], null);

        Assert.Equal(Value.Bigint, array.GetList()[0].GetValueType());
        Assert.Equal(Value.Bigint, array.GetList()[1].GetValueType());

        // Without a component type the elements are promoted to what holds them all — BIGINT here —
        // and then *every* element is cast to that type, including the one that already fit.
        // So the INTEGER comes out a BIGINT. That is the reference's constructor and not an
        // oversight in it: the array has one component type, and a value of a narrower type
        // stored in it is cast on the way in rather than kept as it was.
        ValueArray promoted = ValueArray.Get([ValueInteger.Get(1), ValueBigint.Get(2L)], null);
        Assert.Equal(Value.Bigint, promoted.GetComponentType().GetValueType());
        Assert.Equal(Value.Bigint, promoted.GetList()[0].GetValueType());
        Assert.Equal(Value.Bigint, promoted.GetList()[1].GetValueType());
    }

    [Fact]
    public void TwoRowsOfDifferentWidthsRefuseToBeCompared()
    {
        ValueRow narrow = ValueRow.Get([ValueInteger.Get(1)]);
        ValueRow wide = ValueRow.Get([ValueInteger.Get(1), ValueInteger.Get(2)]);

        // An ARRAY orders by length once the elements match; a ROW has a fixed number of
        // fields, so two rows of different widths are not rows of the same thing at all and
        // the comparison is refused rather than answered.
        H2SharpException error = Assert.Throws<H2SharpException>(
            () => narrow.CompareTo(wide, null, CompareMode.GetInstance(null, 0)));
        Assert.Equal(ErrorCode.ColumnCountDoesNotMatch, error.H2ErrorCode);

        // An ARRAY answers the same pair, and the answer is the length: every element that
        // both have is equal.
        ValueArray shortArray = ValueArray.Get([ValueInteger.Get(1)], null);
        ValueArray longArray = ValueArray.Get([ValueInteger.Get(1), ValueInteger.Get(2)], null);
        Assert.Equal(-1, shortArray.CompareTo(longArray, null, CompareMode.GetInstance(null, 0)));
    }

    [Fact]
    public void ARowCanBeReorderedWithItsFields()
    {
        ValueRow row = ValueRow.Get([ValueInteger.Get(1), ValueInteger.Get(2), ValueInteger.Get(3)]);
        ValueRow swapped = row.CloneWithOrder([2, 1, 0]);

        // The values move, and so do the field names, because a row's type is a list of named
        // types: renaming the values without renaming the types would describe a row that does
        // not exist.
        Assert.Equal([3, 2, 1], swapped.GetList().Select(static v => v.GetInt()));
        Assert.Equal("ROW (3, 2, 1)", swapped.GetString());

        // The field names come out quoted because no flags were given: the reference quotes an
        // identifier unless the caller asks it not to, and flags 0 do not ask it not to.
        Assert.Equal(
            "(\"C3\" INTEGER, \"C2\" INTEGER, \"C1\" INTEGER)",
            swapped.GetTypeInfo().GetExtTypeInfo()!.GetSql(new StringBuilder(), 0).ToString());
    }

    /// <summary>
    /// A boolean as the reference prints it: lower case, where .NET's own would say
    /// <c>True</c>. This is a printing difference and nothing more, and it is why every
    /// boolean in the oracle goes through here rather than being appended directly.
    /// </summary>
    /// <param name="value">the value</param>
    /// <returns>""true"" or ""false""</returns>
    private static string Bool(bool value) => value ? "true" : "false";

    private static string DescribeArray(string left, string right)
    {
        try
        {
            ValueArray l = ValueArray.Get(Elements(left), null);
            ValueArray r = ValueArray.Get(Elements(right), null);
            StringBuilder line = new();
            _ = line.Append(l.GetString()).Append('|')
                .Append(l.GetValueType()).Append('|')
                .Append(l.GetComponentType().GetValueType()).Append('|')
                .Append(l.GetTypeInfo().GetSql(new System.Text.StringBuilder(), 0)).Append('|')
                .Append(Bool(l.ContainsNull())).Append('|')
                .Append(Bool(l.Equals(l))).Append('|')
                .Append(Bool(l.Equals(r))).Append('|')
                .Append(Bool(r.Equals(l))).Append('|')
                .Append(l.CompareTo(r, null, CompareMode.GetInstance(null, 0))).Append('|')
                .Append(r.CompareTo(l, null, CompareMode.GetInstance(null, 0))).Append('|')
                .Append(l.CompareWithNull(r, true, null, CompareMode.GetInstance(null, 0))).Append('|')
                .Append(l.CompareWithNull(r, false, null, CompareMode.GetInstance(null, 0))).Append('|')
                .Append(l.GetMemory().ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(l.GetSql(new System.Text.StringBuilder(), 0));
            return line.ToString();
        }
        catch (H2SharpException e)
        {
            // A pair the reference refuses has to be described the same way on both sides, or
            // the oracle cannot compare the refusals. Only the error code is compared: the
            // messages agree ("Column count does not match"), but the JVM's getMessage() appends
            // its own "[code-state]" to the text, and that is a formatting habit of Java's
            // exception, not a difference in what the engine refused.
            return "THROWS " + e.H2ErrorCode;
        }
    }

    private static string DescribeRow(string left, string right)
    {
        try
        {
            ValueRow l = ValueRow.Get(Elements(left));
            ValueRow r = ValueRow.Get(Elements(right));
            StringBuilder line = new();
            _ = line.Append(l.GetString()).Append('|')
                .Append(l.GetValueType()).Append('|')
                .Append(l.GetTypeInfo().GetSql(new System.Text.StringBuilder(), 0)).Append('|')
                .Append(Bool(l.ContainsNull())).Append('|')
                .Append(Bool(l.Equals(l))).Append('|')
                .Append(Bool(l.Equals(r))).Append('|')
                .Append(Bool(r.Equals(l))).Append('|')
                .Append(l.CompareTo(r, null, CompareMode.GetInstance(null, 0))).Append('|')
                .Append(r.CompareTo(l, null, CompareMode.GetInstance(null, 0))).Append('|')
                .Append(l.CompareWithNull(r, true, null, CompareMode.GetInstance(null, 0))).Append('|')
                .Append(l.CompareWithNull(r, false, null, CompareMode.GetInstance(null, 0))).Append('|')
                .Append(l.GetMemory().ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(l.GetSql(new System.Text.StringBuilder(), 0));
            return line.ToString();
        }
        catch (H2SharpException e)
        {
            return "THROWS " + e.H2ErrorCode;
        }
    }

    /// <summary>
    /// Builds the values a corpus line names, where the only types in play are INTEGER, VARCHAR
    /// and NULL. Three is enough: they differ in type, one of them has no value, and the
    /// reference's promotion between them is a known one.
    /// </summary>
    /// <param name="list">a comma separated list, empty for no values at all</param>
    /// <returns>the values</returns>
    private static Value[] Elements(string list)
    {
        if (list.Length == 0)
        {
            return [];
        }

        return
        [
            .. list.Split(',').Select(static part => part switch
            {
                "NULL" => ValueNull.Instance,
                _ when part.StartsWith('\'') => ValueVarchar.Get(part[1..^1]),
                _ => ValueInteger.Get(int.Parse(part, CultureInfo.InvariantCulture)),
            }),
        ];
    }

    private const string Body = """
                        java.util.function.Function<String, org.h2.value.Value[]> array = spec -> {
                          if (spec.isEmpty()) {
                            return new org.h2.value.Value[0];
                          }
                          String[] parts = spec.split(",", -1);
                          org.h2.value.Value[] a = new org.h2.value.Value[parts.length];
                          for (int i = 0; i < parts.length; i++) {
                            String p = parts[i];
                            if (p.equals("NULL")) {
                              a[i] = org.h2.value.ValueNull.INSTANCE;
                            } else if (p.startsWith("'")) {
                              a[i] = org.h2.value.ValueVarchar.get(p.substring(1, p.length() - 1));
                            } else {
                              a[i] = org.h2.value.ValueInteger.get(Integer.parseInt(p));
                            }
                          }
                          return a;
                        };
                        org.h2.value.CompareMode off = org.h2.value.CompareMode.getInstance(null, 0);
                        for (int i = 0; i < cases.length; i++) {
                          String[] c = cases[i].split("\\|", -1);
                          try {
                          StringBuilder b = new StringBuilder();
                          if (kind.equals("array")) {
                            org.h2.value.ValueArray l = org.h2.value.ValueArray.get(array.apply(c[0]), null);
                            org.h2.value.ValueArray r = org.h2.value.ValueArray.get(array.apply(c[1]), null);
                            b.append(l.getString()).append('|');
                            b.append(l.getValueType()).append('|');
                            b.append(l.getComponentType().getValueType()).append('|');
                            b.append(l.getType().getSQL(0)).append('|');
                            b.append(l.containsNull()).append('|');
                            b.append(l.equals(l)).append('|');
                            b.append(l.equals(r)).append('|');
                            b.append(r.equals(l)).append('|');
                            b.append(l.compareTo(r, null, off)).append('|');
                            b.append(r.compareTo(l, null, off)).append('|');
                            b.append(l.compareWithNull(r, true, null, off)).append('|');
                            b.append(l.compareWithNull(r, false, null, off)).append('|');
                            b.append(l.getMemory()).append('|');
                            b.append(l.getSQL(0));
                          } else {
                            org.h2.value.ValueRow l = org.h2.value.ValueRow.get(array.apply(c[0]));
                            org.h2.value.ValueRow r = org.h2.value.ValueRow.get(array.apply(c[1]));
                            b.append(l.getString()).append('|');
                            b.append(l.getValueType()).append('|');
                            b.append(l.getType().getSQL(0)).append('|');
                            b.append(l.containsNull()).append('|');
                            b.append(l.equals(l)).append('|');
                            b.append(l.equals(r)).append('|');
                            b.append(r.equals(l)).append('|');
                            b.append(l.compareTo(r, null, off)).append('|');
                            b.append(r.compareTo(l, null, off)).append('|');
                            b.append(l.compareWithNull(r, true, null, off)).append('|');
                            b.append(l.compareWithNull(r, false, null, off)).append('|');
                            b.append(l.getMemory()).append('|');
                            b.append(l.getSQL(0));
                          }
                          System.out.println(b.toString());
                          } catch (RuntimeException e) {
                            System.out.println("THROWS "
                                + (e instanceof org.h2.message.DbException
                                    ? ((org.h2.message.DbException)e).getErrorCode() : 0));
                          }
                        }
              """;

    private const string DeclareArrays = """
                        String kind = "array";
                        String[] cases = new String[] {
                            "1,2|1,2", "1,2|1,3", "1,2|2,1", "1,2|1,2,3", "1,2,3|1,2",
                            "1|2", "1|NULL", "NULL,1|1,1", "NULL,NULL|NULL,NULL", "1,NULL|1,NULL",
                            "NULL,1|NULL,1", "|1", "1|", "|",
                            "'a','b'|'a','b'", "'a','b'|'a','c'", "'a'|'a','b'", "'a','b','c'|'a','c','b'",
                            "1,2|'2','b'"
                        };
              """;

    private const string DeclareRows = """
                        String kind = "row";
                        String[] cases = new String[] {
                            "1,2|1,2", "1,2|1,3", "1,2|2,1", "1|1", "1|NULL",
                            "NULL,1|1,1", "NULL,NULL|NULL,NULL", "1,NULL|1,NULL",
                            "'a','b'|'a','b'", "'a','b'|'a','c'", "1|1,2"
                        };
              """;

}
