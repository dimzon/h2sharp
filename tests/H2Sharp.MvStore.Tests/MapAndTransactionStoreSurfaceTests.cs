/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 258: the map's navigation and update surface, and TransactionStore's public state
 * queries, which the ordinary map/transaction tests never ask.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Tx;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class MapAndTransactionStoreSurfaceTests
{
    [Fact]
    public void MapNavigatesKeysAndAppliesConditionalUpdates()
    {
        using MVStore store = new MVStore.Builder().Open();
        MVMap<int, string> map = store.OpenMap<int, string>("map_surface");
        for (int i = 1; i <= 5; i++)
        {
            map.Put(i, "v" + i);
        }

        Assert.Equal(1, map.FirstKey());
        Assert.Equal(5, map.LastKey());
        Assert.Equal(3, map.GetKey(2));
        Assert.Equal(2, map.GetKeyIndex(3));
        Assert.Equal(4, map.HigherKey(3));
        Assert.Equal(3, map.CeilingKey(3));
        Assert.Equal(3, map.FloorKey(3));
        Assert.Equal(2, map.LowerKey(3));
        Assert.True(map.ContainsKey(3));
        Assert.False(map.ContainsKey(9));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, map.KeyList().ToArray());

        Assert.Equal("v3", map.PutIfAbsent(3, "other"));
        Assert.Null(map.PutIfAbsent(6, "v6"));
        Assert.False(map.Remove(3, "wrong"));
        Assert.True(map.Remove(3, "v3"));
        Assert.True(map.Replace(4, "v4", "new"));
        Assert.False(map.Replace(4, "wrong", "x"));
        Assert.Equal("new", map.Replace(4, "new2"));

        Assert.Equal(new[] { 1, 2, 4, 5, 6 }, map.KeySet().OrderBy(k => k).ToArray());
        Assert.False(map.IsClosed());
        map.Clear();
        Assert.Empty(map.EntrySet());
    }

    [Fact]
    public void TransactionStoreAnswersStateQueries()
    {
        using MVStore store = new MVStore.Builder().Open();
        TransactionStore transactionStore = new(store);
        transactionStore.Init();
        try
        {
            Assert.False(transactionStore.HasMap("none"));
            transactionStore.SetMaxTransactionId(100);
            Assert.Empty(transactionStore.GetOpenTransactions());

            Transaction transaction = transactionStore.Begin();
            Assert.False(transactionStore.IsTransactionClosed(transaction.GetId()));
            Assert.Single(transactionStore.GetOpenTransactions());
            transaction.Rollback();
            Assert.True(transactionStore.IsTransactionClosed(transaction.GetId()));
            Assert.Empty(transactionStore.GetOpenTransactions());
            transactionStore.EndLeftoverTransactions();
        }
        finally
        {
            transactionStore.Close();
        }
    }
}
