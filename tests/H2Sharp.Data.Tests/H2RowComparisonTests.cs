/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0. */
using System;
using Dapper;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2RowComparisonTests
{
    [Fact]
    public void Issue18CompositeCursorReturnsTheNextRows()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE T(ID INTEGER PRIMARY KEY, N INTEGER)");
        connection.Execute("INSERT INTO T VALUES (1, 10), (2, 20), (3, 80)");
        Assert.Equal(new[] { 2, 3 }, connection.Query<int>(
            "SELECT ID FROM T WHERE (N, ID) > (15, 1) ORDER BY ID"));
        Assert.Equal(new[] { 2, 3 }, connection.Query<int>(
            "SELECT ID FROM T WHERE (N, ID) > ($n, $id) ORDER BY ID", new { n = 15, id = 1 }));
    }

    [Fact]
    public void TimestampCursorUsesTheIdToBreakTies()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE POSTS(ID INT PRIMARY KEY, CREATED_AT TIMESTAMP)");
        DateTime date = new(2026, 10, 23, 12, 0, 0);
        for (int i = 1; i <= 5; i++) connection.Execute("INSERT INTO POSTS VALUES ($id, $date)",
            new { id = i, date = i <= 3 ? date : date.AddDays(1) });
        connection.Execute("CREATE INDEX POSTS_CURSOR ON POSTS(CREATED_AT, ID)");
        const string Sql = "SELECT p.ID FROM POSTS AS p WHERE (p.CREATED_AT, p.ID) > ($date, $id) ORDER BY p.CREATED_AT, p.ID LIMIT $take";
        Assert.Equal(new[] { 3, 4 }, connection.Query<int>(Sql, new { date, id = 2, take = 2 }));
        Assert.Equal(new[] { 5 }, connection.Query<int>(Sql, new { date = date.AddDays(1), id = 4, take = 2 }));
        Assert.Empty(connection.Query<int>(Sql, new { date = date.AddDays(1), id = 5, take = 2 }));
    }

    private static H2Connection Open()
    {
        H2Connection connection = new("Data Source=row_cursor_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        return connection;
    }
}
