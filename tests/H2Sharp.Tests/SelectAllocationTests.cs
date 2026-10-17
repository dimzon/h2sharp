/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Pins the allocation behaviour of a prepared select's run.
 *
 * The select list is snapshotted once when the statement is prepared and shared by every
 * run, the result and the walk. Two copies per run stood here before: one for the result
 * and one for the walk. What a run still allocates is the materialization itself — the
 * result, its row list and one array per row — and the bound below is that floor with
 * room, not the floor itself.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Indexes;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Db;
using H2Sharp.MvStore.Tx;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(MeasurementCollection.Name)]

public sealed class SelectAllocationTests : IDisposable
{
    private readonly TransactionStore store;

    private readonly SessionLocal session;

    private readonly Select select;

    public SelectAllocationTests()
    {
        session = new SessionLocal(new Database(new ConnectionInfo("jdbc:h2:mem:select_alloc")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };

        store = new TransactionStore(MVStore.Open(null!));
        store.Init();

        Column id = new("ID", TypeInfo.TypeInteger, null, 0);
        id.SetNullable(false);
        Column name = new("NAME", TypeInfo.TypeVarchar, null, 1);
        MVTable table = new(
            session.GetDatabase(), new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false),
            0, "T", [id, name], persistData: false);
        foreach (Column column in table.GetColumns())
        {
            column.SetTable(table, column.GetColumnId());
        }

        table.AddIndex(session, "I", 7, [new IndexColumn(id, ascending: true)], 1,
            IndexType.CreatePrimaryKey(persistent: false, hash: false), create: true, indexComment: null);
        table.AddRow(session, new DefaultRow([ValueInteger.Get(3), ValueVarchar.Get("c")]));
        table.AddRow(session, new DefaultRow([ValueInteger.Get(1), ValueVarchar.Get("a")]));
        table.AddRow(session, new DefaultRow([ValueInteger.Get(2), ValueVarchar.Get("b")]));

        TableFilter f = new(session, table, "T", rightsChecked: true, null, 0, null);
        select = new(session);
        select.AddTableFilter(f, isTop: true);
        select.SetExpressions(
        [
            new ExpressionColumn(table.GetColumn(0), f, 0),
            new ExpressionColumn(table.GetColumn(1), f, 0),
        ]);
        select.PrepareExpressions();
        select.PreparePlan(session, targetTableFilter: null);
    }

    public void Dispose() => store.Dispose();

    [Fact]
    public void PreparedSelectReusesItsExpressionArray()
    {
        // The snapshot is taken once and shared: the result, the walk and this getter all
        // read the same array, and a second run answers the same rows from it.
        Assert.Same(select.GetExpressions(), select.GetExpressions());
        Assert.Equal(["=1:a", "=2:b", "=3:c"], Drain(select.Run(-1)));
        Assert.Equal(["=1:a", "=2:b", "=3:c"], Drain(select.Run(-1)));
    }

    [Fact]
    public void RunningAPreparedSelectCostsNoExpressionArrays()
    {
        // Warmed up below; what is left is the materialization floor and nothing per copy.
        long bytes = Measure(() =>
        {
            ResultInterface result = select.Run(-1);
            while (result.Next())
            {
            }
        });

        Assert.InRange(bytes, 0, 4096);
    }

    private static List<string> Drain(ResultInterface result)
    {
        List<string> rows = [];
        while (result.Next())
        {
            Value?[] row = result.CurrentRow();
            rows.Add("=" + row[0] + ":" + row[1]!.GetString());
        }

        return rows;
    }

    private static long Measure(Action action)
    {
        for (int i = 0; i < 3; i++)
        {
            action();
        }

        long smallest = long.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            action();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (allocated < smallest)
            {
                smallest = allocated;
            }
        }

        return smallest;
    }
}
