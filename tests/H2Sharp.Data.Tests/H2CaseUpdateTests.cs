/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * UPDATE with a CASE expression over the row being updated.
 */
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2CaseUpdateTests
{
    [Fact]
    public void UpdateSetsValuesFromACaseOverTheCurrentRow()
    {
        using H2Connection connection = new("Data Source=ado_case_update;Storage=Memory;Mode=MySQL");
        connection.Open();
        Execute(connection, "CREATE TABLE T(ID INT PRIMARY KEY, U INT)");
        Execute(connection, "INSERT INTO T VALUES (1, 1), (2, 2)");
        Execute(connection, "UPDATE T SET U = CASE WHEN ID = 1 THEN 2 ELSE 1 END");

        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ID, U FROM T ORDER BY ID";
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(2, reader.GetInt32(1));
        Assert.True(reader.Read());
        Assert.Equal(2, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.False(reader.Read());
    }

    private static void Execute(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        _ = command.ExecuteNonQuery();
    }
}
