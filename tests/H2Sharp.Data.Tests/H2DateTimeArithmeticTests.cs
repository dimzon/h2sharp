/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0. */
using System;
using Dapper;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2DateTimeArithmeticTests
{
    [Fact]
    public void Issue20TimestampDifferenceReturnsExactTotalHours()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE T(ID INTEGER PRIMARY KEY, A TIMESTAMP(9), B TIMESTAMP(9))");
        connection.Execute("INSERT INTO T VALUES (1, TIMESTAMP '2024-02-10 09:00:00', TIMESTAMP '2024-05-30 12:00:00'), "
            + "(2, TIMESTAMP '2024-02-28 23:30:00', TIMESTAMP '2024-02-29 01:00:00'), "
            + "(3, TIMESTAMP '2024-02-29 01:00:00', TIMESTAMP '2024-02-28 23:30:00'), "
            + "(4, TIMESTAMP '2024-02-29 01:00:00', NULL)");
        Assert.Equal(new decimal?[] { 2643m, 1.5m, -1.5m, null }, connection.Query<decimal?>(
            "SELECT EXTRACT(EPOCH FROM (B - A)) / 3600 FROM T ORDER BY ID"));
        Assert.Equal(new TimeSpan?[] { TimeSpan.FromHours(2643), TimeSpan.FromMinutes(90), TimeSpan.FromMinutes(-90), null },
            connection.Query<TimeSpan?>("SELECT B - A FROM T ORDER BY ID"));
    }

    [Fact]
    public void ParametersCanBeUsedForTimestampDifferenceAndIntervalAddition()
    {
        using H2Connection connection = Open();
        DateTime start = new(2024, 2, 28, 23, 30, 0), end = start.AddMinutes(90);
        const string HoursSql = "SELECT EXTRACT(EPOCH FROM (CAST(@end AS TIMESTAMP) - CAST(@start AS TIMESTAMP))) / 3600";
        Assert.Equal(1.5m, connection.QuerySingle<decimal>(HoursSql, new { start, end }));
        Assert.Equal(-1.5m, connection.QuerySingle<decimal>(HoursSql, new { start = end, end = start }));
        Assert.Equal(end, connection.QuerySingle<DateTime>(
            "SELECT CAST(@start AS TIMESTAMP) + CAST(@duration AS INTERVAL DAY TO SECOND)",
            new { start, duration = TimeSpan.FromMinutes(90) }));
    }

    [Fact]
    public void InvalidIntervalOperationsReturnH2Errors()
    {
        using H2Connection connection = Open();
        H2SharpException outOfRange = Assert.Throws<H2SharpException>(() => connection.ExecuteScalar(
            "SELECT TIME '23:00:00' + INTERVAL '2' HOUR"));
        Assert.Equal(22003, outOfRange.H2ErrorCode);
        H2SharpException division = Assert.Throws<H2SharpException>(() => connection.ExecuteScalar("SELECT INTERVAL '1' DAY / 0"));
        Assert.Equal(50000, division.H2ErrorCode);
        H2SharpException incompatible = Assert.Throws<H2SharpException>(() => connection.ExecuteScalar(
            "SELECT INTERVAL '1' DAY + INTERVAL '1' MONTH"));
        Assert.Equal(50100, incompatible.H2ErrorCode);
    }

    private static H2Connection Open()
    {
        H2Connection connection = new("jdbc:h2:mem:datetime_ado_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        return connection;
    }
}
