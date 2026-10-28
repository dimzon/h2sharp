/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A derived table whose query joins two tables, filtered from the outside.
 */
using System;
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2DerivedJoinTests
{
    [Fact]
    public void TheOuterFilterSeesTheRowsOfAJoinedDerivedTable()
    {
        using H2Connection connection = new("Data Source=ado_derived_join;Storage=Memory");
        connection.Open();
        Execute(connection, "CREATE TABLE ACCOUNTS(ID INTEGER PRIMARY KEY, STATUS VARCHAR(255), TAG VARCHAR(255))");
        Execute(connection, "INSERT INTO ACCOUNTS VALUES (31, 'X', 'A')");
        Execute(connection, "CREATE TABLE PARENT(ID INT)");
        Execute(connection, "INSERT INTO PARENT VALUES(31)");

        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM (SELECT A.STATUS, A.TAG "
            + "FROM ACCOUNTS A, PARENT T WHERE A.ID = T.ID) X "
            + "WHERE STATUS='X' AND TAG IN ('A','B')";
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("X", reader.GetString(0));
        Assert.Equal("A", reader.GetString(1));
        Assert.False(reader.Read());
    }

    private static void Execute(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        _ = command.ExecuteNonQuery();
    }
}
