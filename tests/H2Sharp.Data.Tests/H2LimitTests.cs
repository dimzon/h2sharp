/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The limits of a query, seen through ADO.NET.
 *
 * The counts of OFFSET and FETCH are expressions, so a limit may name a parameter — that is
 * the form the linq2db provider writes for Take() and First().
 */
using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2LimitTests
{
    [Fact]
    public void TheLimitReadsTheRowsItNames()
    {
        using H2Connection connection = Open("limit");
        Prepare(connection);

        Assert.Equal([1, 2], Ids(connection, "SELECT ID FROM T ORDER BY ID LIMIT 2"));
        Assert.Equal([2, 3], Ids(connection, "SELECT ID FROM T ORDER BY ID LIMIT 2 OFFSET 1"));
        Assert.Equal([3, 4, 5], Ids(connection, "SELECT ID FROM T ORDER BY ID OFFSET 2"));
        Assert.Equal([1, 2], Ids(connection, "SELECT ID FROM T ORDER BY ID FETCH FIRST 2 ROWS ONLY"));
    }

    [Fact]
    public void TheLimitMayBeAParameter()
    {
        using H2Connection connection = Open("limit_parameter");
        Prepare(connection);

        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ID FROM T ORDER BY ID LIMIT $p0";
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "$p0";
        parameter.Value = 3;
        command.Parameters.Add(parameter);

        using DbDataReader reader = command.ExecuteReader();
        System.Collections.Generic.List<int> values = [];
        while (reader.Read())
        {
            values.Add(Convert.ToInt32(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.Equal([1, 2, 3], values);
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
    /// A table with five rows.
    /// </summary>
    /// <param name="connection">the connection</param>
    private static void Prepare(H2Connection connection)
    {
        Execute(connection, "CREATE TABLE T(ID INT)");
        Execute(connection, "INSERT INTO T VALUES (1), (2), (3), (4), (5)");
    }

    /// <summary>
    /// Runs a statement that answers nothing.
    /// </summary>
    /// <param name="connection">the connection</param>
    /// <param name="sql">the statement</param>
    private static void Execute(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        _ = command.ExecuteNonQuery();
    }

    /// <summary>
    /// Reads a column of numbers.
    /// </summary>
    /// <param name="connection">the connection</param>
    /// <param name="sql">the query</param>
    /// <returns>the numbers</returns>
    private static int[] Ids(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using DbDataReader reader = command.ExecuteReader();
        System.Collections.Generic.List<int> values = [];
        while (reader.Read())
        {
            values.Add(Convert.ToInt32(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture));
        }

        return [.. values];
    }
}
