/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the conversions into INTERVAL.
 *
 * An interval is the one type in the family with two families inside it: YEAR TO MONTH, whose
 * fields are whole numbers of months, and DAY TO SECOND, whose last field is a fraction of a
 * second. They do not convert into each other at all — a year is not a fixed number of days —
 * so a conversion inside one family is about turning a length of time into the fields a
 * particular width can hold, and a conversion across the two is refused.
 *
 * The cases that matter are the ones where the number has to be scaled before it can be read:
 * a DOUBLE becomes an INTERVAL SECOND by being multiplied by a billion, and a NUMERIC becomes
 * an INTERVAL YEAR TO MONTH by being multiplied by twelve. Getting the multiplier wrong shows up
 * only on the fractional targets, which is why the corpus has one of every target.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class IntervalConversionOracleTests
{
    /// <summary>
    /// The value to convert, and the type to convert it to.
    /// </summary>
    private sealed record Case(string Value, string Target);

    private static readonly Case[] Corpus =
    [
        // A number into a year-month interval: the leading field is years.
        new("INT '5'", "INTERVAL YEAR"),
        new("INT '-5'", "INTERVAL YEAR"),
        new("INT '5'", "INTERVAL MONTH"),
        new("INT '5'", "INTERVAL YEAR TO MONTH"),
        new("BIGINT '1000000'", "INTERVAL YEAR"),

        // A number into a day-time interval: the leading field is the days.
        new("INT '5'", "INTERVAL DAY"),
        new("INT '-5'", "INTERVAL DAY"),
        new("INT '5'", "INTERVAL HOUR"),
        new("INT '5'", "INTERVAL MINUTE"),
        new("INT '5'", "INTERVAL SECOND"),

        // A decimal into each: this is where the multiplier lives.
        new("NUMERIC '2.5'", "INTERVAL YEAR"),
        new("NUMERIC '2.5'", "INTERVAL YEAR TO MONTH"),
        new("NUMERIC '2.5'", "INTERVAL DAY"),
        new("NUMERIC '2.5'", "INTERVAL SECOND"),
        new("NUMERIC '2.5'", "INTERVAL MINUTE TO SECOND"),
        new("NUMERIC '2.5'", "INTERVAL HOUR TO SECOND"),
        new("NUMERIC '0.000000001'", "INTERVAL SECOND"),
        new("NUMERIC '0.5'", "INTERVAL YEAR TO MONTH"),
        new("DOUBLE '2.5'", "INTERVAL YEAR"),
        new("DOUBLE '2.5'", "INTERVAL SECOND"),
        new("DOUBLE '0.5'", "INTERVAL MINUTE TO SECOND"),
        new("DECFLOAT '2.5'", "INTERVAL YEAR TO MONTH"),
        new("DECFLOAT '2.5'", "INTERVAL DAY TO SECOND"),

        // A string: the reference parses it and then converts the parse.
        new("'P5Y'", "INTERVAL YEAR TO MONTH"),
        new("'P5Y'", "INTERVAL YEAR"),
        new("'PT5H'", "INTERVAL HOUR TO SECOND"),
        new("'PT1H30M'", "INTERVAL MINUTE TO SECOND"),
        new("'PT0.5S'", "INTERVAL SECOND"),

        // An interval into another interval of the same family.
        new("INTERVAL '5' YEAR", "INTERVAL YEAR TO MONTH"),
        new("INTERVAL '5' YEAR", "INTERVAL MONTH"),
        new("INTERVAL '5' YEAR TO MONTH", "INTERVAL YEAR"),
        new("INTERVAL '2' DAY TO SECOND", "INTERVAL HOUR TO SECOND"),
        new("INTERVAL '2' DAY TO SECOND", "INTERVAL DAY"),
        new("INTERVAL '90' MINUTE TO SECOND", "INTERVAL HOUR TO SECOND"),
        new("INTERVAL '90' MINUTE TO SECOND", "INTERVAL MINUTE TO SECOND"),
        new("INTERVAL '1' HOUR TO SECOND", "INTERVAL DAY TO HOUR"),

        // And across the two families, which is refused in both directions.
        new("INTERVAL '5' YEAR", "INTERVAL DAY"),
        new("INTERVAL '5' DAY", "INTERVAL YEAR"),
        new("INT '5'", "INTERVAL YEAR TO MONTH"),
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
            Value v = Source(value);
            Value converted = v.CastToValue(TypeInfoOf(target), null);
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
        catch (ArgumentException)
        {
            return "THROWS";
        }
    }

    private static TypeInfo TypeInfoOf(string target) => TypeInfo.GetTypeInfo(target switch
    {
        "INTERVAL YEAR" => Value.IntervalYear,
        "INTERVAL MONTH" => Value.IntervalMonth,
        "INTERVAL YEAR TO MONTH" => Value.IntervalYearToMonth,
        "INTERVAL DAY" => Value.IntervalDay,
        "INTERVAL HOUR" => Value.IntervalHour,
        "INTERVAL MINUTE" => Value.IntervalMinute,
        "INTERVAL SECOND" => Value.IntervalSecond,
        "INTERVAL DAY TO HOUR" => Value.IntervalDayToHour,
        "INTERVAL DAY TO SECOND" => Value.IntervalDayToSecond,
        "INTERVAL HOUR TO SECOND" => Value.IntervalHourToSecond,
        "INTERVAL MINUTE TO SECOND" => Value.IntervalMinuteToSecond,
        _ => throw new ArgumentException($"Unknown target type '{target}'", nameof(target)),
    });

    private static Value Source(string value)
    {
        string s = value.Trim();
        if (s.StartsWith("INT '", StringComparison.Ordinal))
        {
            return ValueInteger.Get(int.Parse(s[5..^1], CultureInfo.InvariantCulture));
        }

        if (s.StartsWith("BIGINT '", StringComparison.Ordinal))
        {
            return ValueBigint.Get(long.Parse(s[8..^1], CultureInfo.InvariantCulture));
        }

        if (s.StartsWith("NUMERIC '", StringComparison.Ordinal))
        {
            return ValueNumeric.Get(BigDecimal.Parse(s[9..^1]));
        }

        if (s.StartsWith("DOUBLE '", StringComparison.Ordinal))
        {
            return ValueDouble.Get(double.Parse(s[8..^1], CultureInfo.InvariantCulture));
        }

        if (s.StartsWith("DECFLOAT '", StringComparison.Ordinal))
        {
            return ValueDecfloat.Get(BigDecimal.Parse(s[10..^1]));
        }

        if (s.StartsWith("INTERVAL '", StringComparison.Ordinal))
        {
            // The middle is the leading field and the last word is the unit, so the qualifier
            // is whatever follows the last space.
            string body = s[10..^1];
            int lastSpace = body.LastIndexOf(' ');
            string number = body[..lastSpace];
            IntervalQualifier qualifier = body[(lastSpace + 1)..] switch
            {
                "YEAR" => IntervalQualifier.Year,
                "YEAR TO MONTH" => IntervalQualifier.YearToMonth,
                "DAY" => IntervalQualifier.Day,
                "DAY TO SECOND" => IntervalQualifier.DayToSecond,
                "HOUR TO SECOND" => IntervalQualifier.HourToSecond,
                "MINUTE TO SECOND" => IntervalQualifier.MinuteToSecond,
                _ => throw new ArgumentException($"Unknown unit in '{body}'", nameof(value)),
            };

            long leading = long.Parse(number, CultureInfo.InvariantCulture);
            return ValueInterval.From(qualifier, leading < 0, Math.Abs(leading), 0L);
        }

        return ValueVarchar.Get(s[1..^1]);
    }

    private const string Declare = """
                        java.util.function.Function<String, org.h2.value.Value> source = spec -> {
                          String s = spec.trim();
                          if (s.startsWith("INT '")) {
                            return org.h2.value.ValueInteger.get(
                                Integer.parseInt(s.substring(5, s.length() - 1)));
                          }
                          if (s.startsWith("BIGINT '")) {
                            return org.h2.value.ValueBigint.get(
                                Long.parseLong(s.substring(8, s.length() - 1)));
                          }
                          if (s.startsWith("NUMERIC '")) {
                            return org.h2.value.ValueNumeric.get(
                                new java.math.BigDecimal(s.substring(9, s.length() - 1)));
                          }
                          if (s.startsWith("DOUBLE '")) {
                            return org.h2.value.ValueDouble.get(
                                Double.parseDouble(s.substring(8, s.length() - 1)));
                          }
                          if (s.startsWith("DECFLOAT '")) {
                            return org.h2.value.ValueDecfloat.get(
                                new java.math.BigDecimal(s.substring(10, s.length() - 1)));
                          }
                          if (s.startsWith("INTERVAL '")) {
                            String body = s.substring(10, s.length() - 1);
                            int space = body.lastIndexOf(' ');
                            long leading = Long.parseLong(body.substring(0, space));
                            String unit = body.substring(space + 1);
                            org.h2.api.IntervalQualifier q;
                            if (unit.equals("YEAR")) {
                              q = org.h2.api.IntervalQualifier.YEAR;
                            } else if (unit.equals("YEAR TO MONTH")) {
                              q = org.h2.api.IntervalQualifier.YEAR_TO_MONTH;
                            } else if (unit.equals("DAY")) {
                              q = org.h2.api.IntervalQualifier.DAY;
                            } else if (unit.equals("DAY TO SECOND")) {
                              q = org.h2.api.IntervalQualifier.DAY_TO_SECOND;
                            } else if (unit.equals("HOUR TO SECOND")) {
                              q = org.h2.api.IntervalQualifier.HOUR_TO_SECOND;
                            } else if (unit.equals("MINUTE TO SECOND")) {
                              q = org.h2.api.IntervalQualifier.MINUTE_TO_SECOND;
                            } else {
                              throw new IllegalArgumentException(unit);
                            }
                            return org.h2.value.ValueInterval.from(
                                q, leading < 0, Math.abs(leading), 0L);
                          }
                          return org.h2.value.ValueVarchar.get(s.substring(1, s.length() - 1));
                        };

                        java.util.function.Function<String, Integer> target = name -> {
                          if (name.equals("INTERVAL YEAR")) {
                            return org.h2.value.Value.INTERVAL_YEAR;
                          }
                          if (name.equals("INTERVAL MONTH")) {
                            return org.h2.value.Value.INTERVAL_MONTH;
                          }
                          if (name.equals("INTERVAL YEAR TO MONTH")) {
                            return org.h2.value.Value.INTERVAL_YEAR_TO_MONTH;
                          }
                          if (name.equals("INTERVAL DAY")) {
                            return org.h2.value.Value.INTERVAL_DAY;
                          }
                          if (name.equals("INTERVAL HOUR")) {
                            return org.h2.value.Value.INTERVAL_HOUR;
                          }
                          if (name.equals("INTERVAL MINUTE")) {
                            return org.h2.value.Value.INTERVAL_MINUTE;
                          }
                          if (name.equals("INTERVAL SECOND")) {
                            return org.h2.value.Value.INTERVAL_SECOND;
                          }
                          if (name.equals("INTERVAL DAY TO HOUR")) {
                            return org.h2.value.Value.INTERVAL_DAY_TO_HOUR;
                          }
                          if (name.equals("INTERVAL DAY TO SECOND")) {
                            return org.h2.value.Value.INTERVAL_DAY_TO_SECOND;
                          }
                          if (name.equals("INTERVAL HOUR TO SECOND")) {
                            return org.h2.value.Value.INTERVAL_HOUR_TO_SECOND;
                          }
                          if (name.equals("INTERVAL MINUTE TO SECOND")) {
                            return org.h2.value.Value.INTERVAL_MINUTE_TO_SECOND;
                          }
                          throw new IllegalArgumentException(name);
                        };

                        String[][] cases = {
                            {"INT '5'", "INTERVAL YEAR"},
                            {"INT '-5'", "INTERVAL YEAR"},
                            {"INT '5'", "INTERVAL MONTH"},
                            {"INT '5'", "INTERVAL YEAR TO MONTH"},
                            {"BIGINT '1000000'", "INTERVAL YEAR"},
                            {"INT '5'", "INTERVAL DAY"},
                            {"INT '-5'", "INTERVAL DAY"},
                            {"INT '5'", "INTERVAL HOUR"},
                            {"INT '5'", "INTERVAL MINUTE"},
                            {"INT '5'", "INTERVAL SECOND"},
                            {"NUMERIC '2.5'", "INTERVAL YEAR"},
                            {"NUMERIC '2.5'", "INTERVAL YEAR TO MONTH"},
                            {"NUMERIC '2.5'", "INTERVAL DAY"},
                            {"NUMERIC '2.5'", "INTERVAL SECOND"},
                            {"NUMERIC '2.5'", "INTERVAL MINUTE TO SECOND"},
                            {"NUMERIC '2.5'", "INTERVAL HOUR TO SECOND"},
                            {"NUMERIC '0.000000001'", "INTERVAL SECOND"},
                            {"NUMERIC '0.5'", "INTERVAL YEAR TO MONTH"},
                            {"DOUBLE '2.5'", "INTERVAL YEAR"},
                            {"DOUBLE '2.5'", "INTERVAL SECOND"},
                            {"DOUBLE '0.5'", "INTERVAL MINUTE TO SECOND"},
                            {"DECFLOAT '2.5'", "INTERVAL YEAR TO MONTH"},
                            {"DECFLOAT '2.5'", "INTERVAL DAY TO SECOND"},
                            {"'P5Y'", "INTERVAL YEAR TO MONTH"},
                            {"'P5Y'", "INTERVAL YEAR"},
                            {"'PT5H'", "INTERVAL HOUR TO SECOND"},
                            {"'PT1H30M'", "INTERVAL MINUTE TO SECOND"},
                            {"'PT0.5S'", "INTERVAL SECOND"},
                            {"INTERVAL '5' YEAR", "INTERVAL YEAR TO MONTH"},
                            {"INTERVAL '5' YEAR", "INTERVAL MONTH"},
                            {"INTERVAL '5' YEAR TO MONTH", "INTERVAL YEAR"},
                            {"INTERVAL '2' DAY TO SECOND", "INTERVAL HOUR TO SECOND"},
                            {"INTERVAL '2' DAY TO SECOND", "INTERVAL DAY"},
                            {"INTERVAL '90' MINUTE TO SECOND", "INTERVAL HOUR TO SECOND"},
                            {"INTERVAL '90' MINUTE TO SECOND", "INTERVAL MINUTE TO SECOND"},
                            {"INTERVAL '1' HOUR TO SECOND", "INTERVAL DAY TO HOUR"},
                            {"INTERVAL '5' YEAR", "INTERVAL DAY"},
                            {"INTERVAL '5' DAY", "INTERVAL YEAR"},
                            {"INT '5'", "INTERVAL YEAR TO MONTH"},
                        };
              """;

    private const string Body = """
                        for (int i = 0; i < cases.length; i++) {
                          try {
                            org.h2.value.Value v = source.apply(cases[i][0]);
                            org.h2.value.TypeInfo t = org.h2.value.TypeInfo.getTypeInfo(
                                target.apply(cases[i][1]));
                            org.h2.value.Value c = v.castTo(t, null);
                            System.out.println("ok " + c.getString() + "/" + c.getValueType());
                          } catch (RuntimeException e) {
                            System.out.println("THROWS");
                          }
                        }
              """;
}
