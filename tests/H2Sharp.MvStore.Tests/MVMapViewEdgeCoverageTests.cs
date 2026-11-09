/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of MVMap view enumerators, the fluent key/value types and the
 * out-of-range key lookups.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Type;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class MVMapViewEdgeCoverageTests : StoreTestBase
{
    private static MVStore Open() => new MVStore.Builder().Open();

    private static MVMap<int, string> Fill(MVStore s, int count)
    {
        MVMap<int, string> m = s.OpenMap<int, string>("data");
        for (int i = 1; i <= count; i++)
        {
            m.Put(i * 10, "v" + i);
        }

        return m;
    }

    [Fact]
    public void EnumeratorsCoverAllViews()
    {
        using MVStore s = Open();
        MVMap<int, string> m = Fill(s, 3);

        int mapEntries = 0;
        foreach (KeyValuePair<int, string> e in m)
        {
            _ = e.Key;
            _ = e.Value;
            mapEntries++;
        }

        Assert.Equal(3, mapEntries);

        int nonGeneric = 0;
        IEnumerator e2 = ((IEnumerable)m).GetEnumerator();
        while (e2.MoveNext())
        {
            _ = e2.Current;
            nonGeneric++;
        }

        Assert.Equal(3, nonGeneric);

        Assert.Equal(new[] { 10, 20, 30 }, m.KeySet().OrderBy(x => x).ToArray());
        Assert.Equal(3, m.EntrySet().Count);
        Assert.Equal(3, m.KeyList().Count);
        Assert.Equal(new[] { 10, 20, 30 }, m.KeyList().ToArray());
        foreach (int k in m.KeyList())
        {
            Assert.True(k > 0);
        }

        int keySetNonGeneric = 0;
        IEnumerator keys = ((IEnumerable)m.KeySet()).GetEnumerator();
        while (keys.MoveNext())
        {
            keySetNonGeneric++;
        }

        Assert.Equal(3, keySetNonGeneric);

        int entrySetNonGeneric = 0;
        IEnumerator entries = ((IEnumerable)m.EntrySet()).GetEnumerator();
        while (entries.MoveNext())
        {
            entrySetNonGeneric++;
        }

        Assert.Equal(3, entrySetNonGeneric);

        foreach (int k in ((IEnumerable<int>)m.KeyList()))
        {
            Assert.True(k > 0);
        }
    }

    [Fact]
    public void FluentKeyAndValueTypesAnswer()
    {
        using MVStore s = Open();
        MVMap<int, string> m = s.OpenMap(
            "typed",
            new MVMap<int, string>.Builder<int, string>()
                .KeyType(IntegerDataType.Instance)
                .ValueType(StringDataType.Instance));
        m.Put(1, "a");
        Assert.Equal("a", m.Get(1));
        Assert.IsType<IntegerDataType>(m.KeyType);
        Assert.IsType<StringDataType>(m.ValueType);
        Assert.Same(m.KeyType, m.GetKeyType());
        Assert.Same(m.ValueType, m.GetValueType());
    }

    [Fact]
    public void GetKeyOutOfRangeAnswersDefault()
    {
        using MVStore s = Open();
        MVMap<int, string> small = Fill(s, 3);
        Assert.Equal(default, small.GetKey(3));
        Assert.Equal(default, small.GetKey(1000));

        MVMap<int, string> m = s.OpenMap<int, string>("big");
        for (int i = 1; i <= 500; i++)
        {
            m.Put(i, "v" + i);
        }

        Assert.Equal(500, m.Count);
        Assert.Equal(default, m.GetKey(500));
        Assert.Equal(default, m.GetKey(100000));
    }
}