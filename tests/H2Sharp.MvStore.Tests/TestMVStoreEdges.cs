/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The MVStore surface the ported store tests do not walk: the file-to-file compaction the
 * tool uses, the info map, the cache knob, the closed check, and the builder's string form.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class TestMVStoreEdges : StoreTestBase
{
    [Fact]
    public void CompactCopiesOneFileIntoAnother()
    {
        string sourceName = TempFile("compact_source.h3");
        string targetName = TempFile("compact_target.h3");
        DeleteFile(sourceName);
        DeleteFile(targetName);
        using (MVStore s = new MVStore.Builder().FileName(sourceName).AutoCommitDisabled().Open())
        {
            s.SetRetentionTime(0);
            s.SetVersionsToKeep(0);
            MVMap<int, string> m = s.OpenMap<int, string>("data");
            for (int i = 0; i < 1000; i++)
            {
                m.Put(i, "value " + i);
            }

            s.Commit();
        }

        MVStore.Compact(sourceName, targetName, compress: false, fileStore: null);

        using (MVStore s = new MVStore.Builder().FileName(targetName).Open())
        {
            MVMap<int, string> m = s.OpenMap<int, string>("data");
            Assert.Equal(1000L, m.SizeAsLong());
            for (int i = 0; i < 1000; i += 97)
            {
                Assert.Equal("value " + i, m.Get(i));
            }
        }
    }

    [Fact]
    public void CompactReplacesOneFileInPlace()
    {
        string fileName = TempFile("compact_self.h3");
        DeleteFile(fileName);
        using (MVStore s = new MVStore.Builder().FileName(fileName).AutoCommitDisabled().Open())
        {
            s.SetRetentionTime(0);
            s.SetVersionsToKeep(0);
            MVMap<int, string> m = s.OpenMap<int, string>("data");
            for (int i = 0; i < 500; i++)
            {
                m.Put(i, "v" + i);
            }

            s.Commit();
        }

        MVStore.Compact(fileName, compress: false, fileStore: null);

        using (MVStore s = new MVStore.Builder().FileName(fileName).Open())
        {
            MVMap<int, string> m = s.OpenMap<int, string>("data");
            Assert.Equal(500L, m.SizeAsLong());
            Assert.Equal("v0", m.Get(0));
            Assert.Equal("v499", m.Get(499));
        }
    }

    [Fact]
    public void PopulateInfoDescribesTheStore()
    {
        string fileName = TempFile("populate_info.h3");
        DeleteFile(fileName);
        using MVStore s = new MVStore.Builder().
            FileName(fileName).
            AutoCommitDisabled().
            Open();
        s.SetVersionsToKeep(3);
        MVMap<int, string> m = s.OpenMap<int, string>("data");
        for (int i = 0; i < 100; i++)
        {
            m.Put(i, "x");
        }

        s.Commit();
        Dictionary<string, string> info = [];
        s.PopulateInfo((key, value) => info[key] = value);

        Assert.True(info.ContainsKey("info.UPDATE_FAILURE_PERCENT"), "update failure percent");
        Assert.EndsWith("%", info["info.UPDATE_FAILURE_PERCENT"], StringComparison.Ordinal);
        Assert.True(double.TryParse(
            info["info.UPDATE_FAILURE_PERCENT"].TrimEnd('%'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out _));
        Assert.True(info.ContainsKey("info.LEAF_RATIO"), "leaf ratio");
        Assert.True(int.TryParse(info["info.LEAF_RATIO"], out int leafRatio));
        Assert.InRange(leafRatio, 0, 100);
        Assert.Equal("3", info["info.VERSIONS_TO_KEEP"]);
        Assert.True(info.ContainsKey("info.CURRENT_VERSION"), "current version");
        // The file store adds its own rows on top.
        Assert.True(info.ContainsKey("info.FILE_SIZE"), "file size");

        // The same collection over an in-memory store has no file rows and no versions.
        using MVStore mem = new MVStore.Builder().Open();
        _ = mem.OpenMap<int, string>("m").Put(1, "a");
        Dictionary<string, string> memInfo = [];
        mem.PopulateInfo((key, value) => memInfo[key] = value);
        Assert.True(memInfo.ContainsKey("info.UPDATE_FAILURE_PERCENT"));
        Assert.False(memInfo.ContainsKey("info.FILE_SIZE"));
    }

    [Fact]
    public void CacheSizeIsSetOnFileStoresAndIgnoredInMemory()
    {
        string fileName = TempFile("cache_size.h3");
        DeleteFile(fileName);
        using (MVStore s = new MVStore.Builder().FileName(fileName).Open())
        {
            int before = s.GetCacheSize();
            s.SetCacheSize(64 * 1024);
            Assert.True(s.GetCacheSize() >= before);
            Assert.True(s.GetCacheSize() <= 64);
            Assert.InRange(s.GetCacheSizeUsed(), 0, s.GetCacheSize() + 1);
        }

        using MVStore mem = new MVStore.Builder().Open();
        Assert.Equal(0, mem.GetCacheSize());
        Assert.Equal(0, mem.GetCacheSizeUsed());
        mem.SetCacheSize(1024);
        Assert.Equal(0, mem.GetCacheSize());
    }

    [Fact]
    public void IsClosedAnswersOnBothSidesOfClose()
    {
        using MVStore s = new MVStore.Builder().Open();
        Assert.False(s.IsClosed());
        s.Close();
        Assert.True(s.IsClosed());
    }

    [Fact]
    public void BuilderStringFormRoundTrips()
    {
        string fileName = TempFile("builder_string.h3");
        DeleteFile(fileName);
        string text = new MVStore.Builder().
            FileName(fileName).
            AutoCommitDisabled().
            CacheSize(16).
            ToString();
        MVStore.Builder roundTripped = MVStore.Builder.FromString(text);
        Assert.Equal(text, roundTripped.ToString());

        using (MVStore s = roundTripped.Open())
        {
            MVMap<int, string> m = s.OpenMap<int, string>("data");
            m.Put(1, "a");
            s.Commit();
        }

        using (MVStore s = new MVStore.Builder().FileName(fileName).Open())
        {
            Assert.Equal("a", s.OpenMap<int, string>("data").Get(1));
        }
    }
}
