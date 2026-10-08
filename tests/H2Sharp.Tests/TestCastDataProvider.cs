/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A cast data provider for tests: everything it answers is fixed, so a conversion that reads
 * "now" gives the same answer on every run and on every machine.
 */
using H2Sharp.Engine;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Tests;

/// <summary>
/// A <see cref="CastDataProvider"/> whose answers are written down rather than asked of the
/// machine.
/// </summary>
/// <remarks>
/// The reference's contract is that the current timestamp is the same within a transaction or
/// within one command, and a test double that answered with the real clock would break that
/// contract by being different on every call. A fixed timestamp makes a conversion that reads
/// "now" testable at all, which is the whole reason to have this type.
/// </remarks>
internal sealed class TestCastDataProvider : CastDataProvider
{
    /// <summary>
    /// The date every answer of <see cref="CurrentTimestamp"/> is written on: 2017-06-15.
    /// </summary>
    /// <remarks>
    /// Written as a number rather than a constant because it is computed from the date, and
    /// because the year matters more than it looks: a date before the first transition a zone
    /// database records has no rules, and the two runtimes then answer differently for reasons
    /// that have nothing to do with the conversion under test.
    /// </remarks>
    public static readonly long FixedDateValue = DateTimeUtils.DateValue(2017, 6, 15);

    /// <summary>
    /// The time of day every answer of <see cref="CurrentTimestamp"/> has: noon.
    /// </summary>
    public const long FixedTimeNanos = DateTimeUtils.NanosPerHour * 12;

    private readonly TimeZoneProvider timeZone;

    private readonly ValueTimestampTimeZone now;

    /// <summary>
    /// Creates a provider for the regular mode and UTC.
    /// </summary>
    public TestCastDataProvider()
        : this(TimeZoneProvider.Utc)
    {
    }

    /// <summary>
    /// Creates a provider for the regular mode and the given zone.
    /// </summary>
    /// <param name="timeZone">the zone every answer of <see cref="CurrentTimeZone"/> is</param>
    public TestCastDataProvider(TimeZoneProvider timeZone)
    {
        this.timeZone = timeZone;

        // The offset of "now" is the zone's offset at that moment, and not a number written
        // down beside it. A conversion from a TIME reads that offset and prints it as part of
        // the answer, so a provider whose zone is Moscow and whose timestamp says UTC would
        // fail a differential test for a reason that has nothing to do with the conversion.
        now = ValueTimestampTimeZone.FromDateValueAndNanos(
            FixedDateValue, FixedTimeNanos, timeZone.GetTimeZoneOffsetLocal(FixedDateValue, FixedTimeNanos));
    }

    /// <inheritdoc/>
    public bool ZeroBasedEnums() => true;

    /// <inheritdoc/>
    public Mode GetMode() => Mode.GetRegular();

    /// <inheritdoc/>
    public ValueTimestampTimeZone CurrentTimestamp() => now;

    /// <inheritdoc/>
    public TimeZoneProvider CurrentTimeZone() => timeZone;
}
