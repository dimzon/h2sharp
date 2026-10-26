/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0. */
using System;
using System.Data.Common;
using Dapper;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2WindowFunctionTests
{
    [Fact]
    public void Issue42RowNumberRunsOnTheServer()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE \"Blogs\"(\"Id\" INT PRIMARY KEY)");
        connection.Execute("INSERT INTO \"Blogs\" VALUES (3), (1), (2)");
        Assert.Equal(new long[] { 1, 2, 3 }, connection.Query<long>(
            "SELECT ROW_NUMBER() OVER (ORDER BY \"Id\") FROM \"Blogs\" ORDER BY \"Id\""));
        Assert.Equal(new long[] { 1, 2 }, connection.Query<long>(
            "SELECT ROW_NUMBER() OVER (ORDER BY \"Id\") FROM \"Blogs\" WHERE \"Id\" > $id ORDER BY \"Id\"", new { id = 1 }));
    }

    [Fact]
    public void TiedRowsHaveDifferentRankAndDenseRank()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE T(ID INT PRIMARY KEY, N INT)");
        connection.Execute("INSERT INTO T VALUES (1, 10), (2, 10), (3, 20)");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT RANK() OVER (ORDER BY N), DENSE_RANK() OVER (ORDER BY N) FROM T ORDER BY ID";
        using DbDataReader reader = command.ExecuteReader();
        Assert.Equal(typeof(long), reader.GetFieldType(0));
        Assert.True(reader.Read()); Assert.Equal(1, reader.GetInt64(0)); Assert.Equal(1, reader.GetInt64(1));
        Assert.True(reader.Read()); Assert.Equal(1, reader.GetInt64(0)); Assert.Equal(1, reader.GetInt64(1));
        Assert.True(reader.Read()); Assert.Equal(3, reader.GetInt64(0)); Assert.Equal(2, reader.GetInt64(1));
    }

    private static H2Connection Open()
    {
        H2Connection connection = new("jdbc:h2:mem:window_functions_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        return connection;
    }
}
