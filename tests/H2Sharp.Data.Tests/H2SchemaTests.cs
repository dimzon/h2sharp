/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The schema of a database, seen through ADO.NET.
 *
 * A schema is a named set of tables. The statement creates one, and the tables that follow
 * may name it: "S1.T" is the table T of the schema S1.
 */
using System;
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2SchemaTests
{
    [Fact]
    public void TheSchemaHoldsTheTablesThatNameIt()
    {
        using H2Connection connection = Open("schema");

        Execute(connection, "CREATE SCHEMA S1");
        Execute(connection, "CREATE TABLE S1.T(ID INT)");
        Execute(connection, "INSERT INTO S1.T VALUES (1), (2)");

        Assert.Equal(["1", "2"], Strings(connection, "SELECT ID FROM S1.T ORDER BY ID"));
    }

    [Fact]
    public void TheSchemaIsNotCreatedTwice()
    {
        using H2Connection connection = Open("schema_twice");

        Execute(connection, "CREATE SCHEMA S1");
        Execute(connection, "CREATE SCHEMA IF NOT EXISTS S1");

        H2SharpException error = Assert.Throws<H2SharpException>(
            () => Execute(connection, "CREATE SCHEMA S1"));
        Assert.Equal(90078, error.H2ErrorCode);
    }

    /// <summary>
    /// Opens a connection to a database of its own.
    /// </summary>
    /// <param name="name">the name of the database</param>
    /// <returns>the connection</returns>
    private static H2Connection Open(string name)
    {
        H2Connection connection = new($"jdbc:h2:mem:ado_{name}");
        connection.Open();
        return connection;
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
