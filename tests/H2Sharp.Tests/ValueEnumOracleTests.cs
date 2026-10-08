/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for ENUM values.
 *
 * An enum is a number wearing a label, and the two are only as consistent as the list says.
 * The cases here are the ones where they can come apart: whether the ordinals start at 1 or at
 * 0 depends on the connection rather than on the list, a label is matched with surrounding
 * quotes and spaces ignored, two enums of different lists can only be compared by putting both
 * into one of them, and every arithmetic operation on an enum is the arithmetic of its ordinal.
 *
 * Compared: the string, the two lengths, the ordinal, the type, the SQL rendering with and
 * without casts, equality, comparison against the same and against another list, the hash code
 * (both sides build it from the label and the ordinal in the same order), and the arithmetic.
 * Not compared: the memory, which is a constant, and the trace SQL of a label, which goes
 * through the trace flags rather than through the value.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ValueEnumOracleTests
{
    /// <summary>
    /// A list of enumerators, and a second one to compare against or convert into.
    /// </summary>
    private sealed record Lists(string Left, string Right);

    private static readonly Lists[] Corpus =
    [
        // Two enums of the same list.
        new("'A','B','C'", "'A','B','C'"),
        new("'A','B','C'", "'B','A','C'"),
        new("'A','B','C'", "'A','B'"),
        new("'A','B','C'", "'A'"),

        // Two different lists: the ordinals have nothing to do with each other, so the
        // comparison can only work by putting both into one of them.
        new("'A','B','C'", "'X','Y','Z'"),
        new("'X','Y','Z'", "'A','B','C'"),
        new("'A','B','C'", "'A','B','D'"),
        new("'A','B','C'", "'C','B','A'"),

        // Labels that need cleaning before they can be looked up.
        new("'Alpha','Beta'", "' alpha '"),
        new("'Alpha','Beta'", "'ALPHA'"),
        new("'Alpha','Beta'", "'alpha'"),
        new("'a','b'", "'A','B'"),

        // One element, and a list whose only label is itself.
        new("'X'", "'X'"),
        new("'X'", "'Y'"),
    ];

    [JavaOracleFact]
    public void EnumsMatchTheReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                Declare + Body,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .ToList();

        List<string> csharp = Corpus.Select(c => Describe(c.Left, c.Right)).ToList();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [Fact]
    public void OrdinalsCountFromOneUnlessTheProviderSaysOtherwise()
    {
        ExtTypeInfoEnum abc = new(["A", "B", "C"]);

        // Without a provider the reference counts from 1, and the ordinals are the ones stored
        // rather than the positions in the list.
        Assert.Equal(1, abc.GetValue(1, null).GetInt());
        Assert.Equal(3, abc.GetValue(3, null).GetInt());
        Assert.Equal("B", abc.GetValue(2, null).GetString());

        // Out of range either way, and in both directions: 0 is too small when counting from 1.
        Assert.Equal(ErrorCode.EnumValueNotPermitted, Assert.Throws<H2SharpException>(
            () => abc.GetValue(0, null)).H2ErrorCode);
        Assert.Equal(ErrorCode.EnumValueNotPermitted, Assert.Throws<H2SharpException>(
            () => abc.GetValue(4, null)).H2ErrorCode);

        // A label is matched against the cleaned labels, so quotes and spaces and case do not
        // decide whether a label belongs to the list — and a label of another list does not
        // belong to this one, which is the error the reference raises.
        ExtTypeInfoEnum named = new(["Alpha", "Beta"]);
        Assert.Equal("Alpha", named.GetValue(" alpha ", null).GetString());
        Assert.Equal("Alpha", named.GetValue("ALPHA", null).GetString());
        Assert.Equal(2, named.GetValue("beta", null).GetInt());
        Assert.Equal(ErrorCode.EnumValueNotPermitted, Assert.Throws<H2SharpException>(
            () => abc.GetValue("Alpha", null)).H2ErrorCode);

        // The arithmetic is the arithmetic of the ordinal: A is 1, so A + 1 is 2, and the
        // result is an INTEGER rather than an enum.
        ValueEnum a = abc.GetValue(1, null);
        Value sum = a.Add(ValueInteger.Get(1));
        Assert.Equal(Value.Integer, sum.GetValueType());
        Assert.Equal(2, sum.GetInt());
    }

    private static string Describe(string left, string right)
    {
        try
        {
            ExtTypeInfoEnum l = new(Enums(left));
            ExtTypeInfoEnum r = new(Enums(right));
            ValueEnum lv = l.GetValue(1, null);
            ValueEnum rv = r.GetValue(1, null);
            CompareMode off = CompareMode.GetInstance(null, 0);
            StringBuilder line = new();
            _ = line.Append(lv.GetString()).Append('|')
                .Append(lv.GetValueType()).Append('|')
                .Append(lv.GetInt()).Append('|')
                .Append(lv.GetLong()).Append('|')
                .Append(lv.GetSignum()).Append('|')
                .Append(lv.GetTypeInfo().GetSql(new StringBuilder(), 0)).Append('|')
                .Append(Bool(lv.Equals(lv))).Append('|')
                .Append(Bool(lv.Equals(rv))).Append('|')
                .Append(Bool(rv.Equals(lv))).Append('|')
                .Append(lv.GetHashCode().ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(rv.GetHashCode().ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(lv.CompareTo(rv, null, off)).Append('|')
                .Append(rv.CompareTo(lv, null, off)).Append('|')
                .Append(lv.CompareTypeSafe(rv, off, null)).Append('|')
                .Append(Add(lv, 1)).Append('|')
                .Append(Sub(lv, 1)).Append('|')
                .Append(Mul(lv, 2)).Append('|')
                .Append(lv.GetSql(new StringBuilder(), 0)).Append('|')
                .Append(lv.GetSql(new StringBuilder(), HasSql.NoCasts));
            return line.ToString();
        }
        catch (H2SharpException e)
        {
            return "THROWS " + e.H2ErrorCode;
        }
    }

    /// <summary>
    /// The labels of a corpus line, where the line is a list of quoted labels separated by
    /// commas.
    /// </summary>
    /// <param name="list">the labels, each in single quotes</param>
    /// <returns>the labels without the quotes</returns>
    private static string[] Enums(string list)
        => [.. list.Split(',').Select(static p => p[1..^1])];

    /// <summary>
    /// The result of an arithmetic operation, as text.
    /// </summary>
    /// <param name="v">the enum value</param>
    /// <param name="n">the other operand</param>
    /// <returns>the value, as text</returns>
    private static string Add(ValueEnum v, int n) => Text(v.Add(ValueInteger.Get(n)));

    private static string Sub(ValueEnum v, int n) => Text(v.Subtract(ValueInteger.Get(n)));

    private static string Mul(ValueEnum v, int n) => Text(v.Multiply(ValueInteger.Get(n)));

    private static string Text(Value v) => v.GetString() + "/" + v.GetValueType();

    /// <summary>
    /// A boolean as the reference prints it: lower case, where .NET's own would say
    /// <c>True</c>.
    /// </summary>
    /// <param name="value">the value</param>
    /// <returns>"true" or "false"</returns>
    private static string Bool(bool value) => value ? "true" : "false";

    /// <summary>
    /// The corpus, in the same order as <see cref="Corpus"/>, without the quotes the C# side
    /// writes for its own readability. The two lists are kept adjacent for that reason: a case
    /// that moves in one and not in the other shows up as a mismatch on the line it moved to,
    /// and it is a nuisance to find by reading.
    /// </summary>
    private const string Declare = """
                        String[][] cases = new String[][] {
                            {"A,B,C", "A,B,C"}, {"A,B,C", "B,A,C"}, {"A,B,C", "A,B"}, {"A,B,C", "A"},
                            {"A,B,C", "X,Y,Z"}, {"X,Y,Z", "A,B,C"}, {"A,B,C", "A,B,D"},
                            {"A,B,C", "C,B,A"},
                            {"Alpha,Beta", " alpha "}, {"Alpha,Beta", "ALPHA"},
                            {"Alpha,Beta", "alpha"}, {"a,b", "A,B"},
                            {"X", "X"}, {"X", "Y"}
                        };
              """;

    private const string Body = """
                        java.util.function.Function<org.h2.value.Value, String> text =
                            v -> v.getString() + "/" + v.getValueType();
                        org.h2.value.CompareMode off = org.h2.value.CompareMode.getInstance(null, 0);
                        for (int i = 0; i < cases.length; i++) {
                          try {
                            org.h2.value.ExtTypeInfoEnum l =
                                new org.h2.value.ExtTypeInfoEnum(cases[i][0].split(",", -1));
                            org.h2.value.ExtTypeInfoEnum r =
                                new org.h2.value.ExtTypeInfoEnum(cases[i][1].split(",", -1));
                            org.h2.value.ValueEnum lv = l.getValue(1, null);
                            org.h2.value.ValueEnum rv = r.getValue(1, null);
                            StringBuilder b = new StringBuilder();
                            b.append(lv.getString()).append('|');
                            b.append(lv.getValueType()).append('|');
                            b.append(lv.getInt()).append('|');
                            b.append(lv.getLong()).append('|');
                            b.append(lv.getSignum()).append('|');
                            b.append(lv.getType().getSQL(0)).append('|');
                            b.append(lv.equals(lv)).append('|');
                            b.append(lv.equals(rv)).append('|');
                            b.append(rv.equals(lv)).append('|');
                            b.append(lv.hashCode()).append('|');
                            b.append(rv.hashCode()).append('|');
                            b.append(lv.compareTo(rv, null, off)).append('|');
                            b.append(rv.compareTo(lv, null, off)).append('|');
                            b.append(lv.compareTypeSafe(rv, off, null)).append('|');
                            b.append(text.apply(lv.add(org.h2.value.ValueInteger.get(1)))).append('|');
                            b.append(text.apply(lv.subtract(org.h2.value.ValueInteger.get(1)))).append('|');
                            b.append(text.apply(lv.multiply(org.h2.value.ValueInteger.get(2)))).append('|');
                            b.append(lv.getSQL(0)).append('|');
                            b.append(lv.getSQL(org.h2.util.HasSQL.NO_CASTS));
                            System.out.println(b.toString());
                          } catch (RuntimeException e) {
                            System.out.println("THROWS " + (e instanceof org.h2.message.DbException
                                ? ((org.h2.message.DbException)e).getErrorCode() : 0));
                          }
                        }

              """;
}
