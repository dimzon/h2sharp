/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * ROWNUM() smoke test: the reference's Rownum expression reads the running statement's
 * current row number. The port claims ROWNUM is not ported in the DML parse-only layer;
 * this locks the SELECT forms end to end through ADO, the way a consumer runs them.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Tests;

public sealed class RownumSmokeTests
{
    [Fact]
    public void SelectRownumNumbersTheRows()
    {
        using H2Connection connection = new(
            "Data Source=rownum_smoke_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();

        Exec(connection, "CREATE TABLE T(ID INT)");
        Exec(connection, "INSERT INTO T VALUES 10, 20, 30");

        // A plain select over a table numbers the rows in read order.
        Assert.Equal(["1:10", "2:20", "3:30"], Rows(connection, "SELECT ROWNUM(), ID FROM T"));

        // Without FROM the dual table has one row.
        Assert.Equal(["1"], Rows(connection, "SELECT ROWNUM()"));

        // The parentheses are optional in the reference.
        Assert.Equal(["1", "2", "3"], Rows(connection, "SELECT ROWNUM FROM T"));

        // A table function still numbers its rows.
        Assert.Equal(["1", "2", "3"], Rows(connection, "SELECT ROWNUM() FROM SYSTEM_RANGE(1, 3)"));

        // ROWNUM is evaluated after the filter and before ORDER BY, so the condition
        // keeps the first two rows.
        Assert.Equal(["1", "2"], Rows(connection, "SELECT ROWNUM() FROM T WHERE ROWNUM() <= 2"));

        connection.Close();
    }

    private static void Exec(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static List<string> Rows(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using DbDataReader reader = command.ExecuteReader();
        List<string> rows = [];
        while (reader.Read())
        {
            List<string> cells = [];
            for (int i = 0; i < reader.FieldCount; i++)
            {
                cells.Add(reader.IsDBNull(i) ? "-" : reader.GetString(i));
            }

            rows.Add(string.Join(":", cells));
        }

        return rows;
    }
}
