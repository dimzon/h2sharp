// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/functions.html — date and time functions, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Functions;

public sealed class DateTimeFunctionTests
{
    private const string Moment = "TIMESTAMP '2024-05-06 07:08:09'";
    private const string Day = "DATE '2024-05-06'";

    /// <summary>docs/functions.html:2671 — TO_CHAR(value [, formatString[, nlsParamString]])</summary>
    [Fact]
    public void ToChar_FormatsTheValue() // FUN-2671
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("2024-05-06", connection.QuerySingle<string>(
            $"SELECT TO_CHAR({Day}, 'yyyy-MM-dd')"));
        Assert.Equal("2024", connection.QuerySingle<string>(
            $"SELECT TO_CHAR({Day}, 'yyyy', 'NLS_DATE_LANGUAGE=ENGLISH')"));
    }

    /// <summary>docs/functions.html:2689 — TRANSLATE(value, searchString, replacementString)</summary>
    [Fact]
    public void Translate_MapsTheCharacters() // FUN-2689
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("Hippo", connection.QuerySingle<string>("SELECT TRANSLATE('Hello', 'el', 'ip')"));
    }

    /// <summary>docs/functions.html:2709 — CURRENT_DATE</summary>
    [Fact]
    public void CurrentDate_IsAValidDate() // FUN-2709
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT DAYOFMONTH(CURRENT_DATE) BETWEEN 1 AND 31"));
    }

    /// <summary>docs/functions.html:2727 — CURRENT_TIME [ (int) ]</summary>
    [Fact]
    public void CurrentTime_IsAValidTime() // FUN-2727
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT HOUR(CURRENT_TIME(0)) BETWEEN 0 AND 23"));
    }

    /// <summary>docs/functions.html:2745 — CURRENT_TIMESTAMP [ (int) ]</summary>
    [Fact]
    public void CurrentTimestamp_IsAValidMoment() // FUN-2745
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT YEAR(CURRENT_TIMESTAMP(0)) >= 2024"));
    }

    /// <summary>docs/functions.html:2763 — LOCALTIME [ (int) ]</summary>
    [Fact]
    public void LocalTime_IsAValidTime() // FUN-2763
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT HOUR(LOCALTIME(0)) BETWEEN 0 AND 23"));
    }

    /// <summary>docs/functions.html:2781 — LOCALTIMESTAMP [ (int) ]</summary>
    [Fact]
    public void LocalTimestamp_IsAValidMoment() // FUN-2781
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT YEAR(LOCALTIMESTAMP(0)) >= 2024"));
    }

    /// <summary>docs/functions.html:2799 — { DATEADD| TIMESTAMPADD } (datetimeField, addIntLong, dateAndTime)</summary>
    [Fact]
    public void DateAdd_AddsTheUnits() // FUN-2799
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(7, connection.QuerySingle<int>(
            $"SELECT DAYOFMONTH(DATEADD('DAY', 1, {Day}))"));
        Assert.Equal(7, connection.QuerySingle<int>(
            $"SELECT DAYOFMONTH(TIMESTAMPADD('DAY', 1, {Day}))"));
    }

    /// <summary>docs/functions.html:2817 — { DATEDIFF | TIMESTAMPDIFF } (datetimeField, aDateAndTime, bDateAndTime)</summary>
    [Fact]
    public void DateDiff_CountsTheUnits() // FUN-2817
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1L, connection.QuerySingle<long>(
            $"SELECT DATEDIFF('DAY', {Day}, DATE '2024-05-07')"));
        Assert.Equal(1L, connection.QuerySingle<long>(
            $"SELECT TIMESTAMPDIFF('DAY', {Day}, DATE '2024-05-07')"));
    }

    /// <summary>docs/functions.html:2835 — DATE_TRUNC(datetimeField, dateAndTime)</summary>
    [Fact]
    public void DateTrunc_TruncatesToTheField() // FUN-2835
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>(
            $"SELECT DAYOFMONTH(DATE_TRUNC('MONTH', {Day}))"));
    }

    /// <summary>docs/functions.html:2853 — LAST_DAY(date | timestamp | timestampWithTimeZone | string)</summary>
    [Fact]
    public void LastDay_ReturnsTheEndOfTheMonth() // FUN-2853
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(31, connection.QuerySingle<int>($"SELECT DAYOFMONTH(LAST_DAY({Day}))"));
        Assert.Equal(29, connection.QuerySingle<int>(
            "SELECT DAYOFMONTH(LAST_DAY(TIMESTAMP '2024-02-15 10:00:00'))"));
    }

    /// <summary>docs/functions.html:2871 — DAYNAME(dateAndTime)</summary>
    [Fact]
    public void DayName_NamesTheWeekday() // FUN-2871
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.NotEmpty(connection.QuerySingle<string>($"SELECT DAYNAME({Moment})"));
    }

    /// <summary>docs/functions.html:2889 — DAY_OF_MONTH({dateAndTime|interval})</summary>
    [Fact]
    public void DayOfMonth_ReturnsTheDay() // FUN-2889
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(6, connection.QuerySingle<int>($"SELECT DAY_OF_MONTH({Day})"));
        Assert.Equal(3, connection.QuerySingle<int>("SELECT DAY_OF_MONTH(INTERVAL '3' DAY)"));
    }

    /// <summary>docs/functions.html:2907 — DAY_OF_WEEK(dateAndTime)</summary>
    [Fact]
    public void DayOfWeek_CountsFromSunday() // FUN-2907
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>($"SELECT DAY_OF_WEEK({Day})"));
    }

    /// <summary>docs/functions.html:2925 — ISO_DAY_OF_WEEK(dateAndTime)</summary>
    [Fact]
    public void IsoDayOfWeek_CountsFromMonday() // FUN-2925
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>($"SELECT ISO_DAY_OF_WEEK({Day})"));
    }

    /// <summary>docs/functions.html:2943 — DAY_OF_YEAR({dateAndTime|interval})</summary>
    [Fact]
    public void DayOfYear_ReturnsTheOrdinal() // FUN-2943
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(127, connection.QuerySingle<int>($"SELECT DAY_OF_YEAR({Day})"));
    }

    /// <summary>docs/functions.html:2961 — EXTRACT ( datetimeField FROM { dateAndTime | interval })</summary>
    [Fact]
    public void Extract_PullsTheField() // FUN-2961
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2024, connection.QuerySingle<int>($"SELECT EXTRACT(YEAR FROM {Day})"));
        Assert.Equal(5, connection.QuerySingle<int>("SELECT EXTRACT(HOUR FROM INTERVAL '5' HOUR)"));
    }

    /// <summary>docs/functions.html:2979 — FORMATDATETIME ( dateAndTime, formatString</summary>
    [Fact]
    public void FormatDateTime_UsesTheFormat() // FUN-2979
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("2024-05-06", connection.QuerySingle<string>(
            $"SELECT FORMATDATETIME({Moment}, 'yyyy-MM-dd')"));
    }

    /// <summary>docs/functions.html:2980 — FORMATDATETIME [ , localeString [ , timeZoneString ] ] )</summary>
    [Fact]
    public void FormatDateTime_AcceptsLocaleAndTimeZone() // FUN-2980
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("2024-05-06", connection.QuerySingle<string>(
            $"SELECT FORMATDATETIME({Moment}, 'yyyy-MM-dd', 'en', 'UTC')"));
    }

    /// <summary>docs/functions.html:2999 — HOUR({dateAndTime|interval})</summary>
    [Fact]
    public void Hour_ReturnsTheHour() // FUN-2999
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(7, connection.QuerySingle<int>($"SELECT HOUR({Moment})"));
        Assert.Equal(5, connection.QuerySingle<int>("SELECT HOUR(INTERVAL '5' HOUR)"));
    }

    /// <summary>docs/functions.html:3017 — MINUTE({dateAndTime|interval})</summary>
    [Fact]
    public void Minute_ReturnsTheMinute() // FUN-3017
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(8, connection.QuerySingle<int>($"SELECT MINUTE({Moment})"));
        Assert.Equal(5, connection.QuerySingle<int>("SELECT MINUTE(INTERVAL '5' MINUTE)"));
    }

    /// <summary>docs/functions.html:3035 — MONTH({dateAndTime|interval})</summary>
    [Fact]
    public void Month_ReturnsTheMonth() // FUN-3035
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5, connection.QuerySingle<int>($"SELECT MONTH({Moment})"));
        Assert.Equal(2, connection.QuerySingle<int>("SELECT MONTH(INTERVAL '2' MONTH)"));
    }

    /// <summary>docs/functions.html:3053 — MONTHNAME(dateAndTime)</summary>
    [Fact]
    public void MonthName_NamesTheMonth() // FUN-3053
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.NotEmpty(connection.QuerySingle<string>($"SELECT MONTHNAME({Moment})"));
    }

    /// <summary>docs/functions.html:3071 — PARSEDATETIME(string, formatString</summary>
    [Fact]
    public void ParseDateTime_UsesTheFormat() // FUN-3071
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2024, connection.QuerySingle<int>(
            "SELECT YEAR(PARSEDATETIME('2024-05-06', 'yyyy-MM-dd'))"));
    }

    /// <summary>docs/functions.html:3072 — PARSEDATETIME [, localeString [, timeZoneString]])</summary>
    [Fact]
    public void ParseDateTime_AcceptsLocaleAndTimeZone() // FUN-3072
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>(
            "SELECT YEAR(PARSEDATETIME('2024-05-06 12:00:00', 'yyyy-MM-dd HH:mm:ss', 'en', 'UTC')) " +
            "BETWEEN 2023 AND 2025"));
    }

    /// <summary>docs/functions.html:3091 — QUARTER(dateAndTime)</summary>
    [Fact]
    public void Quarter_ReturnsTheQuarter() // FUN-3091
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>($"SELECT QUARTER({Day})"));
    }

    /// <summary>docs/functions.html:3109 — SECOND(dateAndTime)</summary>
    [Fact]
    public void Second_ReturnsTheSecond() // FUN-3109
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(9, connection.QuerySingle<int>($"SELECT SECOND({Moment})"));
    }

    /// <summary>docs/functions.html:3127 — WEEK(dateAndTime)</summary>
    [Fact]
    public void Week_ReturnsTheWeekOfTheYear() // FUN-3127
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>($"SELECT WEEK({Day}) BETWEEN 18 AND 20"));
    }

    /// <summary>docs/functions.html:3145 — ISO_WEEK(dateAndTime)</summary>
    [Fact]
    public void IsoWeek_ReturnsTheIsoWeek() // FUN-3145
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(19, connection.QuerySingle<int>($"SELECT ISO_WEEK({Day})"));
    }

    /// <summary>docs/functions.html:3163 — YEAR({dateAndTime|interval})</summary>
    [Fact]
    public void Year_ReturnsTheYear() // FUN-3163
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2024, connection.QuerySingle<int>($"SELECT YEAR({Moment})"));
        Assert.Equal(2, connection.QuerySingle<int>("SELECT YEAR(INTERVAL '2' YEAR)"));
    }

    /// <summary>docs/functions.html:3181 — ISO_YEAR(dateAndTime)</summary>
    [Fact]
    public void IsoYear_ReturnsTheIsoYear() // FUN-3181
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2024, connection.QuerySingle<int>($"SELECT ISO_YEAR({Day})"));
    }
}
