/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The DROP ALL OBJECTS statement, seen through ADO.NET.
 *
 * The statement empties the database: the tables, the sequences and the schemas but the main
 * one are gone, and what was in them is gone with them.
 */
using System;
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2DropAllObjectsTests
{
    [Fact]
    public void TheStatementEmptiesTheDatabase()
    {
        using H2Connection connection = Open("drop_all_objects");

        Execute(connection, "CREATE TABLE T(ID INT)");
        Execute(connection, "INSERT INTO T VALUES (1)");
        Execute(connection, "CREATE SEQUENCE S1");

        Execute(connection, "DROP ALL OBJECTS");

        Assert.ThrowsAny<Exception>(() => Execute(connection, "INSERT INTO T VALUES (2)"));
        Assert.Equal(0, Count(connection, "SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'T'"));
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
    /// Counts the rows of a query.
    /// </summary>
    /// <param name="connection">the connection</param>
    /// <param name="sql">the query</param>
    /// <returns>the number of rows</returns>
    private static int Count(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using DbDataReader reader = command.ExecuteReader();
        int count = 0;
        while (reader.Read())
        {
            count++;
        }

        return count;
    }
}
