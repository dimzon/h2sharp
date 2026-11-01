/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The DbBatch surface of the provider.
 */
using System;
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2BatchTests
{
    [Fact]
    public void ARepeatedStatementRunsAllParameterSets()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE T(ID INT, NAME VARCHAR)";
            command.ExecuteNonQuery();
        }

        using H2Batch batch = (H2Batch)connection.CreateBatch();
        for (int i = 0; i < 3; i++)
        {
            DbBatchCommand item = batch.CreateBatchCommand();
            item.CommandText = "INSERT INTO T VALUES(?, ?)";
            item.Parameters.Add(new H2Parameter { Value = i });
            item.Parameters.Add(new H2Parameter { Value = "n" + i });
        }

        Assert.Equal(3, batch.ExecuteNonQuery());
        Assert.Equal("3", Scalar(connection, "SELECT COUNT(*) FROM T"));
        Assert.Equal("n2", Scalar(connection, "SELECT NAME FROM T WHERE ID=2"));
    }

    [Fact]
    public void DifferentStatementsRunInOrder()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE T(ID INT)";
            command.ExecuteNonQuery();
        }

        using H2Batch batch = (H2Batch)connection.CreateBatch();
        DbBatchCommand first = batch.CreateBatchCommand();
        first.CommandText = "INSERT INTO T VALUES(1)";
        DbBatchCommand second = batch.CreateBatchCommand();
        second.CommandText = "INSERT INTO T VALUES(2)";
        DbBatchCommand third = batch.CreateBatchCommand();
        third.CommandText = "UPDATE T SET ID=ID+10";

        Assert.Equal(4, batch.ExecuteNonQuery());
        Assert.Equal("11", Scalar(connection, "SELECT ID FROM T WHERE ID=11"));
        Assert.Equal("12", Scalar(connection, "SELECT ID FROM T WHERE ID=12"));
    }

    [Fact]
    public void AFailedSetReportsAndKeepsTheSuccessfulOnes()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE T(ID INT PRIMARY KEY)";
            command.ExecuteNonQuery();
        }

        using H2Batch batch = (H2Batch)connection.CreateBatch();
        DbBatchCommand good = batch.CreateBatchCommand();
        good.CommandText = "INSERT INTO T VALUES(1)";
        DbBatchCommand bad = batch.CreateBatchCommand();
        bad.CommandText = "INSERT INTO T VALUES(1)";

        Assert.ThrowsAny<Exception>(() => batch.ExecuteNonQuery());
        Assert.Equal("1", Scalar(connection, "SELECT COUNT(*) FROM T"));
    }

    [Fact]
    public void ABatchRunsInsideTheConnectionTransaction()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE T(ID INT)";
            command.ExecuteNonQuery();
        }

        using DbTransaction transaction = connection.BeginTransaction();
        using H2Batch batch = (H2Batch)connection.CreateBatch();
        batch.Transaction = transaction;
        DbBatchCommand item = batch.CreateBatchCommand();
        item.CommandText = "INSERT INTO T VALUES(7)";
        Assert.Equal(1, batch.ExecuteNonQuery());
        transaction.Rollback();

        Assert.Equal("0", Scalar(connection, "SELECT COUNT(*) FROM T"));
    }

    [Fact]
    public void ASingleQueryCommandAnswersScalar()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE T(ID INT)";
            command.ExecuteNonQuery();
        }

        using H2Batch batch = (H2Batch)connection.CreateBatch();
        DbBatchCommand item = batch.CreateBatchCommand();
        item.CommandText = "SELECT COUNT(*) FROM T";
        Assert.Equal("0", Convert.ToString(batch.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void EmptyBatchReturnsZero()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        using H2Batch batch = (H2Batch)connection.CreateBatch();
        Assert.Equal(0, batch.ExecuteNonQuery());
    }

    [Fact]
    public void MixedReaderRunsFirstQueryThenRemainingUpdates()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE T(ID INT)";
            command.ExecuteNonQuery();
        }

        using H2Batch batch = (H2Batch)connection.CreateBatch();
        DbBatchCommand first = batch.CreateBatchCommand();
        first.CommandText = "SELECT COUNT(*) FROM T";
        DbBatchCommand second = batch.CreateBatchCommand();
        second.CommandText = "INSERT INTO T VALUES(1)";
        DbBatchCommand third = batch.CreateBatchCommand();
        third.CommandText = "INSERT INTO T VALUES(2)";

        using (DbDataReader reader = batch.ExecuteReader())
        {
            // The reader is opened before the remaining commands run, but the result
            // itself is computed when the reader is consumed; the count already includes
            // the two inserts of the batch.
            Assert.True(reader.Read());
            Assert.Equal(2, reader.GetInt32(0));
            Assert.False(reader.Read());
        }

        Assert.Equal("2", Scalar(connection, "SELECT COUNT(*) FROM T"));
    }

    private static H2Connection NewConnection()
        => new($"Data Source=batch_{Guid.NewGuid():N};IGNORE_UNKNOWN_SETTINGS=TRUE;Storage=Memory");

    private static string Scalar(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value is null or DBNull
            ? "-"
            : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "-";
    }
}
