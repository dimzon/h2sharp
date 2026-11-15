/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 220: the transaction store's two-phase commit across reopens, savepoint rollback,
 * the timeout of a waiter, the change list, and what each isolation level makes of another
 * transaction's uncommitted write.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Tx;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class TransactionPreparedTests : StoreTestBase
{
    [Fact]
    public void ATwoPhaseCommitSurvivesReopens()
    {
        string file = TempFile("two-phase.mv");
        DeleteFile(file);
        using (MVStore s = MVStore.Open(file))
        {
            TransactionStore ts = new(s);
            ts.Init();
            Transaction tx = ts.Begin();
            Assert.Null(tx.GetName());
            tx.SetName("first transaction");
            Assert.Equal(1, tx.GetId());
            Assert.Equal(Transaction.StatusOpen, tx.GetStatus());
            TransactionMap<int, string> map = tx.OpenMap<int, string>("test");
            map.Put(1, "Hello");
            List<Transaction> open = ts.GetOpenTransactions();
            Assert.Single(open);
            Assert.Equal("first transaction", open[0].GetName());
        }

        using (MVStore s = MVStore.Open(file))
        {
            TransactionStore ts = new(s);
            ts.Init();
            Transaction tx = ts.Begin();
            Assert.Equal(2, tx.GetId());
            TransactionMap<int, string> map = tx.OpenMap<int, string>("test");
            Assert.Null(map.Get(1));
            map.Put(2, "World");
            List<Transaction> open = ts.GetOpenTransactions();
            Assert.Equal(2, open.Count);
            Transaction old = open[0];
            Assert.Equal(1, old.GetId());
            Assert.Equal(Transaction.StatusOpen, old.GetStatus());
            Assert.Equal("first transaction", old.GetName());
            old.Prepare();
            Assert.Equal(Transaction.StatusPrepared, old.GetStatus());
            open[1].Commit();
        }

        using (MVStore s = MVStore.Open(file))
        {
            TransactionStore ts = new(s);
            ts.Init();
            Transaction tx = ts.Begin();
            TransactionMap<int, string> map = tx.OpenMap<int, string>("test");
            List<Transaction> open = ts.GetOpenTransactions();
            Assert.Equal(2, open.Count);
            Transaction second = open[1];
            Assert.Equal(2, second.GetId());
            Assert.Equal(Transaction.StatusOpen, second.GetStatus());
            second.Rollback();
            Transaction first = open[0];
            Assert.Equal(1, first.GetId());
            Assert.Equal(Transaction.StatusPrepared, first.GetStatus());
            Assert.Equal("first transaction", first.GetName());
            first.Commit();
            Assert.Equal("Hello", map.Get(1));
            Assert.Equal("World", map.Get(2));
            tx.Rollback();
        }
    }

    [Fact]
    public void ASavepointRollsBackOnlyTheChangesAfterIt()
    {
        using MVStore s = new MVStore.Builder().Open();
        TransactionStore ts = new(s);
        ts.Init();
        Transaction tx = ts.Begin();
        TransactionMap<int, string> map = tx.OpenMap<int, string>("test");
        map.Put(1, "Hello");
        map.Put(2, "World");
        map.Put(1, "Hallo");
        Assert.Equal("World", map.Remove(2));
        map.Put(3, "!");
        long savepoint = tx.SetSavepoint();
        map.Put(1, "Hi");
        map.Put(2, ".");
        map.Remove(3);
        tx.RollbackToSavepoint(savepoint);
        Assert.Equal("Hallo", map.Get(1));
        Assert.Null(map.Get(2));
        Assert.Equal("!", map.Get(3));
        tx.Rollback();

        Transaction again = ts.Begin();
        TransactionMap<int, string> cleared = again.OpenMap<int, string>("test");
        Assert.Null(cleared.Get(1));
        Assert.Null(cleared.Get(2));
        Assert.Null(cleared.Get(3));
        again.Rollback();
    }

    [Fact]
    public void ATimeoutWaitingForALockedRowIsReported()
    {
        using MVStore s = new MVStore.Builder().Open();
        TransactionStore ts = new(s);
        ts.Init();
        Transaction first = ts.Begin();
        TransactionMap<int, string> map = first.OpenMap<int, string>("test");
        map.Put(1, "one");

        Transaction second = ts.Begin(10, 0, IsolationLevel.ReadCommitted);
        TransactionMap<int, string> other = second.OpenMap<int, string>("test");
        MvStoreException e = Assert.Throws<MvStoreException>(() => other.Put(1, "two"));
        Assert.Contains("locked by tx 1", e.Message, StringComparison.OrdinalIgnoreCase);
        second.Rollback();
        first.Rollback();
    }

    [Fact]
    public void ChangesSinceASavepointAreListed()
    {
        using MVStore s = new MVStore.Builder().Open();
        TransactionStore ts = new(s);
        ts.Init();
        Transaction tx = ts.Begin();
        TransactionMap<int, string> map = tx.OpenMap<int, string>("test");
        map.Put(1, "one");
        long savepoint = tx.SetSavepoint();
        map.Put(2, "two");
        map.Remove(1);
        List<TransactionStore.Change> changes = [.. tx.GetChanges(savepoint)];
        Assert.Equal(2, changes.Count);
        // A change carries the value the key had before it, so the removal of 1 carries
        // "one" and the insert of 2 carries null.
        Assert.Contains(changes, c => Equals(c.Key, 1) && Equals(c.Value, "one"));
        Assert.Contains(changes, c => Equals(c.Key, 2) && c.Value is null);
        Assert.All(changes, c => Assert.Equal("test", c.MapName));
        tx.Rollback();
    }

    [Fact]
    public void IsolationLevelsDecideWhatAnUncommittedWriteShows()
    {
        using MVStore s = new MVStore.Builder().Open();
        TransactionStore ts = new(s);
        ts.Init();
        Transaction writer = ts.Begin();
        TransactionMap<int, string> written = writer.OpenMap<int, string>("test");
        written.Put(1, "one");

        Transaction dirty = ts.Begin(IsolationLevel.ReadUncommitted);
        Assert.Equal(1, Assert.Single(Keys(dirty.OpenMap<int, string>("test"))));
        dirty.Rollback();

        Transaction committed = ts.Begin(IsolationLevel.ReadCommitted);
        Assert.Empty(Keys(committed.OpenMap<int, string>("test")));
        committed.Rollback();

        writer.Commit();
        Transaction after = ts.Begin(IsolationLevel.ReadCommitted);
        Assert.Equal("one", after.OpenMap<int, string>("test").Get(1));
        after.Rollback();
    }

    private static List<int> Keys(TransactionMap<int, string> map)
    {
        List<int> keys = [];
        var it = map.KeyIterator(null);
        while (it.HasNext())
        {
            keys.Add(it.Next());
        }

        return keys;
    }
}
