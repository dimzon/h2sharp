/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.util.TimeZoneProvider.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: a named zone is a TimeZoneInfo rather than a java.time.ZoneId. The two agree on
 * what a zone is and disagree on the spelling of a few identifiers, which is why OfId falls
 * back through a table of the Java-only spellings before it gives up.
 */
using System;
using System.Collections.Concurrent;
using System.Globalization;

namespace H2Sharp.Util;

/// <summary>
/// A time zone: either a fixed offset from UTC, or a named zone with its rules.
/// </summary>
/// <remarks>
/// There are two kinds and the difference is not an optimisation. A fixed offset is one
/// number, so every question about it has the same answer forever. A named zone has rules that
/// depend on the moment being asked about, and the two ways of asking differ:
/// <see cref="GetTimeZoneOffsetUtc"/> asks about an instant and <see cref="GetTimeZoneOffsetLocal"/>
/// asks about a wall-clock reading, which around a daylight-saving change are two different
/// questions with two different answers.
/// </remarks>
public abstract class TimeZoneProvider
{
    /// <summary>
    /// The provider for UTC.
    /// </summary>
    public static readonly TimeZoneProvider Utc = new Simple(0);

    private static volatile ConcurrentDictionary<string, TimeZoneProvider>? cache;

    /// <summary>
    /// Returns a provider with the specified offset in seconds.
    /// </summary>
    /// <param name="offset">the offset in seconds</param>
    /// <returns>the provider</returns>
    /// <exception cref="ArgumentOutOfRangeException">if the offset is out of range</exception>
    public static TimeZoneProvider OfOffset(int offset)
    {
        if (offset == 0)
        {
            return Utc;
        }

        if (offset < -(18 * 60 * 60) || offset > (18 * 60 * 60))
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset), offset, "Time zone offset " + offset + " seconds is out of range");
        }

        return new Simple(offset);
    }

    /// <summary>
    /// Returns the provider with the specified name.
    /// </summary>
    /// <param name="id">the ID of the time zone</param>
    /// <returns>the provider with the specified name</returns>
    /// <exception cref="ArgumentException">if the ID is not a known time zone</exception>
    public static TimeZoneProvider OfId(string id)
    {
        int length = id.Length;
        if (length == 1 && id[0] == 'Z')
        {
            return Utc;
        }

        int index = 0;
        if (id.StartsWith("GMT", StringComparison.Ordinal) || id.StartsWith("UTC", StringComparison.Ordinal))
        {
            if (length == 3)
            {
                return Utc;
            }

            index = 3;
        }

        if (length > index)
        {
            bool negative = false;
            char c = id[index];
            if (length > index + 1)
            {
                if (c == '+')
                {
                    c = id[++index];
                }
                else if (c == '-')
                {
                    negative = true;
                    c = id[++index];
                }
            }

            if (index != 3 && c is >= '0' and <= '9')
            {
                int hour = c - '0';
                if (++index < length)
                {
                    c = id[index];
                    if (c is >= '0' and <= '9')
                    {
                        hour = (hour * 10) + c - '0';
                        index++;
                    }
                }

                if (index == length)
                {
                    int offset = hour * 3_600;
                    return OfOffset(negative ? -offset : offset);
                }

                if (id[index] == ':')
                {
                    if (++index < length)
                    {
                        c = id[index];
                        if (c is >= '0' and <= '9')
                        {
                            int minute = c - '0';
                            if (++index < length)
                            {
                                c = id[index];
                                if (c is >= '0' and <= '9')
                                {
                                    minute = (minute * 10) + c - '0';
                                    index++;
                                }
                            }

                            if (index == length)
                            {
                                int offset = ((hour * 60) + minute) * 60;
                                return OfOffset(negative ? -offset : offset);
                            }

                            if (id[index] == ':')
                            {
                                if (++index < length)
                                {
                                    c = id[index];
                                    if (c is >= '0' and <= '9')
                                    {
                                        int second = c - '0';
                                        if (++index < length)
                                        {
                                            c = id[index];
                                            if (c is >= '0' and <= '9')
                                            {
                                                second = (second * 10) + c - '0';
                                                index++;
                                            }
                                        }

                                        if (index == length)
                                        {
                                            int offset = (((hour * 60) + minute) * 60) + second;
                                            return OfOffset(negative ? -offset : offset);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (index > 0)
            {
                throw new ArgumentException(id, nameof(id));
            }
        }

        // The name is cached because a database asks for the same zone once per statement and
        // a named zone is the expensive one to build.
        ConcurrentDictionary<string, TimeZoneProvider>? cached = cache;
        if (cached is not null && cached.TryGetValue(id, out TimeZoneProvider? found))
        {
            return found;
        }

        TimeZoneProvider provider = new WithTimeZone(FindZone(id));
        cache ??= new ConcurrentDictionary<string, TimeZoneProvider>(StringComparer.Ordinal);
        cache[id] = provider;
        return provider;
    }

    /// <summary>
    /// The zone .NET knows under some spelling of this identifier.
    /// </summary>
    /// <remarks>
    /// Java and .NET carry the same IANA database but not the same identifiers: Java accepts
    /// <c>Europe/Moscow</c> and also <c>Europe/Moscow</c> as an alias where .NET wants
    /// <c>Europe/Moscow</c>, and the two disagree outright on such names as
    /// <c>Asia/Calcutta</c>. Each step here is one of those disagreements, and asking the
    /// framework first and then this table is what makes an identifier written for H2 work.
    /// </remarks>
    /// <param name="id">the identifier as written</param>
    /// <returns>the zone</returns>
    /// <exception cref="ArgumentException">if no spelling of the identifier is known</exception>
    private static TimeZoneInfo FindZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            // Fall through to the Java-only spellings below.
        }
        catch (InvalidTimeZoneException)
        {
            throw new ArgumentException(id, nameof(id));
        }

        foreach ((string java, string framework) in JavaOnlyIds)
        {
            if (string.Equals(java, id, StringComparison.Ordinal))
            {
                return TimeZoneInfo.FindSystemTimeZoneById(framework);
            }
        }

        // "UTC+HH:MM" and "GMT+HH:MM" are written as offsets by .NET, and a fixed offset is
        // exactly what such a name means, so it is answered as one rather than refused.
        if (id.StartsWith("UTC+", StringComparison.Ordinal) || id.StartsWith("GMT+", StringComparison.Ordinal))
        {
            return CreateCustomZone(id, ParseOffset(id.Substring(3)));
        }

        if (id.StartsWith("UTC-", StringComparison.Ordinal) || id.StartsWith("GMT-", StringComparison.Ordinal))
        {
            return CreateCustomZone(id, -ParseOffset(id.Substring(3)));
        }

        throw new ArgumentException(id, nameof(id));
    }

    /// <summary>
    /// Parses an "+HH:MM" or "+HHMM" offset into seconds.
    /// </summary>
    /// <param name="text">the text after the sign</param>
    /// <returns>the offset in seconds</returns>
    private static TimeSpan ParseOffset(string text)
    {
        int sign = text[0] == '-' ? -1 : 1;
        string digits = text.Replace(":", string.Empty, StringComparison.Ordinal);
        int hours = int.Parse(digits[..2], CultureInfo.InvariantCulture);
        int minutes = digits.Length >= 4
            ? int.Parse(digits[2..4], CultureInfo.InvariantCulture)
            : 0;
        return TimeSpan.FromSeconds(sign * ((hours * 3_600) + (minutes * 60)));
    }

    /// <summary>
    /// Identifiers Java accepts and .NET does not, or that the two spell differently. Only
    /// names where the *same* zone is meant are here; a name that means a different zone in one
    /// of them is not, and must not be.
    /// </summary>
    private static readonly (string Java, string Framework)[] JavaOnlyIds =
    [
        ("Asia/Calcutta", "Asia/Kolkata"),
        ("Asia/Katmandu", "Asia/Kathmandu"),
        ("Asia/Rangoon", "Asia/Yangon"),
        ("Asia/Saigon", "Asia/Ho_Chi_Minh"),
        ("America/Buenos_Aires", "America/Argentina/Buenos_Aires"),
        ("America/Godthab", "America/Nuuk"),
        ("Atlantic/Faeroe", "Atlantic/Faroe"),
        ("Australia/ACT", "Australia/Sydney"),
        ("Australia/Canberra", "Australia/Sydney"),
        ("Australia/LHI", "Australia/Lord_Howe"),
        ("Australia/NSW", "Australia/Sydney"),
        ("Australia/Queensland", "Australia/Brisbane"),
        ("Australia/South", "Australia/Adelaide"),
        ("Australia/Victoria", "Australia/Melbourne"),
        ("Australia/West", "Australia/Perth"),
        ("Europe/Belfast", "Europe/London"),
        ("Europe/Kiev", "Europe/Kyiv"),
        ("Europe/Nicosia", "Asia/Nicosia"),
        ("Europe/Tiraspol", "Europe/Chisinau"),
        ("Europe/Uzhgorod", "Europe/Kyiv"),
        ("Europe/Zaporozhye", "Europe/Kyiv"),
        ("Pacific/Ponape", "Pacific/Pohnpei"),
        ("Pacific/Truk", "Pacific/Chuuk"),
        ("Pacific/Samoa", "Pacific/Pago_Pago"),
        ("Pacific/Tongatapu", "Pacific/Tongatapu"),
        ("Pacific/Yap", "Pacific/Chuuk"),
        ("ROC", "Asia/Taipei"),
        ("ROK", "Asia/Seoul"),
        ("PRC", "Asia/Shanghai"),
        ("Japan", "Asia/Tokyo"),
        ("Israel", "Asia/Jerusalem"),
        ("Iran", "Asia/Tehran"),
        ("Egypt", "Africa/Cairo"),
        ("Cuba", "America/Havana"),
        ("Poland", "Europe/Warsaw"),
        ("Portugal", "Europe/Lisbon"),
        ("Singapore", "Asia/Singapore"),
        ("Hongkong", "Asia/Hong_Kong"),
        ("Iceland", "Atlantic/Reykjavik"),
        ("Turkey", "Europe/Istanbul"),
        ("Navajo", "America/Denver"),
        ("EST", "America/New_York"),
        ("MST", "America/Phoenix"),
        ("HST", "Pacific/Honolulu"),
        ("ACT", "Australia/Darwin"),
    ];

    /// <summary>
    /// Returns the provider for the system default time zone.
    /// </summary>
    /// <returns>the provider for the system default time zone</returns>
    public static TimeZoneProvider GetDefault()
    {
        TimeZoneInfo zone = TimeZoneInfo.Local;
        // A local zone that is UTC is answered as UTC, so that a machine set to UTC and a
        // machine set to Etc/UTC do not produce two different providers for one answer.
        return zone.BaseUtcOffset == TimeSpan.Zero && zone.SupportsDaylightSavingTime
            ? new WithTimeZone(zone)
            : zone.BaseUtcOffset == TimeSpan.Zero
                ? Utc
                : new WithTimeZone(zone);
    }

    /// <summary>
    /// A zone that is one offset and no rules, under whatever name it was asked for.
    /// </summary>
    /// <param name="id">the name to give it</param>
    /// <param name="offset">the offset from UTC</param>
    /// <returns>the zone</returns>
    private static TimeZoneInfo CreateCustomZone(string id, TimeSpan offset)
        => TimeZoneInfo.CreateCustomTimeZone(id, offset, id, id);

    /// <summary>
    /// Calculates the time zone offset in seconds for the specified EPOCH seconds.
    /// </summary>
    /// <param name="epochSeconds">seconds since EPOCH</param>
    /// <returns>time zone offset in seconds</returns>
    public abstract int GetTimeZoneOffsetUtc(long epochSeconds);

    /// <summary>
    /// Calculates the time zone offset in seconds for the specified date value and nanoseconds
    /// since midnight in local time.
    /// </summary>
    /// <param name="dateValue">date value</param>
    /// <param name="timeNanos">nanoseconds since midnight</param>
    /// <returns>time zone offset in seconds</returns>
    public abstract int GetTimeZoneOffsetLocal(long dateValue, long timeNanos);

    /// <summary>
    /// Calculates the EPOCH seconds from a local date and time.
    /// </summary>
    /// <param name="dateValue">date value</param>
    /// <param name="timeNanos">nanoseconds since midnight</param>
    /// <returns>the EPOCH seconds value</returns>
    public abstract long GetEpochSecondsFromLocal(long dateValue, long timeNanos);

    /// <summary>
    /// Returns the ID of the time zone.
    /// </summary>
    /// <returns>the ID of the time zone</returns>
    public abstract string GetId();

    /// <summary>
    /// Get the standard time name or daylight saving time name of the time zone.
    /// </summary>
    /// <param name="epochSeconds">seconds since EPOCH</param>
    /// <returns>the standard time name or daylight saving time name of the time zone</returns>
    public abstract string GetShortId(long epochSeconds);

    /// <summary>
    /// Whether this is a simple time zone provider with a fixed offset from UTC.
    /// </summary>
    /// <returns>whether this is a simple time zone provider with a fixed offset from UTC</returns>
    public virtual bool HasFixedOffset() => false;

    /// <summary>
    /// A time zone that is one offset from UTC and nothing else.
    /// </summary>
    private sealed class Simple : TimeZoneProvider
    {
        private readonly int offset;

        private string? id;

        internal Simple(int offset)
        {
            this.offset = offset;
        }

        /// <inheritdoc/>
        public override int GetHashCode() => offset + 129607;

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            return obj is Simple other && offset == other.offset;
        }

        /// <inheritdoc/>
        public override int GetTimeZoneOffsetUtc(long epochSeconds) => offset;

        /// <inheritdoc/>
        public override int GetTimeZoneOffsetLocal(long dateValue, long timeNanos) => offset;

        /// <inheritdoc/>
        public override long GetEpochSecondsFromLocal(long dateValue, long timeNanos)
            => DateTimeUtils.GetEpochSeconds(dateValue, timeNanos, offset);

        /// <inheritdoc/>
        public override string GetId() => id ??= DateTimeUtils.TimeZoneNameFromOffsetSeconds(offset);

        /// <inheritdoc/>
        public override string GetShortId(long epochSeconds) => GetId();

        /// <inheritdoc/>
        public override bool HasFixedOffset() => true;

        /// <inheritdoc/>
        public override string ToString() => "TimeZoneProvider " + GetId();
    }

    /// <summary>
    /// A time zone named by the IANA database, with the rules that go with the name.
    /// </summary>
    private sealed class WithTimeZone : TimeZoneProvider
    {
        /// <summary>
        /// The number of seconds in 400 years.
        /// </summary>
        private const long SecondsPerPeriod = 146_097L * 60 * 60 * 24;

        /// <summary>
        /// The number of seconds per year.
        /// </summary>
        private const long SecondsPerYear = SecondsPerPeriod / 400;

        /// <summary>
        /// The widest EPOCH seconds a <see cref="DateTimeOffset"/> can be asked about, which is
        /// year 9999 rather than the billion years H2 allows.
        /// </summary>
        private static long MaxUnixSeconds = 253_402_300_799L;

        /// <summary>
        /// The narrowest, which is year 1.
        /// </summary>
        private static long MinUnixSeconds = -62_167_219_200L;

        private readonly TimeZoneInfo zoneId;

        internal WithTimeZone(TimeZoneInfo timeZone)
        {
            zoneId = timeZone;
        }

        /// <inheritdoc/>
        public override int GetHashCode() => zoneId.GetHashCode() + 951689;

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            return obj is WithTimeZone other && zoneId.Id == other.zoneId.Id;
        }

        /// <inheritdoc/>
        public override int GetTimeZoneOffsetUtc(long epochSeconds)
            => (int)zoneId.GetUtcOffset(EpochSecondsToDateTimeOffset(epochSeconds)).TotalSeconds;

        /// <inheritdoc/>
        public override int GetTimeZoneOffsetLocal(long dateValue, long timeNanos)
        {
            DateTime local = LocalOf(dateValue, timeNanos, YearForCalendar(
                DateTimeUtils.YearFromDateValue(dateValue)));
            return (int)zoneId.GetUtcOffset(local).TotalSeconds;
        }

        /// <inheritdoc/>
        public override long GetEpochSecondsFromLocal(long dateValue, long timeNanos)
        {
            int year = DateTimeUtils.YearFromDateValue(dateValue);
            int yearForCalendar = YearForCalendar(year);
            DateTime local = LocalOf(dateValue, timeNanos, yearForCalendar);
            long epoch = new DateTimeOffset(local, zoneId.GetUtcOffset(local)).ToUnixTimeSeconds();

            // The year was folded into a range a calendar can hold, so it is put back: the
            // answer is the local reading of a year the calendar could not name.
            return epoch + ((year - yearForCalendar) * SecondsPerYear);
        }

        /// <inheritdoc/>
        public override string GetId() => zoneId.Id;

        /// <inheritdoc/>
        public override string GetShortId(long epochSeconds)
            => zoneId.IsDaylightSavingTime(EpochSecondsToDateTimeOffset(epochSeconds))
                ? zoneId.DaylightName
                : zoneId.StandardName;

        /// <inheritdoc/>
        public override string ToString() => "TimeZoneProvider " + zoneId.Id;

        /// <summary>
        /// Returns a year within the range -999,999,999..999,999,999 for the given year.
        /// </summary>
        /// <remarks>
        /// A <c>DateTime</c> cannot hold a year outside 1..9999, and the reference's
        /// <c>LocalDateTime</c> cannot hold one outside a billion either, but it needs the
        /// larger range for its own arithmetic. The fold is the same one the reference uses,
        /// and it is safe here for the same stated reason: the year is needed only to look up a
        /// zone offset, and the rules repeat every 400 years.
        /// </remarks>
        /// <param name="year">the year</param>
        /// <returns>the year, or a replacement within the range</returns>
        private static int YearForCalendar(int year)
        {
            if (year > 999_999_999)
            {
                year -= 400;
            }
            else if (year < -999_999_999)
            {
                year += 400;
            }

            return Math.Clamp(year, 1, 9999);
        }

        /// <summary>
        /// The wall-clock reading that a date value and a time of day name.
        /// </summary>
        /// <param name="dateValue">the date value</param>
        /// <param name="timeNanos">nanoseconds since midnight</param>
        /// <param name="year">the year to use, which may be a folded one</param>
        /// <returns>the reading, without a zone</returns>
        private static DateTime LocalOf(long dateValue, long timeNanos, int year)
        {
            int second = (int)(timeNanos / DateTimeUtils.NanosPerSecond);
            int minute = second / 60;
            second -= minute * 60;
            int hour = minute / 60;
            minute -= hour * 60;
            return new DateTime(
                year,
                DateTimeUtils.MonthFromDateValue(dateValue),
                DateTimeUtils.DayFromDateValue(dateValue),
                hour,
                minute,
                second,
                DateTimeKind.Unspecified);
        }

        /// <summary>
        /// EPOCH seconds as an instant the framework can be asked about.
        /// </summary>
        /// <remarks>
        /// A <see cref="DateTimeOffset"/> holds years 1 to 9999, and H2's dates run to a
        /// billion years, so an instant outside that range is folded in by the 400-year period
        /// of the Gregorian calendar until it fits. The reference folds once and can stop
        /// there, because a <c>java.time.Instant</c> reaches a billion years; this one loops,
        /// and loops many times for the largest dates. The rules of every real zone repeat
        /// inside 400 years, so the answer is the one the reference gives — for a zone with
        /// rules at all, which is every zone but a bare offset.
        /// </remarks>
        /// <param name="epochSeconds">seconds since EPOCH</param>
        /// <returns>the instant</returns>
        private static DateTimeOffset EpochSecondsToDateTimeOffset(long epochSeconds)
        {
            DateTimeOffset folded = DateTimeOffset.FromUnixTimeSeconds(0);
            // Closed form of the loop: the folded value sits in (Max - period, Max] with the
            // input's residue, which is exactly where repeated subtraction stops. The remainder
            // form cannot overflow for any input, where a count-then-multiply could.
            if (epochSeconds > MaxUnixSeconds)
            {
                long excess = (epochSeconds - MaxUnixSeconds) % SecondsPerPeriod;
                epochSeconds = excess == 0
                    ? MaxUnixSeconds
                    : MaxUnixSeconds - SecondsPerPeriod + excess;
            }

            if (epochSeconds < MinUnixSeconds)
            {
                long shortage = (MinUnixSeconds - epochSeconds) % SecondsPerPeriod;
                epochSeconds = shortage == 0
                    ? MinUnixSeconds
                    : MinUnixSeconds + SecondsPerPeriod - shortage;
            }

            return folded.AddSeconds(epochSeconds);
        }
    }
}
