/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A time of day and a length of time, seen through ADO.NET.
 *
 * The two are both a TimeSpan in .NET, and the engine has two types for them: a TIME is a
 * time of day, and an INTERVAL DAY TO SECOND is a length. The declared type of the parameter
 * says which one the caller means, the way the reference's JDBC takes a java.sql.Time for a
 * TIME column and a Duration for an INTERVAL.
 */
using System;
using System.Data;
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2TimeParameterTests
{
    /// <summary>
    /// A time of day declared as a time goes into a TIME column.
    /// </summary>
    [Fact]
    public void ATimeSpanDeclaredAsATimeGoesIntoATimeColumn()
    {
        using H2Connection connection = Open("time_parameter");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE T(VAL TIME)";
        _ = command.ExecuteNonQuery();

        command.CommandText = "INSERT INTO T(VAL) VALUES ($v)";
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "$v";
        parameter.DbType = DbType.Time;
        parameter.Value = new TimeSpan(12, 34, 56);
        command.Parameters.Add(parameter);
        Assert.Equal(1, command.ExecuteNonQuery());

        command.Parameters.Clear();
        command.CommandText = "SELECT VAL FROM T";
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(new TimeSpan(12, 34, 56), (TimeSpan)reader.GetValue(0));
    }

    [Fact]
    public void TimeOnlyWithoutADeclaredTypeIsAlwaysATimeOfDay()
    {
        using H2Connection connection = Open("timeonly_parameter");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE T(VAL TIME(9))";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO T VALUES ($time)";
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "time";
        TimeOnly time = new TimeOnly(10, 0).Add(TimeSpan.FromTicks(1));
        parameter.Value = time;
        command.Parameters.Add(parameter);
        Assert.Equal(1, command.ExecuteNonQuery());
        command.Parameters.Clear();
        command.CommandText = "SELECT VAL FROM T";
        Assert.Equal(time.ToTimeSpan(), command.ExecuteScalar());
    }

    /// <summary>
    /// The same value without the declaration is a length of time, and the engine says so:
    /// the answer is an INTERVAL DAY TO SECOND, not a time of day.
    /// </summary>
    [Fact]
    public void ATimeSpanWithoutADeclaredTypeIsALengthOfTime()
    {
        using H2Connection connection = Open("interval_parameter");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT $v";
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "$v";
        parameter.Value = new TimeSpan(1, 2, 3);
        command.Parameters.Add(parameter);

        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("INTERVAL DAY TO SECOND", reader.GetDataTypeName(0));
        Assert.Equal(new TimeSpan(1, 2, 3), (TimeSpan)reader.GetValue(0));
    }

    /// <summary>
    /// A time of day that does not fit in a day is not a time of day: the engine refuses it
    /// the way it refuses any other impossible time.
    /// </summary>
    [Fact]
    public void ATimeLongerThanADayIsRefused()
    {
        using H2Connection connection = Open("time_parameter_long");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE T(VAL TIME)";
        _ = command.ExecuteNonQuery();

        command.CommandText = "INSERT INTO T(VAL) VALUES ($v)";
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "$v";
        parameter.DbType = DbType.Time;
        parameter.Value = new TimeSpan(25, 0, 0);
        command.Parameters.Add(parameter);
        Assert.Throws<H2SharpException>(() => command.ExecuteNonQuery());
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
}
