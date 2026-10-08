/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the conversions between the date and time types.
 *
 * Four of those conversions read the provider: they have to know where the reader is, or what
 * day it is there. That makes them the only conversions in the family whose answer can change
 * without the inputs changing, so both sides are given the same fixed "now" and the same fixed
 * zone, written out in the test rather than taken from the machine. A conversion answered with
 * the real clock would be a test of the clock.
 *
 * The cases that matter are the asymmetric ones. An instant and a wall-clock reading are
 * different questions, and a value with a zone has to be moved into the reader's zone before
 * its time of day is taken. Two zones that differ by hours and a value written between them
 * are what catch a conversion that dropped the offset instead of applying it.
 * <para>
 * <para>
 * Two differences are known and are not being chased here. A time zone database has no rules
 * before the first transition it records: Java extrapolates backwards and gets Moscow's local
 * mean time, .NET stops, so a zone asked about a year in the seventeenth answers differently
 * on the two sides. And a string date is read the same way by both — including the year, which
 * comes out as seventeen either way — so a case that ends up asking a zone about such a date
 * is comparing the databases rather than the conversion.
 * </para>
 * <para>
 * A refusal is compared as a refusal and not as an exception. H2's dates run to a billion
 * years, and several of these conversions ask a time zone about a date in that range: the
 * reference gets a <c>NumberFormatException</c> out of its own calendar for some of them and
 * ours gets a proper conversion error for others. Which of the two is not a contract, and
 * comparing them would have the test fail on a difference nobody is asking for.
 * </para>
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using H2Sharp.Engine;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class DateTimeConversionOracleTests
{
    /// <summary>
    /// The value to convert, and the type to convert it to.
    /// </summary>
    private sealed record Case(string Value, string Target);

    /// <summary>
    /// The zone both sides are given.
    /// </summary>
    private const string Zone = "Europe/Moscow";

    private static readonly Case[] Corpus =
    [
        // A reading and a reading: nothing to move.
        new("DATE '2017-06-15'", "TIME"),
        new("DATE '2017-06-15'", "TIMESTAMP"),
        new("DATE '2017-06-15'", "TIMESTAMP WITH TIME ZONE"),
        new("TIME '12:34:56.789'", "DATE"),
        new("TIME '12:34:56.789'", "TIME"),
        new("TIME '12:34:56.789'", "TIME WITH TIME ZONE"),
        new("TIME '12:34:56.789'", "TIMESTAMP"),
        new("TIME '12:34:56.789'", "TIMESTAMP WITH TIME ZONE"),

        // A reading and a string.
        new("'2017-06-15'", "DATE"),
        new("'12:34:56'", "TIME"),
        new("'2017-06-15 12:34:56.789'", "TIMESTAMP"),

        // With a zone: the case this file is about. The values are built from a date, a time
        // and an offset rather than parsed from text, because a parsed one lands in a year
        // where the two time zone databases have no rules in common and the conversion is no
        // longer what is being compared.
        new("TS 2017-06-15 12:34:56.789 +03:00", "TIMESTAMP"),
        new("TS 2017-06-15 12:34:56.789 +03:00", "DATE"),
        new("TS 2017-06-15 12:34:56.789 +03:00", "TIME"),
        new("TS 2017-06-15 12:34:56.789 +03:00", "TIME WITH TIME ZONE"),
        new("TS 2017-06-15 12:34:56.789 +03:00", "TIMESTAMP WITH TIME ZONE"),
        new("TS 2017-06-15 23:34:56.789 -08:00", "TIMESTAMP"),
        new("TS 2017-06-15 01:34:56.789 +03:00", "DATE"),
        new("TS 2017-12-15 01:34:56.789 +03:00", "DATE"),
        new("TS 2017-06-15 00:30:00.000 +03:00", "DATE"),
        new("TS 2017-06-14 23:30:00.000 +03:00", "DATE"),
        new("TIME 12:34:56.789 +03:00", "TIME"),
        new("TIME 12:34:56.789 +03:00", "TIMESTAMP"),
        new("TIME 12:34:56.789 +03:00", "TIME WITH TIME ZONE"),

        // Scale: converting to a narrower scale truncates the fraction.
        new("TIME '12:34:56.789'", "TIME(2)"),
        new("TIME '12:34:56.789'", "TIME(0)"),
        new("TIMESTAMP '2017-06-15 12:34:56.789'", "TIMESTAMP(3)"),
        new("TIMESTAMP '2017-06-15 12:34:56.789'", "TIMESTAMP(0)"),
        new("TS 2017-06-15 12:34:56.789 +03:00", "TIMESTAMP WITH TIME ZONE(3)"),
        new("TIME 12:34:56.789 +03:00", "TIME WITH TIME ZONE(1)"),

        // A value that already has a zone, converted to another one: both answers have to move
        // the same instant, and a zone with rules has to be asked about the moment it lands on
        // rather than about the moment it was written on.
        new("TS 2017-06-15 12:34:56.789 +03:00", "TIMESTAMP WITH TIME ZONE"),
        new("TS 2017-01-15 12:34:56.789 +03:00", "TIMESTAMP WITH TIME ZONE"),
        new("TS 2017-06-15 12:34:56.789 -08:00", "TIMESTAMP"),
    ];

    [JavaOracleFact]
    public void ConversionsMatchTheReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                Declare + Body,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .ToList();

        List<string> csharp = [.. Corpus.Select(c => Describe(c.Value, c.Target))];

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(
                java[i] == csharp[i],
                $"line {i}, {Corpus[i].Value} -> {Corpus[i].Target}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    private static string Describe(string value, string target)
    {
        try
        {
            TestCastDataProvider provider = new(TimeZoneProvider.OfId(Zone));
            Value v = Source(value, provider);
            Value converted = v.CastToValue(TypeInfoOf(target), provider);
            return "ok " + converted.GetString() + "/" + converted.GetValueType();
        }
        catch (H2SharpException)
        {
            return "THROWS";
        }
        catch (NotSupportedException)
        {
            return "THROWS";
        }
    }

    /// <summary>
    /// The target type a corpus line names, where a name in brackets is a scale.
    /// </summary>
    /// <remarks>
    /// The digits are taken from between the brackets rather than from a fixed offset into the
    /// string. An offset would have to be right about the length of the name as well as about
    /// the position of the bracket, and one of the five names here is twenty-four characters
    /// long where another is nineteen — which is how the first version of this read the scale of
    /// a TIMESTAMP WITH TIME ZONE as the empty string.
    /// </remarks>
    /// <param name="target">the name</param>
    /// <returns>the type</returns>
    private static TypeInfo TypeInfoOf(string target)
    {
        int open = target.IndexOf('(', StringComparison.Ordinal);
        if (open >= 0)
        {
            int type = ValueOf(target[..open]);
            long scale = long.Parse(target[(open + 1)..^1], CultureInfo.InvariantCulture);
            return TypeInfo.GetTypeInfo(type, scale, 0, null);
        }

        return TypeInfo.GetTypeInfo(ValueOf(target));
    }

    /// <summary>
    /// The type number a name stands for.
    /// </summary>
    /// <param name="name">the SQL name of the type</param>
    /// <returns>the type number</returns>
    private static int ValueOf(string name) => name switch
    {
        "DATE" => Value.Date,
        "TIME" => Value.Time,
        "TIMESTAMP" => Value.Timestamp,
        "TIME WITH TIME ZONE" => Value.TimeTz,
        "TIMESTAMP WITH TIME ZONE" => Value.TimestampTz,
        _ => throw new ArgumentException($"Unknown target type '{name}'", nameof(name)),
    };

    /// <summary>
    /// The nanoseconds of a written time of day.
    /// </summary>
    /// <param name="text">the time, as written in a corpus line</param>
    /// <returns>nanoseconds since midnight</returns>
    private static long TimeOfDayNanos(string text)
    {
        string[] parts = text.Split(':');
        long nanos = long.Parse(parts[0], CultureInfo.InvariantCulture) * DateTimeUtils.NanosPerHour
            + (long.Parse(parts[1], CultureInfo.InvariantCulture) * DateTimeUtils.NanosPerMinute)
            + (long.Parse(parts[2].Split('.')[0], CultureInfo.InvariantCulture) * DateTimeUtils.NanosPerSecond);
        if (parts[2].Contains('.', StringComparison.Ordinal))
        {
            string fraction = parts[2].Split('.')[1];
            long scale = 1;
            long value = 0;
            foreach (char c in fraction)
            {
                value = (value * 10) + (c - '0');
                scale *= 10;
            }

            nanos += (long)((NanosPerSecondFraction * value) / scale);
        }

        return nanos;
    }

    private const long NanosPerSecondFraction = 1_000_000_000L;

    /// <summary>
    /// The offset a written zone stands for, in seconds.
    /// </summary>
    /// <param name="text">the offset, as <c>+03:00</c> or <c>-08:00</c></param>
    /// <returns>the offset in seconds</returns>
    private static int ZoneOffset(string text)
    {
        int sign = text[0] == '-' ? -1 : 1;
        string[] parts = text[1..].Split(':');
        return sign * (
            (int.Parse(parts[0], CultureInfo.InvariantCulture) * 3_600)
            + (int.Parse(parts[1], CultureInfo.InvariantCulture) * 60));
    }
    /// <summary>
    /// The value a corpus line names, where the spelling says which of the five types it is.
    /// </summary>
    /// <param name="value">the spelling</param>
    /// <returns>the value</returns>
    private static Value Source(string value, CastDataProvider provider)
    {
        string s = value.Trim();
        if (s.StartsWith("TS ", StringComparison.Ordinal)
            || s.StartsWith("TIME 0", StringComparison.Ordinal)
            || s.StartsWith("TIME 1", StringComparison.Ordinal))
        {
            // "TS <date> <time> <zone>" has four fields and "TIME <time> <zone>" three, so the
            // time is second from the end either way and the date, where there is one, is the
            // field before it.
            string[] fields = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            long nanos = TimeOfDayNanos(fields[^2]);
            int offset = ZoneOffset(fields[^1]);
            return fields.Length == 3
                ? ValueTimeTimeZone.FromNanos(nanos, offset)
                : ValueTimestampTimeZone.FromDateValueAndNanos(
                    DateTimeUtils.DateValue(
                        int.Parse(fields[1][..4], CultureInfo.InvariantCulture),
                        int.Parse(fields[1][5..7], CultureInfo.InvariantCulture),
                        int.Parse(fields[1][8..10], CultureInfo.InvariantCulture)),
                    nanos,
                    offset);
        }

        if (s.StartsWith("DATE '", StringComparison.Ordinal))
        {
            return ValueDate.Parse(s[6..^1]);
        }

        if (s.StartsWith("TIME WITH TIME ZONE '", StringComparison.Ordinal))
        {
            return ValueTimeTimeZone.Parse(s[21..^1], provider);
        }

        if (s.StartsWith("TIME '", StringComparison.Ordinal))
        {
            return ValueTime.Parse(s[6..^1], provider);
        }

        if (s.StartsWith("TIMESTAMP WITH TIME ZONE '", StringComparison.Ordinal))
        {
            return ValueTimestampTimeZone.Parse(s[27..^1], provider);
        }

        if (s.StartsWith("TIMESTAMP '", StringComparison.Ordinal))
        {
            return ValueTimestamp.Parse(s[12..^1], provider);
        }

        return ValueVarchar.Get(s[1..^1]);
    }

    /// <summary>
    /// The corpus in the reference's spelling, and the provider both sides are given. The
    /// provider is a local class because the reference's own implementation needs a database and
    /// a user, and the answer being compared is the one for a written-down "now" rather than
    /// the one for whatever the machine thinks.
    /// </summary>
    private const string Declare = """
                        long NOW_SECONDS = org.h2.util.DateTimeUtils.dateValueFromLocalSeconds(0);
                        java.util.TimeZone zone = java.util.TimeZone.getTimeZone("Europe/Moscow");

                        class Prov implements org.h2.engine.CastDataProvider {
                            private final org.h2.value.ValueTimestampTimeZone now =
                                org.h2.value.ValueTimestampTimeZone.fromDateValueAndNanos(
                                    org.h2.util.DateTimeUtils.dateValue(2017, 6, 15), 43200000000000L, 10800);
                            @Override
                            public org.h2.value.ValueTimestampTimeZone currentTimestamp() {
                                return now;
                            }
                            @Override
                            public org.h2.util.TimeZoneProvider currentTimeZone() {
                                return org.h2.util.TimeZoneProvider.ofId("Europe/Moscow");
                            }
                            @Override
                            public org.h2.engine.Mode getMode() {
                                return org.h2.engine.Mode.getRegular();
                            }
                            @Override
                            public org.h2.api.JavaObjectSerializer getJavaObjectSerializer() {
                                return null;
                            }
                            @Override
                            public boolean zeroBasedEnums() {
                                return true;
                            }
                        }

                        Prov provider = new Prov();

                        java.util.function.Function<String, Long> timeOfDay = text -> {
                          String[] parts = text.split(":");
                          long nanos = Long.parseLong(parts[0])
                              * org.h2.util.DateTimeUtils.NANOS_PER_HOUR
                              + Long.parseLong(parts[1])
                              * org.h2.util.DateTimeUtils.NANOS_PER_MINUTE
                              + Long.parseLong(parts[2].split("\\.")[0])
                              * org.h2.util.DateTimeUtils.NANOS_PER_SECOND;
                          if (parts[2].indexOf('.') >= 0) {
                            String fraction = parts[2].split("\\.")[1];
                            long scale = 1;
                            long value = 0;
                            for (int i = 0; i < fraction.length(); i++) {
                              value = value * 10 + (fraction.charAt(i) - '0');
                              scale *= 10;
                            }
                            nanos += 1_000_000_000L * value / scale;
                          }
                          return nanos;
                        };

                        java.util.function.Function<String, Integer> zoneOffset = text -> {
                          int sign = text.charAt(0) == '-' ? -1 : 1;
                          String[] parts = text.substring(1).split(":");
                          return sign * (Integer.parseInt(parts[0]) * 3600
                              + Integer.parseInt(parts[1]) * 60);
                        };

                        java.util.function.Function<String, org.h2.value.Value> source = spec -> {
                          String s = spec.trim();
                          if (s.startsWith("DATE '")) {
                            return org.h2.value.ValueDate.parse(s.substring(6, s.length() - 1));
                          }
                          if (s.startsWith("TIME WITH TIME ZONE '")) {
                            return org.h2.value.ValueTimeTimeZone.parse(
                                s.substring(21, s.length() - 1), provider);
                          }
                          if (s.startsWith("TIME '")) {
                            return org.h2.value.ValueTime.parse(
                                s.substring(6, s.length() - 1), provider);
                          }
                          if (s.startsWith("TIMESTAMP WITH TIME ZONE '")) {
                            return org.h2.value.ValueTimestampTimeZone.parse(
                                s.substring(27, s.length() - 1), provider);
                          }
                          if (s.startsWith("TIMESTAMP '")) {
                            return org.h2.value.ValueTimestamp.parse(
                                s.substring(12, s.length() - 1), provider);
                          }
                          if (s.startsWith("TS ") || s.startsWith("TIME 0") || s.startsWith("TIME 1")) {
                            // "TS <date> <time> <zone>" has four fields and "TIME <time> <zone>"
                            // has three, so the time is second from the end either way and the
                            // date, when there is one, is the field before it.
                            String[] parts = s.split(" ");
                            long nanos = timeOfDay.apply(parts[parts.length - 2]);
                            int offset = zoneOffset.apply(parts[parts.length - 1]);
                            if (s.startsWith("TIME ")) {
                              return org.h2.value.ValueTimeTimeZone.fromNanos(nanos, offset);
                            }
                            String[] d = parts[1].split("-");
                            return org.h2.value.ValueTimestampTimeZone.fromDateValueAndNanos(
                                org.h2.util.DateTimeUtils.dateValue(
                                    Long.parseLong(d[0]), Integer.parseInt(d[1]),
                                    Integer.parseInt(d[2])),
                                nanos, offset);
                          }
                          return org.h2.value.ValueVarchar.get(s.substring(1, s.length() - 1));
                        };

                        java.util.function.Function<String, Integer> typeOf = name -> {
                          if (name.equals("DATE")) {
                            return org.h2.value.Value.DATE;
                          }
                          if (name.equals("TIME")) {
                            return org.h2.value.Value.TIME;
                          }
                          if (name.equals("TIMESTAMP")) {
                            return org.h2.value.Value.TIMESTAMP;
                          }
                          if (name.equals("TIME WITH TIME ZONE")) {
                            return org.h2.value.Value.TIME_TZ;
                          }
                          if (name.equals("TIMESTAMP WITH TIME ZONE")) {
                            return org.h2.value.Value.TIMESTAMP_TZ;
                          }
                          throw new IllegalArgumentException(name);
                        };

                        java.util.function.Function<String, org.h2.value.TypeInfo> target = spec -> {
                          int open = spec.indexOf('(');
                          int type = typeOf.apply(open < 0 ? spec : spec.substring(0, open));
                          if (open < 0) {
                            return org.h2.value.TypeInfo.getTypeInfo(type);
                          }
                          return org.h2.value.TypeInfo.getTypeInfo(type,
                              Long.parseLong(spec.substring(open + 1, spec.length() - 1)), 0, null);
                        };

                        String[][] cases = {
                            {"DATE '2017-06-15'", "TIME"},
                            {"DATE '2017-06-15'", "TIMESTAMP"},
                            {"DATE '2017-06-15'", "TIMESTAMP WITH TIME ZONE"},
                            {"TIME '12:34:56.789'", "DATE"},
                            {"TIME '12:34:56.789'", "TIME"},
                            {"TIME '12:34:56.789'", "TIME WITH TIME ZONE"},
                            {"TIME '12:34:56.789'", "TIMESTAMP"},
                            {"TIME '12:34:56.789'", "TIMESTAMP WITH TIME ZONE"},
                            {"'2017-06-15'", "DATE"},
                            {"'12:34:56'", "TIME"},
                            {"'2017-06-15 12:34:56.789'", "TIMESTAMP"},
                            {"TS 2017-06-15 12:34:56.789 +03:00", "TIMESTAMP"},
                            {"TS 2017-06-15 12:34:56.789 +03:00", "DATE"},
                            {"TS 2017-06-15 12:34:56.789 +03:00", "TIME"},
                            {"TS 2017-06-15 12:34:56.789 +03:00", "TIME WITH TIME ZONE"},
                            {"TS 2017-06-15 12:34:56.789 +03:00", "TIMESTAMP WITH TIME ZONE"},
                            {"TS 2017-06-15 23:34:56.789 -08:00", "TIMESTAMP"},
                            {"TS 2017-06-15 01:34:56.789 +03:00", "DATE"},
                            {"TS 2017-12-15 01:34:56.789 +03:00", "DATE"},
                            {"TS 2017-06-15 00:30:00.000 +03:00", "DATE"},
                            {"TS 2017-06-14 23:30:00.000 +03:00", "DATE"},
                            {"TIME 12:34:56.789 +03:00", "TIME"},
                            {"TIME 12:34:56.789 +03:00", "TIMESTAMP"},
                            {"TIME 12:34:56.789 +03:00", "TIME WITH TIME ZONE"},
                            {"TIME '12:34:56.789'", "TIME(2)"},
                            {"TIME '12:34:56.789'", "TIME(0)"},
                            {"TIMESTAMP '2017-06-15 12:34:56.789'", "TIMESTAMP(3)"},
                            {"TIMESTAMP '2017-06-15 12:34:56.789'", "TIMESTAMP(0)"},
                            {"TS 2017-06-15 12:34:56.789 +03:00", "TIMESTAMP WITH TIME ZONE(3)"},
                            {"TIME 12:34:56.789 +03:00", "TIME WITH TIME ZONE(1)"},
                            {"TS 2017-06-15 12:34:56.789 +03:00", "TIMESTAMP WITH TIME ZONE"},
                            {"TS 2017-01-15 12:34:56.789 +03:00", "TIMESTAMP WITH TIME ZONE"},
                            {"TS 2017-06-15 12:34:56.789 -08:00", "TIMESTAMP"},
                        };
              """;

    private const string Body = """
                        for (int i = 0; i < cases.length; i++) {
                          try {
                            org.h2.value.Value v = source.apply(cases[i][0]);
                            org.h2.value.TypeInfo t = target.apply(cases[i][1]);
                            org.h2.value.Value c = v.castTo(t, provider);
                            System.out.println("ok " + c.getString() + "/" + c.getValueType());
                          } catch (RuntimeException e) {
                            System.out.println("THROWS");
                          }
                        }
              """;
}
