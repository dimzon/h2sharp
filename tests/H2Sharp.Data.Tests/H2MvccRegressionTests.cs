/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Regression coverage for the MVCC and locking fixes found by the native H2 tests.
 */
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2MvccRegressionTests
{
    [Fact]
    public void RowLockTimesOutAndIsReleasedByCommit()
    {
        using H2Connection first = Open(nameof(RowLockTimesOutAndIsReleasedByCommit));
        using H2Connection second = Open(nameof(RowLockTimesOutAndIsReleasedByCommit));
        Execute(first, "CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR(20))");
        Execute(first, "INSERT INTO TEST VALUES (1, 'first')");
        Execute(second, "SET LOCK_TIMEOUT 10");

        using (DbTransaction transaction = first.BeginTransaction())
        {
            using DbCommand update = Command(first, transaction, "UPDATE TEST SET NAME='locked' WHERE ID=1");
            Assert.Equal(1, update.ExecuteNonQuery());

            using DbTransaction other = second.BeginTransaction();
            using DbCommand blocked = Command(second, other, "UPDATE TEST SET NAME='blocked' WHERE ID=1");
            Assert.Equal(50200, Assert.Throws<H2SharpException>(() => blocked.ExecuteNonQuery()).H2ErrorCode);

            transaction.Commit();
            using DbCommand retry = Command(second, other, "UPDATE TEST SET NAME='after' WHERE ID=1");
            Assert.Equal(1, retry.ExecuteNonQuery());
            other.Commit();
        }

        Assert.Equal("after", Scalar(first, "SELECT NAME FROM TEST WHERE ID=1"));
    }

    [Fact]
    public void DropTableWaitsForUncommittedWriter()
    {
        using H2Connection first = Open(nameof(DropTableWaitsForUncommittedWriter));
        using H2Connection second = Open(nameof(DropTableWaitsForUncommittedWriter));
        Execute(first, "CREATE TABLE TEST(ID INT)");
        Execute(second, "SET LOCK_TIMEOUT 10");

        using (DbTransaction transaction = first.BeginTransaction())
        {
            Execute(first, transaction, "INSERT INTO TEST VALUES (1)");
            Assert.Equal(50200, Assert.Throws<H2SharpException>(
                () => Execute(second, "DROP TABLE TEST")).H2ErrorCode);
            transaction.Rollback();
        }

        _ = Execute(second, "DROP TABLE TEST");
    }

    [Fact]
    public void AutoCommitSelectForUpdateReleasesTheRowLock()
    {
        using H2Connection first = Open(nameof(AutoCommitSelectForUpdateReleasesTheRowLock));
        using H2Connection second = Open(nameof(AutoCommitSelectForUpdateReleasesTheRowLock));
        Execute(first, "CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR(20))");
        Execute(first, "INSERT INTO TEST VALUES (1, 'first')");
        Execute(second, "SET LOCK_TIMEOUT 10");

        using (DbCommand select = first.CreateCommand())
        {
            select.CommandText = "SELECT * FROM TEST WHERE ID=1 FOR UPDATE";
            using DbDataReader reader = select.ExecuteReader();
            Assert.True(reader.Read());
        }

        using DbTransaction transaction = second.BeginTransaction();
        using DbCommand update = Command(second, transaction, "UPDATE TEST SET NAME='after' WHERE ID=1");
        Assert.Equal(1, update.ExecuteNonQuery());
        transaction.Commit();
    }

    [Fact]
    public void DroppedTableMapIsNotReusedByANewTable()
    {
        using H2Connection connection = Open(nameof(DroppedTableMapIsNotReusedByANewTable));
        Execute(connection, "CREATE TABLE TEST(ID INT PRIMARY KEY)");
        Execute(connection, "INSERT INTO TEST VALUES (1)");
        Execute(connection, "DROP TABLE TEST");
        Execute(connection, "CREATE TABLE TEST(ID INT PRIMARY KEY)");

        Assert.Equal("0", Scalar(connection, "SELECT COUNT(*) FROM TEST"));
    }

    private static H2Connection Open(string name)
    {
        H2Connection connection = new($"jdbc:h2:mem:mvcc_regression_{name}");
        connection.Open();
        return connection;
    }

    private static DbCommand Command(H2Connection connection, DbTransaction? transaction, string sql)
    {
        DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static int Execute(H2Connection connection, string sql)
    {
        using DbCommand command = Command(connection, null, sql);
        return command.ExecuteNonQuery();
    }

    private static int Execute(H2Connection connection, DbTransaction transaction, string sql)
    {
        using DbCommand command = Command(connection, transaction, sql);
        return command.ExecuteNonQuery();
    }

    private static string Scalar(H2Connection connection, string sql)
    {
        using DbCommand command = Command(connection, null, sql);
        return System.Convert.ToString(
            command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!;
    }
}
