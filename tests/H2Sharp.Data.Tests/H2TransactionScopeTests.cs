/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * System.Transactions enlistment for the single-resource case.
 */
using System;
using System.Data.Common;
using System.Transactions;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2TransactionScopeTests
{
    [Fact]
    public void AConnectionOpenedInAScopeIsEnlistedAndTheScopeCommits()
    {
        using H2Connection control = Open("tx_scope_commit");
        using (TransactionScope scope = new())
        {
            using H2Connection enlisted = Open("tx_scope_commit");
            Execute(enlisted, "CREATE TABLE T(ID INT)");
            Execute(enlisted, "INSERT INTO T VALUES (1)");
            scope.Complete();
        }

        Assert.Equal(1, Count(control));
    }

    [Fact]
    public void AScopeWithoutCompleteRollsBackTheEnlistedConnection()
    {
        using H2Connection control = Open("tx_scope_rollback");
        Execute(control, "CREATE TABLE T(ID INT)");
        using (TransactionScope scope = new())
        {
            using H2Connection enlisted = Open("tx_scope_rollback");
            Execute(enlisted, "INSERT INTO T VALUES (1)");
        }

        Assert.Equal(0, Count(control));
    }

    [Fact]
    public void AnOpenConnectionCanBeEnlistedExplicitly()
    {
        using H2Connection connection = Open("tx_scope_explicit");
        using (TransactionScope scope = new())
        {
            Execute(connection, "CREATE TABLE T(ID INT)");
            connection.EnlistTransaction(Transaction.Current);
            Execute(connection, "INSERT INTO T VALUES (1)");
            scope.Complete();
        }

        Assert.Equal(1, Count(connection));
    }

    [Fact]
    public void AnExplicitTransactionIsRefusedWhileEnlisted()
    {
        using H2Connection connection = Open("tx_scope_explicit_refused");
        using (TransactionScope scope = new())
        {
            connection.EnlistTransaction(Transaction.Current);
            _ = Assert.Throws<InvalidOperationException>(() => connection.BeginTransaction());
        }
    }

    private static H2Connection Open(string name)
    {
        return H2TestDataSource.Open("Data Source=" + name + ";Storage=Memory");
    }

    private static void Execute(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        _ = command.ExecuteNonQuery();
    }

    private static int Count(H2Connection connection)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM T";
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
