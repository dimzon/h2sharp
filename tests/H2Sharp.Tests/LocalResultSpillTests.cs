/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * LocalResult over a persistent database with a tiny in-memory row budget: the paths where
 * rows spill to the external (MVStore-backed) storage and are read back, DISTINCT folding,
 * WITH TIES pulling the equal peers, and the FETCH PERCENT refusal of a bogus percentage.
 */
using System;
using System.IO;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class LocalResultSpillTests : IDisposable
{
    private readonly string directory;

    public LocalResultSpillTests()
    {
        directory = Path.Combine(Path.GetTempPath(), "local_result_spill_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
    }

    public void Dispose()
    {
        try { Directory.Delete(directory, true); } catch { }
    }

    private SessionLocal OpenSession()
    {
        Database database = new(ConnectionInfo.File(Path.Combine(directory, "db")));
        database.SetMaxMemoryRows(2);
        return new SessionLocal(database, "sa");
    }

    private static LocalResult NewResult(SessionLocal session)
    {
        LocalResult result = new(
            session,
            new Expression[] { }, visibleColumnCount: 1, resultColumnCount: 1);
        result.SetMaxMemoryRows(2);
        return result;
    }

    [Fact]
    public void RowsSpillToExternalStorageAndReadBack()
    {
        using SessionLocal session = OpenSession();
        using LocalResult result = NewResult(session);
        for (int i = 1; i <= 5; i++)
        {
            result.AddRow(ValueInteger.Get(i));
        }

        result.Done();
        Assert.Equal(5, result.GetRowCount());
        for (int i = 1; i <= 5; i++)
        {
            Assert.True(result.Next());
            Assert.Equal(ValueInteger.Get(i), result.CurrentRow()[0]);
        }

        Assert.False(result.Next());

        // Reading the spilled rows again, from the start.
        result.Reset();
        Assert.True(result.Next());
        Assert.Equal(ValueInteger.Get(1), result.CurrentRow()[0]);
    }

    [Fact]
    public void DistinctFoldsDuplicateRows()
    {
        using SessionLocal session = OpenSession();
        LocalResult result = NewResult(session);
        result.SetDistinct();
        result.AddRow(ValueInteger.Get(1));
        result.AddRow(ValueInteger.Get(2));
        result.AddRow(ValueInteger.Get(1));
        result.Done();

        Assert.Equal(2, result.GetRowCount());
        Assert.True(result.ContainsDistinct(new Value?[] { ValueInteger.Get(1) }));
        Assert.False(result.ContainsDistinct(new Value?[] { ValueInteger.Get(3) }));
        Assert.True(result.ContainsNull() == false);
    }

    [Fact]
    public void WithTiesPullsEqualPeers()
    {
        using SessionLocal session = OpenSession();
        using LocalResult result = NewResult(session);
        SortOrder sortOrder = new(session, new[] { 0 }, new[] { Result.SortOrder.Ascending }, null);
        result.SetSortOrder(sortOrder);
        result.SetWithTies(sortOrder);
        result.SetLimit(1);
        result.AddRow(ValueInteger.Get(1));
        result.AddRow(ValueInteger.Get(1));
        result.AddRow(ValueInteger.Get(2));
        result.Done();

        Assert.Equal(2, result.GetRowCount());
    }

    [Fact]
    public void BogusFetchPercentIsRefused()
    {
        using SessionLocal session = OpenSession();
        using LocalResult result = NewResult(session);
        result.SetFetchPercent(true);
        result.SetLimit(150);
        result.AddRow(ValueInteger.Get(1));
        Assert.ThrowsAny<H2SharpException>(() => result.Done());
    }
}
