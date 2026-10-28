/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0. */
using Dapper;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2PositionTests
{
    [Fact]
    public void BinaryTokenIsSearchedForAMarkerByte()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE SETTINGS(ID INT PRIMARY KEY, TOKEN VARBINARY)");
        connection.Execute("INSERT INTO SETTINGS VALUES (1, X'007F'), (2, X'807F'), (3, NULL)");

        Assert.Equal(3, connection.QuerySingle<int>("SELECT POSITION(X'80' IN X'007F80FF')"));
        Assert.Equal([2], connection.Query<int>(
            "SELECT ID FROM SETTINGS WHERE POSITION(X'80' IN TOKEN) > 0 ORDER BY ID"));
        Assert.Equal([1, 2], connection.Query<int>(
            "SELECT ID FROM SETTINGS WHERE POSITION(X'7F' IN TOKEN) > 0 ORDER BY ID"));
        Assert.Equal([2], connection.Query<int>(
            "SELECT ID FROM SETTINGS AS s WHERE POSITION($marker IN s.TOKEN) > 0",
            new { marker = new byte[] { 0x80 } }));
        Assert.Empty(connection.Query<int>(
            "SELECT ID FROM SETTINGS WHERE POSITION(X'02' IN TOKEN) > 0"));
    }

    private static H2Connection Open()
    {
        H2Connection connection = new("Data Source=position_ado_" + System.Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        return connection;
    }
}
