/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The current date and time functions, seen through ADO.NET.
 *
 * The functions read the clock of the machine, so what is checked here is the shape of the
 * answer: the type the caller gets, and the fact that the answer is the time of the day the
 * test was run on. The second test holds the clock still through the seam of
 * <see cref="IH2Test"/>, and then every answer is an exact one.
 */
using System;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2CurrentDateTimeTests
{
    [Fact]
    public void TheCurrentDateAndTimeFunctionsAnswerTheTimeOfTheDay()
    {
        using H2Connection connection = Open("current_time");
        DateTime before = DateTime.Now;

        Assert.Equal(typeof(DateTime), FieldType(connection, "SELECT CURRENT_DATE FROM (VALUES (1))"));
        Assert.Equal(typeof(DateTimeOffset), FieldType(connection, "SELECT CURRENT_TIME FROM (VALUES (1))"));
        Assert.Equal(typeof(TimeSpan), FieldType(connection, "SELECT LOCALTIME FROM (VALUES (1))"));
        Assert.Equal(typeof(DateTimeOffset), FieldType(connection, "SELECT CURRENT_TIMESTAMP FROM (VALUES (1))"));
        Assert.Equal(typeof(DateTime), FieldType(connection, "SELECT LOCALTIMESTAMP FROM (VALUES (1))"));

        object? date = Value(connection, "SELECT CURRENT_DATE FROM (VALUES (1))");
        Assert.IsType<DateTime>(date);
        Assert.Equal(DateTime.Now.Date, ((DateTime)date!).Date);

        object? time = Value(connection, "SELECT LOCALTIME FROM (VALUES (1))");
        Assert.IsType<TimeSpan>(time);
        Assert.True(
            (TimeSpan)time! >= before.TimeOfDay - TimeSpan.FromMinutes(1)
                && (TimeSpan)time! <= DateTime.Now.TimeOfDay + TimeSpan.FromMinutes(1),
            "the time of the day " + ((TimeSpan)time!).ToString()
                + " is not the time of the day of the test");
    }

    /// <summary>
    /// The clock the test holds still: the port asks the seam of <see cref="IH2Test"/> for the
    /// time instead of the machine, and then every answer is the one the seam gives.
    /// </summary>
    [Fact]
    public void TheCurrentDateAndTimeFunctionsReadTheClockTheTestHolds()
    {
        DateTimeOffset now = FixedLocalTime(2017, 6, 15, 12, 34, 56);
        using H2Connection connection = new($"Data Source=ado_current_time_seam;Storage=Memory");

        // The seam is set before the connection opens: the connection reads it when it opens,
        // and a clock that is set afterwards is a clock the session never learns about.
        ((IH2Test)connection).SystemClockProvider = () => now;
        connection.Open();

        DateTime local = now.LocalDateTime;
        int offsetSeconds = (int)now.Offset.TotalSeconds;

        // A DATE is a reading with no zone, so the instant is moved into the zone of the
        // reader first: the date is the one the machine's calendar shows.
        Assert.Equal(
            local.Date,
            Assert.IsType<DateTime>(Value(connection, "SELECT CURRENT_DATE FROM (VALUES (1))")).Date);

        // A TIME WITH TIME ZONE is the time of day with the offset of the zone.
        DateTimeOffset time = Assert.IsType<DateTimeOffset>(
            Value(connection, "SELECT CURRENT_TIME FROM (VALUES (1))"));
        Assert.Equal(local.TimeOfDay, time.TimeOfDay);
        Assert.Equal(offsetSeconds, (int)time.Offset.TotalSeconds);

        // A TIME is a time of day with no zone: the one of the reader.
        Assert.Equal(
            local.TimeOfDay,
            Assert.IsType<TimeSpan>(Value(connection, "SELECT LOCALTIME FROM (VALUES (1))")));

        // A TIMESTAMP WITH TIME ZONE is the instant itself.
        DateTimeOffset timestamp = Assert.IsType<DateTimeOffset>(
            Value(connection, "SELECT CURRENT_TIMESTAMP FROM (VALUES (1))"));
        Assert.Equal(now, timestamp);

        // A TIMESTAMP is a reading with no zone: the one of the reader.
        DateTime localTimestamp = Assert.IsType<DateTime>(
            Value(connection, "SELECT LOCALTIMESTAMP FROM (VALUES (1))"));
        Assert.Equal(local.Date, localTimestamp.Date);
        Assert.Equal(local.TimeOfDay, localTimestamp.TimeOfDay);
    }

    /// <summary>
    /// The scale in parentheses narrows the fraction of a second, and the reference refuses
    /// a scale it does not have.
    /// </summary>
    [Fact]
    public void TheScaleOfTheCurrentTimestampIsTheOneWritten()
    {
        using H2Connection connection = Open("current_time_scale");

        object? value = Value(connection, "SELECT CURRENT_TIMESTAMP(3) FROM (VALUES (1))");
        Assert.IsType<DateTimeOffset>(value);

        // A scale of three leaves three digits after the point, so what is left of the second
        // is a whole number of milliseconds.
        Assert.Equal(0, ((DateTimeOffset)value!).Ticks % TimeSpan.TicksPerMillisecond);

        Assert.Equal(
            90151,
            ErrorCode(connection, "SELECT CURRENT_TIMESTAMP(99) FROM (VALUES (1))"));
    }

    /// <summary>
    /// A moment of the machine's own calendar, with the offset the machine's zone has at that
    /// moment.
    /// </summary>
    /// <remarks>
    /// The offset is the machine's own on purpose. The clock answers with an instant, and the
    /// readings without a zone are the ones of the zone of the reader — the machine's zone, the
    /// same one the reference reads. A clock that answered with some other offset would put
    /// the two apart, and the test would then be testing the difference between the offsets
    /// rather than the functions.
    /// </remarks>
    /// <param name="year">the year</param>
    /// <param name="month">the month</param>
    /// <param name="day">the day</param>
    /// <param name="hour">the hour</param>
    /// <param name="minute">the minute</param>
    /// <param name="second">the second</param>
    /// <returns>the moment</returns>
    private static DateTimeOffset FixedLocalTime(
        int year, int month, int day, int hour, int minute, int second)
    {
        DateTime local = new(year, month, day, hour, minute, second, DateTimeKind.Local);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    /// <summary>
    /// Opens a connection to a database of its own.
    /// </summary>
    /// <param name="name">the name of the database</param>
    /// <returns>the connection</returns>
    private static H2Connection Open(string name)
    {
        H2Connection connection = new($"Data Source=ado_{name};Storage=Memory");
        connection.Open();
        return connection;
    }

    /// <summary>
    /// The type the caller reads the first column as.
    /// </summary>
    /// <param name="connection">the connection</param>
    /// <param name="sql">the query</param>
    /// <returns>the type</returns>
    private static Type FieldType(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        return reader.GetFieldType(0);
    }

    /// <summary>
    /// The value of the first column of the first row.
    /// </summary>
    /// <param name="connection">the connection</param>
    /// <param name="sql">the query</param>
    /// <returns>the value</returns>
    private static object? Value(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        return reader.GetValue(0);
    }

    /// <summary>
    /// The H2 error code the statement fails with.
    /// </summary>
    /// <param name="connection">the connection</param>
    /// <param name="sql">the statement</param>
    /// <returns>the error code</returns>
    private static int ErrorCode(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        H2SharpException error = Assert.Throws<H2SharpException>(() => command.ExecuteReader());
        return error.H2ErrorCode;
    }
}
