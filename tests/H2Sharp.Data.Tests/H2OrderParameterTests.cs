/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0. */
using System;
using System.Data.Common;
using Dapper;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2OrderParameterTests
{
    [Fact]
    public void Issue28PositionChangesSortingAndRecoversAfterInvalidValue()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE T(ID INT PRIMARY KEY, N INT)");
        connection.Execute("INSERT INTO T VALUES (1, 80), (2, 10), (3, 20)");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ID, N FROM T ORDER BY $position";
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "position";
        parameter.Value = 2;
        command.Parameters.Add(parameter);
        command.Prepare();
        Assert.Equal(new[] { 2, 3, 1 }, ReadIds(command));
        parameter.Value = 1;
        Assert.Equal(new[] { 1, 2, 3 }, ReadIds(command));
        parameter.Value = -2;
        Assert.Equal(new[] { 1, 3, 2 }, ReadIds(command));
        parameter.Value = 3;
        Assert.Equal(90068, Assert.Throws<H2SharpException>(() => command.ExecuteReader()).H2ErrorCode);
        parameter.Value = 2;
        Assert.Equal(new[] { 2, 3, 1 }, ReadIds(command));
        parameter.Value = DBNull.Value;
        Assert.Equal(new[] { 1, 2, 3 }, ReadIds(command));
    }

    [Fact]
    public void BatchUsesEachStatementsTokensAndKeepsParameterPositions()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE T(ID INT PRIMARY KEY, N INT)");
        connection.Execute("INSERT INTO T VALUES (1, 80), (2, 10), (3, 20)");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ID, N FROM T ORDER BY $first; SELECT ID, N FROM T ORDER BY $second DESC";
        foreach (string name in new[] { "first", "second" })
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = 2;
            command.Parameters.Add(parameter);
        }
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(2, reader.GetInt32(0));
        Assert.True(reader.NextResult());
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.False(reader.NextResult());
    }

    [Fact]
    public void IndexedParameterUsedTwiceKeepsOneValue()
    {
        using H2Connection connection = Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ?1 + ?1";
        DbParameter parameter = command.CreateParameter();
        parameter.Value = 7;
        command.Parameters.Add(parameter);
        Assert.Equal(14m, command.ExecuteScalar());
    }

    private static int[] ReadIds(DbCommand command)
    {
        using DbDataReader reader = command.ExecuteReader();
        System.Collections.Generic.List<int> ids = [];
        while (reader.Read()) ids.Add(reader.GetInt32(0));
        return ids.ToArray();
    }

    private static H2Connection Open()
    {
        H2Connection connection = new("jdbc:h2:mem:order_parameter_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        return connection;
    }
}
