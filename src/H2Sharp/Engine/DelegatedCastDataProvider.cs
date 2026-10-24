/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * The cast data provider that reads the time from a delegate.
 *
 * The reference answers the question "what time is it" on the session itself, with the
 * machine behind it. A test that compares the port with the reference needs both sides to
 * answer with the same instant, so the port's session takes a provider, and this one asks a
 * delegate instead of the machine. Everything else the provider answers is the same as the
 * default provider answers.
 */
using System;

namespace H2Sharp.Engine;

/// <summary>
/// The cast data provider of a session whose clock a test holds still.
/// </summary>
internal sealed class DelegatedCastDataProvider : CastDataProvider
{
    private readonly Func<DateTimeOffset>? clock;

    private readonly Func<Util.TimeZoneProvider>? timeZoneProvider;

    /// <summary>
    /// Creates a provider that reads the time from the given clock.
    /// </summary>
    /// <param name="clock">the clock</param>
    internal DelegatedCastDataProvider(Func<DateTimeOffset> clock)
        : this(clock, null)
    {
    }

    /// <summary>
    /// Creates a provider that reads the time from the given clock and the default zone
    /// from the given provider, either of which may be absent.
    /// </summary>
    /// <param name="clock">the clock, or null for the machine's</param>
    /// <param name="timeZoneProvider">the zone provider, or null for the machine's</param>
    internal DelegatedCastDataProvider(Func<DateTimeOffset>? clock,
        Func<Util.TimeZoneProvider>? timeZoneProvider)
    {
        this.clock = clock;
        this.timeZoneProvider = timeZoneProvider;
    }

    /// <inheritdoc/>
    public Values.ValueTimestampTimeZone CurrentTimestamp()
    {
        DateTimeOffset now = clock is not null ? clock() : DateTimeOffset.Now;
        return Values.ValueTimestampTimeZone.FromDateValueAndNanos(
            Util.DateTimeUtils.DateValue(now.Year, now.Month, now.Day),
            now.TimeOfDay.Ticks * 100L,
            (int)now.Offset.TotalSeconds);
    }

    /// <inheritdoc/>
    public Util.TimeZoneProvider CurrentTimeZone() => timeZoneProvider is not null
        ? timeZoneProvider()
        : DefaultCastDataProvider.Instance.CurrentTimeZone();

    /// <inheritdoc/>
    public bool ZeroBasedEnums() => DefaultCastDataProvider.Instance.ZeroBasedEnums();

    /// <inheritdoc/>
    public Mode GetMode() => DefaultCastDataProvider.Instance.GetMode();
}
