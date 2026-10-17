/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The DbTransaction semantics as the contract states them, written without reading the
 * implementation: ADO.NET says a transaction isolates its statements until Commit or Rollback,
 * that Rollback (including Dispose without completion) undoes them, and that after completion
 * the connection behaves as it did before. DDL is included: H2 documents transactional DDL —
 * CREATE, ALTER and DROP roll back together with the transaction — and nothing in the provider
 * contract exempts it.
 *
 * These tests are red on purpose while the commit-per-statement gap of issue #11 (F1) is open:
 * every statement inside a DbTransaction still commits the session transaction on its way out,
 * so the rollback-family tests below name exactly that member. The state-machine tests are
 * green and stay the ones to keep.
 *
 * Dapper is used as the caller convenience only: Execute/Query over IDbConnection with an
 * explicit transaction, nothing written against the provider's own members.
 */
using System;
using System.Data;
using Dapper;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2TransactionSemanticsTests
{
    /// <summary>
    /// Opens one connection to its own in-memory database, so tests cannot see each other.
    /// </summary>
    private static IDbConnection Open() => OpenNamed(NewName());

    private static IDbConnection OpenNamed(string name)
    {
        IDbConnection connection = new H2Connection($"jdbc:h2:mem:{name}");
        connection.Open();
        return connection;
    }

    private static string NewName() => "txn_" + Guid.NewGuid().ToString("N");

    private static void PrepareTable(IDbConnection connection)
    {
        _ = connection.Execute("DROP TABLE IF EXISTS T");
        _ = connection.Execute("CREATE TABLE T(ID INT PRIMARY KEY, V INT)");
    }

    // ————— Rollback family: nothing written inside survives a Rollback —————

    [Fact]
    public void Rollback_UndoesInsert()
    {
        using IDbConnection connection = Open();
        PrepareTable(connection);
        using IDbTransaction transaction = connection.BeginTransaction();
        _ = connection.Execute("INSERT INTO T(ID, V) VALUES (1, 10)", transaction: transaction);
        transaction.Rollback();

        int count = connection.QuerySingle<int>("SELECT COUNT(*) FROM T");
        Assert.Equal(0, count);
    }

    [Fact]
    public void Rollback_UndoesUpdate()
    {
        using IDbConnection connection = Open();
        PrepareTable(connection);
        _ = connection.Execute("INSERT INTO T(ID, V) VALUES (1, 5)");
        using IDbTransaction transaction = connection.BeginTransaction();
        _ = connection.Execute("UPDATE T SET V = 99 WHERE ID = 1", transaction: transaction);
        transaction.Rollback();

        int value = connection.QuerySingle<int>("SELECT V FROM T WHERE ID = 1");
        Assert.Equal(5, value);
    }

    [Fact]
    public void Rollback_UndoesDelete()
    {
        using IDbConnection connection = Open();
        PrepareTable(connection);
        _ = connection.Execute("INSERT INTO T(ID, V) VALUES (1, 5)");
        using IDbTransaction transaction = connection.BeginTransaction();
        _ = connection.Execute("DELETE FROM T WHERE ID = 1", transaction: transaction);
        transaction.Rollback();

        int count = connection.QuerySingle<int>("SELECT COUNT(*) FROM T");
        Assert.Equal(1, count);
    }

    [Fact]
    public void Dispose_WithoutCompletion_RollsBack()
    {
        using IDbConnection connection = Open();
        PrepareTable(connection);
        IDbTransaction transaction = connection.BeginTransaction();
        _ = connection.Execute("INSERT INTO T(ID, V) VALUES (1, 10)", transaction: transaction);
        transaction.Dispose();

        int count = connection.QuerySingle<int>("SELECT COUNT(*) FROM T");
        Assert.Equal(0, count);
    }

    // ————— Commit family: committed writes persist and become visible —————

    [Fact]
    public void Commit_PersistsRows()
    {
        using IDbConnection connection = Open();
        PrepareTable(connection);
        using IDbTransaction transaction = connection.BeginTransaction();
        _ = connection.Execute("INSERT INTO T(ID, V) VALUES (1, 10), (2, 20)", transaction: transaction);
        transaction.Commit();

        int count = connection.QuerySingle<int>("SELECT COUNT(*) FROM T");
        Assert.Equal(2, count);
    }

    // ————— Isolation between sessions on one in-memory database —————

    [Fact]
    public void UncommittedRows_AreInvisibleToOtherSession()
    {
        string name = NewName();
        using IDbConnection first = OpenNamed(name);
        PrepareTable(first);
        using IDbConnection second = OpenNamed(name);

        using IDbTransaction transaction = first.BeginTransaction();
        _ = first.Execute("INSERT INTO T(ID, V) VALUES (1, 10)", transaction: transaction);
        Assert.Equal(0, second.QuerySingle<int>("SELECT COUNT(*) FROM T"));

        transaction.Commit();
        Assert.Equal(1, second.QuerySingle<int>("SELECT COUNT(*) FROM T"));
    }

    // ————— The connection outside a transaction —————

    [Fact]
    public void Statements_AfterTransaction_CommitThemselves()
    {
        string name = NewName();
        using IDbConnection first = OpenNamed(name);
        PrepareTable(first);
        using IDbConnection second = OpenNamed(name);

        using (IDbTransaction transaction = first.BeginTransaction())
        {
            _ = first.Execute("INSERT INTO T(ID, V) VALUES (1, 10)", transaction: transaction);
            transaction.Commit();
        }

        _ = first.Execute("INSERT INTO T(ID, V) VALUES (2, 20)");

        Assert.Equal(2, second.QuerySingle<int>("SELECT COUNT(*) FROM T"));
    }

    [Fact]
    public void Connection_Reusable_AfterRollback()
    {
        using IDbConnection connection = Open();
        PrepareTable(connection);
        using (IDbTransaction transaction = connection.BeginTransaction())
        {
            _ = connection.Execute("INSERT INTO T(ID, V) VALUES (1, 10)", transaction: transaction);
            transaction.Rollback();
        }

        using IDbTransaction again = connection.BeginTransaction();
        _ = connection.Execute("INSERT INTO T(ID, V) VALUES (2, 20)", transaction: again);
        again.Commit();

        int count = connection.QuerySingle<int>("SELECT COUNT(*) FROM T");
        Assert.Equal(1, count);
    }

    // ————— State machine, short hand-written duplicates of the inherited suite —————

    [Fact]
    public void Commit_Twice_Throws()
    {
        using IDbConnection connection = Open();
        using IDbTransaction transaction = connection.BeginTransaction();
        transaction.Commit();

        _ = Assert.Throws<InvalidOperationException>(transaction.Commit);
    }

    [Fact]
    public void Rollback_AfterCommit_Throws()
    {
        using IDbConnection connection = Open();
        using IDbTransaction transaction = connection.BeginTransaction();
        transaction.Commit();

        _ = Assert.Throws<InvalidOperationException>(transaction.Rollback);
    }

    [Fact]
    public void Second_BeginTransaction_WhileOpen_Throws()
    {
        using IDbConnection connection = Open();
        using IDbTransaction transaction = connection.BeginTransaction();

        _ = Assert.Throws<InvalidOperationException>(connection.BeginTransaction);

        transaction.Rollback();
    }

    [Fact]
    public void Operations_AfterDispose_Throw()
    {
        using IDbConnection connection = Open();
        IDbTransaction transaction = connection.BeginTransaction();
        transaction.Dispose();

        _ = Assert.ThrowsAny<Exception>(transaction.Commit);
        _ = Assert.ThrowsAny<Exception>(transaction.Rollback);
    }

    // ————— Transactional DDL, the H2-documented behaviour —————

    [Fact]
    public void Rollback_UndoesCreateTable()
    {
        using IDbConnection connection = Open();
        PrepareTable(connection);
        using IDbTransaction transaction = connection.BeginTransaction();
        _ = connection.Execute("CREATE TABLE T2(ID INT PRIMARY KEY)", transaction: transaction);
        transaction.Rollback();

        Assert.ThrowsAny<Exception>(
            () => connection.QuerySingle<int>("SELECT COUNT(*) FROM T2"));
    }

    [Fact]
    public void Commit_PersistsCreateTable()
    {
        using IDbConnection connection = Open();
        PrepareTable(connection);
        using IDbTransaction transaction = connection.BeginTransaction();
        _ = connection.Execute("CREATE TABLE T2(ID INT PRIMARY KEY)", transaction: transaction);
        _ = connection.Execute("INSERT INTO T2(ID) VALUES (1)", transaction: transaction);
        transaction.Commit();

        int count = connection.QuerySingle<int>("SELECT COUNT(*) FROM T2");
        Assert.Equal(1, count);
    }

    [Fact]
    public void Rollback_UndoesDropTable()
    {
        using IDbConnection connection = Open();
        PrepareTable(connection);
        _ = connection.Execute("INSERT INTO T(ID, V) VALUES (1, 10)");
        using IDbTransaction transaction = connection.BeginTransaction();
        _ = connection.Execute("DROP TABLE T", transaction: transaction);
        transaction.Rollback();

        int count = connection.QuerySingle<int>("SELECT COUNT(*) FROM T");
        Assert.Equal(1, count);
    }
}
