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
