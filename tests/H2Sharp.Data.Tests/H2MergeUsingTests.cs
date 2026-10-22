/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * MERGE INTO ... USING ... ON ... WHEN ..., seen through ADO.NET.
 *
 * A matched row is updated or deleted, a missed one is inserted, and the clauses are tried in
 * the order the statement wrote them.
 */
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2MergeUsingTests
{
    [Fact]
    public void AMergeUpdatesTheMatchedRowsAndInsertsTheOthers()
    {
        using H2Connection connection = Open("merge_using");
        Execute(connection, "CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR(10))");
        Execute(connection, "CREATE TABLE U(ID INT, NAME VARCHAR(10))");
        Execute(connection, "INSERT INTO T VALUES (1, 'a'), (2, 'b')");
        Execute(connection, "INSERT INTO U VALUES (1, 'x'), (3, 'y')");

        int affected = Execute(
            connection,
            "MERGE INTO T AS t USING U AS u ON t.ID = u.ID"
            + " WHEN MATCHED THEN UPDATE SET NAME = u.NAME"
            + " WHEN NOT MATCHED THEN INSERT (ID, NAME) VALUES (u.ID, u.NAME)");

        Assert.Equal(2, affected);
        Assert.Equal(["1:x", "2:b", "3:y"], Rows(connection, "SELECT ID, NAME FROM T ORDER BY ID"));
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
    /// Runs a statement and answers the number of rows it changed.
    /// </summary>
    /// <param name="connection">the connection</param>
    /// <param name="sql">the statement</param>
    /// <returns>the number of rows</returns>
    private static int Execute(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    /// <summary>
    /// Reads all columns of the rows of a query, one line per row.
    /// </summary>
    /// <param name="connection">the connection</param>
    /// <param name="sql">the query</param>
    /// <returns>the rows</returns>
    private static string[] Rows(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using DbDataReader reader = command.ExecuteReader();
        System.Collections.Generic.List<string> rows = [];
        while (reader.Read())
        {
            System.Collections.Generic.List<string> cells = [];
            for (int i = 0; i < reader.FieldCount; i++)
            {
                cells.Add(System.Convert.ToString(
                    reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? "-");
            }

            rows.Add(string.Join(":", cells));
        }

        return [.. rows];
    }
}
