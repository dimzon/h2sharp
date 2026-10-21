/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The derived table, seen through ADO.NET.
 *
 * A derived table is a query in the FROM clause that stands for a table: the rows are the
 * rows of the query, and the name after it is the name the table is known by. The names in
 * parentheses after that name are the names the columns of the table are known by.
 */
using System;
using System.Data.Common;
using System.Linq;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2DerivedTableTests
{
    [Fact]
    public void TheDerivedTableReadsTheRowsOfItsQuery()
    {
        using H2Connection connection = Open("derived");
        Prepare(connection);

        Assert.Equal([1, 2, 3], Ids(connection, "SELECT ID FROM (SELECT ID FROM T) x ORDER BY ID"));
        Assert.Equal([1, 2, 3], Ids(connection, "SELECT ID FROM (SELECT ID FROM T) AS x ORDER BY ID"));
        Assert.Equal([1, 2, 3], Ids(connection, "SELECT x.ID FROM (SELECT ID FROM T) x ORDER BY x.ID"));
    }

    [Fact]
    public void TheDerivedTableMayNameItsColumns()
    {
        using H2Connection connection = Open("derived_columns");
        Prepare(connection);

        Assert.Equal([1, 2, 3], Ids(connection, "SELECT C FROM (SELECT ID FROM T) x(C) ORDER BY C"));
        Assert.Equal([1, 2, 3], Ids(connection, "SELECT x.C FROM (SELECT ID FROM T) AS x(C) ORDER BY x.C"));
    }

    [Fact]
    public void TheDerivedTableIsJoinedLikeAnyOtherTable()
    {
        using H2Connection connection = Open("derived_join");
        Prepare(connection);
        Execute(connection, "CREATE TABLE U(ID INT)");
        Execute(connection, "INSERT INTO U VALUES (2)");

        Assert.Equal(
            [2],
            Ids(connection, "SELECT x.ID FROM (SELECT ID FROM T) x JOIN U ON x.ID = U.ID ORDER BY x.ID"));
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
    /// A table with three rows.
    /// </summary>
    /// <param name="connection">the connection</param>
    private static void Prepare(H2Connection connection)
    {
        Execute(connection, "CREATE TABLE T(ID INT)");
        Execute(connection, "INSERT INTO T VALUES (1), (2), (3)");
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
    /// Reads the column ID of the rows of a query.
    /// </summary>
    /// <param name="connection">the connection</param>
    /// <param name="sql">the query</param>
    /// <returns>the values</returns>
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
