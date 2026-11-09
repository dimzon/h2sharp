/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of Page's saved-page surface: the dump used for corrupt-file
 * messages, the accurate and approximate disk-space counters, and the base
 * Remove(long) refusal.
 */
using System;
using H2Sharp.MvStore;
using H2Sharp.MvStore.RTree;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class PageCoverageTests : StoreTestBase
{
    private static Page<int, string> FindLeaf(Page<int, string> page)
    {
        Page<int, string> current = page;
        while (!current.IsLeaf())
        {
            current = current.GetChildPage(0);
        }

        return current;
    }

    [Fact]
    public void SavedPagesDumpAndCountDiskSpace()
    {
        string file = TempFile("page-dump.mv");
        DeleteFile(file);
        using MVStore s = MVStore.Open(file);
        MVMap<int, string> m = s.OpenMap<int, string>("m");
        for (int i = 0; i < 200; i++)
        {
            m.Put(i, "value-" + i);
        }

        s.Commit();
        Page<int, string> root = m.GetRootPage();
        string rootText = root.ToString();
        Assert.Contains("pos:", rootText, StringComparison.Ordinal);
        _ = root.GetDiskSpaceUsed(true);
        _ = root.GetDiskSpaceUsed(false);
        if (!root.IsLeaf())
        {
            Assert.Throws<NotSupportedException>(() => root.Remove(0L));
            Assert.Throws<NotSupportedException>(() => root.GetValue(0));
            Assert.Throws<NotSupportedException>(() => root.SetValue(0, "x"));
            Assert.Throws<NotSupportedException>(() => root.InsertLeaf(0, 0, "x"));
            Page<int, string> leaf = FindLeaf(root);
            Assert.Contains("pos:", leaf.ToString(), StringComparison.Ordinal);
            _ = leaf.GetDiskSpaceUsed(true);
            _ = leaf.GetDiskSpaceUsed(false);
            Assert.Throws<NotSupportedException>(() => leaf.GetChildPage(0));
            Assert.Throws<NotSupportedException>(() => leaf.GetChildPagePos(0));
            Assert.Throws<NotSupportedException>(() => leaf.SetChild(0, leaf));
            Assert.Throws<NotSupportedException>(() => leaf.InsertNode(0, 0, leaf));
        }

        m.Put(1000, "late");
        s.Commit();
    }

    [Fact]
    public void DeepTreeApproximationAndDump()
    {
        string file = TempFile("page-deep.mv");
        DeleteFile(file);
        using MVStore s = new MVStore.Builder()
            .FileName(file)
            .PageSplitSize(64)
            .Open();
        MVMap<int, string> m = s.OpenMap<int, string>("m");
        for (int i = 0; i < 600; i++)
        {
            m.Put(i, "v" + i);
        }

        s.Commit();
        Page<int, string> root = m.GetRootPage();
        Assert.False(root.IsLeaf());
        _ = root.GetDiskSpaceUsed(true);
        _ = root.GetDiskSpaceUsed(false);
        _ = root.ToString();
        Page<int, string> leaf = FindLeaf(root);
        _ = leaf.GetDiskSpaceUsed(true);
        _ = leaf.ToString();
    }

    [Fact]
    public void EmptyMapRootDumpsUnsaved()
    {
        using MVStore s = new MVStore.Builder().Open();
        MVMap<int, string> m = s.OpenMap<int, string>("empty");
        Page<int, string> root = m.GetRootPage();
        Assert.True(root.IsLeaf());
        Assert.Contains("pos:", root.ToString(), StringComparison.Ordinal);
        Assert.Equal(0L, root.GetDiskSpaceUsed(true));
        Assert.False(string.IsNullOrEmpty(Page<int, string>.PageReference.EmptyRef().ToString()));
    }

    [Fact]
    public void SpatialTreeSplitsInternalNodes()
    {
        string file = TempFile("page-rtree.mv");
        DeleteFile(file);
        using MVStore s = new MVStore.Builder()
            .FileName(file)
            .PageSplitSize(128)
            .Open();
        MVRTreeMap<string> map = s.OpenMap("rtree", new MVRTreeMap<string>.Builder<string>());
        var random = new Random(7);
        for (int i = 0; i < 400; i++)
        {
            float x = (float)random.NextDouble();
            float y = (float)random.NextDouble();
            float width = (float)random.NextDouble() / 10;
            float height = (float)random.NextDouble() / 10;
            map.Put(new DefaultSpatial(i, x, x + width, y, y + height), "v" + i);
        }

        s.Commit();
        Page<object, string> root = map.GetRootPage();
        Assert.Contains("pos:", root.ToString(), StringComparison.Ordinal);
        _ = root.GetDiskSpaceUsed(false);
    }
}