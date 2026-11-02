// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/functions.html — DATE_TRUNC over every deterministic field.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Functions;

public sealed class DateTruncCoverageTests
{
    private const string Moment = "TIMESTAMP '2024-05-06 07:08:09.123456789'";

    private static string Trunc(IDbConnection connection, string field)
        => connection.QuerySingle<string>($"SELECT CAST(DATE_TRUNC('{field}', {Moment}) AS VARCHAR)");

    /// <summary>docs/functions.html:2835 — DATE_TRUNC to a sub-day field</summary>
    [Fact]
    public void DateTrunc_SubDayFields_CutTheFraction()
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("2024-05-06 07:08:09.123456", Trunc(connection, "MICROSECOND"));
        Assert.Equal("2024-05-06 07:08:09.123", Trunc(connection, "MILLISECOND"));
        Assert.Equal("2024-05-06 07:08:09", Trunc(connection, "SECOND"));
        Assert.Equal("2024-05-06 07:08:00", Trunc(connection, "MINUTE"));
        Assert.Equal("2024-05-06 07:00:00", Trunc(connection, "HOUR"));
        Assert.Equal("2024-05-06 00:00:00", Trunc(connection, "DAY"));
    }

    /// <summary>docs/functions.html:2835 — DATE_TRUNC to a calendar field</summary>
    [Fact]
    public void DateTrunc_CalendarFields_CutToTheBoundary()
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("2024-05-06 00:00:00", Trunc(connection, "ISO_WEEK"));
        Assert.Equal("2024-04-01 00:00:00", Trunc(connection, "QUARTER"));
        Assert.Equal("2024-01-01 00:00:00", Trunc(connection, "YEAR"));
        Assert.Equal("2020-01-01 00:00:00", Trunc(connection, "DECADE"));
        Assert.Equal("2001-01-01 00:00:00", Trunc(connection, "CENTURY"));
        Assert.Equal("2024-01-01 00:00:00", Trunc(connection, "ISO_WEEK_YEAR"));
    }

}
