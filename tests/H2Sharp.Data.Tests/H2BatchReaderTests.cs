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
            Assert.Equal(0, reader.FieldCount);
            Assert.Equal(1, reader.RecordsAffected);
            Assert.False(reader.Read());
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
            Assert.Equal(1, reader.RecordsAffected);
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
        H2Connection connection = new("jdbc:h2:mem:batch_reader_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        return connection;
    }
}
