/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of the MVStore public API: the file-store accessors, commit and
 * rollback, the close variants, static compaction and panic handling.
 */
using System;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class MVStoreApiCoverageTests : StoreTestBase
{
    [Fact]
    public void FileStoreAccessorsAnswer()
    {
        string file = TempFile("api.mv");
        DeleteFile(file);
        using (MVStore s = MVStore.Open(file))
        {
            MVMap<int, string> m = s.OpenMap<int, string>("m");
            m.Put(1, "a");
            Assert.True(s.HasMap("m"));
            Assert.Contains("m", s.GetMapNames());
            Assert.Equal("m", s.GetMapName(m.GetId()));
            Assert.True(s.IsPersistent());
            _ = s.GetCurrentVersion();
            _ = s.GetStoreVersion();
            s.SetStoreVersion(1);
            _ = s.GetKeysPerPage();
            _ = s.GetMaxPageSize();
            _ = s.GetCacheSize();
            s.SetCacheSize(1024);
            _ = s.GetCacheSizeUsed();
            _ = s.GetRetentionTime();
            s.SetRetentionTime(1000);
            s.SetVersionsToKeep(1);
            _ = s.GetVersionsToKeep();
            _ = s.IsVersioningRequired();
            Assert.NotNull(s.GetFileStore());
            Assert.NotNull(s.GetStoreHeader());
            Assert.NotNull(s.GetLayoutMap());
            s.Sync();
            long version = s.Commit();
            Assert.True(version > 0);
            Assert.True(s.HasData("m"));
            Assert.False(s.HasUnsavedChanges());
            int infoCount = 0;
            s.PopulateInfo((_, _) => infoCount++);
            Assert.True(infoCount > 0);
            Assert.Same(m, s.GetMap<int, string>(m.GetId()));
            s.Close(100);
        }
    }

    [Fact]
    public void RollbackAndTryCommitAnswer()
    {
        using MVStore s = new MVStore.Builder().Open();
        MVMap<int, string> m = s.OpenMap<int, string>("m");
        m.Put(1, "a");
        long v = s.Commit();
        m.Put(2, "b");
        long v2 = s.TryCommit();
        Assert.True(v2 >= v);
        m.Put(3, "c");
        s.Rollback();
        Assert.False(m.ContainsKey(3));
    }

    [Fact]
    public void StaticCompactRuns()
    {
        string file = TempFile("api3.mv");
        string target = TempFile("api3c.mv");
        DeleteFile(file);
        DeleteFile(target);
        using (MVStore s = MVStore.Open(file))
        {
            MVMap<int, string> m = s.OpenMap<int, string>("m");
            for (int i = 1; i <= 100; i++)
            {
                m.Put(i, "v" + i);
            }

            s.Commit();
        }

        MVStore.Compact(file, false, null);
        MVStore.Compact(file, target, false, null);
        Assert.True(System.IO.File.Exists(target));
    }

    [Fact]
    public void PanicMarksTheStore()
    {
        using MVStore s = new MVStore.Builder().Open();
        MvStoreException e = new(50, "panic");
        Assert.Throws<MvStoreException>(() => s.Panic(e));
        Assert.Same(e, s.GetPanicException());
        Assert.True(s.IsClosed());
        s.CloseImmediately();
    }
}