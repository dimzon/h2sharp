/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The .NET collection surface of MVMap and the map's key/refusal edges.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.MvStore.Tests;

/// <summary>
/// The collection views <see cref="MVMap{K,V}"/> exposes to .NET callers: the key list,
/// the entry set and the key set. The reference expresses the same three as java.util views;
/// here they are ICollection/IList adapters over the cursor, and the read-only road is what
/// they promise — every mutation throws rather than pretending.
/// <para>
/// The neighbor and refusal edges sit here too: key lookups by index, the negative answer of
/// the index search, and the two ways a write is refused (a closed map and a read-only map).
/// </para>
/// </summary>
public sealed class TestMVMapViews : StoreTestBase
{
    private static MVStore Open() => new MVStore.Builder().Open();

    private static MVMap<int, string> Fill(MVStore s)
    {
        MVMap<int, string> m = s.OpenMap<int, string>("data");
        m.Put(10, "a");
        m.Put(20, "b");
        m.Put(30, "c");
        return m;
    }

    [Fact]
    public void KeyListReadsByIndexAndRefusesWrites()
    {
        using MVStore s = Open();
        MVMap<int, string> m = Fill(s);
        IList<int> keys = m.KeyList();

        Assert.Equal(3, keys.Count);
        Assert.True(keys.IsReadOnly);
        Assert.Equal(10, keys[0]);
        Assert.Equal(20, keys[1]);
        Assert.Equal(30, keys[2]);
        Assert.Equal(default, keys[-1]);
        Assert.Equal(default, keys[99]);

        Assert.Equal(1, keys.IndexOf(20));
        Assert.True(keys.IndexOf(99) < 0);
        Assert.True(keys.Contains(30));
        Assert.False(keys.Contains(99));

        int[] array = new int[5];
        keys.CopyTo(array, 1);
        Assert.Equal(0, array[0]);
        Assert.Equal(10, array[1]);
        Assert.Equal(20, array[2]);
        Assert.Equal(30, array[3]);
        Assert.Equal(0, array[4]);

        Assert.Equal(new[] { 10, 20, 30 }, keys.ToArray());

        Assert.Throws<NotSupportedException>(() => keys.Add(1));
        Assert.Throws<NotSupportedException>(() => keys.Insert(0, 1));
        Assert.Throws<NotSupportedException>(() => keys.Remove(10));
        Assert.Throws<NotSupportedException>(() => keys.RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => keys.Clear());
        Assert.Throws<NotSupportedException>(() => keys[0] = 5);
    }

    [Fact]
    public void EntrySetReadsPairsAndRefusesWrites()
    {
        using MVStore s = Open();
        MVMap<int, string> m = Fill(s);
        ICollection<KeyValuePair<int, string>> entries = m.EntrySet();

        Assert.Equal(3, entries.Count);
        Assert.True(entries.IsReadOnly);
        Assert.Equal(
            [new KeyValuePair<int, string>(10, "a"), new(20, "b"), new(30, "c")],
            entries.ToArray());

        // The reference's entry view answers contains() by key alone.
        Assert.True(entries.Contains(new KeyValuePair<int, string>(20, "not the value")));
        Assert.False(entries.Contains(new KeyValuePair<int, string>(99, "b")));

        KeyValuePair<int, string>[] array = new KeyValuePair<int, string>[4];
        entries.CopyTo(array, 1);
        Assert.Equal(10, array[1].Key);
        Assert.Equal("a", array[1].Value);
        Assert.Equal(30, array[3].Key);

        Assert.Throws<NotSupportedException>(() => entries.Add(default));
        Assert.Throws<NotSupportedException>(() => entries.Remove(default));
        Assert.Throws<NotSupportedException>(() => entries.Clear());
    }

    [Fact]
    public void KeySetReadsAndRefusesWrites()
    {
        using MVStore s = Open();
        MVMap<int, string> m = Fill(s);
        ICollection<int> keys = m.KeySet();

        Assert.Equal(3, keys.Count);
        Assert.True(keys.IsReadOnly);
        Assert.Equal(new[] { 10, 20, 30 }, keys.ToArray());
        Assert.True(keys.Contains(20));
        Assert.False(keys.Contains(99));

        int[] array = new int[4];
        keys.CopyTo(array, 1);
        Assert.Equal(0, array[0]);
        Assert.Equal(10, array[1]);
        Assert.Equal(20, array[2]);
        Assert.Equal(30, array[3]);

        Assert.Throws<NotSupportedException>(() => keys.Add(1));
        Assert.Throws<NotSupportedException>(() => keys.Remove(10));
        Assert.Throws<NotSupportedException>(() => keys.Clear());
    }

    [Fact]
    public void GetKeyAndGetKeyIndexHandleEmptyAndMissing()
    {
        using MVStore s = Open();
        MVMap<int, string> m = s.OpenMap<int, string>("empty");
        Assert.Equal(default, m.GetKey(0));
        Assert.Equal(-1, m.GetKeyIndex(1));

        MVMap<int, string> full = Fill(s);
        Assert.Equal(default, full.GetKey(-1));
        Assert.Equal(default, full.GetKey(3));
        Assert.Equal(0, full.GetKeyIndex(10));
        Assert.Equal(2, full.GetKeyIndex(30));
        Assert.True(full.GetKeyIndex(25) < 0);
        Assert.True(full.GetKeyIndex(5) < 0);
    }

    [Fact]
    public void NeighborKeysWalkADeepTree()
    {
        // A tiny page split size forces several page levels, so the neighbor search walks
        // down through child pages instead of answering from one leaf.
        using MVStore s = new MVStore.Builder().PageSplitSize(64).Open();
        MVMap<int, int> m = s.OpenMap<int, int>("big");
        for (int i = 0; i < 2000; i += 2)
        {
            m.Put(i, i);
        }

        Assert.Equal(2, m.HigherKey(0));
        Assert.Equal(0, m.CeilingKey(0));
        Assert.Equal(0, m.FloorKey(1));
        Assert.Equal(0, m.LowerKey(2));
        Assert.Equal(1998, m.LowerKey(1999));
        Assert.Equal(1998, m.FloorKey(1999));
        // No key above the last one: the value-type map answers default, the same road the
        // reference walks with a null answer.
        Assert.Equal(0, m.HigherKey(1999));

        Assert.Equal(0, m.HigherKey(null));
        Assert.Equal(0, m.CeilingKey(null));
        Assert.Equal(0, m.FloorKey(null));
        Assert.Equal(0, m.LowerKey(null));

        // The root-ref overloads answer the same as the root-less ones.
        RootReference<int, int> root = m.GetRoot();
        Assert.Equal(2, m.HigherKey(root, 0));
        Assert.Equal(1998, m.LowerKey(root, 1999));
    }

    [Fact]
    public void WritesToAClosedMapAndAReadOnlyMapAreRefused()
    {
        using MVStore s = Open();
        MVMap<int, string> m = Fill(s);

        MVMap<int, string> readOnly = m.OpenVersion(m.GetVersion());
        Assert.True(readOnly.IsReadOnly());
        NotSupportedException readOnlyRefusal =
            Assert.Throws<NotSupportedException>(() => readOnly.Put(40, "d"));
        Assert.Contains("read-only", readOnlyRefusal.Message, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => readOnly.OpenVersion(readOnly.GetVersion()));

        MVMap<int, string> removed = s.OpenMap<int, string>("temporary");
        removed.Put(1, "x");
        s.RemoveMap(removed);
        Assert.True(removed.IsClosed());
        MvStoreException closedRefusal =
            Assert.Throws<MvStoreException>(() => removed.Put(2, "y"));
        Assert.Equal(DataUtils.ErrorClosed, closedRefusal.GetErrorCode());
    }

    [Fact]
    public void MapIdentityIsByInstance()
    {
        using MVStore s = Open();
        MVMap<int, string> m = Fill(s);
        MVMap<int, string> sameName = s.OpenMap<int, string>("data");
        MVMap<int, string> other = s.OpenMap<int, string>("other");
        // Opening a map by the name it already has answers the same map, not a copy.
        Assert.True(ReferenceEquals(m, sameName));
        Assert.True(m.Equals(m));
        Assert.False(m.Equals(null));
        Assert.False(m.Equals(new object()));
        Assert.False(m.Equals(other));
        Assert.True(m.GetHashCode() == m.GetHashCode());
    }
}
