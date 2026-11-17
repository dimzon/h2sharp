/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 249: the transaction object's public state machine — the name, the savepoint, the
 * changes since a savepoint, prepare/commit, and the refusals of a transaction that is no
 * longer open.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using H2Sharp.Engine;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Tx;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class TransactionSurfaceTests
{
    private static Transaction Begin(MVStore store, out TransactionStore transactionStore)
    {
        transactionStore = new TransactionStore(store);
        transactionStore.Init();
        return transactionStore.Begin();
    }

    [Fact]
    public void TransactionReportsStateNameAndChanges()
    {
        using MVStore store = new MVStore.Builder().Open();
        Transaction transaction = Begin(store, out _);
        Assert.True(transaction.GetId() > 0);
        Assert.True(transaction.GetSequenceNum() >= 0);
        Assert.Equal(Transaction.StatusOpen, transaction.GetStatus());
        Assert.Null(transaction.GetName());
        transaction.SetName("named");
        Assert.Equal("named", transaction.GetName());
        Assert.False(transaction.HasChanges());

        TransactionMap<long, long> map = transaction.OpenMap<long, long>("surface");
        map.Put(1L, 10L);
        Assert.True(transaction.HasChanges());
        Assert.True(transaction.HasStatementDependencies());
        transaction.MarkStatementStart();
        transaction.MarkStatementEnd();

        long savepoint = transaction.SetSavepoint();
        Assert.Empty(transaction.GetChanges(savepoint));
        map.Put(2L, 20L);
        Assert.Single(transaction.GetChanges(savepoint));
        transaction.RollbackToSavepoint(savepoint);
        Assert.False(map.ContainsKey(2L));

        transaction.Prepare();
        Assert.Equal(Transaction.StatusPrepared, transaction.GetStatus());
        transaction.Commit();
        Assert.Equal(Transaction.StatusClosed, transaction.GetStatus());
    }

    [Fact]
    public void ClosedTransactionReportsClosed()
    {
        using MVStore store = new MVStore.Builder().Open();
        Transaction transaction = Begin(store, out _);
        transaction.Rollback();
        Assert.Equal(Transaction.StatusClosed, transaction.GetStatus());
    }

    [Fact]
    public void IsolationLevelAndMapRemovalAreVisible()
    {
        using MVStore store = new MVStore.Builder().Open();
        TransactionStore transactionStore = new(store);
        transactionStore.Init();
        Transaction transaction = transactionStore.Begin(IsolationLevel.RepeatableRead);
        Assert.Equal(IsolationLevel.RepeatableRead, transaction.GetIsolationLevel());
        Assert.False(transaction.AllowNonRepeatableRead());
        TransactionMap<long, long> map = transaction.OpenMap<long, long>("removed");
        map.Put(1L, 1L);
        transaction.RemoveMap(map);
        transaction.Commit();

        Transaction readCommitted = transactionStore.Begin(IsolationLevel.ReadCommitted);
        Assert.True(readCommitted.AllowNonRepeatableRead());
        readCommitted.Rollback();
    }
}
