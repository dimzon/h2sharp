/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 279: TimeZoneProvider's id forms — a platform id, GMT/UTC offsets with both signs and
 * an unknown id, which the SQL suites reach only through SET TIME ZONE.
 */
using System;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class TimeZoneProviderSurfaceTests
{
    [Fact]
    public void TimeZoneIdsAndOffsetFormsAreResolved()
    {
        Assert.NotNull(TimeZoneProvider.OfId("UTC"));
        Assert.NotNull(TimeZoneProvider.OfId("GMT+05:30"));
        Assert.NotNull(TimeZoneProvider.OfId("GMT-05:00"));
        Assert.NotNull(TimeZoneProvider.OfId("UTC+01"));
        Assert.NotNull(TimeZoneProvider.GetDefault());
        Assert.NotNull(TimeZoneProvider.OfOffset(3 * 3600));
    }

    [Fact]
    public void AnUnknownTimeZoneIdIsRefused()
        => Assert.Throws<ArgumentException>(() => TimeZoneProvider.OfId("No/Such_Zone"));

    [Fact]
    public void JavaOnlyIdsAndOffsetSpellingAreResolved()
    {
        Assert.NotNull(TimeZoneProvider.FindZone("Asia/Calcutta"));
        Assert.NotNull(TimeZoneProvider.FindZone("UTC+05:30"));
        Assert.NotNull(TimeZoneProvider.FindZone("GMT-05:00"));
        Assert.Throws<ArgumentException>(() => TimeZoneProvider.FindZone("No/Such_Zone"));
    }

    [Fact]
    public void OffsetsAreParsedWithBothSignsAndOptionalMinutes()
    {
        Assert.Equal(new TimeSpan(5, 30, 0), TimeZoneProvider.ParseOffset("05:30"));
        Assert.Equal(TimeSpan.FromHours(5), TimeZoneProvider.ParseOffset("05:00"));
        Assert.Equal(TimeSpan.FromHours(2), TimeZoneProvider.ParseOffset("02"));
    }
}
