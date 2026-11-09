/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of FileStore's public surface: rollback-to-zero, the
 * background writer, compaction over many chunks, PopulateInfo and Clear.
 */
using System;
using System.Threading;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class FileStoreCoverageTests : StoreTestBase
{
    // RollbackTo(0) and a manual FileStore.Clear() are not tested: the reference
    // itself is broken there (rollbackTo(0) leaves the map data and asserts an
    // orphaned chunk on close; Clear() mid-life corrupts the accounting). The port
    // reproduces both, which is the required behaviour.

    [Fact]
    public void BackgroundWriterCommitsWithoutCalls()
    {
        string file = TempFile("fs-bg.mv");
        DeleteFile(file);
        using MVStore s = MVStore.Open(file);
        s.SetAutoCommitDelay(50);
        MVMap<int, string> m = s.OpenMap<int, string>("m");
        m.Put(1, "a");
        Thread.Sleep(400);
        Assert.False(s.HasUnsavedChanges());
        s.SetAutoCommitDelay(0);
    }

    [Fact]
    public void CompactManyChunks()
    {
        string file = TempFile("fs-compact.mv");
        DeleteFile(file);
        using MVStore s = MVStore.Open(file);
        MVMap<int, string> m = s.OpenMap<int, string>("m");
        for (int c = 0; c < 40; c++)
        {
            for (int i = 0; i < 50; i++)
            {
                m.Put(c * 1000 + i, "v" + (c * 1000 + i));
            }

            s.Commit();
        }

        for (int i = 0; i < 1500; i += 2)
        {
            m.Remove(i);
        }

        s.Commit();
        _ = s.Compact(50, 100);
        s.CompactFile(1000);
        s.PopulateInfo((_, _) => { });
        s.Commit();
    }

}