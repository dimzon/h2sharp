/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Regression tests for three traps met while porting org.h2.mvstore.tx, distilled from
 * TestTransactionStore failures. None of these has a counterpart in the reference suite;
 * each failed here for a reason that only exists in C#.
 */

using System;
using System.Collections.Generic;
using System.Threading;
using H2Sharp.MvStore.Tx;
using Xunit;

namespace H2Sharp.MvStore.Tests;

/// <summary>
/// What the transliteration got wrong once.
/// </summary>
public sealed class TestTxRegression : StoreTestBase
{
    /// <summary>
    /// Rollback with no reads in between: every change must vanish, including entries whose
    /// undo records never left the append buffer. An intermediate read used to mask this by
    /// flushing the buffer first (FlushAppendBuffer returned the stale root).
    /// </summary>
    [Fact]
    public void TestRollbackWithoutReads()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();
        Transaction tx = ts.Begin();
        TransactionMap<string, string> m = tx.OpenMap<string, string>("test");
        m.Put("1", "Hello");
        m.Put("2", "World");
        m.Put("3", "!");
        tx.Rollback();

        tx = ts.Begin();
        m = tx.OpenMap<string, string>("test");
        Assert.Null(m.Get("1"));
        Assert.Null(m.Get("2"));
        Assert.Null(m.Get("3"));
        Assert.False(tx.GetChanges(0).GetEnumerator().MoveNext());
        tx.Commit();
        Assert.True(ts.GetOpenTransactions().Count == 0);
    }

    /// <summary>
    /// Neighbors on an empty long map: entries read as null, keys as 0. A missing bound is
    /// null, and a (0, 0) entry is a present one: both were once confused with each other
    /// (a `? value : default` ternary and `default` bounds).
    /// </summary>
    [Fact]
    public void TestEmptyMapNeighbors()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();
        Transaction tx = ts.Begin();
        TransactionMap<long, long> m = tx.OpenMap<long, long>("test");
        Assert.Null(m.FirstEntry());
        Assert.Equal(0L, m.FirstKey());
        Assert.Null(m.LastEntry());
        Assert.Equal(0L, m.LastKey());
        Assert.Null(m.CeilingEntry(5L));
        Assert.Equal(0L, m.CeilingKey(5L));
        Assert.Null(m.FloorEntry(5L));
        Assert.Equal(0L, m.FloorKey(5L));
        Assert.Null(m.HigherEntry(5L));
        Assert.Equal(0L, m.HigherKey(5L));
        Assert.Null(m.LowerEntry(5L));
        Assert.Equal(0L, m.LowerKey(5L));

        m.Put(0L, 0L);
        Assert.Equal(new KeyValuePair<long, long>(0L, 0L), m.FirstEntry());
        Assert.Equal(0L, m.FirstKey());
        Assert.Equal(new KeyValuePair<long, long>(0L, 0L), m.HigherEntry(-1L));
        tx.Rollback();
    }

    /// <summary>
    /// Missing reads on an int map answer 0 instead of throwing: a direct (V?) cast of null
    /// throws for value types (unbox of null), where Java answers null.
    /// </summary>
    [Fact]
    public void TestUnboxMissingInt()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();
        Transaction tx = ts.Begin();
        TransactionMap<int, int> m = tx.OpenMap<int, int>("data");
        Assert.Equal(0, m.Get(42));
        Assert.Equal(0, m.Remove(42));
        Assert.False(m.ContainsKey(42));
        m.Put(1, 10);
        Assert.Equal(10, m.Get(1));
        m.Remove(1);
        Assert.Equal(0, m.Get(1));
        tx.Commit();

        tx = ts.Begin();
        m = tx.OpenMap<int, int>("data");
        Assert.Equal(0, m.Get(1));
    }

    /// <summary>
    /// Concurrent begin/commit churn: ids stay unique and every transaction ends exactly once.
    /// BitSetHelper.Flip once mutated the shared array in place (Array.Resize is a no-op when
    /// the size matches), corrupting every snapshot held by another thread.
    /// </summary>
    [Fact]
    public void TestConcurrentBeginCommit()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();

        const int threadCount = 4;
        const int txPerThread = 200;
        var ready = new CountdownEvent(threadCount);
        var done = new CountdownEvent(threadCount);
        var failures = new List<Exception>();
        for (int i = 0; i < threadCount; i++)
        {
            int thread = i;
            var worker = new Thread(() =>
            {
                try
                {
                    ready.Signal();
                    ready.Wait();
                    for (int j = 0; j < txPerThread; j++)
                    {
                        Transaction tx = ts.Begin();
                        TransactionMap<int, int> m = tx.OpenMap<int, int>("data");
                        m.Put(thread * txPerThread + j, j);
                        tx.Commit();
                    }
                }
                catch (Exception e)
                {
                    lock (failures)
                    {
                        failures.Add(e);
                    }
                }
                finally
                {
                    done.Signal();
                }
            })
            {
                IsBackground = true,
            };
            worker.Start();
        }

        done.Wait();
        Assert.True(failures.Count == 0, failures.Count == 0 ? "" : failures[0].ToString());
        Assert.True(ts.GetOpenTransactions().Count == 0);

        Transaction tx = ts.Begin();
        TransactionMap<int, int> m = tx.OpenMap<int, int>("data");
        Assert.Equal(threadCount * txPerThread, m.Size());
    }
}
