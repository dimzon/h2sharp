/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The CLR surface of the time zone provider, from the coverage data (uncovered.py): the
 * offset factory with its bounds, the offset spelling with and without minutes and seconds,
 * the refusal of an unknown identifier, and the equality and hash of a fixed-offset zone.
 */
using System;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class TimeZoneProviderPortTests
{
    [Fact]
    public void OfOffsetBoundsAndIdentity()
    {
        Assert.Same(TimeZoneProvider.Utc, TimeZoneProvider.OfOffset(0));
        TimeZoneProvider plus = TimeZoneProvider.OfOffset(3_600);
        Assert.Equal(3_600, plus.GetTimeZoneOffsetUtc(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TimeZoneProvider.OfOffset(19 * 3_600));
        Assert.Throws<ArgumentOutOfRangeException>(() => TimeZoneProvider.OfOffset(-(19 * 3_600)));
    }

    [Fact]
    public void OfIdOffsetsAndCustomZones()
    {
        // A bare Z and UTC are UTC.
        Assert.Same(TimeZoneProvider.Utc, TimeZoneProvider.OfId("Z"));
        Assert.Same(TimeZoneProvider.Utc, TimeZoneProvider.OfId("UTC"));
        // Hour only, hour and minute, hour minute and second.
        Assert.Equal(5 * 3_600, TimeZoneProvider.OfId("UTC+5").GetTimeZoneOffsetUtc(0));
        Assert.Equal(5 * 3_600 + 30 * 60, TimeZoneProvider.OfId("UTC+05:30").GetTimeZoneOffsetUtc(0));
        Assert.Equal(-(3 * 3_600 + 30 * 60 + 30), TimeZoneProvider.OfId("GMT-03:30:30").GetTimeZoneOffsetUtc(0));
        Assert.Equal("GMT+05:30", TimeZoneProvider.OfId("UTC+05:30").GetId());
    }

    [Fact]
    public void OfIdRefusesUnknown()
    {
        Assert.Throws<ArgumentException>(() => TimeZoneProvider.OfId("no/such/zone"));
        Assert.Throws<ArgumentException>(() => TimeZoneProvider.OfId(string.Empty));
    }

    [Fact]
    public void FixedOffsetZonesCompareByOffset()
    {
        TimeZoneProvider a = TimeZoneProvider.OfOffset(3_600);
        TimeZoneProvider b = TimeZoneProvider.OfOffset(3_600);
        TimeZoneProvider c = TimeZoneProvider.OfOffset(7_200);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void EdgePathsAnswerThePort()
    {
        // Simple (fixed offset): GetShortId, ToString, Equals same instance, GetHashCode.
        TimeZoneProvider fixed1 = TimeZoneProvider.OfOffset(3_600);
        Assert.Equal(fixed1.GetId(), fixed1.GetShortId(0));
        Assert.Equal("TimeZoneProvider " + fixed1.GetId(), fixed1.ToString());
        Assert.True(fixed1.Equals(fixed1));
        _ = fixed1.GetHashCode();

        // WithTimeZone: use a zone that resolves on this machine.
        TimeZoneProvider? zone = null;
        foreach (string candidate in new[] { "Asia/Calcutta", "Asia/Kolkata", "America/New_York", "Europe/Berlin" })
        {
            try { zone = TimeZoneProvider.OfId(candidate); break; } catch { }
        }

        if (zone is not null)
        {
            string id = zone.GetId();
            Assert.False(string.IsNullOrEmpty(zone.GetShortId(0)));
            Assert.Equal("TimeZoneProvider " + id, zone.ToString());
            Assert.True(zone.Equals(zone));
            _ = zone.GetHashCode();
            string shortId = zone.GetShortId(1720000000L);
            Assert.False(string.IsNullOrEmpty(shortId));
            long off = zone.GetTimeZoneOffsetUtc(-100_000_000_000L);
            Assert.NotEqual(0, off);
        }
    }
}
