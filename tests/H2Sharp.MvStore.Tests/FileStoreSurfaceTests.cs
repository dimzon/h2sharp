/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 249: the file store's public surface, which the map tests never touch — the
 * counters, the cache sizing, the header and info projections, reuse of freed space, and the
 * refusals a page reader answers for a position that is not a saved page or a chunk that is
 * not in the file.
 */
using System;
using System.Collections.Generic;
using System.IO;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class FileStoreSurfaceTests : IDisposable
{
    private readonly string file = Path.Combine(
        Path.GetTempPath(), "filestore_surface_" + Guid.NewGuid().ToString("N") + ".mv");

    public void Dispose()
    {
        foreach (string f in Directory.GetFiles(
            Path.GetTempPath(), Path.GetFileNameWithoutExtension(file) + "*"))
        {
            try { File.Delete(f); } catch { }
        }
    }

    [Fact]
    public void StoreSurfaceReportsCountersHeaderAndInfo()
    {
        using MVStore store = MVStore.Open(file);
        MVMap<int, string> map = store.OpenMap<int, string>("data");
        map.Put(1, "a");
        _ = store.Commit();

        IFileStore fileStore = store.GetFileStore()!;
        Assert.True(fileStore.IsSpaceReused());
        fileStore.SetReuseSpace(false);
        Assert.False(fileStore.IsSpaceReused());
        fileStore.SetReuseSpace(true);
        Assert.True(fileStore.IsSpaceReused());

        Assert.True(fileStore.IsKnownVersion(store.GetCurrentVersion()));
        Assert.False(fileStore.IsKnownVersion(-123456));

        Assert.True(fileStore.GetWriteCount() > 0);
        Assert.True(fileStore.GetReadCount() >= 0);
        Assert.True(fileStore.Size() > 0);
        _ = fileStore.GetFillRate();
        fileStore.SetCacheSize(1);
        Assert.True(fileStore.GetCacheSize() >= 0);
        Assert.True(fileStore.GetCacheSizeUsed() >= 0);

        fileStore.SetRetentionTime(1234);
        Assert.Equal(1234, fileStore.GetRetentionTime());
        Assert.Equal(file, fileStore.GetFileName());
        Assert.False(fileStore.IsReadOnly());

        Dictionary<string, string> info = [];
        fileStore.PopulateInfo((key, value) => info[key] = value);
        Assert.True(info.ContainsKey("info.FILE_SIZE"));
        Assert.True(info.ContainsKey("info.CACHE_SIZE"));
        Assert.True(info.ContainsKey("info.CACHE_HIT_RATIO"));

        IDictionary<string, object?> header = fileStore.GetStoreHeader();
        Assert.Equal(2, Convert.ToInt32(header["H"], System.Globalization.CultureInfo.InvariantCulture));
        fileStore.Sync();
        fileStore.SetAutoCommitDelay(0);
        fileStore.SetAutoCommitDelay(-1);
    }

    [Fact]
    public void PageReaderRefusesPositionsThatAreNotSavedPages()
    {
        using MVStore store = MVStore.Open(file);
        MVMap<int, string> map = store.OpenMap<int, string>("data");
        map.Put(1, "a");
        _ = store.Commit();

        IFileStore fileStore = store.GetFileStore()!;
        MvStoreException zero = Assert.ThrowsAny<MvStoreException>(() => fileStore.ReadPage(map, 0));
        Assert.Contains("Position 0", zero.Message, StringComparison.Ordinal);
        _ = Assert.ThrowsAny<MvStoreException>(() => fileStore.ReadPage(map, 1));
        _ = Assert.ThrowsAny<MvStoreException>(() => fileStore.CreateChunk("not a chunk"));
        // A version the file never had is not an error: the reference just leaves the layout
        // at the newest root it can still read.
        fileStore.RollbackTo(123456);
    }

    [Fact]
    public void OfflineCompactionKeepsTheData()
    {
        using (MVStore store = MVStore.Open(file))
        {
            MVMap<int, string> map = store.OpenMap<int, string>("data");
            for (int i = 0; i < 100; i++)
            {
                map.Put(i, new string('x', 100));
            }

            _ = store.Commit();
        }

        MVStore.Compact(file, false, null);
        using MVStore reopened = MVStore.Open(file);
        Assert.Equal(new string('x', 100), reopened.OpenMap<int, string>("data").Get(7));
    }
}
