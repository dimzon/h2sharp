/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * TransactionMap key handling on large row ids: the small-long cache covers 0..127,
 * so ids above it show the real cost of the key path (boxed object-keyed map vs a
 * typed MVMap<K, VersionedValue<object>>). Issue #88 / SP22.
 */
using System;
using BenchmarkDotNet.Attributes;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Type;
using H2Sharp.MvStore.Tx;

namespace H2Sharp.Benchmarks;

public class TransactionMapBench
{
    private const int Rows = 100_000;

    private MVStore store = null!;

    private TransactionStore transactions = null!;

    private Transaction transaction = null!;

    private TransactionMap<long, string> map = null!;

    [GlobalSetup]
    public void Setup()
    {
        store = MVStore.Open(null!);
        transactions = new TransactionStore(store);
        transactions.Init();
        transaction = transactions.Begin();
        map = transaction.OpenMap<long, string>("bench", LongDataType.Instance, null);
        for (long id = 0; id < Rows; id++)
        {
            map.Put(id, "v" + id);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        transaction.Rollback();
        transactions.Dispose();
        store.Close();
    }

    [Benchmark]
    public string? GetSmallId() => map.GetImmediate(42);

    [Benchmark]
    public string? GetLargeId() => map.GetImmediate(Rows - 1);

    [Benchmark]
    public string? PutLargeId()
    {
        // Overwrite an existing large id: the undo log records the old value, so this is
        // the full write path with its one undo-log box (the reference boxes it too).
        return map.Put(Rows - 1, "v" + (Rows - 1));
    }
}
