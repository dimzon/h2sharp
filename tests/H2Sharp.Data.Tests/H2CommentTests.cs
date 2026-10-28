/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * COMMENT ON, seen through ADO.NET.
 *
 * The comment is stored on the object it names and the catalog views read it back from
 * there.
 */
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2CommentTests
{
    [Fact]
    public void ACommentOnATableAndItsColumnShowsUpInTheCatalog()
    {
        using H2Connection connection = Open("comment_table");
        Execute(connection, "CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR(20))");
        Execute(connection, "COMMENT ON TABLE T IS 'the things'");
        Execute(connection, "COMMENT ON COLUMN T.NAME IS 'the name'");

        Assert.Equal(
            ["the things"],
            Strings(connection, "SELECT REMARKS FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'T'"));
        Assert.Equal(
            ["the name"],
            Strings(
                connection,
                "SELECT REMARKS FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'T' AND COLUMN_NAME = 'NAME'"));
    }

    [Fact]
    public void AnEmptyCommentRemovesTheComment()
    {
        using H2Connection connection = Open("comment_remove");
        Execute(connection, "CREATE TABLE T(ID INT)");
        Execute(connection, "COMMENT ON TABLE T IS 'temporary'");
        Execute(connection, "COMMENT ON TABLE T IS ''");

        Assert.Equal(
            ["-"],
            Strings(connection, "SELECT REMARKS FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'T'"));
    }

    [Fact]
    public void ACommentNamesMissingObjectsLikeTheDatabaseDoes()
    {
        using H2Connection connection = Open("comment_missing");
        Execute(connection, "CREATE TABLE T(ID INT)");

        Assert.Equal(42102, ErrorCode(connection, "COMMENT ON TABLE NOSUCH IS 'x'"));
        Assert.Equal(42102, ErrorCode(connection, "COMMENT ON COLUMN NOSUCH.C IS 'x'"));
        Assert.Equal(42122, ErrorCode(connection, "COMMENT ON COLUMN T.NOSUCH IS 'x'"));
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
    /// Runs a statement that needs no answer.
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
    /// Reads the first column of the rows of a query, writing NULL as a dash.
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
            object value = reader.GetValue(0);
            values.Add(value is System.DBNull ? "-" : value.ToString() ?? "-");
        }

        return [.. values];
    }

    /// <summary>
    /// The H2 error code the statement fails with.
    /// </summary>
    /// <param name="connection">the connection</param>
    /// <param name="sql">the statement</param>
    /// <returns>the error code</returns>
    private static int ErrorCode(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        H2SharpException error = Assert.Throws<H2SharpException>(() => command.ExecuteNonQuery());
        return error.H2ErrorCode;
    }
}
