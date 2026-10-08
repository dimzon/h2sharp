/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the API's Interval.
 *
 * The value type and the API's interval are the same two numbers with a qualifier, but they are
 * built from opposite ends: the value is built from text and the API's from the fields a caller
 * thinks in, and the factories are where those fields are checked. Every factory insists that the
 * fields it is given have the same sign, because an interval of one day and minus one hour is not an
 * interval: it is twenty-three hours with a different qualifier, and writing it the other way round
 * would give a value whose text and whose number disagree.
 *
 * So the corpus is every factory, called with fields that agree and fields that do not, and what is
 * compared is the whole of the result: the qualifier, the sign, the two numbers and the text. The
 * field readers are asked separately, one field at a time, because a field a qualifier does not
 * have is a refusal rather than a zero, and a refusal in the middle of a line would hide the fields
 * after it.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class IntervalApiOracleTests
{
    /// <summary>
    /// The factories, in the order the reference declares them, each with the fields that agree and
    /// a pair that does not.
    /// </summary>
    private static readonly (string Name, long[] Arguments)[] Factories =
    [
        ("ofYears", [3, -3, 0]),
        ("ofMonths", [5, -5, 0]),
        ("ofDays", [7, -7, 0]),
        ("ofHours", [9, -9, 0]),
        ("ofMinutes", [11, -11, 0]),
        ("ofSeconds", [13, -13, 0]),
        ("ofSeconds(2)", [13, -13, 500_000_000]),
        ("ofNanos", [1_500_000_000, -1_500_000_000, 0]),
        ("ofYearsMonths", [2, -2, 3]),
        ("ofYearsMonths(disagreeing months)", [2, -2, -3]),
        ("ofDaysHours", [2, -2, 23]),
        ("ofDaysHours(disagreeing hours)", [2, -2, -23]),
        ("ofDaysHours(24 hours)", [2, -2, 24]),
        ("ofDaysHoursMinutes", [2, -2, 23]),
        ("ofDaysHoursMinutes(60 minutes)", [2, -2, 60]),
        ("ofDaysHoursMinutesSeconds", [2, -2, 23]),
        ("ofDaysHoursMinutesNanos", [2, -2, 1_500_000_000]),
        ("ofHoursMinutes", [3, -3, 59]),
        ("ofHoursMinutesSeconds", [3, -3, 59]),
        ("ofHoursMinutesNanos", [3, -3, 1_500_000_000]),
        ("ofMinutesSeconds", [4, -4, 59]),
        ("ofMinutesNanos", [4, -4, 1_500_000_000]),
        ("ofMinutesNanos(no nanos)", [4, -4, 0]),
    ];

    /// <summary>
    /// Every field of the interval, each asked of an interval that has it and of one that does not.
    /// </summary>
    private static readonly string[] Fields =
        ["years", "months", "days", "hours", "minutes", "seconds", "nanos"];

    [JavaOracleFact]
    public void TheFactoriesMatchTheReference()
    {
        int[] arguments = [.. Factories.SelectMany(f => f.Arguments).Select(a => (int)a)];

        List<string> java = OracleRunner
            .Run([], FactoriesProgram, numbers: arguments, extraJavaArgs: ["-Duser.language=en"])
            .Select(Decode)
            .ToList();

        List<string> csharp = [];
        foreach ((string name, long[] cases) in Factories)
        {
            foreach (long argument in cases)
            {
                csharp.Add(Describe(name, argument));
            }
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            (string name, long[] cases) = Factories[i / 3];
            Assert.True(
                java[i] == csharp[i],
                $"{name}({cases[i % 3]}):\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [JavaOracleFact]
    public void TheFieldsMatchTheReference()
    {
        List<string> java = OracleRunner
            .Run([], FieldsProgram, numbers: [5, -5, 0], extraJavaArgs: ["-Duser.language=en"])
            .Select(Decode)
            .ToList();

        List<string> csharp =
        [
            .. Fields.Select(f => $"{f}|has|{ReadField(f, 5)}"),
            .. Fields.Select(f => $"{f}|has|{ReadField(f, -5)}"),
            .. Fields.Select(f => $"{f}|none|{ReadField(f, 0)}"),
        ];

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(
                java[i] == csharp[i],
                $"{Fields[i % 7]} of {csharp[i / 7].Split('|')[1]}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// A value built from an interval is the interval, which is how the parser gets from one to the
    /// other.
    /// </summary>
    [Fact]
    public void AnIntervalAndItsValueAreTheSameThing()
    {
        Interval interval = Interval.OfDaysHoursMinutesSeconds(2, 3, 4, 5);

        Assert.Equal("INTERVAL '2 03:04:05' DAY TO SECOND", interval.ToString());
        Assert.Equal(2, interval.GetDays());
        Assert.Equal(3, interval.GetHours());
        Assert.Equal(4, interval.GetMinutes());
        Assert.Equal(5, interval.GetSeconds());
        Assert.Equal(0, interval.GetNanosOfSecond());
        Assert.False(interval.IsNegative());
        Assert.Equal(IntervalQualifier.DayToSecond, interval.GetQualifier());

        H2Sharp.Values.ValueInterval value = H2Sharp.Values.ValueInterval.From(
            interval.GetQualifier(),
            interval.IsNegative(),
            interval.GetLeading(),
            interval.GetRemaining());
        Assert.Equal(interval.ToString(), value.GetString());

        // And the fields of a negative one come out negative, which is what makes it a different
        // value and not the same value with a sign.
        Interval negative = Interval.OfDaysHoursMinutesSeconds(-2, -3, -4, -5);
        Assert.True(negative.IsNegative());
        Assert.Equal(-2, negative.GetDays());
        Assert.Equal(-5, negative.GetSeconds());
        Assert.NotEqual(interval, negative);
    }

    /// <summary>
    /// Fields that do not have the same sign are refused, because the result would be an interval
    /// whose text and whose number disagree.
    /// </summary>
    [Fact]
    public void FieldsThatDisagreeOnSignAreRefused()
    {
        Assert.Throws<ArgumentException>(() => Interval.OfYearsMonths(1, -1));
        Assert.Throws<ArgumentException>(() => Interval.OfDaysHours(1, -1));
        Assert.Throws<ArgumentException>(() => Interval.OfHoursMinutes(1, -1));
        Assert.Throws<ArgumentException>(() => Interval.OfMinutesNanos(1, -1));
        Assert.Throws<ArgumentException>(() => Interval.OfSeconds(1, -1));

        // A field out of its own range is refused as well, and so is a remaining half that is not
        // below the bound of the qualifier.
        Assert.Throws<ArgumentException>(() => Interval.OfDaysHours(1, 24));
        Assert.Throws<ArgumentException>(() => Interval.OfDaysHoursMinutes(1, 0, 60));
        Assert.Throws<ArgumentException>(() => Interval.OfYearsMonths(1, 12));
        Assert.Throws<ArgumentException>(() => new Interval(IntervalQualifier.MinuteToSecond, false, 1, DateTimeUtils.NanosPerMinute));

        // Zero with a sign is not negative: an interval of nothing is not less than nothing.
        Assert.False(Interval.OfYears(0).IsNegative());
        Assert.Throws<ArgumentException>(() => Interval.OfYears(long.MinValue));
    }

    /// <summary>
    /// What the interval is made of, and why the reference calls it a refusal.
    /// </summary>
    private static string B(bool value) => value ? "true" : "false";

    private static string Describe(string name, long argument)
    {
        try
        {
            Interval interval = Build(name, argument);
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{IntervalQualifiers.Name(interval.GetQualifier())}|{B(interval.IsNegative())}|{interval.GetLeading()}|{interval.GetRemaining()}|{interval}");
        }
        catch (Exception e) when (e is ArgumentException or H2SharpException)
        {
            return "REFUSED";
        }
    }

    /// <summary>
    /// One field, read of an interval built around it. The zero asks about an interval that has no
    /// time fields at all, where every one of them is a refusal.
    /// </summary>
    private static string ReadField(string field, long value)
    {
        int s = value < 0 ? -1 : 1;
        try
        {
            Interval interval = value == 0
                ? Interval.OfYearsMonths(0, 0)
                : field switch
                {
                    "years" => Interval.OfYearsMonths(value, 3 * s),
                    "months" => Interval.OfYearsMonths(value, 3 * s),
                    "days" => Interval.OfDaysHours(value, 3 * s),
                    "hours" => Interval.OfDaysHoursMinutes(value, 3 * s, 4 * s),
                    "minutes" => Interval.OfDaysHoursMinutes(value, 3 * s, 4 * s),
                    "seconds" => Interval.OfDaysHoursMinutesSeconds(value, 3 * s, 4 * s, 5 * s),
                    _ => Interval.OfDaysHoursMinutesNanos(value, 3 * s, 4 * s, 1_500_000_000 * s),
                };

            long read = field switch
            {
                "years" => interval.GetYears(),
                "months" => interval.GetMonths(),
                "days" => interval.GetDays(),
                "hours" => interval.GetHours(),
                "minutes" => interval.GetMinutes(),
                "seconds" => interval.GetSeconds(),
                _ => interval.GetNanosOfSecond(),
            };

            return read.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is ArgumentException or H2SharpException)
        {
            return "REFUSED";
        }
    }

    private static Interval Build(string name, long value) => name switch
    {
        "ofYears" => Interval.OfYears(value),
        "ofMonths" => Interval.OfMonths(value),
        "ofDays" => Interval.OfDays(value),
        "ofHours" => Interval.OfHours(value),
        "ofMinutes" => Interval.OfMinutes(value),
        "ofSeconds" => Interval.OfSeconds(value),
        "ofSeconds(2)" => Interval.OfSeconds(value, 500_000_000),
        "ofNanos" => Interval.OfNanos(value),
        "ofYearsMonths" => Interval.OfYearsMonths(value, SameSign(value, 3)),
        "ofYearsMonths(disagreeing months)" => Interval.OfYearsMonths(value, -SameSign(value, 3)),
        "ofDaysHours" => Interval.OfDaysHours(value, SameSign(value, 23)),
        "ofDaysHours(disagreeing hours)" => Interval.OfDaysHours(value, -SameSign(value, 23)),
        "ofDaysHours(24 hours)" => Interval.OfDaysHours(value, 24),
        "ofDaysHoursMinutes" => Interval.OfDaysHoursMinutes(value, SameSign(value, 23), SameSign(value, 23)),
        "ofDaysHoursMinutes(60 minutes)" => Interval.OfDaysHoursMinutes(value, SameSign(value, 23), 60),
        "ofDaysHoursMinutesSeconds" =>
            Interval.OfDaysHoursMinutesSeconds(value, SameSign(value, 23), SameSign(value, 23), SameSign(value, 59)),
        "ofDaysHoursMinutesNanos" =>
            Interval.OfDaysHoursMinutesNanos(value, SameSign(value, 23), SameSign(value, 23), 1_500_000_000),
        "ofHoursMinutes" => Interval.OfHoursMinutes(value, SameSign(value, 59)),
        "ofHoursMinutesSeconds" => Interval.OfHoursMinutesSeconds(value, SameSign(value, 59), SameSign(value, 59)),
        "ofHoursMinutesNanos" => Interval.OfHoursMinutesNanos(value, SameSign(value, 59), 1_500_000_000),
        "ofMinutesSeconds" => Interval.OfMinutesSeconds(value, SameSign(value, 59)),
        "ofMinutesNanos" => Interval.OfMinutesNanos(value, 1_500_000_000),
        "ofMinutesNanos(no nanos)" => Interval.OfMinutesNanos(value, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static int SameSign(long value, int other) => value < 0 ? -other : other;

    private static string Decode(string hex)
    {
        string only = hex.TrimStart('=');
        if (only.Length == 0 || !only.All(static c => char.IsAsciiHexDigit(c)))
        {
            // The JVM answered with something that is not a result line, and it must be
            // visible in the failure message rather than swallowed by the decoder.
            return hex;
        }

        StringBuilder b = new(only.Length / 4);
        for (int i = 0; i + 3 < only.Length; i += 4)
        {
            _ = b.Append((char)Convert.ToInt32(only.Substring(i, 4), 16));
        }

        return b.ToString();
    }

    /// <remarks>
    /// The factories of the reference, called the way the C# side calls them.
    /// </remarks>
    private const string FactoriesProgram = """
              String[] names = {"ofYears", "ofMonths", "ofDays", "ofHours", "ofMinutes", "ofSeconds",
                  "ofSeconds2", "ofNanos", "ofYearsMonths", "ofYearsMonthsDisagree", "ofDaysHours",
                  "ofDaysHoursDisagree", "ofDaysHours24", "ofDaysHoursMinutes",
                  "ofDaysHoursMinutes60", "ofDaysHoursMinutesSeconds", "ofDaysHoursMinutesNanos",
                  "ofHoursMinutes", "ofHoursMinutesSeconds", "ofHoursMinutesNanos",
                  "ofMinutesSeconds", "ofMinutesNanos", "ofMinutesNanosNoNanos"};
              int at = 0;
              for (int c = 0; c < names.length; c++) {
                for (int k = 0; k < 3; k++) {
                  long a = nums[at++];
                  int s = a < 0 ? -1 : 1;
                  StringBuilder b = new StringBuilder();
                  try {
                  org.h2.api.Interval interval;
                  if (names[c].equals("ofYears")) {
                    interval = org.h2.api.Interval.ofYears(a);
                  } else if (names[c].equals("ofMonths")) {
                    interval = org.h2.api.Interval.ofMonths(a);
                  } else if (names[c].equals("ofDays")) {
                    interval = org.h2.api.Interval.ofDays(a);
                  } else if (names[c].equals("ofHours")) {
                    interval = org.h2.api.Interval.ofHours(a);
                  } else if (names[c].equals("ofMinutes")) {
                    interval = org.h2.api.Interval.ofMinutes(a);
                  } else if (names[c].equals("ofSeconds")) {
                    interval = org.h2.api.Interval.ofSeconds(a);
                  } else if (names[c].equals("ofSeconds2")) {
                    interval = org.h2.api.Interval.ofSeconds(a, 500_000_000);
                  } else if (names[c].equals("ofNanos")) {
                    interval = org.h2.api.Interval.ofNanos(a);
                  } else if (names[c].equals("ofYearsMonths")) {
                    interval = org.h2.api.Interval.ofYearsMonths(a, 3 * s);
                  } else if (names[c].equals("ofYearsMonthsDisagree")) {
                    interval = org.h2.api.Interval.ofYearsMonths(a, -3 * s);
                  } else if (names[c].equals("ofDaysHours")) {
                    interval = org.h2.api.Interval.ofDaysHours(a, 23 * s);
                  } else if (names[c].equals("ofDaysHoursDisagree")) {
                    interval = org.h2.api.Interval.ofDaysHours(a, -23 * s);
                  } else if (names[c].equals("ofDaysHours24")) {
                    interval = org.h2.api.Interval.ofDaysHours(a, 24);
                  } else if (names[c].equals("ofDaysHoursMinutes")) {
                    interval = org.h2.api.Interval.ofDaysHoursMinutes(a, 23 * s, 23 * s);
                  } else if (names[c].equals("ofDaysHoursMinutes60")) {
                    interval = org.h2.api.Interval.ofDaysHoursMinutes(a, 23 * s, 60);
                  } else if (names[c].equals("ofDaysHoursMinutesSeconds")) {
                    interval = org.h2.api.Interval.ofDaysHoursMinutesSeconds(a, 23 * s, 23 * s, 59 * s);
                  } else if (names[c].equals("ofDaysHoursMinutesNanos")) {
                    interval = org.h2.api.Interval.ofDaysHoursMinutesNanos(a, 23 * s, 23 * s, 1500000000L);
                  } else if (names[c].equals("ofHoursMinutes")) {
                    interval = org.h2.api.Interval.ofHoursMinutes(a, 59 * s);
                  } else if (names[c].equals("ofHoursMinutesSeconds")) {
                    interval = org.h2.api.Interval.ofHoursMinutesSeconds(a, 59 * s, 59 * s);
                  } else if (names[c].equals("ofHoursMinutesNanos")) {
                    interval = org.h2.api.Interval.ofHoursMinutesNanos(a, 59 * s, 1500000000L);
                  } else if (names[c].equals("ofMinutesSeconds")) {
                    interval = org.h2.api.Interval.ofMinutesSeconds(a, 59 * s);
                  } else if (names[c].equals("ofMinutesNanos")) {
                    interval = org.h2.api.Interval.ofMinutesNanos(a, 1500000000L);
                  } else {
                    interval = org.h2.api.Interval.ofMinutesNanos(a, 0);
                  }
                  b.append(interval.getQualifier().name().replace('_', ' ')).append('|');
                  b.append(interval.isNegative()).append('|');
                  b.append(interval.getLeading()).append('|');
                  b.append(interval.getRemaining()).append('|');
                  b.append(interval);
                  System.out.println(hex(b.toString()));
                  } catch (IllegalArgumentException e) {
                    System.out.println(hex("REFUSED"));
                  }
                }
              }
        """;

    /// <remarks>
    /// One field at a time, because a refusal in the middle of a line would hide the fields after it.
    /// The zero asks about an interval of years and months, where every time field is a refusal.
    /// </remarks>
    private const string FieldsProgram = """
              String[] fields = {"years", "months", "days", "hours", "minutes", "seconds", "nanos"};
              for (int k = 0; k < 3; k++) {
                long a = nums[k];
                String kind = k == 2 ? "none" : "has";
                for (int i = 0; i < fields.length; i++) {
                  String f = fields[i];
                  try {
                  org.h2.api.Interval interval = k == 2
                      ? org.h2.api.Interval.ofYearsMonths(0, 0)
                      : f.equals("years") || f.equals("months") ? org.h2.api.Interval.ofYearsMonths(a, a < 0 ? -3 : 3)
                      : f.equals("days") ? org.h2.api.Interval.ofDaysHours(a, a < 0 ? -3 : 3)
                      : f.equals("hours") || f.equals("minutes") ? org.h2.api.Interval.ofDaysHoursMinutes(a, a < 0 ? -3 : 3, a < 0 ? -4 : 4)
                      : f.equals("seconds") ? org.h2.api.Interval.ofDaysHoursMinutesSeconds(a, a < 0 ? -3 : 3, a < 0 ? -4 : 4, a < 0 ? -5 : 5)
                      : org.h2.api.Interval.ofDaysHoursMinutesNanos(a, a < 0 ? -3 : 3, a < 0 ? -4 : 4, a < 0 ? -1500000000L : 1500000000L);
                  long read;
                  if (f.equals("years")) {
                    read = interval.getYears();
                  } else if (f.equals("months")) {
                    read = interval.getMonths();
                  } else if (f.equals("days")) {
                    read = interval.getDays();
                  } else if (f.equals("hours")) {
                    read = interval.getHours();
                  } else if (f.equals("minutes")) {
                    read = interval.getMinutes();
                  } else if (f.equals("seconds")) {
                    read = interval.getSeconds();
                  } else {
                    read = interval.getNanosOfSecond();
                  }
                  System.out.println(hex(f + "|" + kind + "|" + read));
                  } catch (IllegalArgumentException e) {
                    System.out.println(hex(f + "|" + kind + "|" + "REFUSED"));
                  }
                }
              }
        """;
}
