// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/grammar.html — date and time literals, one test per checklist row.
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Grammar;

public sealed class TemporalLiteralTests
{
    /// <summary>docs/grammar.html:885 — date | time | timeWithTimeZone | timestamp | timestampWithTimeZone</summary>
    [Fact]
    public void DateAndTime_AllFiveKinds_FormARow() // GRM-0885
    {
        using IDbConnection connection = TestDatabase.Open();

        List<dynamic> rows = connection
            .Query("SELECT DATE '2024-01-02', TIME '12:30:00', TIME WITH TIME ZONE '12:30:00+01', TIMESTAMP '2024-01-02 12:30:00', TIMESTAMP WITH TIME ZONE '2024-01-02 12:30:00+01'")
            .AsList();

        Assert.Single(rows);
    }

    /// <summary>docs/grammar.html:1172 — TIME [ WITHOUT TIME ZONE ] 'hh:mm:ss[.nnnnnnnnn]'</summary>
    [Fact]
    public void TimeLiteral_KeepsTheClockFields() // GRM-1172
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(12, connection.QuerySingle<int>("SELECT HOUR(TIME '12:30:45')"));
        Assert.Equal(45, connection.QuerySingle<int>("SELECT SECOND(TIME '12:30:45')"));
    }

    /// <summary>docs/grammar.html:1191 — TIME WITH TIME ZONE 'hh:mm:ss[.nnnnnnnnn]{ { Z } | { - | + } timeZoneOffsetString}'</summary>
    [Fact]
    public void TimeWithTimeZone_AcceptsOffsetAndZ() // GRM-1191
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(23, connection.QuerySingle<int>("SELECT HOUR(TIME WITH TIME ZONE '23:59:59+01')"));
        Assert.Equal(0, connection.QuerySingle<int>("SELECT HOUR(TIME WITH TIME ZONE '0:00:00Z')"));
    }

    /// <summary>docs/grammar.html:1210 — TIMESTAMP [ WITHOUT TIME ZONE ] '[-]yyyy-MM-dd hh:mm:ss[.nnnnnnnnn]'</summary>
    [Fact]
    public void TimestampLiteral_KeepsTheFields() // GRM-1210
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2024, connection.QuerySingle<int>("SELECT YEAR(TIMESTAMP '2024-01-02 03:04:05')"));
        Assert.Equal(5, connection.QuerySingle<int>("SELECT SECOND(TIMESTAMP '2024-01-02 03:04:05')"));
    }

    /// <summary>docs/grammar.html:1229 — TIMESTAMP WITH TIME ZONE '[-]yyyy-MM-dd hh:mm:ss[.nnnnnnnnn]</summary>
    [Fact]
    public void TimestampWithTimeZone_WithOffset_KeepsLocalFields() // GRM-1229
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2024, connection.QuerySingle<int>(
            "SELECT YEAR(TIMESTAMP WITH TIME ZONE '2024-01-02 03:04:05+01:00')"));
    }

    /// <summary>docs/grammar.html:1230 — [ { Z } | { - | + } timeZoneOffsetString | { timeZoneNameString } ]'</summary>
    [Fact]
    public void TimestampWithTimeZone_AcceptZOffsetAndName() // GRM-1230
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(23, connection.QuerySingle<int>(
            "SELECT HOUR(TIMESTAMP WITH TIME ZONE '2005-12-31 23:59:59Z')"));
        Assert.Equal(23, connection.QuerySingle<int>(
            "SELECT HOUR(TIMESTAMP WITH TIME ZONE '2005-12-31 23:59:59-10:00')"));
        Assert.Equal(23, connection.QuerySingle<int>(
            "SELECT HOUR(TIMESTAMP WITH TIME ZONE '2005-12-31 23:59:59 Europe/London')"));
    }
}
