/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A DbTransaction over the engine session.
 *
 * The engine half is the session transaction: begin switches the session out of auto-commit,
 * and commit or rollback finishes that transaction, so the statements run on it are really
 * isolated. The test that matters is the one the audit found missing: a DML inside a
 * transaction, rolled back, must leave the table as it was — at every isolation level.
 */
using System;
using System.Data;
using System.Data.Common;
using H2Sharp;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2TransactionIsolationTests
{
    [Theory]
    [InlineData(IsolationLevel.ReadUncommitted)]
    [InlineData(IsolationLevel.ReadCommitted)]
    [InlineData(IsolationLevel.RepeatableRead)]
    [InlineData(IsolationLevel.Serializable)]
    public void RollbackUndoesDmlInsideATransaction(IsolationLevel level)
    {
        using H2Connection connection = new("jdbc:h2:mem:tx_rollback");
        connection.Open();
        using (H2Command setup = new("CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR)", connection))
        {
            _ = setup.ExecuteNonQuery();
            setup.CommandText = "INSERT INTO T VALUES (1, 'b')";
            _ = setup.ExecuteNonQuery();
        }

        using DbTransaction transaction = connection.BeginTransaction(level);
        using (H2Command insert = new("INSERT INTO T VALUES (2, 'x')", connection))
        {
            insert.Transaction = transaction;
            _ = insert.ExecuteNonQuery();
        }

        transaction.Rollback();

        using H2Command count = new("SELECT COUNT(*) FROM T", connection);
        Assert.Equal(1L, Convert.ToInt64(count.ExecuteScalar()));
    }

    [Fact]
    public void CommitKeepsDmlInsideATransaction()
    {
        using H2Connection connection = new("jdbc:h2:mem:tx_commit");
        connection.Open();
        using (H2Command setup = new("CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR)", connection))
        {
            _ = setup.ExecuteNonQuery();
        }

        using (DbTransaction transaction = connection.BeginTransaction())
        using (H2Command insert = new("INSERT INTO T VALUES (1, 'b')", connection))
        {
            insert.Transaction = transaction;
            _ = insert.ExecuteNonQuery();
            transaction.Commit();
        }

        using H2Command count = new("SELECT COUNT(*) FROM T", connection);
        Assert.Equal(1L, Convert.ToInt64(count.ExecuteScalar()));
    }
}
