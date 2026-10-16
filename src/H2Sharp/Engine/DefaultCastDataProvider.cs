/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * The cast data provider engine sessions run on.
 *
 * The reference answers these questions on the session itself, with the machine behind
 * them: the mode is the database's, the timestamp is now, the zone is the system's. Tests
 * override it with a fixed double because a clock that moves breaks differential tests,
 * and that is the only reason the session carries the provider as a member rather than
 * answering directly — production code never sets it.
 */
using System;

namespace H2Sharp.Engine;

/// <summary>
/// The cast data provider of a session that nobody overrode: regular mode, the real
/// clock, and the system zone.
/// </summary>
internal sealed class DefaultCastDataProvider : CastDataProvider
{
    /// <summary>
    /// The single instance: it holds no state, and one per session would only be garbage.
    /// </summary>
    internal static readonly DefaultCastDataProvider Instance = new();

    private DefaultCastDataProvider()
    {
    }

    /// <inheritdoc/>
    public Values.ValueTimestampTimeZone CurrentTimestamp()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        return Values.ValueTimestampTimeZone.FromDateValueAndNanos(
            Util.DateTimeUtils.DateValue(now.Year, now.Month, now.Day),
            now.TimeOfDay.Ticks * 100L,
            (int)now.Offset.TotalSeconds);
    }

    /// <inheritdoc/>
    public Util.TimeZoneProvider CurrentTimeZone() => Util.TimeZoneProvider.GetDefault();

    /// <inheritdoc/>
    public bool ZeroBasedEnums() => true;

    /// <inheritdoc/>
    public Mode GetMode() => Mode.GetRegular();
}
