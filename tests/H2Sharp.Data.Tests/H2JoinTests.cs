/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The joins, seen through ADO.NET.
 *
 * A join reads two tables as one: the rows of the left table and, for each of them, the rows
 * of the right table that the condition of the join accepts. An outer join keeps the rows of
 * the left table that nothing on the right matched.
 */
using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2JoinTests
{
    [Fact]
    public void TheInnerJoinPairsTheRows()
    {
        using H2Connection connection = Open("join");
        Prepare(connection);

        Assert.Equal(
            [1, 2],
            Ids(connection, "SELECT t.ID FROM T t JOIN U u ON t.ID = u.ID ORDER BY t.ID"));
    }

    [Fact]
    public void TheLeftJoinKeepsTheUnmatchedRows()
    {
        using H2Connection connection = Open("join_left");
        Prepare(connection);

        // The third row of T has no match in U, and the outer join keeps it.
        Assert.Equal(
            [1, 2, 3],
            Ids(connection, "SELECT t.ID FROM T t LEFT JOIN U u ON t.ID = u.ID ORDER BY t.ID"));
    }

    [Fact]
    public void TheColumnsOfBothTablesAreReadable()
    {
        using H2Connection connection = Open("join_columns");
        Prepare(connection);

        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT t.NAME, u.NOTE FROM T t JOIN U u ON t.ID = u.ID ORDER BY t.ID";
        using DbDataReader reader = command.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal("a", reader.GetString(0));
        Assert.Equal("x", reader.GetString(1));
        Assert.True(reader.Read());
        Assert.Equal("b", reader.GetString(0));
        Assert.Equal("y", reader.GetString(1));
        Assert.False(reader.Read());
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
    /// Two tables with three and two rows.
    /// </summary>
    /// <param name="connection">the connection</param>
    private static void Prepare(H2Connection connection)
    {
        Execute(connection, "CREATE TABLE T(ID INT, NAME VARCHAR(20))");
        Execute(connection, "CREATE TABLE U(ID INT, NOTE VARCHAR(20))");
        Execute(connection, "INSERT INTO T VALUES (1, 'a'), (2, 'b'), (3, 'c')");
        Execute(connection, "INSERT INTO U VALUES (1, 'x'), (2, 'y')");
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
