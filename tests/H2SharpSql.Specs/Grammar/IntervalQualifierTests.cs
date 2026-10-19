// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/grammar.html — interval qualifiers, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Grammar;

public sealed class IntervalQualifierTests
{
    /// <summary>docs/grammar.html:2905 — YEAR [(precisionInt)] [ TO MONTH ]</summary>
    [Fact]
    public void IntervalQualifier_YearAndYearToMonth_AreAccepted() // GRM-2905
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5, connection.QuerySingle<int>(
            "SELECT EXTRACT(YEAR FROM CAST('5' AS INTERVAL YEAR))"));
        Assert.Equal(6, connection.QuerySingle<int>(
            "SELECT EXTRACT(MONTH FROM CAST('5-6' AS INTERVAL YEAR(2) TO MONTH))"));
    }

    /// <summary>docs/grammar.html:2906 — | MONTH [(precisionInt)]</summary>
    [Fact]
    public void IntervalQualifier_Month_IsAccepted() // GRM-2906
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5, connection.QuerySingle<int>(
            "SELECT EXTRACT(MONTH FROM CAST('5' AS INTERVAL MONTH))"));
    }

    /// <summary>docs/grammar.html:2907 — | DAY [(precisionInt)] [ TO { HOUR | MINUTE | SECOND [(scaleInt)] } ]</summary>
    [Fact]
    public void IntervalQualifier_DayAndItsRanges_AreAccepted() // GRM-2907
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5, connection.QuerySingle<int>(
            "SELECT EXTRACT(DAY FROM CAST('5' AS INTERVAL DAY))"));
        Assert.Equal(2, connection.QuerySingle<int>(
            "SELECT EXTRACT(HOUR FROM CAST('5 2' AS INTERVAL DAY TO HOUR))"));
        Assert.Equal(30, connection.QuerySingle<int>(
            "SELECT EXTRACT(MINUTE FROM CAST('5 12:30' AS INTERVAL DAY TO MINUTE))"));
        Assert.Equal(45.5m, connection.QuerySingle<decimal>(
            "SELECT EXTRACT(SECOND FROM CAST('5 12:30:45.5' AS INTERVAL DAY TO SECOND(1)))"));
    }

    /// <summary>docs/grammar.html:2908 — | HOUR [(precisionInt)] [ TO { MINUTE | SECOND [(scaleInt)] } ]</summary>
    [Fact]
    public void IntervalQualifier_HourAndItsRanges_AreAccepted() // GRM-2908
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5, connection.QuerySingle<int>(
            "SELECT EXTRACT(HOUR FROM CAST('5' AS INTERVAL HOUR))"));
        Assert.Equal(30, connection.QuerySingle<int>(
            "SELECT EXTRACT(MINUTE FROM CAST('5:30' AS INTERVAL HOUR TO MINUTE))"));
        Assert.Equal(2, connection.QuerySingle<int>(
            "SELECT EXTRACT(SECOND FROM CAST('5:30:02' AS INTERVAL HOUR TO SECOND(2)))"));
    }

    /// <summary>docs/grammar.html:2909 — | MINUTE [(precisionInt)] [ TO SECOND [(scaleInt)] ]</summary>
    [Fact]
    public void IntervalQualifier_MinuteAndMinuteToSecond_AreAccepted() // GRM-2909
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5, connection.QuerySingle<int>(
            "SELECT EXTRACT(MINUTE FROM CAST('5' AS INTERVAL MINUTE))"));
        Assert.Equal(30, connection.QuerySingle<int>(
            "SELECT EXTRACT(SECOND FROM CAST('5:30' AS INTERVAL MINUTE TO SECOND(2)))"));
    }

    /// <summary>docs/grammar.html:2910 — | SECOND [(precisionInt [, scaleInt])]</summary>
    [Fact]
    public void IntervalQualifier_SecondWithPrecision_IsAccepted() // GRM-2910
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5.5m, connection.QuerySingle<decimal>(
            "SELECT EXTRACT(SECOND FROM CAST('5.5' AS INTERVAL SECOND(2, 1)))"));
    }
}
