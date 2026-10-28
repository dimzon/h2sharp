/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * IN with a subquery and with a correlated subquery, seen through ADO.NET.
 */
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2InQueryTests
{
    [Fact]
    public void InWithASubqueryFiltersTheRows()
    {
        using H2Connection connection = Open("in_query");
        Execute(connection, "CREATE TABLE T(ID INT)");
        Execute(connection, "CREATE TABLE U(ID INT)");
        Execute(connection, "INSERT INTO T VALUES (1), (2), (3)");
        Execute(connection, "INSERT INTO U VALUES (2), (3)");

        Assert.Equal(["2", "3"], Strings(
            connection, "SELECT ID FROM T WHERE ID IN (SELECT ID FROM U) ORDER BY ID"));
        Assert.Equal(["1"], Strings(
            connection, "SELECT ID FROM T WHERE ID NOT IN (SELECT ID FROM U) ORDER BY ID"));
    }

    [Fact]
    public void InWithACorrelatedSubqueryReadsTheOuterRow()
    {
        using H2Connection connection = Open("in_query_correlated");
        Execute(connection, "CREATE TABLE T(ID INT, N INT)");
        Execute(connection, "CREATE TABLE U(ID INT, N INT)");
        Execute(connection, "INSERT INTO T VALUES (1, 10), (2, 20)");
        Execute(connection, "INSERT INTO U VALUES (1, 10), (2, 99)");

        Assert.Equal(["1"], Strings(
            connection,
            "SELECT t.ID FROM T t WHERE t.ID IN (SELECT u.ID FROM U u WHERE u.N = t.N) ORDER BY t.ID"));
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
    /// Runs a statement that changes nothing the test reads.
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
            values.Add(reader.GetInt32(0).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return [.. values];
    }
}
