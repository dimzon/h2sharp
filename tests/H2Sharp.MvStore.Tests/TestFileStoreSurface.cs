/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The file store's own surface: versions, clearing, syncing, cache counters, reuse
 * switches, and the background writer.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class TestFileStoreSurface : StoreTestBase
{
    private static MVStore OpenStore(string fileName)
        => new MVStore.Builder().FileName(fileName).PageSplitSize(1000).Open();

    [Fact]
    public void KnownVersionsFollowTheCommits()
    {
        string fileName = TempFile("known_versions.h3");
        DeleteFile(fileName);
        using (MVStore s = OpenStore(fileName))
        {
            IFileStore fileStore = s.GetFileStore()!;
            MVMap<int, string> m = s.OpenMap<int, string>("data");
            m.Put(1, "a");
            long v1 = s.Commit();
            // A version is known once a chunk carrying it reached the file; sync flushes
            // the buffer so the test does not depend on the background writer.
            s.GetFileStore()!.Sync();
            Assert.True(fileStore.IsKnownVersion(v1));
            m.Put(2, "b");
            long v2 = s.Commit();
            s.GetFileStore()!.Sync();
            Assert.True(fileStore.IsKnownVersion(v1));
            Assert.True(fileStore.IsKnownVersion(v2));

            // Before the oldest chunk there is no version to answer for. A version past
            // the newest one reads as known: the newest chunk answers for it, which is
            // what the reference's lookup means by "known".
            Assert.False(fileStore.IsKnownVersion(0));
            Assert.True(fileStore.IsKnownVersion(v2 + 1000));
        }
    }

    [Fact]
    public void SpaceReuseSwitchAnswersBothWays()
    {
        string fileName = TempFile("reuse_switch.h3");
        DeleteFile(fileName);
        using MVStore s = OpenStore(fileName);
        bool before = s.IsSpaceReused();
        s.SetReuseSpace(!before);
        Assert.Equal(!before, s.IsSpaceReused());
        s.SetReuseSpace(before);
        Assert.Equal(before, s.IsSpaceReused());
    }

    [Fact]
    public void SyncAndCacheCountersAnswer()
    {
        string fileName = TempFile("sync_cache.h3");
        DeleteFile(fileName);
        using MVStore s = OpenStore(fileName);
        MVMap<int, string> m = s.OpenMap<int, string>("data");
        for (int i = 0; i < 100; i++)
        {
            m.Put(i, "v" + i);
        }

        RandomAccessStore fileStore = (RandomAccessStore)s.GetFileStore()!;
        long created = fileStore.GetCreationTime();
        Assert.True(created > 0);
        fileStore.Sync();
        fileStore.SetCacheSize(4);
        Assert.True(fileStore.GetCacheSize() >= 1);
        Assert.InRange(fileStore.GetCacheHitRatio(), 0, 100);
        Assert.InRange(fileStore.GetTocCacheHitRatio(), 0, 100);
        Assert.InRange(fileStore.GetChunksFillRate(), 0, 100);
    }

    [Fact]
    public void BackgroundWriterStoresWithoutACommit()
    {
        string fileName = TempFile("background_writer.h3");
        DeleteFile(fileName);
        using MVStore s = OpenStore(fileName);
        s.SetAutoCommitDelay(10);
        MVMap<int, string> m = s.OpenMap<int, string>("data");
        for (int i = 0; i < 1000; i++)
        {
            m.Put(i, "background value " + i);
        }

        // The writer thread stores on its own; give it a bounded wait and require that it
        // did, because the whole point of the delay is that nobody had to ask.
        long deadline = Environment.TickCount64 + 5000;
        while (s.GetFileStore()!.GetWriteCount() == 0 && Environment.TickCount64 < deadline)
        {
            Thread.Sleep(10);
        }

        Assert.True(s.GetFileStore()!.GetWriteCount() > 0, "the background writer wrote");
        s.Commit();
        Assert.True(s.GetFileStore()!.Size() > 0);
    }

    [Fact]
    public void StopEndsBackgroundWorkAndTheStoreStaysUsable()
    {
        string fileName = TempFile("stop_background.h3");
        DeleteFile(fileName);
        using MVStore s = OpenStore(fileName);
        s.SetAutoCommitDelay(10);
        MVMap<int, string> m = s.OpenMap<int, string>("data");
        for (int i = 0; i < 100; i++)
        {
            m.Put(i, "v" + i);
        }

        s.GetFileStore()!.Stop(0);
        // Writing and committing after the stop still reaches the disk through the
        // caller's thread.
        m.Put(1000, "after stop");
        s.Commit();
        Assert.True(s.GetFileStore()!.Size() > 0);
    }
}
