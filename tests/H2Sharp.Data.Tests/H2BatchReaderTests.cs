/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * ExecuteReader over a batch whose first statement is not a query. The tail still runs, and a
 * query in it is drained rather than sent to ExecuteUpdate.
 */
using System;
using System.Data.Common;
using Dapper;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2BatchReaderTests
{
    [Fact]
    public void ExecuteReaderRunsAQueryTailAfterAnUpdate()
    {
        using H2Connection connection = Open();
        Execute(connection, "CREATE TABLE BOOK(ID INT PRIMARY KEY, TITLE VARCHAR(50))");

        using DbCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO BOOK(ID, TITLE) VALUES (1, 'Dune'); SELECT COUNT(*) FROM BOOK";
        using (DbDataReader reader = command.ExecuteReader())
        {
            // The INSERT's update count is skipped, as JDBC's execute/getMoreResults does:
            // the reader opens on the SELECT, and its row is available immediately.
            Assert.Equal(1, reader.FieldCount);
            Assert.Equal(-1, reader.RecordsAffected);
            Assert.True(reader.Read());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.False(reader.NextResult());
        }

        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM BOOK"));
    }

    [Fact]
    public void ExecuteReaderRunsEveryStatementInAMixedBatch()
    {
        using H2Connection connection = Open();
        Execute(connection, "CREATE TABLE BOOK(ID INT PRIMARY KEY, TITLE VARCHAR(50))");

        using DbCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO BOOK(ID, TITLE) VALUES (1, 'Dune');"
            + " UPDATE BOOK SET TITLE = 'Children of Dune' WHERE ID = 1;"
            + " SELECT COUNT(*) FROM BOOK";
        using (DbDataReader reader = command.ExecuteReader())
        {
            // Like JDBC's getMoreResults, the reader skips update counts to the first
            // result set: the two updates already ran, and the SELECT is the first result.
            Assert.True(reader.Read());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.False(reader.NextResult());
        }

        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM BOOK"));
        Assert.Equal(
            "Children of Dune",
            connection.QuerySingle<string>("SELECT TITLE FROM BOOK WHERE ID = 1"));
    }

    private static void Execute(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        _ = command.ExecuteNonQuery();
    }

    private static H2Connection Open()
    {
        H2Connection connection = new("Data Source=batch_reader_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        return connection;
    }
}
