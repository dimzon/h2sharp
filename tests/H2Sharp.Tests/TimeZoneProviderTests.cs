/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 239: the timezone identifiers the reference accepts besides the platform's own list —
 * Java's UTC/GMT offset spellings and the names it maps to framework identifiers.
 */
using System;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class TimeZoneProviderTests
{
    [Theory]
    [InlineData("Z", 0)]
    [InlineData("UTC", 0)]
    [InlineData("UTC+03:00", 3 * 3600)]
    [InlineData("GMT-05", -5 * 3600)]
    [InlineData("UTC+01:00", 3600)]
    [InlineData("GMT-02:30", -2 * 3600 - 1800)]
    public void OffsetSpellingsAnswerTheSameOffset(string id, int expected)
    {
        TimeZoneProvider zone = TimeZoneProvider.OfId(id);
        Assert.Equal(expected, zone.GetTimeZoneOffsetUtc(0));
    }

    [Fact]
    public void UnknownIdentifiersAreRefused()
        => Assert.Throws<ArgumentException>(() => TimeZoneProvider.OfId("No/Such_Zone"));
}
