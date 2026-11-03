/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * TransactionMap under the stricter isolation levels and with uncommitted changes of its own:
 * the repeatable iterator, the size with changes, snapshot reads of own values, the deleted /
 * same-transaction questions, append, and the size correction over undo logs.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.MvStore.Tx;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class TestTransactionMapIsolation : StoreTestBase
{
    private static TransactionMap<long, long> OpenMap(
        TransactionStore ts, IsolationLevel level, string name, out Transaction tx)
    {
        tx = ts.Begin(level);
        return tx.OpenMap<long, long>(name);
    }

    private static void Seed(TransactionStore ts, string name, params long[] keys)
    {
        Transaction seed = ts.Begin();
        TransactionMap<long, long> seedMap = seed.OpenMap<long, long>(name);
        foreach (long key in keys)
        {
            seedMap.Put(key, key * 10);
        }

        seed.Commit();
    }

    [Fact]
    public void RepeatableReadIteratorSeesOwnChanges()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();
        Seed(ts, "isolated", 10, 30, 50);

        TransactionMap<long, long> map = OpenMap(ts, IsolationLevel.RepeatableRead, "isolated", out Transaction tx);
        map.Put(20L, 200L);
        map.Put(30L, 301L);
        map.Remove(50L);
        map.Put(40L, 400L);

        List<long> keys = [];
        List<long> values = [];
        var iterator = map.EntryIterator(null, null, false);
        // HasNext/Next is the peeking path; TryFetchNext alone would skip it.
        while (iterator.HasNext())
        {
            KeyValuePair<long, long> entry = iterator.Next();
            keys.Add(entry.Key);
            values.Add(entry.Value);
        }

        Assert.Equal([10L, 20L, 30L, 40L], keys);
        Assert.Equal([100L, 200L, 301L, 400L], values);
        Assert.Equal(4, map.Size());
        Assert.Equal(4, map.SizeAsLong());
        tx.Rollback();
    }

    [Fact]
    public void SnapshotAndSerializableUseTheSameIterator()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();

        foreach (IsolationLevel level in new[]
                 {
                     IsolationLevel.Snapshot,
                     IsolationLevel.Serializable,
                 })
        {
            string name = "isolated_" + level;
            Seed(ts, name, 1, 3);

            TransactionMap<long, long> map = OpenMap(ts, level, name, out Transaction tx);
            map.Put(2L, 20L);
            map.Remove(1L);

            List<long> keys = [];
            var iterator = map.EntryIterator(null, null, false);
            while (iterator.TryFetchNext(out KeyValuePair<long, long> entry))
            {
                keys.Add(entry.Key);
            }

            Assert.Equal([2L, 3L], keys);
            Assert.Equal(2, map.Size());
            tx.Rollback();
        }
    }

    [Fact]
    public void GetFromSnapshotSeesOwnUncommittedValues()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();
        Seed(ts, "isolated", 1);

        TransactionMap<long, long> map = OpenMap(ts, IsolationLevel.RepeatableRead, "isolated", out Transaction tx);
        map.Put(1L, 99L);
        map.Put(2L, 20L);

        Assert.Equal(99L, map.GetFromSnapshot(1L));
        Assert.Equal(20L, map.GetFromSnapshot(2L));
        tx.Rollback();
    }

    [Fact]
    public void DeletedAndSameTransactionQuestions()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();
        Seed(ts, "isolated", 1);

        TransactionMap<long, long> map = OpenMap(ts, IsolationLevel.ReadCommitted, "isolated", out Transaction tx);
        Assert.False(map.IsDeletedByCurrentTransaction(1L));
        Assert.False(map.IsSameTransaction(1L));

        map.Put(2L, 20L);
        map.Remove(1L);
        Assert.True(map.IsDeletedByCurrentTransaction(1L));
        Assert.True(map.IsSameTransaction(1L));
        Assert.True(map.IsSameTransaction(2L));
        Assert.False(map.IsSameTransaction(3L));
        tx.Rollback();
    }

    [Fact]
    public void AppendAddsAfterTheLastKey()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();

        TransactionMap<long, long> map = OpenMap(ts, IsolationLevel.ReadCommitted, "isolated", out Transaction tx);
        map.Append(1L, 10L);
        map.Append(2L, 20L);

        Assert.Equal(10L, map.Get(1L));
        Assert.Equal(20L, map.Get(2L));
        Assert.Equal(2, map.Size());
        tx.Commit();

        Transaction reader = ts.Begin();
        TransactionMap<long, long> readMap = reader.OpenMap<long, long>("isolated");
        Assert.Equal(20L, readMap.Get(2L));
        reader.Rollback();
    }

    [Fact]
    public void SizeSkipsTheUndoLogEntriesOfOtherTransactions()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();
        Seed(ts, "isolated", 1, 2, 3, 4, 5);

        // An uncommitted change of another transaction stays out of this size.
        Transaction other = ts.Begin();
        TransactionMap<long, long> otherMap = other.OpenMap<long, long>("isolated");
        otherMap.Put(6L, 60L);

        TransactionMap<long, long> map = OpenMap(ts, IsolationLevel.ReadCommitted, "isolated", out Transaction tx);
        Assert.Equal(5, map.SizeAsLong());
        tx.Rollback();
        other.Rollback();
    }

    [Fact]
    public void SizeCorrectsOverTheUndoLogsAfterAStatementStart()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();
        Seed(ts, "isolated", 1, 2, 3, 4, 5);

        TransactionMap<long, long> map = OpenMap(ts, IsolationLevel.ReadCommitted, "isolated", out Transaction tx);
        // A statement start is what collects the undo log roots.
        HashSet<MVMap<object, VersionedValue<object>>> maps =
            [ s.OpenMap<object, VersionedValue<object>>("isolated") ];
        tx.MarkStatementStart(maps);

        // Another transaction's uncommitted addition is invisible here. Its undo log entry
        // survives the sync (the buffer is flushed, the transaction is open), so the size
        // correction walks the undo logs, finds no entry in this snapshot, and keeps the count.
        Transaction other = ts.Begin();
        other.OpenMap<long, long>("isolated").Put(6L, 60L);
        s.Commit();
        tx.MarkStatementStart(maps);

        Assert.Equal(5, map.SizeAsLong());
        tx.Rollback();
        other.Rollback();
    }

    [Fact]
    public void ReadUncommittedSizeReadsCurrentValues()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();
        Seed(ts, "isolated", 1, 2, 3);

        TransactionMap<long, long> map = OpenMap(ts, IsolationLevel.ReadUncommitted, "isolated", out Transaction tx);
        map.Put(4L, 40L);
        map.Remove(1L);

        // The transaction sees its own additions and removals.
        Assert.Equal(3, map.SizeAsLong());
        tx.Rollback();
    }
}
