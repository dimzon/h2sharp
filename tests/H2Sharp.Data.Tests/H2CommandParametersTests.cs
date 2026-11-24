/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The parameter collection is created once per command, not once per access: Dapper touches
 * Parameters on every execution, and a fresh wrapper each time is garbage per statement.
 */
using System;
using System.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2CommandParametersTests
{
    [Fact]
    public void ParametersAreStableAcrossAccesses()
    {
        using var cmd = new H2Command();
        Assert.Same(cmd.Parameters, cmd.Parameters);
    }

    [Fact]
    public void ByteArrayDeclaredAsByteIsAnArrayNotBinary()
    {
        using var connection = new H2Connection("Data Source=byte_array_param;Storage=Memory");
        connection.Open();

        using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE T(A integer ARRAY); INSERT INTO T VALUES (ARRAY[20])";
            create.ExecuteNonQuery();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM T WHERE 20 = ANY(?)";
        var parameter = command.CreateParameter();
        parameter.DbType = DbType.Byte;
        parameter.Value = new byte[] { 20 };
        command.Parameters.Add(parameter);

        Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
    }
}
