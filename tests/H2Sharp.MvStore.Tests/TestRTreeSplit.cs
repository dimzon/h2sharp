/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The r-tree under pressure: the quadratic split, the linear split's fallback for keys
 * without extremes, the collapse when everything is removed, and the cursor's skip.
 */
using System.Collections.Generic;
using H2Sharp.MvStore;
using H2Sharp.MvStore.RTree;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class TestRTreeSplit : StoreTestBase
{
    [Fact]
    public void QuadraticSplitKeepsEveryKey()
    {
        using MVStore store = new MVStore.Builder().PageSplitSize(1000).Open();
        MVRTreeMap<string> map = store.OpenMap("rtree", new MVRTreeMap<string>.Builder<string>());
        map.SetQuadraticSplit(true);
        AssertTrue(map.IsQuadraticSplit());

        for (int i = 0; i < 400; i++)
        {
            float x = i % 20, y = i / 20;
            map.Add(new DefaultSpatial(i, x, x + 1, y, y + 1), "v" + i);
        }

        AssertEqual(400L, map.SizeAsLong());
        AssertEqual("v333", map.Get(new DefaultSpatial(333, 13, 14, 16, 17)));

        // The tree split into inner pages: their bounds are node keys of their own.
        List<Spatial> nodeKeys = [];
        map.AddNodeKeys(nodeKeys, map.GetRootPage());
        AssertTrue(nodeKeys.Count > 0);

        Spatial all = new DefaultSpatial(-1, -1f, 25f, -1f, 25f);
        AssertEqual(400, ReadIds(map.FindIntersectingKeys(all)).Count);
        AssertEqual(400, ReadIds(map.FindContainedKeys(all)).Count);
    }

    [Fact]
    public void RemovingEverythingCollapsesTheTree()
    {
        using MVStore store = new MVStore.Builder().PageSplitSize(1000).Open();
        MVRTreeMap<string> map = store.OpenMap("rtree", new MVRTreeMap<string>.Builder<string>());
        List<Spatial> keys = [];
        for (int i = 0; i < 400; i++)
        {
            float x = i % 20, y = i / 20;
            Spatial key = new DefaultSpatial(i, x, x + 1, y, y + 1);
            keys.Add(key);
            map.Put(key, "v" + i);
        }

        AssertTrue(map.SizeAsLong() > 0);
        for (int i = 0; i < keys.Count; i++)
        {
            AssertEqual("v" + i, map.Remove(keys[i]));
        }

        AssertEqual(0L, map.SizeAsLong());
        AssertTrue(map.Get(keys[0]) is null);

        // The emptied root is reused: a new entry lands and answers.
        map.Add(new DefaultSpatial(1, 0, 1, 0, 1), "again");
        AssertEqual(1L, map.SizeAsLong());
        AssertEqual("again", map.Get(new DefaultSpatial(1, 0, 1, 0, 1)));
    }

    /// <summary>Keys that all sit at one point have no extremes to split between, so the
    /// linear split falls back to the quadratic one</summary>
    [Fact]
    public void IdenticalPointsFallBackToTheQuadraticSplit()
    {
        using MVStore store = new MVStore.Builder().PageSplitSize(1000).Open();
        MVRTreeMap<string> map = store.OpenMap("rtree", new MVRTreeMap<string>.Builder<string>());
        for (int i = 0; i < 120; i++)
        {
            map.Put(new DefaultSpatial(i, 5, 5, 5, 5), "v" + i);
        }

        AssertEqual(120L, map.SizeAsLong());
        AssertEqual("v77", map.Get(new DefaultSpatial(77, 5, 5, 5, 5)));

        List<Spatial> nodeKeys = [];
        map.AddNodeKeys(nodeKeys, map.GetRootPage());
        AssertTrue(nodeKeys.Count > 0);
    }

    [Fact]
    public void TheCursorSkipsEntries()
    {
        using MVStore store = new MVStore.Builder().PageSplitSize(1000).Open();
        MVRTreeMap<string> map = store.OpenMap("rtree", new MVRTreeMap<string>.Builder<string>());
        for (int i = 0; i < 10; i++)
        {
            map.Put(new DefaultSpatial(i, i, i + 1, i, i + 1), "v" + i);
        }

        Spatial all = new DefaultSpatial(-1, -1f, 12f, -1f, 12f);
        MVRTreeMap<string>.RTreeCursor<string> cursor = map.FindIntersectingKeys(all);
        cursor.Skip(3);
        AssertEqual(7, ReadIds(cursor).Count);
    }

    private static List<long> ReadIds(MVRTreeMap<string>.RTreeCursor<string> cursor)
    {
        List<long> ids = [];
        while (cursor.HasNext())
        {
            ids.Add(cursor.Next()!.GetId());
        }

        ids.Sort();
        return ids;
    }
}
