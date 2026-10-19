// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/grammar.html — EXTRACT datetime fields, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Grammar;

public sealed class DatetimeFieldTests
{
    private const string Date = "DATE '2024-05-06'";
    private const string Timestamp = "TIMESTAMP '2024-05-06 07:08:09.123456789'";
    private const string TimestampWithZone = "TIMESTAMP WITH TIME ZONE '2024-05-06 07:08:09.123+05:30'";

    /// <summary>docs/grammar.html:1532 — yearField | monthField | dayOfMonthField</summary>
    [Fact]
    public void DatetimeField_YearMonthDay_AreExtracted() // GRM-1532
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2024, Extract(connection, "YEAR", Date));
        Assert.Equal(5, Extract(connection, "MONTH", Date));
        Assert.Equal(6, Extract(connection, "DAY", Date));
    }

    /// <summary>docs/grammar.html:1533 — | hourField | minuteField | secondField</summary>
    [Fact]
    public void DatetimeField_HourMinuteSecond_AreExtracted() // GRM-1533
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(7, Extract(connection, "HOUR", Timestamp));
        Assert.Equal(8, Extract(connection, "MINUTE", Timestamp));
        Assert.Equal(9, Extract(connection, "SECOND", Timestamp));
    }

    /// <summary>docs/grammar.html:1534 — | timezoneHourField | timezoneMinuteField</summary>
    [Fact]
    public void DatetimeField_TimezoneHourAndMinute_AreExtracted() // GRM-1534
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5, Extract(connection, "TIMEZONE_HOUR", TimestampWithZone));
        Assert.Equal(30, Extract(connection, "TIMEZONE_MINUTE", TimestampWithZone));
    }

    /// <summary>docs/grammar.html:1535 — | { timezoneSecondField</summary>
    [Fact]
    public void DatetimeField_TimezoneSecond_IsExtracted() // GRM-1535
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(15, Extract(connection, "TIMEZONE_SECOND",
            "TIMESTAMP WITH TIME ZONE '2024-05-06 07:08:09+05:30:15'"));
    }

    /// <summary>docs/grammar.html:1536 — | millenniumField | centuryField | decadeField</summary>
    [Fact]
    public void DatetimeField_MillenniumCenturyDecade_AreExtracted() // GRM-1536
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3, Extract(connection, "MILLENNIUM", Date));
        Assert.Equal(21, Extract(connection, "CENTURY", Date));
        Assert.Equal(203, Extract(connection, "DECADE", Date));
    }

    /// <summary>docs/grammar.html:1537 — | quarterField</summary>
    [Fact]
    public void DatetimeField_Quarter_IsExtracted() // GRM-1537
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, Extract(connection, "QUARTER", Date));
    }

    /// <summary>docs/grammar.html:1538 — | millisecondField | microsecondField | nanosecondField</summary>
    [Fact]
    public void DatetimeField_SubsecondFields_AreExtracted() // GRM-1538
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(123, Extract(connection, "MILLISECOND", Timestamp));
        Assert.Equal(123456, Extract(connection, "MICROSECOND", Timestamp));
        Assert.Equal(123456789, Extract(connection, "NANOSECOND", Timestamp));
    }

    /// <summary>docs/grammar.html:1539 — | dayOfYearField</summary>
    [Fact]
    public void DatetimeField_DayOfYear_IsExtracted() // GRM-1539
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(127, Extract(connection, "DAY_OF_YEAR", Date));
    }

    /// <summary>docs/grammar.html:1540 — | isoDayOfWeekField | isoWeekField | isoWeekYearField</summary>
    [Fact]
    public void DatetimeField_IsoFields_AreExtracted() // GRM-1540
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, Extract(connection, "ISO_DAY_OF_WEEK", Date));
        Assert.Equal(19, Extract(connection, "ISO_WEEK", Date));
        Assert.Equal(2024, Extract(connection, "ISO_WEEK_YEAR", Date));
    }

    /// <summary>docs/grammar.html:1541 — | dayOfWeekField | weekField | weekYearField</summary>
    [Fact]
    public void DatetimeField_LocaleFields_AreInRange() // GRM-1541
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>(
            $"SELECT CASE WHEN EXTRACT(DAY_OF_WEEK FROM {Date}) BETWEEN 1 AND 7 THEN 1 ELSE 0 END"));
        Assert.Equal(1, connection.QuerySingle<int>(
            $"SELECT CASE WHEN EXTRACT(WEEK FROM {Date}) BETWEEN 1 AND 53 THEN 1 ELSE 0 END"));
        Assert.Equal(1, connection.QuerySingle<int>(
            $"SELECT CASE WHEN EXTRACT(WEEK_YEAR FROM {Date}) >= 2000 THEN 1 ELSE 0 END"));
    }

    /// <summary>docs/grammar.html:1542 — | epochField }</summary>
    [Fact]
    public void DatetimeField_Epoch_IsExtracted() // GRM-1542
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>(
            $"SELECT CASE WHEN EXTRACT(EPOCH FROM {Timestamp}) > 0 THEN 1 ELSE 0 END"));
    }

    /// <summary>docs/grammar.html:1571 — YEAR | { YYYY | YY | SQL_TSI_YEAR }</summary>
    [Fact]
    public void YearField_AllNames_AreAccepted() // GRM-1571
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2024, Extract(connection, "YEAR", Date));
        Assert.Equal(2024, Extract(connection, "YYYY", Date));
        Assert.Equal(2024, Extract(connection, "YY", Date));
        Assert.Equal(2024, Extract(connection, "SQL_TSI_YEAR", Date));
    }

    /// <summary>docs/grammar.html:1590 — MONTH | { MM | M | SQL_TSI_MONTH }</summary>
    [Fact]
    public void MonthField_AllNames_AreAccepted() // GRM-1590
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5, Extract(connection, "MONTH", Date));
        Assert.Equal(5, Extract(connection, "MM", Date));
        Assert.Equal(5, Extract(connection, "M", Date));
        Assert.Equal(5, Extract(connection, "SQL_TSI_MONTH", Date));
    }

    /// <summary>docs/grammar.html:1609 — DAY | { DD | D | SQL_TSI_DAY }</summary>
    [Fact]
    public void DayOfMonthField_AllNames_AreAccepted() // GRM-1609
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(6, Extract(connection, "DAY", Date));
        Assert.Equal(6, Extract(connection, "DD", Date));
        Assert.Equal(6, Extract(connection, "D", Date));
        Assert.Equal(6, Extract(connection, "SQL_TSI_DAY", Date));
    }

    /// <summary>docs/grammar.html:1628 — HOUR | { HH | SQL_TSI_HOUR }</summary>
    [Fact]
    public void HourField_AllNames_AreAccepted() // GRM-1628
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(7, Extract(connection, "HOUR", Timestamp));
        Assert.Equal(7, Extract(connection, "HH", Timestamp));
        Assert.Equal(7, Extract(connection, "SQL_TSI_HOUR", Timestamp));
    }

    /// <summary>docs/grammar.html:1647 — MINUTE | { MI | N | SQL_TSI_MINUTE }</summary>
    [Fact]
    public void MinuteField_AllNames_AreAccepted() // GRM-1647
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(8, Extract(connection, "MINUTE", Timestamp));
        Assert.Equal(8, Extract(connection, "MI", Timestamp));
        Assert.Equal(8, Extract(connection, "N", Timestamp));
        Assert.Equal(8, Extract(connection, "SQL_TSI_MINUTE", Timestamp));
    }

    /// <summary>docs/grammar.html:1666 — SECOND | { SS | S | SQL_TSI_SECOND }</summary>
    [Fact]
    public void SecondField_AllNames_AreAccepted() // GRM-1666
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(9, Extract(connection, "SECOND", Timestamp));
        Assert.Equal(9, Extract(connection, "SS", Timestamp));
        Assert.Equal(9, Extract(connection, "S", Timestamp));
        Assert.Equal(9, Extract(connection, "SQL_TSI_SECOND", Timestamp));
    }

    /// <summary>docs/grammar.html:1685 — TIMEZONE_HOUR</summary>
    [Fact]
    public void TimezoneHourField_IsAccepted() // GRM-1685
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5, Extract(connection, "TIMEZONE_HOUR", TimestampWithZone));
    }

    /// <summary>docs/grammar.html:1704 — TIMEZONE_MINUTE</summary>
    [Fact]
    public void TimezoneMinuteField_IsAccepted() // GRM-1704
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(30, Extract(connection, "TIMEZONE_MINUTE", TimestampWithZone));
    }

    /// <summary>docs/grammar.html:1723 — TIMEZONE_SECOND</summary>
    [Fact]
    public void TimezoneSecondField_IsAccepted() // GRM-1723
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(15, Extract(connection, "TIMEZONE_SECOND",
            "TIMESTAMP WITH TIME ZONE '2024-05-06 07:08:09+05:30:15'"));
    }

    /// <summary>docs/grammar.html:1742 — MILLENNIUM</summary>
    [Fact]
    public void MillenniumField_IsAccepted() // GRM-1742
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3, Extract(connection, "MILLENNIUM", Date));
    }

    /// <summary>docs/grammar.html:1761 — CENTURY</summary>
    [Fact]
    public void CenturyField_IsAccepted() // GRM-1761
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(21, Extract(connection, "CENTURY", Date));
    }

    /// <summary>docs/grammar.html:1780 — DECADE</summary>
    [Fact]
    public void DecadeField_IsAccepted() // GRM-1780
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(203, Extract(connection, "DECADE", Date));
    }

    /// <summary>docs/grammar.html:1799 — QUARTER</summary>
    [Fact]
    public void QuarterField_IsAccepted() // GRM-1799
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, Extract(connection, "QUARTER", Date));
    }

    /// <summary>docs/grammar.html:1818 — { MILLISECOND } | { MS }</summary>
    [Fact]
    public void MillisecondField_BothNames_AreAccepted() // GRM-1818
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(123, Extract(connection, "MILLISECOND", Timestamp));
        Assert.Equal(123, Extract(connection, "MS", Timestamp));
    }

    /// <summary>docs/grammar.html:1837 — { MICROSECOND } | { MCS }</summary>
    [Fact]
    public void MicrosecondField_BothNames_AreAccepted() // GRM-1837
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(123456, Extract(connection, "MICROSECOND", Timestamp));
        Assert.Equal(123456, Extract(connection, "MCS", Timestamp));
    }

    /// <summary>docs/grammar.html:1856 — { NANOSECOND } | { NS }</summary>
    [Fact]
    public void NanosecondField_BothNames_AreAccepted() // GRM-1856
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(123456789, Extract(connection, "NANOSECOND", Timestamp));
        Assert.Equal(123456789, Extract(connection, "NS", Timestamp));
    }

    /// <summary>docs/grammar.html:1875 — { DAYOFYEAR | DAY_OF_YEAR } | { DOY | DY }</summary>
    [Fact]
    public void DayOfYearField_AllNames_AreAccepted() // GRM-1875
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(127, Extract(connection, "DAYOFYEAR", Date));
        Assert.Equal(127, Extract(connection, "DAY_OF_YEAR", Date));
        Assert.Equal(127, Extract(connection, "DOY", Date));
        Assert.Equal(127, Extract(connection, "DY", Date));
    }

    /// <summary>docs/grammar.html:1894 — { ISO_DAY_OF_WEEK } | { ISODOW }</summary>
    [Fact]
    public void IsoDayOfWeekField_BothNames_AreAccepted() // GRM-1894
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, Extract(connection, "ISO_DAY_OF_WEEK", Date));
        Assert.Equal(1, Extract(connection, "ISODOW", Date));
    }

    /// <summary>docs/grammar.html:1913 — ISO_WEEK</summary>
    [Fact]
    public void IsoWeekField_IsAccepted() // GRM-1913
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(19, Extract(connection, "ISO_WEEK", Date));
    }

    /// <summary>docs/grammar.html:1932 — { ISO_WEEK_YEAR } | { ISO_YEAR | ISOYEAR }</summary>
    [Fact]
    public void IsoWeekYearField_AllNames_AreAccepted() // GRM-1932
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2024, Extract(connection, "ISO_WEEK_YEAR", Date));
        Assert.Equal(2024, Extract(connection, "ISO_YEAR", Date));
        Assert.Equal(2024, Extract(connection, "ISOYEAR", Date));
    }

    /// <summary>docs/grammar.html:1951 — { DAY_OF_WEEK | DAYOFWEEK } | { DOW }</summary>
    [Fact]
    public void DayOfWeekField_AllNames_AreAccepted() // GRM-1951
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>(
            $"SELECT CASE WHEN EXTRACT(DAY_OF_WEEK FROM {Date}) BETWEEN 1 AND 7 THEN 1 ELSE 0 END"));
        Assert.Equal(1, connection.QuerySingle<int>(
            $"SELECT CASE WHEN EXTRACT(DAYOFWEEK FROM {Date}) BETWEEN 1 AND 7 THEN 1 ELSE 0 END"));
        Assert.Equal(1, connection.QuerySingle<int>(
            $"SELECT CASE WHEN EXTRACT(DOW FROM {Date}) BETWEEN 1 AND 7 THEN 1 ELSE 0 END"));
    }

    /// <summary>docs/grammar.html:1970 — { WEEK } | { WW | W | SQL_TSI_WEEK }</summary>
    [Fact]
    public void WeekField_AllNames_AreAccepted() // GRM-1970
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>(
            $"SELECT CASE WHEN EXTRACT(WEEK FROM {Date}) BETWEEN 1 AND 53 THEN 1 ELSE 0 END"));
        Assert.Equal(1, connection.QuerySingle<int>(
            $"SELECT CASE WHEN EXTRACT(WW FROM {Date}) BETWEEN 1 AND 53 THEN 1 ELSE 0 END"));
        Assert.Equal(1, connection.QuerySingle<int>(
            $"SELECT CASE WHEN EXTRACT(W FROM {Date}) BETWEEN 1 AND 53 THEN 1 ELSE 0 END"));
        Assert.Equal(1, connection.QuerySingle<int>(
            $"SELECT CASE WHEN EXTRACT(SQL_TSI_WEEK FROM {Date}) BETWEEN 1 AND 53 THEN 1 ELSE 0 END"));
    }

    /// <summary>docs/grammar.html:1989 — { WEEK_YEAR }</summary>
    [Fact]
    public void WeekYearField_IsAccepted() // GRM-1989
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>(
            $"SELECT CASE WHEN EXTRACT(WEEK_YEAR FROM {Date}) >= 2000 THEN 1 ELSE 0 END"));
    }

    /// <summary>docs/grammar.html:2008 — EPOCH</summary>
    [Fact]
    public void EpochField_IsAccepted() // GRM-2008
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>(
            $"SELECT CASE WHEN EXTRACT(EPOCH FROM {Timestamp}) > 0 THEN 1 ELSE 0 END"));
    }

    private static int Extract(IDbConnection connection, string field, string value) =>
        connection.QuerySingle<int>($"SELECT EXTRACT({field} FROM {value})");
}
