/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 107: the store core that only a large persistent map reaches — the append
 * buffer flush that splits pages and nodes, the page disk-space estimate and exact
 * count, page read/write on reopen, the filestore operation hook, Clear, and the
 * closed state.
 */
using System;
using System.IO;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.Tests;

public sealed class MvStoreCoreEdgeTests
{
    [Fact]
    public void ALargePersistentMapFlushesReadsBackAndCountsItsSpace()
    {
        string file = Path.Combine(
            Path.GetTempPath(), "mvs_core_" + Guid.NewGuid().ToString("N") + ".mv");
        try
        {
            MVStore store = new MVStore.Builder()
                .FileName(file)
                .PageSplitSize(64)
                .Open();
            MVMap<int, string> map = store.OpenMap<int, string>("m");
            for (int i = 0; i < 2_000; i++)
            {
                map.Put(i, "v" + i);
            }

            RootReference<int, string> root = map.FlushAndGetRoot();
            Assert.True(root.root.GetKeyCount() > 0);
            Assert.Equal(0, map.FirstKey());
            Assert.Equal(1_999, map.LastKey());
            _ = map.ToString();

            store.ExecuteFilestoreOperation(() => map.Put(5_000, "after"));
            Assert.Equal("after", map.Get(5_000));
            _ = store.Commit();
            RootReference<int, string> persisted = map.GetRoot();
            Assert.True(persisted.root.GetDiskSpaceUsed(approximate: false) >= 0);
            Assert.True(persisted.root.GetDiskSpaceUsed(approximate: true) >= 0);

            MVMap<int, int> cleared = store.OpenMap<int, int>("c");
            cleared.Put(1, 1);
            cleared.Clear();
            Assert.True(cleared.IsEmpty());

            Assert.False(store.IsClosed());
            store.Close();
            Assert.True(store.IsClosed());

            using (MVStore reopened = new MVStore.Builder().FileName(file).ReadOnly().Open())
            {
                MVMap<int, string> again = reopened.OpenMap<int, string>("m");
                Assert.Equal("v10", again.Get(10));
                Assert.Equal("after", again.Get(5_000));
            }
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // The read-only handle may outlive the closed store for a moment.
            }
        }
    }

    [Fact]
    public void AnInMemoryMapFlushesWithoutFiles()
    {
        using MVStore store = new MVStore.Builder().Open();
        MVMap<int, int> map = store.OpenMap<int, int>("m");
        for (int i = 0; i < 1_000; i++)
        {
            map.Put(i, i * 2);
        }

        RootReference<int, int> root = map.FlushAndGetRoot();
        Assert.True(root.root.GetKeyCount() > 0);
        map.Put(1_001, 3);
        _ = store.Commit();
        Assert.Equal(3, map.Get(1_001));
        Assert.False(store.IsClosed());
    }
}
