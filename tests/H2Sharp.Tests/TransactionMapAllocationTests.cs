/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The transaction-map iterators build their elements without boxing: an entries
 * iterator answers KeyValuePair structs reinterpreted in place, a keys iterator
 * unboxes without re-boxing. A box per row here costs the order of the row itself
 * on every scan, so the allocation is pinned, not just the answers.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using H2Sharp.Engine;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Db;
using H2Sharp.MvStore.Tx;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class TransactionMapAllocationTests : IDisposable
{
    private const int Rows = 2000;

    private readonly MVStore store = MVStore.Open(null!);

    private readonly TransactionStore transactions;

    private readonly SessionLocal session;

    public TransactionMapAllocationTests()
    {
        transactions = new TransactionStore(store);
        transactions.Init();
        session = new SessionLocal(new Database(new ConnectionInfo("jdbc:h2:mem:txmap_alloc")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
    }

    public void Dispose()
    {
        transactions.Dispose();
        store.Close();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void EntryScanElementsAreTypedPairs()
    {
        MVTable table = NewTable();
        for (int i = 0; i < Rows; i++)
        {
            table.AddRow(session, Row(i, "v" + i));
        }

        List<SearchRow> read = table.Scan(session);

        // Keys in order with the values that went in: a misread pair type would surface
        // here as garbage keys, not just as a count.
        Assert.Equal(Rows, read.Count);
        for (int i = 0; i < Rows; i++)
        {
            Assert.Equal(ValueInteger.Get(i), read[i].GetValue(0));
            Assert.Equal(ValueVarchar.Get("v" + i), read[i].GetValue(1));
        }
    }

    [Fact]
    public void EntryScanDoesNotBoxPairs()
    {
        MVTable table = NewTable();
        for (int i = 0; i < Rows; i++)
        {
            table.AddRow(session, Row(i, "v" + i));
        }

        // Warmup: JIT, page cache, snapshot machinery.
        List<SearchRow> warm = table.Scan(session);
        Assert.Equal(Rows, warm.Count);

        long before = GC.GetAllocatedBytesForCurrentThread();
        List<SearchRow> read = table.Scan(session);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(Rows, read.Count);

        // Was: one boxed KeyValuePair per row (~48KB here on top) plus the unbox. Is: the
        // pair is reinterpreted in place, so a full scan stays far below one box a row.
        // Calibrated: ~34KB after the fix (list growth, cursor, snapshot), ~82KB+ before.
        Assert.True(allocated < 60000, $"allocated {allocated} bytes for {Rows} rows");
    }

    private static Row Row(int id, string name)
        => new DefaultRow([ValueInteger.Get(id), ValueVarchar.Get(name)]);

    private MVTable NewTable()
    {
        Column id = new("ID", TypeInfo.TypeInteger, null, 0);
        Column name = new("NAME", TypeInfo.TypeVarchar, null, 1);
        MVTable table = new(session.GetDatabase(), null, 1, "PERSON", [id, name], persistIndexes: false, persistData: false);
        foreach (Column column in table.GetColumns())
        {
            column.SetTable(table, column.GetColumnId());
        }

        return table;
    }
}
