/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 274: the MySQL mode helpers' own surface — epoch conversion of a local timestamp and
 * MySQL's %-format translation — which the SQL suites reach only through the function wrapper.
 */
using System;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Modes;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ModeMySQLHelperTests
{
    [Fact]
    public void UnixTimestampConvertsALocalTimestamp()
    {
        Database database = new(ConnectionInfo.Memory("mysql_unix_timestamp"));
        SessionLocal session = new(database, "sa");
        try
        {
            ValueTimestamp local = ValueTimestamp.Parse("2020-01-01 00:00:00", session);
            TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(new DateTime(2020, 1, 1, 0, 0, 0));
            Assert.Equal(1577836800 - (int)offset.TotalSeconds,
                FunctionsMySQL.UnixTimestamp(session, local));
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void FromUnixTimeTranslatesMySQLFormatTokens()
    {
        Assert.Equal("2020-01-01", FunctionsMySQL.FromUnixTime(1577836800, "%Y-%m-%d"));
        Assert.Equal("2020-01-01 00:00:00", FunctionsMySQL.FromUnixTime(1577836800));
    }
}
