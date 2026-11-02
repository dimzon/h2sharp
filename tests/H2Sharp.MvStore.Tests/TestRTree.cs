/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Exercises the r-tree map itself: inserts that split pages, contained and
 * intersecting scans, removal and spatial key serialization.
 */
using System.Collections.Generic;
using H2Sharp.MvStore;
using H2Sharp.MvStore.RTree;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class TestRTree : StoreTestBase
{
    [Fact]
    public void InsertFindAndRemove()
    {
        using MVStore store = new MVStore.Builder().Open();
        MVRTreeMap<string> map = store.OpenMap("rtree", new MVRTreeMap<string>.Builder<string>());
        for (int i = 0; i < 200; i++)
        {
            float x = i % 20, y = i / 20;
            map.Put(new DefaultSpatial(i, x, x + 1, y, y + 1), "v" + i);
        }

        AssertEqual(200L, map.SizeAsLong());
        AssertEqual("v42", map.Get(new DefaultSpatial(42, 2, 3, 2, 3)));
        Assert.True(map.Get(new DefaultSpatial(1042, 2, 3, 2, 3)) is null);

        Spatial query = new DefaultSpatial(-1, -0.5f, 1.5f, -0.5f, 1.5f);
        Assert.Equal(new List<int> { 0 }, ReadIds(map.FindContainedKeys(query)));
        Assert.Equal(
            new List<int> { 0, 1, 2, 20, 21, 22, 40, 41, 42 },
            ReadIds(map.FindIntersectingKeys(
                new DefaultSpatial(-1, 0.1f, 2.0f, 0.1f, 2.0f))));

        AssertEqual("v42", map.Remove(new DefaultSpatial(42, 2, 3, 2, 3)));
        AssertEqual(199L, map.SizeAsLong());
        Assert.True(map.Get(new DefaultSpatial(42, 2, 3, 2, 3)) is null);
        Assert.True(map.Remove(new DefaultSpatial(42, 2, 3, 2, 3)) is null);
    }

    [Fact]
    public void SpatialKeysRoundTrip()
    {
        SpatialDataType type = new(2);
        AssertTrue(type.GetMemory(new DefaultSpatial(1, 0, 1, 0, 1)) > 0);
        AssertEqual(0, type.Compare(null, null));
        AssertEqual(-1, type.Compare(null, new DefaultSpatial(1, 0, 1, 0, 1)));
        AssertEqual(1, type.Compare(new DefaultSpatial(1, 0, 1, 0, 1), null));
        AssertTrue(type.Equals(null, null));
        AssertFalse(type.Equals(null, new DefaultSpatial(1, 0, 1, 0, 1)));

        AssertEnvelope(new DefaultSpatial(7), RoundTrip(type, new DefaultSpatial(7)));
        AssertEnvelope(
            new DefaultSpatial(1, 0.5f, 2.5f, -1.5f, 1.5f),
            RoundTrip(type, new DefaultSpatial(1, 0.5f, 2.5f, -1.5f, 1.5f)));
        AssertEnvelope(
            new DefaultSpatial(2, 1.0f, 1.0f, 2.0f, 2.0f),
            RoundTrip(type, new DefaultSpatial(2, 1.0f, 1.0f, 2.0f, 2.0f)));
    }

    private static List<int> ReadIds(MVRTreeMap<string>.RTreeCursor<string> cursor)
    {
        List<int> ids = [];
        while (cursor.HasNext())
        {
            ids.Add((int)cursor.Next()!.GetId());
        }

        ids.Sort();
        return ids;
    }

    private static Spatial RoundTrip(SpatialDataType type, Spatial key)
    {
        WriteBuffer buffer = new();
        type.Write(buffer, key);
        byte[] bytes = buffer.ToArray();
        int position = 0;
        Spatial read = type.Read(bytes, ref position);
        AssertEqual(bytes.Length, position);
        return read;
    }

    private static void AssertEnvelope(Spatial expected, Spatial actual)
    {
        AssertEqual(expected.GetId(), actual.GetId());
        AssertEqual(expected.IsNull(), actual.IsNull());
        if (!expected.IsNull())
        {
            AssertEqual(expected.Min(0), actual.Min(0));
            AssertEqual(expected.Max(0), actual.Max(0));
            AssertEqual(expected.Min(1), actual.Min(1));
            AssertEqual(expected.Max(1), actual.Max(1));
        }
    }
}
