/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for TimeZoneProvider.
 *
 * A named zone is where a port is most likely to quietly disagree with the reference: the two
 * runtimes carry the same IANA database and spell its identifiers differently, so a zone the
 * reference resolves may not resolve here, and a zone that resolves may have a different name.
 * The corpus is therefore mostly names — the awkward ones, the short ones, the fixed offsets
 * written in every way H2 accepts them — and one moment per zone where the offsets, in both
 * directions of asking, are compared.
 *
 * The two directions matter and are not the same question. Asked about an instant, a zone
 * answers with whatever rule was in force at that instant. Asked about a wall-clock reading, it
 * answers with the rule that makes that reading correct, and the two differ exactly around a
 * daylight-saving change, which is why the corpus has a moment in northern summer and one in
 * northern winter.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]

public sealed class TimeZoneProviderOracleTests
{
    /// <summary>
    /// EPOCH seconds for 2015-06-15T12:00:00Z, in the middle of northern summer, so a zone
    /// with daylight saving answers something other than its standard offset.
    /// </summary>
    private const long Summer = 1_434_379_200L;

    /// <summary>
    /// EPOCH seconds for 2015-01-15T12:00:00Z, in northern winter.
    /// </summary>
    private const long Winter = 1_421_323_200L;

    private static readonly string[] Ids =
    [
        "UTC",
        "Z",
        "GMT",
        "GMT+0",
        "GMT+3",
        "GMT-5",
        "GMT+05:30",
        "GMT+05:30:15",
        "UTC-08:00",
        "Europe/Moscow",
        "Europe/London",
        "Europe/Belfast",
        "Europe/Kiev",
        "Europe/Istanbul",
        "America/New_York",
        "America/Los_Angeles",
        "America/Sao_Paulo",
        "America/Argentina/Buenos_Aires",
        "America/Godthab",
        "Asia/Tokyo",
        "Asia/Calcutta",
        "Asia/Katmandu",
        "Asia/Rangoon",
        "Asia/Saigon",
        "Asia/Shanghai",
        "Australia/Sydney",
        "Australia/ACT",
        "Australia/Lord_Howe",
        "Pacific/Auckland",
        "Pacific/Chatham",
        "Africa/Cairo",
        "Israel",
        "Japan",
        "Iran",
        "Singapore",
    ];

    [JavaOracleFact]
    public void OffsetsMatchTheReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                Declare + Body,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .ToList();

        List<string> csharp = [.. Ids.Select(Describe)];

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// Instants outside DateTimeOffset's years 1..9999 fold by the 400-year Gregorian period
    /// before the zone is asked; the zone rules repeat inside the period, so the folded answer
    /// is the true one. The loop this replaced walked there one period at a time.
    /// </summary>
    /// <remarks>
    /// Only the modern era is compared: below it the runtimes read different history tables
    /// (.NET on Windows has no local-mean-time past, the JVM does), and a fold landing there
    /// answers the standing offset of its own table — the min-1 probe showed exactly that
    /// (-18000 here against -17762 there), which vindicates the landing while forbidding the
    /// comparison. A fold landing in year zero would leave nothing representable on this
    /// side — that edge is recorded in MIGRATION_STATUS.md, and none of these land there.
    /// </remarks>
    [JavaOracleFact]
    public void ExtremeInstantsFoldToTheSameOffset()
    {
        Assert.Equal(43_200L, Noon(1970, 1, 1));

        // The fold's own constants, in seconds: the top of DateTimeOffset's range and the
        // 400-year Gregorian period.
        const long max = 253_402_300_799L;
        const long period = 12_622_780_800L;
        long[] extremes =
        [
            max + 1,
            max + period + 12_345,
            Noon(500_000, 6, 15),
            Noon(999_999_999, 6, 15),
        ];
        string[] zones = ["America/New_York", "Europe/London"];

        List<string> inputs = [];
        foreach (string zone in zones)
        {
            foreach (long t in extremes)
            {
                inputs.Add(zone + "|" + t.ToString(CultureInfo.InvariantCulture));
            }
        }

        const string body = """
            for (String line : inputs) {
                int bar = line.indexOf('|');
                String zone = line.substring(0, bar);
                long t = Long.parseLong(line.substring(bar + 1));
                int off = java.time.ZoneId.of(zone).getRules()
                    .getOffset(java.time.Instant.ofEpochSecond(t)).getTotalSeconds();
                System.out.println("=" + zone + " " + t + ":" + off);
            }
            """;
        List<string> java = OracleRunner.Run(inputs, body, null, null, null);

        List<string> csharp = [];
        foreach (string line in inputs)
        {
            int bar = line.IndexOf('|');
            string zone = line[..bar];
            long t = long.Parse(line[(bar + 1)..], CultureInfo.InvariantCulture);
            TimeZoneProvider tz = TimeZoneProvider.OfId(zone);
            csharp.Add("=" + zone + " " + t.ToString(CultureInfo.InvariantCulture) + ":"
                + tz.GetTimeZoneOffsetUtc(t).ToString(CultureInfo.InvariantCulture));
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// Noon UTC of the given proleptic Gregorian date, in EPOCH seconds. Howard Hinnant's
    /// days-from-civil, because DateTime cannot hold these years and both runtimes date with
    /// the same calendar.
    /// </summary>
    private static long Noon(int year, int month, int day)
    {
        int y = month <= 2 ? year - 1 : year;
        int era = (y >= 0 ? y : y - 399) / 400;
        int yoe = y - era * 400;
        int mp = (month + 9) % 12;
        int doy = (153 * mp + 2) / 5 + day - 1;
        int doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
        long days = (long)era * 146097 + doe - 719468;
        return days * 86_400 + 43_200;
    }

    /// <summary>
    /// A bare abbreviation is the one name the two runtimes resolve differently, and the
    /// difference is recorded here rather than left out of the oracle silently.
    /// </summary>
    /// <remarks>
    /// Java resolves <c>EST</c> through its short-identifier table to <c>America/Panama</c>,
    /// which is the IANA zone that has been at -05:00 without daylight saving for as long as
    /// the database remembers. .NET resolves it to a zone it calls <c>EST</c>. Both answer
    /// -18000 seconds for every question, and both say the zone is not a bare offset, so every
    /// offset agrees; only the name differs. Keeping the spelling the caller wrote is the
    /// better behaviour for a database — a name that comes back is a name that was written —
    /// and it is the reason <c>EST</c>, <c>MST</c> and <c>HST</c> are not in the compared
    /// corpus: they would fail on the name while agreeing on everything that matters.
    /// </remarks>
    [Fact]
    public void ABareAbbreviationKeepsTheNameItWasGiven()
    {
        TimeZoneProvider est = TimeZoneProvider.OfId("EST");

        Assert.Equal("EST", est.GetId());
        Assert.Equal(-18_000, est.GetTimeZoneOffsetUtc(Summer));
        Assert.Equal(-18_000, est.GetTimeZoneOffsetUtc(Winter));
        Assert.False(est.HasFixedOffset());
    }

    private static string Describe(string id)
    {
        try
        {
            TimeZoneProvider tz = TimeZoneProvider.OfId(id);
            long dateValue = DateTimeUtils.DateValueFromLocalSeconds(Summer);
            long timeNanos = DateTimeUtils.NanosFromLocalSeconds(Summer);
            return id
                + " utc:" + tz.GetTimeZoneOffsetUtc(Summer).ToString(CultureInfo.InvariantCulture)
                + " utc2:" + tz.GetTimeZoneOffsetUtc(Winter).ToString(CultureInfo.InvariantCulture)
                + " local:" + tz.GetTimeZoneOffsetLocal(dateValue, timeNanos).ToString(CultureInfo.InvariantCulture)
                + " epoch:" + tz.GetEpochSecondsFromLocal(dateValue, timeNanos).ToString(CultureInfo.InvariantCulture)
                + " fixed:" + (tz.HasFixedOffset() ? "true" : "false")
                + " id:" + tz.GetId();
        }
        catch (ArgumentException)
        {
            return id + " THROWS";
        }
    }

    private const string Body = """
                        for (int i = 0; i < ids.length; i++) {
                          org.h2.util.TimeZoneProvider tz;
                          try {
                            tz = org.h2.util.TimeZoneProvider.ofId(ids[i]);
                          } catch (RuntimeException e) {
                            System.out.println(ids[i] + " THROWS");
                            continue;
                          }
                          long dateValue =
                              org.h2.util.DateTimeUtils.dateValueFromLocalSeconds(SUMMER);
                          long timeNanos =
                              org.h2.util.DateTimeUtils.nanosFromLocalSeconds(SUMMER);
                          System.out.println(ids[i]
                              + " utc:" + tz.getTimeZoneOffsetUTC(SUMMER)
                              + " utc2:" + tz.getTimeZoneOffsetUTC(WINTER)
                              + " local:" + tz.getTimeZoneOffsetLocal(dateValue, timeNanos)
                              + " epoch:" + tz.getEpochSecondsFromLocal(dateValue, timeNanos)
                              + " fixed:" + tz.hasFixedOffset()
                              + " id:" + tz.getId());
                        }
              """;

    private const string Declare = """
                        long SUMMER = 1434379200L;
                        long WINTER = 1421323200L;
                        String[] ids = new String[] {
                            "UTC", "Z", "GMT", "GMT+0", "GMT+3", "GMT-5", "GMT+05:30", "GMT+05:30:15",
                            "UTC-08:00",
                            "Europe/Moscow", "Europe/London", "Europe/Belfast", "Europe/Kiev",
                            "Europe/Istanbul",
                            "America/New_York", "America/Los_Angeles", "America/Sao_Paulo",
                            "America/Argentina/Buenos_Aires", "America/Godthab",
                            "Asia/Tokyo", "Asia/Calcutta", "Asia/Katmandu", "Asia/Rangoon",
                            "Asia/Saigon", "Asia/Shanghai",
                            "Australia/Sydney", "Australia/ACT", "Australia/Lord_Howe",
                            "Pacific/Auckland", "Pacific/Chatham",
                            "Africa/Cairo",
                            "Israel", "Japan", "Iran", "Singapore"
                        };
              """;
}
