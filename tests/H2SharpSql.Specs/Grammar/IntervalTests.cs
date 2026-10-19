// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/grammar.html — interval literals, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Grammar;

public sealed class IntervalTests
{
    /// <summary>docs/grammar.html:1250 — intervalYear | intervalMonth | intervalDay | intervalHour | intervalMinute</summary>
    [Fact]
    public void Interval_SingleFieldKinds_KeepTheirValue() // GRM-1250
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>("SELECT EXTRACT(YEAR FROM INTERVAL '1' YEAR)"));
        Assert.Equal(2, connection.QuerySingle<int>("SELECT EXTRACT(MONTH FROM INTERVAL '2' MONTH)"));
        Assert.Equal(3, connection.QuerySingle<int>("SELECT EXTRACT(DAY FROM INTERVAL '3' DAY)"));
        Assert.Equal(4, connection.QuerySingle<int>("SELECT EXTRACT(HOUR FROM INTERVAL '4' HOUR)"));
        Assert.Equal(5, connection.QuerySingle<int>("SELECT EXTRACT(MINUTE FROM INTERVAL '5' MINUTE)"));
    }

    /// <summary>docs/grammar.html:1251 — | intervalSecond | intervalYearToMonth | intervalDayToHour</summary>
    [Fact]
    public void Interval_SecondYearToMonthDayToHour_KeepTheirValue() // GRM-1251
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(6, connection.QuerySingle<int>("SELECT EXTRACT(SECOND FROM INTERVAL '6' SECOND)"));
        Assert.Equal(2, connection.QuerySingle<int>("SELECT EXTRACT(MONTH FROM INTERVAL '1-2' YEAR TO MONTH)"));
        Assert.Equal(4, connection.QuerySingle<int>("SELECT EXTRACT(HOUR FROM INTERVAL '3 4' DAY TO HOUR)"));
    }

    /// <summary>docs/grammar.html:1252 — | intervalDayToMinute | intervalDayToSecond | intervalHourToMinute</summary>
    [Fact]
    public void Interval_DayToMinuteAndFriends_KeepTheirValue() // GRM-1252
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3, connection.QuerySingle<int>("SELECT EXTRACT(MINUTE FROM INTERVAL '1 02:03' DAY TO MINUTE)"));
        Assert.Equal(4, connection.QuerySingle<int>("SELECT EXTRACT(SECOND FROM INTERVAL '1 02:03:04' DAY TO SECOND)"));
        Assert.Equal(3, connection.QuerySingle<int>("SELECT EXTRACT(MINUTE FROM INTERVAL '2:03' HOUR TO MINUTE)"));
    }

    /// <summary>docs/grammar.html:1253 — | intervalHourToSecond | intervalMinuteToSecond</summary>
    [Fact]
    public void Interval_HourToSecondAndMinuteToSecond_KeepTheirValue() // GRM-1253
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3, connection.QuerySingle<int>("SELECT EXTRACT(SECOND FROM INTERVAL '1:02:03' HOUR TO SECOND)"));
        Assert.Equal(3, connection.QuerySingle<int>("SELECT EXTRACT(SECOND FROM INTERVAL '2:03' MINUTE TO SECOND)"));
    }

    /// <summary>docs/grammar.html:1275 — INTERVAL [-|+] '[-|+]yearInt' YEAR [ ( precisionInt ) ]</summary>
    [Fact]
    public void IntervalYear_AndPrecision_KeepTheirValue() // GRM-1275
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5, connection.QuerySingle<int>("SELECT EXTRACT(YEAR FROM INTERVAL '5' YEAR)"));
        Assert.Equal(123, connection.QuerySingle<int>("SELECT EXTRACT(YEAR FROM INTERVAL '123' YEAR(3))"));
    }

    /// <summary>docs/grammar.html:1294 — INTERVAL [-|+] '[-|+]monthInt' MONTH [ ( precisionInt ) ]</summary>
    [Fact]
    public void IntervalMonth_AndPrecision_KeepTheirValue() // GRM-1294
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5, connection.QuerySingle<int>("SELECT EXTRACT(MONTH FROM INTERVAL '5' MONTH)"));
        Assert.Equal(12, connection.QuerySingle<int>("SELECT EXTRACT(MONTH FROM INTERVAL '12' MONTH(2))"));
    }

    /// <summary>docs/grammar.html:1313 — INTERVAL [-|+] '[-|+]dayInt' DAY [ ( precisionInt ) ]</summary>
    [Fact]
    public void IntervalDay_AndPrecision_KeepTheirValue() // GRM-1313
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5, connection.QuerySingle<int>("SELECT EXTRACT(DAY FROM INTERVAL '5' DAY)"));
        Assert.Equal(12, connection.QuerySingle<int>("SELECT EXTRACT(DAY FROM INTERVAL '12' DAY(2))"));
    }

    /// <summary>docs/grammar.html:1332 — INTERVAL [-|+] '[-|+]hourInt' HOUR [ ( precisionInt ) ]</summary>
    [Fact]
    public void IntervalHour_AndPrecision_KeepTheirValue() // GRM-1332
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5, connection.QuerySingle<int>("SELECT EXTRACT(HOUR FROM INTERVAL '5' HOUR)"));
        Assert.Equal(12, connection.QuerySingle<int>("SELECT EXTRACT(HOUR FROM INTERVAL '12' HOUR(2))"));
    }

    /// <summary>docs/grammar.html:1351 — INTERVAL [-|+] '[-|+]minuteInt' MINUTE [ ( precisionInt ) ]</summary>
    [Fact]
    public void IntervalMinute_AndPrecision_KeepTheirValue() // GRM-1351
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5, connection.QuerySingle<int>("SELECT EXTRACT(MINUTE FROM INTERVAL '5' MINUTE)"));
        Assert.Equal(12, connection.QuerySingle<int>("SELECT EXTRACT(MINUTE FROM INTERVAL '12' MINUTE(2))"));
    }

    /// <summary>docs/grammar.html:1370 — INTERVAL [-|+] '[-|+]secondInt[.nnnnnnnnn]'</summary>
    [Fact]
    public void IntervalSecond_FractionIsKept() // GRM-1370
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5, connection.QuerySingle<int>("SELECT EXTRACT(SECOND FROM INTERVAL '5' SECOND)"));
        Assert.Equal(5.5m, connection.QuerySingle<decimal>("SELECT EXTRACT(SECOND FROM INTERVAL '5.5' SECOND)"));
    }

    /// <summary>docs/grammar.html:1371 — SECOND [ ( precisionInt [, fractionalPrecisionInt ] ) ]</summary>
    [Fact]
    public void IntervalSecond_WithPrecision_KeepsTheFraction() // GRM-1371
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5.5m, connection.QuerySingle<decimal>(
            "SELECT EXTRACT(SECOND FROM INTERVAL '5.5' SECOND(2, 1))"));
    }

    /// <summary>docs/grammar.html:1391 — INTERVAL [-|+] '[-|+]yearInt-monthInt' YEAR [ ( precisionInt ) ] TO MONTH</summary>
    [Fact]
    public void IntervalYearToMonth_KeepsBothFields() // GRM-1391
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>("SELECT EXTRACT(YEAR FROM INTERVAL '1-2' YEAR TO MONTH)"));
        Assert.Equal(2, connection.QuerySingle<int>("SELECT EXTRACT(MONTH FROM INTERVAL '1-2' YEAR TO MONTH)"));
    }

    /// <summary>docs/grammar.html:1410 — INTERVAL [-|+] '[-|+]dayInt hoursInt' DAY [ ( precisionInt ) ] TO HOUR</summary>
    [Fact]
    public void IntervalDayToHour_KeepsBothFields() // GRM-1410
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>("SELECT EXTRACT(DAY FROM INTERVAL '1 2' DAY TO HOUR)"));
        Assert.Equal(2, connection.QuerySingle<int>("SELECT EXTRACT(HOUR FROM INTERVAL '1 2' DAY TO HOUR)"));
    }

    /// <summary>docs/grammar.html:1429 — INTERVAL [-|+] '[-|+]dayInt hh:mm' DAY [ ( precisionInt ) ] TO MINUTE</summary>
    [Fact]
    public void IntervalDayToMinute_KeepsItsFields() // GRM-1429
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>("SELECT EXTRACT(HOUR FROM INTERVAL '1 02:03' DAY TO MINUTE)"));
        Assert.Equal(3, connection.QuerySingle<int>("SELECT EXTRACT(MINUTE FROM INTERVAL '1 02:03' DAY TO MINUTE)"));
    }

    /// <summary>docs/grammar.html:1448 — INTERVAL [-|+] '[-|+]dayInt hh:mm:ss[.nnnnnnnnn]' DAY [ ( precisionInt ) ]</summary>
    [Fact]
    public void IntervalDayToSecond_KeepsItsFields() // GRM-1448
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>("SELECT EXTRACT(DAY FROM INTERVAL '1 02:03:04' DAY TO SECOND)"));
        Assert.Equal(4, connection.QuerySingle<int>("SELECT EXTRACT(SECOND FROM INTERVAL '1 02:03:04' DAY TO SECOND)"));
    }

    /// <summary>docs/grammar.html:1449 — TO SECOND [ ( fractionalPrecisionInt ) ]</summary>
    [Fact]
    public void IntervalDayToSecond_FractionalPrecision_KeepsTheFraction() // GRM-1449
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(4.5m, connection.QuerySingle<decimal>(
            "SELECT EXTRACT(SECOND FROM INTERVAL '1 02:03:04.5' DAY TO SECOND(2))"));
    }

    /// <summary>docs/grammar.html:1469 — INTERVAL [-|+] '[-|+]hh:mm' HOUR [ ( precisionInt ) ] TO MINUTE</summary>
    [Fact]
    public void IntervalHourToMinute_KeepsItsFields() // GRM-1469
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>("SELECT EXTRACT(HOUR FROM INTERVAL '1:02' HOUR TO MINUTE)"));
        Assert.Equal(2, connection.QuerySingle<int>("SELECT EXTRACT(MINUTE FROM INTERVAL '1:02' HOUR TO MINUTE)"));
    }

    /// <summary>docs/grammar.html:1488 — INTERVAL [-|+] '[-|+]hh:mm:ss[.nnnnnnnnn]' HOUR [ ( precisionInt ) ]</summary>
    [Fact]
    public void IntervalHourToSecond_KeepsItsFields() // GRM-1488
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>("SELECT EXTRACT(MINUTE FROM INTERVAL '1:02:03' HOUR TO SECOND)"));
        Assert.Equal(3, connection.QuerySingle<int>("SELECT EXTRACT(SECOND FROM INTERVAL '1:02:03' HOUR TO SECOND)"));
    }

    /// <summary>docs/grammar.html:1489 — TO SECOND [ ( fractionalPrecisionInt ) ]</summary>
    [Fact]
    public void IntervalHourToSecond_FractionalPrecision_KeepsTheFraction() // GRM-1489
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3.5m, connection.QuerySingle<decimal>(
            "SELECT EXTRACT(SECOND FROM INTERVAL '1:02:03.5' HOUR TO SECOND(2))"));
    }

    /// <summary>docs/grammar.html:1509 — INTERVAL [-|+] '[-|+]mm:ss[.nnnnnnnnn]' MINUTE [ ( precisionInt ) ]</summary>
    [Fact]
    public void IntervalMinuteToSecond_KeepsItsFields() // GRM-1509
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>("SELECT EXTRACT(MINUTE FROM INTERVAL '2:03' MINUTE TO SECOND)"));
        Assert.Equal(3, connection.QuerySingle<int>("SELECT EXTRACT(SECOND FROM INTERVAL '2:03' MINUTE TO SECOND)"));
    }

    /// <summary>docs/grammar.html:1510 — TO SECOND [ ( fractionalPrecisionInt ) ]</summary>
    [Fact]
    public void IntervalMinuteToSecond_FractionalPrecision_KeepsTheFraction() // GRM-1510
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3.5m, connection.QuerySingle<decimal>(
            "SELECT EXTRACT(SECOND FROM INTERVAL '2:03.5' MINUTE TO SECOND(2))"));
    }
}
