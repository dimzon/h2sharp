// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// FORMATDATETIME / PARSEDATETIME coverage with .NET format strings, locales and zones.
using System;
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Functions;

public sealed class DateTimeFormatCoverageTests
{
    [Fact]
    public void FormatDateTimeSupportsNetFormatComponents()
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("2024-05-06", connection.QuerySingle<string>(
            "SELECT FORMATDATETIME(TIMESTAMP '2024-05-06 10:20:30', 'yyyy-MM-dd')"));
        Assert.Equal("10:20:30", connection.QuerySingle<string>(
            "SELECT FORMATDATETIME(TIMESTAMP '2024-05-06 10:20:30', 'HH:mm:ss')"));
        Assert.Equal("Monday, May 6, 2024", connection.QuerySingle<string>(
            "SELECT FORMATDATETIME(TIMESTAMP '2024-05-06 10:20:30', 'dddd, MMMM d, yyyy', 'en-US')"));
        Assert.Equal("123", connection.QuerySingle<string>(
            "SELECT FORMATDATETIME(TIMESTAMP '2024-05-06 10:20:30.123', 'fff')"));
    }

    [Fact]
    public void FormatDateTimeAppliesLocaleAndTimeZone()
    {
        using IDbConnection connection = TestDatabase.Open();

        string formatted = connection.QuerySingle<string>(
            "SELECT FORMATDATETIME(" +
            "TIMESTAMP WITH TIME ZONE '2024-05-06 10:20:30+02:00', " +
            "'yyyy-MM-dd HH:mm:ss zzz', 'en-US', 'UTC')");

        Assert.StartsWith("2024-05-06 08:20:30 ", formatted);
    }

    [Fact]
    public void ParseDateTimeAppliesLocaleAndReturnsOffset()
    {
        using IDbConnection connection = TestDatabase.Open();

        int offset = connection.QuerySingle<int>(
            "SELECT EXTRACT(TIMEZONE_HOUR FROM " +
            "PARSEDATETIME('2024-05-06 10:20:30', 'yyyy-MM-dd HH:mm:ss', 'en-US', 'Europe/Berlin'))");

        Assert.Equal(2, offset);
    }

    [Fact]
    public void NullInputsProduceNullOutputs()
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Null(connection.QuerySingle<string>(
            "SELECT FORMATDATETIME(NULL, 'yyyy')"));
        Assert.Null(connection.QuerySingle<string>(
            "SELECT FORMATDATETIME(TIMESTAMP '2024-01-01 00:00:00', NULL)"));
    }

    [Fact]
    public void InvalidInputTypeIsRejected()
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.ThrowsAny<Exception>(() => connection.Execute(
            "SELECT FORMATDATETIME('2024-01-01', 'yyyy')"));
    }
}
