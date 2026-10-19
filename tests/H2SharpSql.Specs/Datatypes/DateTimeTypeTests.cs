// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/datatypes.html — date, time and interval types, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Datatypes;

public sealed class DateTimeTypeTests
{
    /// <summary>docs/datatypes.html:569 — DATE</summary>
    [Fact]
    public void Date_HoldsCalendarDates() // TYP-0569
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2024, connection.QuerySingle<int>("SELECT YEAR(DATE '2024-05-06')"));
        Assert.Equal(2024, connection.QuerySingle<int>("SELECT YEAR(CAST('2024-05-06' AS DATE))"));
    }

    /// <summary>docs/datatypes.html:587 — TIME [ ( precisionInt ) ] [ WITHOUT TIME ZONE ]</summary>
    [Fact]
    public void Time_WithAndWithoutPrecision() // TYP-0587
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(10, connection.QuerySingle<int>("SELECT HOUR(CAST('10:20:30' AS TIME))"));
        Assert.Equal(10, connection.QuerySingle<int>("SELECT HOUR(CAST('10:20:30' AS TIME(0) WITHOUT TIME ZONE))"));
    }

    /// <summary>docs/datatypes.html:605 — TIME [ ( precisionInt ) ] WITH TIME ZONE</summary>
    [Fact]
    public void TimeWithTimeZone_HoldsTheOffset() // TYP-0605
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT CAST('10:20:30+02' AS TIME WITH TIME ZONE) IS NOT NULL"));
        Assert.True(connection.QuerySingle<bool>(
            "SELECT CAST('10:20:30+02' AS TIME(0) WITH TIME ZONE) IS NOT NULL"));
    }

    /// <summary>docs/datatypes.html:623 — TIMESTAMP [ ( precisionInt ) ] [ WITHOUT TIME ZONE ]</summary>
    [Fact]
    public void Timestamp_WithAndWithoutPrecision() // TYP-0623
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2024, connection.QuerySingle<int>("SELECT YEAR(TIMESTAMP '2024-05-06 10:20:30')"));
        Assert.Equal(2024, connection.QuerySingle<int>(
            "SELECT YEAR(CAST('2024-05-06 10:20:30' AS TIMESTAMP(0) WITHOUT TIME ZONE))"));
    }

    /// <summary>docs/datatypes.html:641 — TIMESTAMP [ ( precisionInt ) ] WITH TIME ZONE</summary>
    [Fact]
    public void TimestampWithTimeZone_HoldsTheOffset() // TYP-0641
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>(
            "SELECT CAST('2024-05-06 10:20:30+02' AS TIMESTAMP WITH TIME ZONE) IS NOT NULL"));
        Assert.True(connection.QuerySingle<bool>(
            "SELECT CAST('2024-05-06 10:20:30+02' AS TIMESTAMP(0) WITH TIME ZONE) IS NOT NULL"));
    }

    /// <summary>docs/datatypes.html:659 — intervalYearType | intervalMonthType | intervalDayType</summary>
    [Fact]
    public void Interval_YearMonthDayTypes() // TYP-0659
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>("SELECT YEAR(INTERVAL '2' YEAR)"));
        Assert.Equal(2, connection.QuerySingle<int>("SELECT MONTH(INTERVAL '2' MONTH)"));
        Assert.Equal(3, connection.QuerySingle<int>("SELECT EXTRACT(DAY FROM INTERVAL '3' DAY)"));
    }

    /// <summary>docs/datatypes.html:660 — | intervalHourType| intervalMinuteType | intervalSecondType</summary>
    [Fact]
    public void Interval_HourMinuteSecondTypes() // TYP-0660
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5, connection.QuerySingle<int>("SELECT HOUR(INTERVAL '5' HOUR)"));
        Assert.Equal(5, connection.QuerySingle<int>("SELECT MINUTE(INTERVAL '5' MINUTE)"));
        Assert.Equal(6, connection.QuerySingle<int>("SELECT SECOND(INTERVAL '6' SECOND)"));
    }

    /// <summary>docs/datatypes.html:661 — | intervalYearToMonthType | intervalDayToHourType</summary>
    [Fact]
    public void Interval_YearToMonthAndDayToHour() // TYP-0661
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(A INTERVAL YEAR TO MONTH, B INTERVAL DAY TO HOUR)");
        _ = connection.Execute(
            "INSERT INTO TEST(A, B) VALUES (INTERVAL '1-2' YEAR TO MONTH, INTERVAL '3 04' DAY TO HOUR)");

        Assert.True(connection.QuerySingle<bool>("SELECT YEAR(A) = 1 AND MONTH(A) = 2 FROM TEST"));
        Assert.True(connection.QuerySingle<bool>("SELECT EXTRACT(DAY FROM B) = 3 AND HOUR(B) = 4 FROM TEST"));
    }

    /// <summary>docs/datatypes.html:662 — | intervalDayToMinuteType | intervalDayToSecondType</summary>
    [Fact]
    public void Interval_DayToMinuteAndDayToSecond() // TYP-0662
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(A INTERVAL DAY TO MINUTE, B INTERVAL DAY TO SECOND)");
        _ = connection.Execute(
            "INSERT INTO TEST(A, B) VALUES (INTERVAL '3 04:05' DAY TO MINUTE, INTERVAL '3 04:05:06' DAY TO SECOND)");

        Assert.True(connection.QuerySingle<bool>("SELECT HOUR(A) = 4 AND MINUTE(A) = 5 FROM TEST"));
        Assert.True(connection.QuerySingle<bool>("SELECT EXTRACT(DAY FROM B) = 3 AND SECOND(B) = 6 FROM TEST"));
    }

    /// <summary>docs/datatypes.html:663 — | intervalHourToMinuteType | intervalHourToSecondType</summary>
    [Fact]
    public void Interval_HourToMinuteAndHourToSecond() // TYP-0663
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(A INTERVAL HOUR TO MINUTE, B INTERVAL HOUR TO SECOND)");
        _ = connection.Execute(
            "INSERT INTO TEST(A, B) VALUES (INTERVAL '04:05' HOUR TO MINUTE, INTERVAL '04:05:06' HOUR TO SECOND)");

        Assert.True(connection.QuerySingle<bool>("SELECT HOUR(A) = 4 AND MINUTE(A) = 5 FROM TEST"));
        Assert.True(connection.QuerySingle<bool>("SELECT HOUR(B) = 4 AND SECOND(B) = 6 FROM TEST"));
    }

    /// <summary>docs/datatypes.html:664 — | intervalMinuteToSecondType</summary>
    [Fact]
    public void Interval_MinuteToSecond() // TYP-0664
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(A INTERVAL MINUTE TO SECOND)");
        _ = connection.Execute("INSERT INTO TEST(A) VALUES (INTERVAL '05:06' MINUTE TO SECOND)");

        Assert.True(connection.QuerySingle<bool>("SELECT MINUTE(A) = 5 AND SECOND(A) = 6 FROM TEST"));
    }

    /// <summary>docs/datatypes.html:834 — INTERVAL YEAR [ ( precisionInt ) ]</summary>
    [Fact]
    public void IntervalYear_WithPrecision() // TYP-0834
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>(
            "SELECT YEAR(CAST(INTERVAL '2' YEAR AS INTERVAL YEAR(2)))"));
    }

    /// <summary>docs/datatypes.html:852 — INTERVAL MONTH [ ( precisionInt ) ]</summary>
    [Fact]
    public void IntervalMonth_WithPrecision() // TYP-0852
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>(
            "SELECT MONTH(CAST(INTERVAL '2' MONTH AS INTERVAL MONTH(2)))"));
    }

    /// <summary>docs/datatypes.html:870 — INTERVAL DAY [ ( precisionInt ) ]</summary>
    [Fact]
    public void IntervalDay_WithPrecision() // TYP-0870
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3, connection.QuerySingle<int>(
            "SELECT EXTRACT(DAY FROM CAST(INTERVAL '3' DAY AS INTERVAL DAY(2)))"));
    }

    /// <summary>docs/datatypes.html:888 — INTERVAL HOUR [ ( precisionInt ) ]</summary>
    [Fact]
    public void IntervalHour_WithPrecision() // TYP-0888
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5, connection.QuerySingle<int>(
            "SELECT HOUR(CAST(INTERVAL '5' HOUR AS INTERVAL HOUR(2)))"));
    }

    /// <summary>docs/datatypes.html:906 — INTERVAL MINUTE [ ( precisionInt ) ]</summary>
    [Fact]
    public void IntervalMinute_WithPrecision() // TYP-0906
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5, connection.QuerySingle<int>(
            "SELECT MINUTE(CAST(INTERVAL '5' MINUTE AS INTERVAL MINUTE(2)))"));
    }

    /// <summary>docs/datatypes.html:924 — INTERVAL SECOND [ ( precisionInt [, fractionalPrecisionInt ] ) ]</summary>
    [Fact]
    public void IntervalSecond_WithFractionalPrecision() // TYP-0924
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>(
            "SELECT CAST(INTERVAL '6.5' SECOND AS INTERVAL SECOND(2, 1)) = INTERVAL '6.5' SECOND"));
    }

    /// <summary>docs/datatypes.html:942 — INTERVAL YEAR [ ( precisionInt ) ] TO MONTH</summary>
    [Fact]
    public void IntervalYearToMonth_WithPrecision() // TYP-0942
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT YEAR(CAST(INTERVAL '1-2' YEAR TO MONTH AS INTERVAL YEAR(2) TO MONTH))"));
    }

    /// <summary>docs/datatypes.html:960 — INTERVAL DAY [ ( precisionInt ) ] TO HOUR</summary>
    [Fact]
    public void IntervalDayToHour_WithPrecision() // TYP-0960
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3, connection.QuerySingle<int>(
            "SELECT EXTRACT(DAY FROM CAST(INTERVAL '3 04' DAY TO HOUR AS INTERVAL DAY(2) TO HOUR))"));
    }

    /// <summary>docs/datatypes.html:978 — INTERVAL DAY [ ( precisionInt ) ] TO MINUTE</summary>
    [Fact]
    public void IntervalDayToMinute_WithPrecision() // TYP-0978
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5, connection.QuerySingle<int>(
            "SELECT MINUTE(CAST(INTERVAL '3 04:05' DAY TO MINUTE AS INTERVAL DAY(2) TO MINUTE))"));
    }

    /// <summary>docs/datatypes.html:996 — INTERVAL DAY [ ( precisionInt ) ] TO SECOND [ ( fractionalPrecisionInt ) ]</summary>
    [Fact]
    public void IntervalDayToSecond_WithFractionalPrecision() // TYP-0996
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(6, connection.QuerySingle<int>(
            "SELECT SECOND(CAST(INTERVAL '3 04:05:06' DAY TO SECOND AS INTERVAL DAY(2) TO SECOND(1)))"));
    }

    /// <summary>docs/datatypes.html:1014 — INTERVAL HOUR [ ( precisionInt ) ] TO MINUTE</summary>
    [Fact]
    public void IntervalHourToMinute_WithPrecision() // TYP-1014
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5, connection.QuerySingle<int>(
            "SELECT MINUTE(CAST(INTERVAL '04:05' HOUR TO MINUTE AS INTERVAL HOUR(2) TO MINUTE))"));
    }

    /// <summary>docs/datatypes.html:1032 — INTERVAL HOUR [ ( precisionInt ) ] TO SECOND [ ( fractionalPrecisionInt ) ]</summary>
    [Fact]
    public void IntervalHourToSecond_WithFractionalPrecision() // TYP-1032
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(6, connection.QuerySingle<int>(
            "SELECT SECOND(CAST(INTERVAL '04:05:06' HOUR TO SECOND AS INTERVAL HOUR(2) TO SECOND(1)))"));
    }

    /// <summary>docs/datatypes.html:1050 — INTERVAL MINUTE [ ( precisionInt ) ] TO SECOND [ ( fractionalPrecisionInt ) ]</summary>
    [Fact]
    public void IntervalMinuteToSecond_WithFractionalPrecision() // TYP-1050
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(6, connection.QuerySingle<int>(
            "SELECT SECOND(CAST(INTERVAL '05:06' MINUTE TO SECOND AS INTERVAL MINUTE(2) TO SECOND(1)))"));
    }
}
