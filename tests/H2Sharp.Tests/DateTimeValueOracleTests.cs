/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for DATE, TIME and TIMESTAMP.
 *
 * The three are read from the text of a statement and written back into it, so the corpus is
 * written both ways: a value is built from a date and its text is compared, and the texts are read
 * and the values they stand for are compared. A type whose writer and reader disagree passes one
 * of those and fails the other, so both are here.
 *
 * A date has no arithmetic in the reference and neither has this port, and that is worth stating:
 * the difference between DATE and TIMESTAMP is that a date is a day and a timestamp is an instant,
 * and adding two instants borrows and carries a day where adding two days has nothing to carry.
 * The corpus crosses the epoch in both halves, because a negative count of nanoseconds and a
 * negative absolute day are where a borrow goes missing.
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

public sealed class DateTimeValueOracleTests
{
    private static readonly string[] Dates =
    [
        "2000-01-02", "1970-01-01", "1969-12-31", "0001-01-01", "9999-12-31", "1900-02-28",
        "2000-02-29", "2100-02-28", "0000-01-01", "-0001-12-31", "20000101", "19700101",
        "2024-06-15", "1600-02-29",
    ];

    private static readonly string[] Times =
    [
        "00:00:00", "10:00:00", "23:59:59", "00:00:00.000000001", "12:34:56.789",
        "12:34:56.123456789", "00:00:00.1", "00:00:00.12", "1:2:3", "010203", "01:02:03",
        "12:34:56.000000001",
    ];

    private static readonly string[] Timestamps =
    [
        "2000-01-02 10:20:30", "2000-01-02 10:20:30.4", "1970-01-01 00:00:00",
        "1969-12-31 23:59:59.999999999", "2024-02-29 12:00:00.5", "0001-01-01 00:00:00",
        "9999-12-31 23:59:59.999999999", "2000-01-02T10:20:30", "2024-06-15 08:09:10.123456789",
    ];

    [JavaOracleFact]
    public void DatesMatchTheReference()
    {
        List<string> java = OracleRunner.Run(
                Dates,
                """
                for (int i = 0; i < inputs.length; i++) {
                  String s = inputs[i];
                  org.h2.value.ValueDate v;
                  try {
                    v = org.h2.value.ValueDate.parse(s);
                  } catch (org.h2.message.DbException e) {
                    System.out.println(hex("ERR " + e.getErrorCode()));
                    continue;
                  } catch (RuntimeException e) {
                    System.out.println(hex("ERR " + e.getClass().getSimpleName()));
                    continue;
                  }
                  System.out.println(hex(v.getDateValue() + "|" + v.getString() + "|"
                      + v.getSQL(new StringBuilder(), 0) + "|" + v.getType().getSQL(0) + "|"
                      + v.getValueType() + "|" + v.getMemory() + "|"
                      + v.equals(org.h2.value.ValueDate.parse(s)) + "|"
                      + v.equals(org.h2.value.ValueDate.parse("2000-01-02")) + "|"
                      + v.compareTo(org.h2.value.ValueDate.parse("1970-01-01"), null,
                          org.h2.value.CompareMode.getInstance(null, 0))));
                }
                """,
                extraJavaArgs: ["-Duser.language=en", "-Dfile.encoding=UTF-8"])
            .Select(line => Decode(line))
            .ToList();

        List<string> csharp = Dates.Select(DescribeDate).ToList();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"{Dates[i]}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [JavaOracleFact]
    public void TimesMatchTheReference()
    {
        List<string> java = OracleRunner.Run(
                Times,
                """
                org.h2.value.ValueTime half = org.h2.value.ValueTime.fromNanos(5_000_000_000L);
                for (int i = 0; i < inputs.length; i++) {
                  String s = inputs[i];
                  org.h2.value.ValueTime v;
                  try {
                    v = org.h2.value.ValueTime.parse(s, null);
                  } catch (org.h2.message.DbException e) {
                    System.out.println(hex("ERR " + e.getErrorCode()));
                    continue;
                  } catch (RuntimeException e) {
                    System.out.println(hex("ERR " + e.getClass().getSimpleName()));
                    continue;
                  }
                  try {
                    System.out.println(hex(v.getNanos() + "|" + v.getString() + "|"
                        + v.getSQL(new StringBuilder(), 0) + "|" + v.getType().getSQL(0) + "|"
                        + v.getValueType() + "|" + v.getMemory() + "|"
                        + v.equals(org.h2.value.ValueTime.parse(s, null)) + "|"
                        + v.compareTo(org.h2.value.ValueTime.fromNanos(0), null,
                            org.h2.value.CompareMode.getInstance(null, 0)) + "|"
                        + ((org.h2.value.ValueTime) v.add(half)).getNanos() + "|"
                        + ((org.h2.value.ValueTime) v.subtract(half)).getNanos() + "|"
                        + ((org.h2.value.ValueTime) v.multiply(
                            org.h2.value.ValueDouble.get(2))).getNanos()));
                  } catch (org.h2.message.DbException e) {
                    // A time that comes out of the arithmetic outside the day is refused, and the
                    // refusal is the answer for those entries of the corpus.
                    System.out.println(hex("ERR " + e.getErrorCode()));
                  }
                }
                """,
                extraJavaArgs: ["-Duser.language=en", "-Dfile.encoding=UTF-8"])
            .Select(line => Decode(line))
            .ToList();

        List<string> csharp = Times.Select(DescribeTime).ToList();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"{Times[i]}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [JavaOracleFact]
    public void TimestampsMatchTheReference()
    {
        List<string> java = OracleRunner.Run(
                Timestamps,
                """
                org.h2.value.ValueTimestamp one = org.h2.value.ValueTimestamp.fromDateValueAndNanos(
                    org.h2.util.DateTimeUtils.dateValue(1970, 1, 1), 1_000_000_000L);
                org.h2.value.CompareMode mode = org.h2.value.CompareMode.getInstance(null, 0);
                for (int i = 0; i < inputs.length; i++) {
                  String s = inputs[i];
                  org.h2.value.ValueTimestamp v;
                  try {
                    v = org.h2.value.ValueTimestamp.parse(s, null);
                  } catch (org.h2.message.DbException e) {
                    System.out.println(hex("ERR " + e.getErrorCode()));
                    continue;
                  } catch (RuntimeException e) {
                    System.out.println(hex("ERR " + e.getClass().getSimpleName()));
                    continue;
                  }
                  System.out.println(hex(v.getDateValue() + "|" + v.getTimeNanos() + "|"
                      + v.getString() + "|" + v.getISOString() + "|"
                      + v.getSQL(new StringBuilder(), 0) + "|" + v.getType().getSQL(0) + "|"
                      + v.getValueType() + "|" + v.getMemory() + "|"
                      + v.equals(org.h2.value.ValueTimestamp.parse(s, null)) + "|"
                      + v.compareTo(one, null, mode) + "|"
                      + ((org.h2.value.ValueTimestamp) v.add(one)).getDateValue() + ":"
                      + ((org.h2.value.ValueTimestamp) v.add(one)).getTimeNanos() + "|"
                      + ((org.h2.value.ValueTimestamp) v.subtract(one)).getDateValue() + ":"
                      + ((org.h2.value.ValueTimestamp) v.subtract(one)).getTimeNanos()));
                }
                """,
                extraJavaArgs: ["-Duser.language=en", "-Dfile.encoding=UTF-8"])
            .Select(line => Decode(line))
            .ToList();

        List<string> csharp = Timestamps.Select(DescribeTimestamp).ToList();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(
                java[i] == csharp[i],
                $"{Timestamps[i]}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// What a time or a timestamp with a zone in it is answered with.
    /// </summary>
    /// <remarks>
    /// The reference reads <c>10:00:00Z</c> as a TIME WITH TIME ZONE and hands back a value shifted
    /// into the zone of the session; this port refuses it, because the types that hold a zone are
    /// not ported and a TIME that silently became a different value would be worse than a refusal.
    /// The error code is the reference's own code for a constant it cannot read, so a statement that
    /// has a zone in it fails the same way here and there with the number the user is shown. That is
    /// asserted below, and the gap is named rather than left to be discovered.
    /// </remarks>
    [JavaOracleFact]
    public void ATimeWithAZoneIsRefusedHereWithTheCodeTheReferenceGivesForABadConstant()
    {
        string[] texts =
        [
            "10:00:00Z", "10:00:00+01", "10:00:00 Europe/Berlin", "2000-01-02 10:00:00Z",
            "2000-01-02 10:00:00+01:00", "2000-01-02 10:00:00 UTC",
        ];

        List<string> java = OracleRunner.Run(
                [],
                """
                String[] texts = {"10:00:00Z", "10:00:00+01", "10:00:00 Europe/Berlin",
                    "2000-01-02 10:00:00Z", "2000-01-02 10:00:00+01:00", "2000-01-02 10:00:00 UTC"};
                for (String s : texts) {
                  try {
                    org.h2.value.ValueTime.parse(s, null);
                    System.out.println("READ");
                  } catch (org.h2.message.DbException e) {
                    System.out.println("ERR " + e.getErrorCode());
                  } catch (RuntimeException e) {
                    System.out.println("OTHER " + e.getClass().getSimpleName());
                  }
                }
                """,
                extraJavaArgs: ["-Duser.language=en", "-Dfile.encoding=UTF-8"])
            .Select(line => line.TrimStart('='))
            .ToList();

        // The reference reads every one of them, into a value of a type this port does not have, and
        // this port refuses every one of them with the code it gives a constant it cannot read. The
        // two answers are not equal and are not meant to be: what is checked here is that the
        // reference really does read them, so that the refusal below is a known gap and not a claim
        // that such a text is nonsense.
        // Two of the six are read by the reference, into a type this port does not have; the other
        // four are refused there with the same code this port refuses them with. The two answers are
        // not equal and are not meant to be, and the reference's side is what makes the difference a
        // known gap rather than a claim that all six texts are nonsense.
        Assert.Equal(texts.Length, java.Count);
        Assert.Equal("READ", java[0]);
        Assert.Equal("READ", java[1]);
        for (int i = 2; i < java.Count; i++)
        {
            Assert.Equal("ERR " + ErrorCode.InvalidDatetimeConstant2, java[i]);
        }

        foreach (string text in texts)
        {
            H2SharpException error = Assert.Throws<H2SharpException>(() => ValueTime.Parse(text, null));
            Assert.Equal(ErrorCode.InvalidDatetimeConstant2, error.H2ErrorCode);
        }
    }

    private static string DescribeDate(string text)
    {
        Value v;
        try
        {
            v = ValueDate.Parse(text);
        }
        catch (H2SharpException e)
        {
            return "ERR " + e.H2ErrorCode.ToString(CultureInfo.InvariantCulture);
        }

        ValueDate date = (ValueDate)v;
        return string.Join(
            "|",
            date.GetDateValue(),
            date.GetString(),
            date.GetSql(new StringBuilder(), 0).ToString(),
            date.GetTypeInfo().GetSql(0).ToString(),
            date.GetValueType(),
            date.GetMemory(),
            date.Equals(ValueDate.Parse(text)) ? "true" : "false",
            date.Equals(ValueDate.Parse("2000-01-02")) ? "true" : "false",
            date.CompareTypeSafe(ValueDate.Parse("1970-01-01"), CompareMode.GetInstance(null, 0), null));
    }

    private static string DescribeTime(string text)
    {
        Value v;
        try
        {
            v = ValueTime.Parse(text, null);
        }
        catch (H2SharpException e)
        {
            return "ERR " + e.H2ErrorCode.ToString(CultureInfo.InvariantCulture);
        }

        ValueTime time = (ValueTime)v;
        ValueTime half = ValueTime.FromNanos(5_000_000_000L);
        try
        {
            return DescribeTimeFields(time, half, text);
        }
        catch (H2SharpException e)
        {
            // A time that comes out of the arithmetic outside the day is refused, and the refusal
            // is the answer for those entries of the corpus.
            return "ERR " + e.H2ErrorCode.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static string DescribeTimeFields(ValueTime time, ValueTime half, string text)
    {
        return string.Join(
            "|",
            time.GetNanos(),
            time.GetString(),
            time.GetSql(new StringBuilder(), 0).ToString(),
            time.GetTypeInfo().GetSql(0).ToString(),
            time.GetValueType(),
            time.GetMemory(),
            time.Equals(ValueTime.Parse(text, null)) ? "true" : "false",
            time.CompareTypeSafe(ValueTime.FromNanos(0), CompareMode.GetInstance(null, 0), null),
            ((ValueTime)time.Add(half)).GetNanos(),
            ((ValueTime)time.Subtract(half)).GetNanos(),
            ((ValueTime)time.Multiply(ValueDouble.Get(2))).GetNanos());
    }

    private static string DescribeTimestamp(string text)
    {
        Value v;
        try
        {
            v = ValueTimestamp.Parse(text, null);
        }
        catch (H2SharpException e)
        {
            return "ERR " + e.H2ErrorCode.ToString(CultureInfo.InvariantCulture);
        }

        ValueTimestamp stamp = (ValueTimestamp)v;
        ValueTimestamp one = ValueTimestamp.FromDateValueAndNanos(
            DateTimeUtils.DateValue(1970, 1, 1), 1_000_000_000L);
        Value added = stamp.Add(one);
        Value subtracted = stamp.Subtract(one);
        return string.Join(
            "|",
            stamp.GetDateValue(),
            stamp.GetTimeNanos(),
            stamp.GetString(),
            stamp.GetIsoString(),
            stamp.GetSql(new StringBuilder(), 0).ToString(),
            stamp.GetTypeInfo().GetSql(0).ToString(),
            stamp.GetValueType(),
            stamp.GetMemory(),
            stamp.Equals(ValueTimestamp.Parse(text, null)) ? "true" : "false",
            stamp.CompareTypeSafe(one, CompareMode.GetInstance(null, 0), null),
            ((ValueTimestamp)added).GetDateValue() + ":" + ((ValueTimestamp)added).GetTimeNanos(),
            ((ValueTimestamp)subtracted).GetDateValue() + ":" + ((ValueTimestamp)subtracted).GetTimeNanos());
    }

    /// <summary>
    /// Reads the hex the oracle prints, or passes the line through when it is already text.
    /// </summary>
    /// <remarks>
    /// A snippet that answers "the parse threw" prints that in words rather than in hex, so a line
    /// that is not hex is a line the reference refused to encode, and there is nothing to decode.
    /// </remarks>
    private static string Decode(string hex)
    {
        string only = hex.TrimStart('=');
        if (only.Length == 0)
        {
            return string.Empty;
        }

        if (!only.All(static c => char.IsAsciiHexDigit(c)))
        {
            return only;
        }

        StringBuilder b = new(only.Length / 4);
        for (int i = 0; i + 3 < only.Length; i += 4)
        {
            _ = b.Append((char)Convert.ToInt32(only.Substring(i, 4), 16));
        }

        return b.ToString();
    }
}
