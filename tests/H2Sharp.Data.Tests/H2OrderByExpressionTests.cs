/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0. */
using System;
using System.Data.Common;
using Dapper;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2OrderByExpressionTests
{
    [Fact]
    public void Issue21OrdersByAnExpressionOutsideTheProjection()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE T(ID INTEGER PRIMARY KEY, N INTEGER)");
        connection.Execute("INSERT INTO T VALUES (1, 10), (2, 20), (3, 80)");
        Assert.Equal(new[] { 1, 2, 3 }, connection.Query<int>("SELECT ID FROM T ORDER BY MOD(ID, 7)"));
        Assert.Equal(new[] { 1, 3, 2 }, connection.Query<int>("SELECT ID FROM T ORDER BY N % 7, ID"));
        Assert.Equal(new[] { 1, 3 }, connection.Query<int>(
            "SELECT ID FROM T ORDER BY MOD(N, $divisor), ID LIMIT $take", new { divisor = 7, take = 2 }));
        Assert.Equal(new[] { 1, 2 }, connection.Query<int>(
            "SELECT ID FROM T ORDER BY MOD(N, $divisor), ID LIMIT $take", new { divisor = 9, take = 2 }));

        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ID AS RESULT_ID FROM T ORDER BY MOD(N, 7), ID";
        using DbDataReader reader = command.ExecuteReader();
        Assert.Equal(1, reader.FieldCount);
        Assert.Equal("RESULT_ID", reader.GetName(0));
        Assert.Equal(1, reader.GetSchemaTable()!.Rows.Count);
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
    }

    [Fact]
    public void BirthWeekdayOrderingRunsOnTheServer()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE USERS(ID INT PRIMARY KEY, BIRTH_DATE DATE)");
        connection.Execute("INSERT INTO USERS VALUES (1, DATE '2024-03-02'), (2, DATE '2024-03-03'), "
            + "(3, DATE '2024-03-04'), (4, DATE '2024-03-01'), (5, NULL)");
        Assert.Equal(new[] { 2, 3, 4, 1, 5 }, connection.Query<int>(
            "SELECT \"u\".\"ID\" FROM \"USERS\" AS \"u\" "
            + "ORDER BY MOD(ISO_DAY_OF_WEEK(\"u\".\"BIRTH_DATE\"), 7) NULLS LAST, \"u\".\"ID\""));
    }

    [Fact]
    public void GroupedCountsAreOrderedByAggregateExpression()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE T(ID INTEGER PRIMARY KEY)");
        connection.Execute("INSERT INTO T VALUES (1), (2), (3), (4), (5)");
        Assert.Equal(new[] { 1, 0 }, connection.Query<int>(
            "SELECT MOD(ID, 2) AS G FROM T GROUP BY MOD(ID, 2) ORDER BY COUNT(*) DESC, G"));
    }

    private static H2Connection Open()
    {
        H2Connection connection = new("Data Source=order_expression_ado_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        return connection;
    }
}
