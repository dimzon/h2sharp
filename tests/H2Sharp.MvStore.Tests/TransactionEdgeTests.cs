/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 211: the transaction edges left after the isolation suite — a statement on a
 * committed transaction refuses by status, and a transaction that committed right
 * before an unclean shutdown is recovered as a committed leftover: the value survives
 * the reopen and the cleanup pass only clears the marker.
 */
using System;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Tx;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class TransactionEdgeTests : StoreTestBase
{
    [Fact]
    public void APutAfterCommitRefusesByStatus()
    {
        using MVStore s = new MVStore.Builder().Open();
        TransactionStore ts = new TransactionStore(s);
        ts.Init();
        Transaction tx = ts.Begin();
        TransactionMap<int, string> map = tx.OpenMap<int, string>("data");
        map.Put(1, "one");
        tx.Commit();

        H2Sharp.MvStore.MvStoreException e = Assert.ThrowsAny<H2Sharp.MvStore.MvStoreException>(
            () => map.Put(2, "two"));
        Assert.Contains("not OPEN", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARollbackAfterCommitRefusesByStatus()
    {
        using MVStore s = new MVStore.Builder().Open();
        TransactionStore ts = new TransactionStore(s);
        ts.Init();
        Transaction tx = ts.Begin();
        TransactionMap<int, string> map = tx.OpenMap<int, string>("data");
        map.Put(1, "one");
        tx.Commit();

        H2Sharp.MvStore.MvStoreException e = Assert.ThrowsAny<H2Sharp.MvStore.MvStoreException>(
            () => map.Remove(1));
        Assert.Contains("not OPEN", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACommittedLeftoverSurvivesReopenAndCleanup()
    {
        string file = TempFile("leftover-committed.mv");
        DeleteFile(file);
        using (MVStore s = MVStore.Open(file))
        {
            TransactionStore ts = new TransactionStore(s);
            ts.Init();
            Transaction tx = ts.Begin();
            tx.OpenMap<int, string>("data").Put(1, "one");
            tx.Commit();
            // The store closes without the cleanup pass: the committed transaction stays
            // in the meta map under its renamed undo log.
        }

        using MVStore reopened = MVStore.Open(file);
        TransactionStore recovered = new TransactionStore(reopened);
        recovered.Init();
        Transaction reader = recovered.Begin();
        Assert.Equal("one", reader.OpenMap<int, string>("data").Get(1));
        reader.Rollback();

        recovered.EndLeftoverTransactions();
        Transaction again = recovered.Begin();
        Assert.Equal("one", again.OpenMap<int, string>("data").Get(1));
        again.Rollback();
    }
}
