// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.unit.TestDateTimeTemplate.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified: the reference's main() entry is the runner; xUnit runs Test() directly. The
// Provider is a CastDataProvider answering only the current timestamp (the port's
// interface members that the reference left null are answered with nulls the same way).
// The assertEquals(expected, value, template, provider) helper of the reference becomes
// AssertFormatted.
using H2Sharp.Engine;
using H2Sharp.Util;
using H2Sharp.Message;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Native.Tests.Unit;

/// <summary>
/// Test cases for DateTimeTemplate.
/// </summary>
public sealed class TestDateTimeTemplate
{
    private static void AssertFormatted(string? expected, Value value, string template,
        CastDataProvider provider)
    {
        Assert.Equal(expected, DateTimeTemplate.Of(template).Format(value));
    }

    private static void AssertFormatted(string? expected, Value value, string template,
        CastDataProvider provider, Value expectedValue)
    {
        AssertFormatted(expected, value, template, provider);
        Assert.Equal(expectedValue, DateTimeTemplate.Of(template).Parse(
            expected!, value.GetTypeInfo(), provider));
    }

    private static void AssertFormattedAndFail(Value value, string template,
        CastDataProvider provider)
    {
        Assert.Throws<H2SharpException>(() => DateTimeTemplate.Of(template).Format(value));
    }

    private static ValueDate Date(int year, int month, int day)
        => ValueDate.FromDateValue(DateTimeUtils.DateValue(year, month, day));

    private static ValueTime Time(int hour, int minute, int second, int nanos)
        => ValueTime.FromNanos((((hour * 60L) + minute) * 60 + second) * 1_000_000_000L
            + nanos);

    private static ValueTimestampTimeZone TimestampTimeZone(int year, int month, int day,
        int hour, int minute, int second, int nanos, int offsetMinutes)
        => ValueTimestampTimeZone.FromDateValueAndNanos(
            DateTimeUtils.DateValue(year, month, day),
            (((hour * 60L) + minute) * 60 + second) * 1_000_000_000L + nanos,
            offsetMinutes * 60);

    /// <summary>
    /// The reference's Provider: a cast data provider with a fixed current timestamp.
    /// </summary>
    private sealed class Provider : CastDataProvider
    {
        private readonly ValueTimestampTimeZone currentTimestamp;

        /// <summary>
        /// Builds the provider with the given current month.
        /// </summary>
        /// <param name="year">the current year</param>
        /// <param name="month">the current month</param>
        public Provider(int year, int month)
        {
            currentTimestamp = ValueTimestampTimeZone.FromDateValueAndNanos(
                DateTimeUtils.DateValue(year, month, 15), 1_234_567_890_123L, -12233);
        }

        /// <inheritdoc/>
        public ValueTimestampTimeZone CurrentTimestamp() => currentTimestamp;

        /// <inheritdoc/>
        public Util.TimeZoneProvider CurrentTimeZone() => null!;

        /// <inheritdoc/>
        public Mode GetMode() => null!;

        /// <inheritdoc/>
        public bool ZeroBasedEnums() => false;
    }

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public void Test()
    {
        TestDate();
        TestTime();
        TestTimestamp();
        TestOutOfRange();
    }

    private static void TestDate()
    {
        Provider provider = new(2023, 4);

        ValueDate date = Date(2022, 10, 12);
        AssertFormatted("2022-10-12", date, "YYYY-MM-DD", provider);
        AssertFormatted("022-10-12", date, "YYY-MM-DD", provider);
        AssertFormatted("22-10-12", date, "YY-MM-DD", provider);
        AssertFormatted("2-10-12", date, "Y-MM-DD", provider);
        AssertFormatted("2022-10-12", date, "RRRR-MM-DD", provider);
        AssertFormatted("22-10-12", date, "RR-MM-DD", provider);

        AssertFormatted("2022-12", Date(2022, 4, 12), "YYYY-DD", provider);
        AssertFormatted("2022-10", Date(2022, 10, 1), "YYYY-MM", provider);
        AssertFormatted("12-10", Date(2023, 10, 12), "DD-MM", provider);

        AssertFormatted("22-10-12", date, "RR-MM-DD", provider);
        AssertFormatted("73-01-01", Date(2073, 1, 1), "RR-MM-DD", provider);
        AssertFormatted("74-01-01", Date(1974, 1, 1), "RR-MM-DD", provider,
            Date(2074, 1, 1));
        AssertFormatted("73-01-01", Date(2073, 1, 1), "RR-MM-DD", provider,
            Date(1973, 1, 1));
        Provider altProvider = new(2090, 1);
        AssertFormatted("40-01-01", Date(2040, 1, 1), "RR-MM-DD", provider,
            Date(2040, 1, 1));
        AssertFormatted("40-01-01", Date(2140, 1, 1), "RR-MM-DD", altProvider,
            Date(2040, 1, 1));

        date = Date(12345, 5, 7);
        AssertFormatted("12345-05-07", date, "YYYY-MM-DD", provider);
        AssertFormatted("345-05-07", Date(2345, 5, 7), "YYY-MM-DD", provider);
        AssertFormatted("45-05-07", Date(2045, 5, 7), "YY-MM-DD", provider);
        AssertFormatted("5-05-07", Date(2025, 5, 7), "Y-MM-DD", provider);
        AssertFormatted("12345-05-07", date, "RRRR-MM-DD", provider);
        AssertFormatted("45-05-07", Date(2045, 5, 7), "RR-MM-DD", provider);

        date = Date(-12345, 5, 7);
        AssertFormatted("-12345-05-07", date, "YYYY-MM-DD", provider);
        AssertFormattedAndFail(date, "YYY-MM-DD", provider);
        AssertFormattedAndFail(date, "YY-MM-DD", provider);
        AssertFormattedAndFail(date, "Y-MM-DD", provider);
        AssertFormattedAndFail(date, "RRRR-MM-DD", provider);
        AssertFormattedAndFail(date, "RR-MM-DD", provider);

        AssertFormatted("1900-061", Date(1900, 3, 2), "YYYY-DDD", provider);
        AssertFormatted("1904-062", Date(1904, 3, 2), "YYYY-DDD", provider);
        AssertFormatted("2000-062", Date(2000, 3, 2), "YYYY-DDD", provider);
    }

    private static void TestTime()
    {
        Provider provider = new(2023, 4);

        AssertFormatted("12 A.M.", Time(0, 0, 0, 0), "HH A.M.", provider);
        AssertFormatted("01 A.M.", Time(1, 0, 0, 0), "HH A.M.", provider);
        AssertFormatted("12 P.M.", Time(12, 0, 0, 0), "HH A.M.", provider);
        AssertFormatted("01 P.M.", Time(13, 0, 0, 0), "HH A.M.", provider);
        AssertFormatted("11 P.M.", Time(23, 0, 0, 0), "HH A.M.", provider);
        AssertFormatted("00", Time(0, 0, 0, 0), "HH24", provider);
        AssertFormatted("23", Time(23, 0, 0, 0), "HH24", provider);
        AssertFormatted("10:20:30", Time(10, 20, 30, 0), "HH24:MI:SS", provider);
        AssertFormatted("10:20:30.123456789",
            Time(10, 20, 30, 123456789), "HH24:MI:SS.FF9", provider);
        AssertFormatted("10:20:30.123",
            Time(10, 20, 30, 123456789), "HH24:MI:SS.FF3", provider);
    }

    private static void TestTimestamp()
    {
        Provider provider = new(2023, 4);

        ValueTimestampTimeZone ts = TimestampTimeZone(2022, 10, 12, 14, 30, 5, 123456789, 120);
        AssertFormatted("2022-10-12 14:30:05.123456789", ts,
            "YYYY-MM-DD HH24:MI:SS.FF9", provider);
        AssertFormatted("2022-10-12 14:30:05.123456789 +02:00", ts,
            "YYYY-MM-DD HH24:MI:SS.FF9 TZH:TZM", provider);
        AssertFormatted("2022-10-12 02:30:05 P.M.", ts,
            "YYYY-MM-DD HH12:MI:SS A.M.", provider);
    }

    private static void TestOutOfRange()
    {
        Provider provider = new(2023, 4);

        AssertFormattedAndFail(Time(24, 0, 0, 0), "HH24", provider);
        AssertFormattedAndFail(Time(0, 60, 0, 0), "MI", provider);
        AssertFormattedAndFail(Time(0, 0, 60, 0), "SS", provider);
    }
}
