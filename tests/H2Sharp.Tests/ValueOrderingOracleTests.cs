/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Exhaustive differential coverage for the value type ordering table.
 *
 * Every pair of the 42 type numbers goes through getHigherOrder, and both the resulting
 * type and the failure mode are compared. 1764 combinations is exactly the kind of surface
 * that hand written tests cover thinly and that hand porting gets wrong in a corner nobody
 * thought about: the interval combinations alone are two thirds of the table, and
 * INTERVAL_HOUR_TO_MINUTE with INTERVAL_DAY_TO_SECOND is a plausible thing to transpose.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]

public sealed class ValueOrderingOracleTests
{
    /// <summary>
    /// All type numbers from UNKNOWN to TYPE_COUNT - 1, which is the range the reference
    /// walks: TYPE_COUNT itself is a bound, not a type, and asking for its name indexes
    /// past the end of the table.
    /// </summary>
    private static int[] AllTypes()
        => [.. Enumerable.Range(Value.Unknown, Value.TypeCount - Value.Unknown)];

    [Fact]
    public void GroupTableCoversEveryTypeNumber()
    {
        // The group table is indexed by the type number, so a short or long table is an
        // out of range read or a silently ignored type. An off-by-one here once shifted
        // every group after the numeric block, which the exhaustive comparison caught; this
        // catches it first and without a JVM.
        Assert.Equal(Value.TypeCount, Value.GroupCount);

        // Spot check the boundaries of each run, since a wrong entry there misclassifies a
        // whole block rather than one type.
        Assert.Equal(Value.GroupNull, Value.GroupOf(Value.Null));
        Assert.Equal(Value.GroupCharacterString, Value.GroupOf(Value.VarcharIgnorecase));
        Assert.Equal(Value.GroupBinaryString, Value.GroupOf(Value.Blob));
        Assert.Equal(Value.GroupBoolean, Value.GroupOf(Value.Boolean));
        Assert.Equal(Value.GroupNumeric, Value.GroupOf(Value.Tinyint));
        Assert.Equal(Value.GroupNumeric, Value.GroupOf(Value.Decfloat));
        Assert.Equal(Value.GroupDatetime, Value.GroupOf(Value.Date));
        Assert.Equal(Value.GroupDatetime, Value.GroupOf(Value.TimestampTz));
        Assert.Equal(Value.GroupIntervalYm, Value.GroupOf(Value.IntervalYearToMonth));
        Assert.Equal(Value.GroupIntervalDt, Value.GroupOf(Value.IntervalMinuteToSecond));
        Assert.Equal(Value.GroupOther, Value.GroupOf(Value.Uuid));
        Assert.Equal(Value.GroupCollection, Value.GroupOf(Value.Array));
        Assert.Equal(Value.GroupCollection, Value.GroupOf(Value.Row));
    }

    [JavaOracleFact]
    public void GetHigherOrder_MatchesReferenceForEveryPair()
    {
        int[] types = AllTypes();

        string body = """
                      int[] all = new int[org.h2.value.Value.TYPE_COUNT + 1];
                      for (int i = 0; i < all.length; i++) {
                        all[i] = i - 1;
                      }
                      for (int t1 : all) {
                        for (int t2 : all) {
                          String result;
                          try {
                            result = "OK:" + org.h2.value.Value.getHigherOrder(t1, t2);
                          } catch (org.h2.message.DbException e) {
                            // The code, not the message: the message carries the localised
                            // catalogue text plus a [code-build] suffix, which depends on the
                            // locale of the process rather than on this table. The message
                            // shape is covered by ErrorMessagesTests instead.
                            result = "ERR:" + e.getErrorCode();
                          } catch (RuntimeException e) {
                            result = "UNEXPECTED:" + e.getClass().getName();
                          }
                          System.out.println(hex(result));
                        }
                      }
            """;

        List<string> java = OracleRunner.Run([], body);

        List<string> csharp = [];
        foreach (int t1 in types)
        {
            foreach (int t2 in types)
            {
                csharp.Add(Report(t1, t2));
            }
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(
                java[i] == csharp[i],
                $"getHigherOrder({types[i / types.Length]}, "
                + $"{types[i % types.Length]}):\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [JavaOracleFact]
    public void GetTypeName_MatchesReferenceForEveryType()
    {
        int[] types = AllTypes();

        string body = """
                      for (int i = -1; i < org.h2.value.Value.TYPE_COUNT; i++) {
                        System.out.println("=" + org.h2.value.Value.getTypeName(i));
                      }
            """;

        List<string> java = OracleRunner.Run([], body);
        List<string> csharp = [.. types.Select(static t => "=" + Value.GetTypeName(t))];

        Assert.Equal(java, csharp);
    }

    [Fact]
    public void UnknownCombinesWithNothing()
    {
        // The table never sees UNKNOWN or NULL: getHigherOrder handles them before
        // delegating, and it throws for the pair that carries no type at all.
        Assert.ThrowsAny<Exception>(() => Value.GetHigherOrder(Value.Unknown, Value.Unknown));
        Assert.ThrowsAny<Exception>(() => Value.GetHigherOrder(Value.Unknown, Value.Null));
        Assert.ThrowsAny<Exception>(() => Value.GetHigherOrder(Value.Null, Value.Unknown));

        // NULL is the lowest type number, so it loses to anything real rather than winning,
        // and UNKNOWN passes a real type straight through. That is the opposite of the
        // intuition that "NULL absorbs", and it is why the exhaustive comparison matters.
        Assert.Equal(Value.Integer, Value.GetHigherOrder(Value.Null, Value.Integer));
        Assert.Equal(Value.Integer, Value.GetHigherOrder(Value.Integer, Value.Null));
        Assert.Equal(Value.Integer, Value.GetHigherOrder(Value.Unknown, Value.Integer));
        Assert.Equal(Value.Integer, Value.GetHigherOrder(Value.Integer, Value.Unknown));
    }

    [Fact]
    public void RealAndDoubleAreWidenedToDecfloat()
    {
        // A handful of the promotions worth naming, so a table rewrite cannot quietly change
        // arithmetic semantics even if the exhaustive test were ever weakened.
        Assert.Equal(Value.Double, Value.GetHigherOrder(Value.Real, Value.Integer));
        Assert.Equal(Value.Decfloat, Value.GetHigherOrder(Value.Real, Value.Bigint));
        Assert.Equal(Value.Decfloat, Value.GetHigherOrder(Value.Real, Value.Numeric));
        Assert.Equal(Value.Decfloat, Value.GetHigherOrder(Value.Double, Value.Bigint));
        Assert.Equal(Value.Decfloat, Value.GetHigherOrder(Value.Double, Value.Numeric));

        // Same-type pairs are the identity, including the ones that need no promotion.
        Assert.Equal(Value.Integer, Value.GetHigherOrder(Value.Integer, Value.Integer));
        Assert.Equal(Value.Timestamp, Value.GetHigherOrder(Value.Timestamp, Value.Timestamp));
    }

    private static string Report(int t1, int t2)
    {
        try
        {
            return OracleRunner.Hex(
            "OK:" + Value.GetHigherOrder(t1, t2).ToString(CultureInfo.InvariantCulture));
        }
        catch (H2SharpException e)
        {
            return OracleRunner.Hex("ERR:" + e.H2ErrorCode.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception e)
        {
            // Anything else is a defect on our side: the reference has no path that throws
            // something other than a DbException here.
            return OracleRunner.Hex("UNEXPECTED:" + e.GetType().Name);
        }
    }
}
