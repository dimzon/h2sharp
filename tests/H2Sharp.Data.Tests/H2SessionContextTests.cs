/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * H2Database.CurrentSessionConnection: a function reads the calling transaction, cannot
 * change it, gets a connection that is closed after the call, and nested functions keep the
 * outer context.
 */
using System;
using System.Data;
using System.Data.Common;
using H2Sharp.Data;
using H2Sharp.Data.Extensions;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2SessionContextTests
{
    [Fact]
    public void AFunctionSeesTheUncommittedRowOfItsTransaction()
    {
        using H2Connection connection = NewConnection(extensions =>
            extensions.RegisterUdf("COUNT_TX", () => CountRows()));
        connection.Open();
        Execute(connection, "CREATE TABLE T(ID INT)");

        using (DbTransaction transaction = connection.BeginTransaction())
        {
            Execute(connection, "INSERT INTO T VALUES (1)", transaction);
            Assert.Equal(1, Scalar(connection, "SELECT COUNT_TX()", transaction));
            transaction.Rollback();
        }

        Assert.Equal(0, Scalar(connection, "SELECT COUNT_TX()"));
    }

    [Fact]
    public void TheContextConnectionRefusesToChangeTheTransaction()
    {
        using H2Connection connection = NewConnection(extensions =>
            extensions.RegisterUdf<string?, int>("TRY_SQL", TrySql));
        connection.Open();

        Assert.Equal(-1, Scalar(connection, "SELECT TRY_SQL('BEGIN')"));
        Assert.Equal(-1, Scalar(connection, "SELECT TRY_SQL('COMMIT')"));
        Assert.Equal(-1, Scalar(connection, "SELECT TRY_SQL('ROLLBACK')"));
        Assert.Equal(-1, Scalar(connection, "SELECT TRY_SQL('SET AUTOCOMMIT TRUE')"));
        Assert.Equal(-1, Scalar(connection, "SELECT TRY_SQL('CREATE TABLE D(I INT)')"));
        Assert.Equal(0, Scalar(connection, "SELECT TRY_SQL('SELECT 1')"));
    }

    [Fact]
    public void BeginEnlistAndAnExplicitTransactionAreRefused()
    {
        H2Transaction? outerTransaction = null;
        using H2Connection connection = NewConnection(extensions =>
        {
            extensions.RegisterUdf("TRY_BEGIN", TryBegin);
            extensions.RegisterUdf("TRY_ENLIST", TryEnlist);
            extensions.RegisterUdf("TRY_SET_TX", () => TrySetTransaction(outerTransaction!));
        });
        connection.Open();

        Assert.Equal(-1, Scalar(connection, "SELECT TRY_BEGIN()"));
        Assert.Equal(-1, Scalar(connection, "SELECT TRY_ENLIST()"));

        using (H2Transaction transaction = (H2Transaction)connection.BeginTransaction())
        {
            outerTransaction = transaction;
            Assert.Equal(-1, Scalar(connection, "SELECT TRY_SET_TX()", transaction));
            transaction.Rollback();
        }
    }

    [Fact]
    public void TheStoredConnectionIsClosedAfterTheCall()
    {
        H2Connection? captured = null;
        using H2Connection connection = NewConnection(extensions =>
            extensions.RegisterUdf("CAPTURE", () =>
            {
                captured = H2Database.CurrentSessionConnection;
                return 0;
            }));
        connection.Open();
        Assert.Equal(0, Scalar(connection, "SELECT CAPTURE()"));
        Assert.NotNull(captured);
        using DbCommand command = captured!.CreateCommand();
        command.CommandText = "SELECT 1";
        _ = Assert.ThrowsAny<Exception>(() => command.ExecuteScalar());
        Assert.Null(H2Database.CurrentSessionConnection);
    }

    [Fact]
    public void ANestedFunctionKeepsTheOuterContext()
    {
        using H2Connection connection = NewConnection(extensions =>
        {
            extensions.RegisterUdf("INNER_CTX", () => H2Database.CurrentSessionConnection is null ? -5 : 5);
            extensions.RegisterUdf("OUTER_CTX", () =>
            {
                H2Connection context = H2Database.CurrentSessionConnection!;
                using DbCommand command = context.CreateCommand();
                command.CommandText = "SELECT INNER_CTX()";
                return Convert.ToInt32(command.ExecuteScalar(),
                    System.Globalization.CultureInfo.InvariantCulture) + 1;
            });
        });
        connection.Open();

        Assert.Equal(6, Scalar(connection, "SELECT OUTER_CTX()"));
    }

    private static int CountRows()
    {
        H2Connection? context = H2Database.CurrentSessionConnection;
        if (context is null)
        {
            return -1;
        }

        using DbCommand command = context.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM T";
        return Convert.ToInt32(command.ExecuteScalar(),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int TrySql(string? sql)
    {
        H2Connection? context = H2Database.CurrentSessionConnection;
        if (context is null || sql is null)
        {
            return -999;
        }

        try
        {
            using DbCommand command = context.CreateCommand();
            command.CommandText = sql;
            _ = command.ExecuteNonQuery();
            return 0;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
        catch (H2SharpException e)
        {
            return e.H2ErrorCode;
        }
    }

    private static int TryBegin()
    {
        H2Connection? context = H2Database.CurrentSessionConnection;
        if (context is null)
        {
            return -999;
        }

        try
        {
            using DbTransaction transaction = context.BeginTransaction();
            return 0;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private static int TryEnlist()
    {
        H2Connection? context = H2Database.CurrentSessionConnection;
        if (context is null)
        {
            return -999;
        }

        try
        {
            using var ambient = new System.Transactions.CommittableTransaction();
            context.EnlistTransaction(ambient);
            return 0;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private static int TrySetTransaction(H2Transaction transaction)
    {
        H2Connection? context = H2Database.CurrentSessionConnection;
        if (context is null || transaction is null)
        {
            return -999;
        }

        try
        {
            using DbCommand command = context.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT 1";
            _ = command.ExecuteNonQuery();
            return 0;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private static H2Connection NewConnection(Action<IH2Extensions> configure)
    {
        H2Connection connection = new(
            $"Data Source=session_ctx_{Guid.NewGuid():N};Storage=Memory");
        configure((IH2Extensions)connection);
        return connection;
    }

    private static void Execute(H2Connection connection, string sql, DbTransaction? transaction = null)
    {
        using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        _ = command.ExecuteNonQuery();
    }

    private static int Scalar(H2Connection connection, string sql, DbTransaction? transaction = null)
    {
        using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar(),
            System.Globalization.CultureInfo.InvariantCulture);
    }
}
