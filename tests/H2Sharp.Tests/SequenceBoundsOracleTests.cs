/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The bounds of a type, checked against the reference.
 *
 * These are the numbers a sequence may not leave, and they are the first thing a port of this
 * layer gets subtly wrong: every one of them looks like the limit of its type, and only two of
 * them are. REAL and DOUBLE are bounded by what can be told apart, not by what can be written,
 * and a port that used the real limits would let a sequence promise numbers nobody can store.
 *
 * The NUMERIC cases are where a test earns its keep. Precision 18 gives 999_999_999_999_999_999
 * and precision 19 gives the whole range of a long rather than 10^19 — the switch happens at
 * eighteen digits, not nineteen, and the difference is one digit that the reference treats as
 * "no bound at all". DECFLOAT does not do that: the switch is above eighteen, so precision 19
 * is a real number there and the same 19 is unbounded in NUMERIC.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Command.Ddl;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class SequenceBoundsOracleTests
{
    [JavaOracleFact]
    public void BoundsOfEveryType_MatchReference()
    {
        string body = """
                      long[] b = org.h2.command.ddl.SequenceOptions.getBounds(
                          org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.TINYINT));
                      System.out.println(b[0] + " " + b[1]);
                      b = org.h2.command.ddl.SequenceOptions.getBounds(
                          org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.SMALLINT));
                      System.out.println(b[0] + " " + b[1]);
                      b = org.h2.command.ddl.SequenceOptions.getBounds(
                          org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.INTEGER));
                      System.out.println(b[0] + " " + b[1]);
                      b = org.h2.command.ddl.SequenceOptions.getBounds(
                          org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.BIGINT));
                      System.out.println(b[0] + " " + b[1]);
                      b = org.h2.command.ddl.SequenceOptions.getBounds(
                          org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.REAL));
                      System.out.println(b[0] + " " + b[1]);
                      b = org.h2.command.ddl.SequenceOptions.getBounds(
                          org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.DOUBLE));
                      System.out.println(b[0] + " " + b[1]);
                      for (int p = 1; p <= 20; p++) {
                          b = org.h2.command.ddl.SequenceOptions.getBounds(
                              org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.NUMERIC, p, 0, null));
                          System.out.println("n" + p + " " + b[0] + " " + b[1]);
                      }
                      for (int p = 1; p <= 20; p++) {
                          b = org.h2.command.ddl.SequenceOptions.getBounds(
                              org.h2.value.TypeInfo.getTypeInfo(org.h2.value.Value.DECFLOAT, p, 0, null));
                          System.out.println("d" + p + " " + b[0] + " " + b[1]);
                      }
            """;

        List<string> java = OracleRunner.Run([], body);
        List<string> csharp = Describe();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [Fact]
    public void NumericWithAScaleIsRefusedRatherThanClamped()
    {
        // A scale is a fraction: a sequence cannot hand out 1.5, and pretending otherwise would
        // give a column values its own type cannot hold. The reference refuses, and the refusal
        // is what a caller has to be told.
        Assert.ThrowsAny<Exception>(() => SequenceOptions.GetBounds(
            TypeInfo.GetTypeInfo(Value.Numeric, 10, 2, null)));
    }

    [Fact]
    public void ATypeWithNoNumbersIsRefused()
    {
        // VARCHAR has no bounds to give, so there is no default minimum or maximum to compute.
        Assert.ThrowsAny<Exception>(() => SequenceOptions.GetBounds(TypeInfo.TypeVarchar));
    }

    private static List<string> Describe()
    {
        List<string> lines = [];
        foreach (int type in new[] { Value.Tinyint, Value.Smallint, Value.Integer, Value.Bigint, Value.Real, Value.Double })
        {
            long[] b = SequenceOptions.GetBounds(TypeInfo.GetTypeInfo(type));
            lines.Add(b[0] + " " + b[1]);
        }

        for (long p = 1; p <= 20; p++)
        {
            long[] b = SequenceOptions.GetBounds(TypeInfo.GetTypeInfo(Value.Numeric, p, 0, null));
            lines.Add("n" + p + " " + b[0] + " " + b[1]);
        }

        for (long p = 1; p <= 20; p++)
        {
            long[] b = SequenceOptions.GetBounds(TypeInfo.GetTypeInfo(Value.Decfloat, p, 0, null));
            lines.Add("d" + p + " " + b[0] + " " + b[1]);
        }

        return lines;
    }
}
