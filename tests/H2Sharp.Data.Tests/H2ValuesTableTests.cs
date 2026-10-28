/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The table of a VALUES clause, seen through ADO.NET.
 *
 * The rows a VALUES clause names can stand for a table: the table has the rows, and the
 * name after it is the name the table is known by. The names in parentheses after that name
 * are the names the columns of the table are known by.
 */
using System;
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2ValuesTableTests
{
    [Fact]
    public void TheValuesTableReadsTheRowsItNames()
    {
        using H2Connection connection = Open("values_table");

        Assert.Equal(["1", "2"], Strings(connection, "SELECT * FROM (VALUES (1), (2)) v"));
        Assert.Equal(["1", "2"], Strings(connection, "SELECT * FROM (VALUES (1), (2)) AS v"));
        Assert.Equal(["1", "2"], Strings(connection, "SELECT X FROM (VALUES (1), (2)) v(X) ORDER BY X"));
    }

    /// <summary>
    /// Opens a connection to a database of its own.
    /// </summary>
    /// <param name="name">the name of the database</param>
    /// <returns>the connection</returns>
    private static H2Connection Open(string name)
    {
        H2Connection connection = new($"Data Source=ado_{name};Storage=Memory");
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Reads the first column of the rows of a query.
    /// </summary>
    /// <param name="connection">the connection</param>
    /// <param name="sql">the query</param>
    /// <returns>the values</returns>
    private static string[] Strings(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using DbDataReader reader = command.ExecuteReader();
        System.Collections.Generic.List<string> values = [];
        while (reader.Read())
        {
            values.Add(Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture) ?? "-");
        }

        return [.. values];
    }
}
