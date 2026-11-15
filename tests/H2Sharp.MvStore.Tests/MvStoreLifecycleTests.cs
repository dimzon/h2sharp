/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 218: the store lifecycle edges — a compaction carries the store settings over,
 * a removed map is pruned once its version ages out, a rollback trims the newer versions,
 * the leaf surface answers, a four-level tree is measured from the top, and a concurrent
 * open of one new map converges on a single instance.
 */
using System;
using System.Linq;
using System.Threading.Tasks;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class MvStoreLifecycleTests : StoreTestBase
{
    [Fact]
    public void CompactionCarriesTheStoreSettingsOver()
    {
        string file = TempFile("compact-settings.mv");
        DeleteFile(file);
        using (MVStore s = MVStore.Open(file))
        {
            s.SetStoreVersion(7);
            MVMap<int, string> m = s.OpenMap<int, string>("alpha");
            m.Put(1, "one");
            _ = s.Commit();
            Assert.Contains("alpha", s.GetMapNames());
        }

        MVStore.Compact(file, true, null);

        using MVStore reopened = MVStore.Open(file);
        Assert.Equal(7, reopened.GetStoreVersion());
        Assert.Equal("one", reopened.OpenMap<int, string>("alpha").Get(1));
    }

    [Fact]
    public void ARemovedMapIsPrunedOnceItsVersionAgesOut()
    {
        string file = TempFile("prune-removed.mv");
        DeleteFile(file);
        using MVStore s = MVStore.Open(file);
        s.SetVersionsToKeep(0);
        MVMap<int, string> gone = s.OpenMap<int, string>("gone");
        gone.Put(1, "a");
        _ = s.Commit();
        s.RemoveMap(gone);

        MVMap<int, string> keeper = s.OpenMap<int, string>("keeper");
        for (int i = 0; i < 5; i++)
        {
            keeper.Put(i, "v" + i);
            _ = s.Commit();
        }

        Assert.DoesNotContain("gone", s.GetMapNames());
        Assert.Contains("keeper", s.GetMapNames());
        Assert.Equal("v4", keeper.Get(4));
    }

    [Fact]
    public void RollingBackToAnOlderVersionDropsTheNewerOnes()
    {
        string file = TempFile("rollback-trim.mv");
        DeleteFile(file);
        using MVStore s = MVStore.Open(file);
        MVMap<int, string> m = s.OpenMap<int, string>("m");
        m.Put(1, "one");
        long v1 = s.Commit();
        m.Put(2, "two");
        m.Put(3, "three");
        _ = s.Commit();

        s.RollbackTo(v1);

        Assert.Equal(v1, s.GetCurrentVersion());
        Assert.Equal("one", m.Get(1));
        Assert.Null(m.Get(2));
    }

    [Fact]
    public void TheLeafSurfaceAnswers()
    {
        using MVStore s = new MVStore.Builder().Open();
        MVMap<int, string> m = s.OpenMap<int, string>("m");
        for (int i = 0; i < 200; i++)
        {
            m.Put(i, "value-" + i);
        }

        Page<int, string> page = m.GetRootPage();
        Assert.False(page.IsLeaf());
        page.SetComplete();
        while (!page.IsLeaf())
        {
            page = page.GetChildPage(0);
        }

        Assert.Equal(0, page.GetRawChildPageCount());
        Assert.True(page.IsComplete());
        page.SetComplete();
        _ = page.GetDiskSpaceUsed(false);
    }

    [Fact]
    public void AFourLevelTreeIsMeasuredFromTheTop()
    {
        string file = TempFile("four-levels.mv");
        DeleteFile(file);
        using MVStore s = new MVStore.Builder()
            .FileName(file)
            .PageSplitSize(2048)
            .Open();
        MVMap<int, string> m = s.OpenMap<int, string>("m");
        // Random insert order keeps the tree balanced, so the leftmost path itself carries
        // nodes with several children.
        int[] keys = Enumerable.Range(0, 60000).ToArray();
        var random = new Random(42);
        for (int i = keys.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (keys[i], keys[j]) = (keys[j], keys[i]);
        }

        foreach (int key in keys)
        {
            m.Put(key, "value-" + key);
        }

        _ = s.Commit();
        Page<int, string> root = m.GetRootPage();
        int depth = 1;
        Page<int, string> level = root;
        while (!level.IsLeaf())
        {
            level = level.GetChildPage(0);
            depth++;
        }

        Assert.True(depth >= 4, "depth: " + depth);
        Page<int, string> leftmost2 = root.GetChildPage(0).GetChildPage(0);
        Assert.True(leftmost2.GetRawChildPageCount() > 4,
            "depth: " + depth + " root:" + root.GetRawChildPageCount()
            + " left2:" + leftmost2.GetRawChildPageCount());
        Assert.True(root.GetDiskSpaceUsed(true) > 0);
    }

    [Fact]
    public void ConcurrentOpeningOfOneNewMapConvergesOnASingleInstance()
    {
        using MVStore s = MVStore.Open(TempFile("race-open.mv"));
        const int threads = 8;
        var maps = new MVMap<int, string>[threads];
        using (var barrier = new System.Threading.Barrier(threads))
        {
            Parallel.For(0, threads, i =>
            {
                barrier.SignalAndWait();
                maps[i] = s.OpenMap<int, string>("raced");
            });
        }

        Assert.All(maps, map => Assert.Same(maps[0], map));
        Assert.Single(maps.Distinct());
        maps[0].Put(1, "one");
        Assert.Equal("one", maps[7].Get(1));
    }
}
