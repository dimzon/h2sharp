/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct unit tests for Transaction: state transitions, savepoint handling,
 * and error conditions that are hard to reach through normal SQL execution.
 */
using System.Collections.Generic;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Tx;
using H2Sharp.MvStore.Type;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class TransactionTests : StoreTestBase
{
    [Fact]
    public void TransactionGetIdAndSequence()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();
        Transaction t = ts.Begin();

        // Slot 0 is never used, so the first transaction gets id 1.
        // VersionedBitSet starts at version 0 and increments on each flip, so sequence is 1.
        Assert.Equal(1, t.GetId());
        Assert.Equal(1, t.GetSequenceNum());
    }

    [Fact]
    public void TransactionSetNameAfterCommitThrows()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();
        Transaction t = ts.Begin();
        t.Commit();
        Assert.Throws<MvStoreException>(() => t.SetName("test"));
    }

    [Fact]
    public void TransactionOpenMapAfterCommitThrows()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();
        Transaction t = ts.Begin();
        t.Commit();
        Assert.Throws<MvStoreException>(() => t.OpenMap<long, string>("test", LongDataType.Instance, StringDataType.Instance));
    }

    [Fact]
    public void TransactionSetNameAfterRollbackThrows()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();
        Transaction t = ts.Begin();
        t.Rollback();
        Assert.Throws<MvStoreException>(() => t.SetName("test"));
    }

    [Fact]
    public void TransactionSavepoint()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();
        Transaction t = ts.Begin();
        long savepoint = t.SetSavepoint();
        t.RollbackToSavepoint(savepoint);
        Assert.Equal(Transaction.StatusOpen, t.GetStatus());
    }

    [Fact]
    public void TransactionGetChangesFromSavepoint()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();
        Transaction t = ts.Begin();
        long savepoint = t.SetSavepoint();
        var map = t.OpenMap<long, string>("test", LongDataType.Instance, StringDataType.Instance);
        map.Put(1L, "one");
        var changes = t.GetChanges(savepoint);
        bool found = false;
        foreach (var change in changes)
        {
            if (change.Key is long k && k == 1L)
            {
                found = true;
                break;
            }
        }
        Assert.True(found, "Expected to find the put change recorded after the savepoint");
    }

    [Fact]
    public void LeftoverTransactionIsRecoveredAndRolledBack()
    {
        string file = TempFile("leftover.mv");
        DeleteFile(file);

        int unfinishedId;
        using (MVStore s = new MVStore.Builder().FileName(file).Open())
        {
            var ts = new TransactionStore(s);
            ts.Init();
            Transaction t = ts.Begin();
            var map = t.OpenMap<long, string>("test", LongDataType.Instance, StringDataType.Instance);
            // Enough entries to flush the single-writer undo-log append buffer into the map
            // tree; a single buffered record would never reach the file on a clean close.
            for (long i = 0; i < 64; i++)
            {
                map.Put(i, "v" + i);
            }
            unfinishedId = t.GetId();
            s.Commit();
            // The store is closed with the transaction still open: the undo log record is
            // persisted, and the next init must recover the transaction as a leftover.
        }

        using (MVStore s = new MVStore.Builder().FileName(file).Open())
        {
            var ts = new TransactionStore(s);
            ts.Init();
            List<Transaction> open = ts.GetOpenTransactions();
            Assert.Contains(open, tx => tx.GetId() == unfinishedId);

            ts.EndLeftoverTransactions();
            Assert.Empty(ts.GetOpenTransactions());
        }
    }

    [Fact]
    public void OpenTransactionsInitializesTheStore()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        // Init() не вызываем: GetOpenTransactions инициализирует store сам.
        Assert.Empty(ts.GetOpenTransactions());
    }

    [Fact]
    public void CloseImmediatelySkipsTheCommit()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();
        ts.CloseImmediately();
        ts.Dispose();
    }
}
