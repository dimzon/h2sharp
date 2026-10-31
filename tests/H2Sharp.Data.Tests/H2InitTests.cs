/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The INIT connection-string option: its statements run on the opening session before
 * the connection is handed out, as the reference does.
 */
using System;
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2InitTests
{
    [Fact]
    public void InitScriptRunsOnOpen()
    {
        using H2Connection connection = new(
            "Data Source=init_probe;Storage=Memory;INIT=\"CREATE TABLE T(ID INT);INSERT INTO T VALUES(42)\"");
        connection.Open();

        Assert.Equal("42", Scalar(connection, "SELECT ID FROM T"));
    }

    [Fact]
    public void FailedInitRefusesTheOpen()
    {
        using H2Connection connection = new(
            "Data Source=init_bad;Storage=Memory;INIT=\"THIS IS NOT SQL\"");
        Assert.ThrowsAny<Exception>(() => connection.Open());
    }

    private static string Scalar(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value is null or DBNull
            ? "-"
            : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "-";
    }
}
