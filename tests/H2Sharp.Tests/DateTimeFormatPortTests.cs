/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * FORMATDATETIME and PARSEDATETIME with .NET format patterns — the approved decision for
 * date-time formatting (see AGENTS.md, TO_CHAR/REGEXP area), so there is no common part with
 * the reference to compare and the tests pin the .NET behavior: patterns, the locale and
 * timeZone arguments, and the refusals.
 */
using System;
using System.Data.Common;
using H2Sharp;
using H2Sharp.Data;
using H2Sharp.Message;
using Xunit;

namespace H2Sharp.Tests;

public sealed class DateTimeFormatPortTests
{
    /// <summary>SELECT over an in-memory connection, one scalar per statement.</summary>
    private static object? SelectScalar(string sql)
    {
        using H2Connection connection = new("Data Source=dt_format_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    [Fact]
    public void FormatDateTimeUsesThePattern()
    {
        Assert.Equal("2020-01-02 03:04:05",
            SelectScalar("SELECT FORMATDATETIME(TIMESTAMP '2020-01-02 03:04:05', 'yyyy-MM-dd HH:mm:ss')"));
        Assert.Equal("02.01.2020 03:04",
            SelectScalar("SELECT FORMATDATETIME(TIMESTAMP '2020-01-02 03:04:05', 'dd.MM.yyyy HH:mm')"));
        // A DATE value formats through its midnight timestamp.
        Assert.Equal("2020-01-02",
            SelectScalar("SELECT FORMATDATETIME(DATE '2020-01-02', 'yyyy-MM-dd')"));
    }

    [Fact]
    public void FormatDateTimeAnswersNullForNullInputs()
    {
        Assert.Equal(DBNull.Value,
            SelectScalar("SELECT FORMATDATETIME(CAST(NULL AS TIMESTAMP), 'yyyy')"));
        Assert.Equal(DBNull.Value,
            SelectScalar("SELECT FORMATDATETIME(TIMESTAMP '2020-01-02 03:04:05', NULL)"));
    }

    [Fact]
    public void FormatDateTimeAppliesLocaleAndTimeZone()
    {
        // The locale chooses the culture the pattern runs under: the German January.
        Assert.Equal("Januar",
            SelectScalar("SELECT FORMATDATETIME(TIMESTAMP '2020-01-02 03:04:05', 'MMMM', 'de')"));
        // The zone shifts a timestamp with offset before formatting: 03:04:05+01 is 02:04:05
        // in UTC. A plain TIMESTAMP has no zone to shift, so the argument is ignored for it.
        Assert.Equal("2020-01-02 02:04:05",
            SelectScalar("SELECT FORMATDATETIME(TIMESTAMP WITH TIME ZONE '2020-01-02 03:04:05+01', 'yyyy-MM-dd HH:mm:ss', NULL, 'UTC')"));
    }

    [Fact]
    public void ParseDateTimeBuildsTheTimestamp()
    {
        // Without a zone the parsed value carries the zero offset, as a timestamp with time zone.
        Assert.Equal("2020-01-02 03:04:05+00",
            SelectScalar("SELECT CAST(PARSEDATETIME('2020-01-02 03:04:05', 'yyyy-MM-dd HH:mm:ss') AS VARCHAR)"));
        // The timeZone argument becomes the parsed value's offset.
        Assert.Equal("2020-01-02 03:04:05+02",
            SelectScalar("SELECT CAST(PARSEDATETIME('2020-01-02 03:04:05', 'yyyy-MM-dd HH:mm:ss', NULL, 'Europe/Athens') AS VARCHAR)"));
    }

    [Fact]
    public void FormatAndParseRefuseBadInput()
    {
        // A non-date-time value cannot be formatted.
        Assert.Equal(H2Sharp.Api.ErrorCode.InvalidValue2,
            Assert.Throws<H2SharpException>(
                () => SelectScalar("SELECT FORMATDATETIME(1, 'yyyy')")).H2ErrorCode);
        // An unparsable text and an unknown zone refuse with their codes.
        Assert.Equal(H2Sharp.Api.ErrorCode.ParseError1,
            Assert.Throws<H2SharpException>(
                () => SelectScalar("SELECT PARSEDATETIME('no date', 'yyyy-MM-dd')")).H2ErrorCode);
        Assert.Equal(H2Sharp.Api.ErrorCode.InvalidValue2,
            Assert.Throws<H2SharpException>(
                () => SelectScalar("SELECT FORMATDATETIME(TIMESTAMP WITH TIME ZONE '2020-01-02 03:04:05+01', 'yyyy', NULL, 'Not/AZone')")).H2ErrorCode);
    }
}
