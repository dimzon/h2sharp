/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.test.store.TestMVStoreTool.
 *
 * The r-tree section uses DefaultSpatial instead of SpatialKey: SpatialKey extends the
 * engine's Value, which is not ported, while DefaultSpatial is the plain implementation
 * the data type itself creates. Ordering and iteration are what the test exercises.
 */

using System;
using System.Collections.Generic;
using System.IO;
using H2Sharp.MvStore.RTree;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class TestMVStoreTool : StoreTestBase
{
    private static readonly string BigStringWithC = new('c', 3000);

    private static readonly string BigStringWithH = new('H', 3000);

    [Fact]
    public void TestCompact()
    {
        string fileName = TempFile("testCompact.h3");
        string fileNameNew = fileName + ".new";
        string fileNameCompressed = fileNameNew + ".compress";

        DeleteFile(fileName);
        // store with a very small page size, to make sure
        // there are many leaf pages
        MVStore s = new MVStore.Builder().
                PageSplitSize(1000).
                FileName(fileName).AutoCommitDisabled().Open();
        s.SetRetentionTime(0);
        MVMap<int, string> map = s.OpenMap<int, string>("data");
        const int size = 20_000;
        for (int i = 0; i < size; i++)
        {
            map.Put(i, "Hello World " + (i * 10));
            if (i % 10000 == 0)
            {
                s.Commit();
            }
        }

        for (int i = 0; i < size; i += 2)
        {
            map.Remove(i);
            if (i % 10000 == 0)
            {
                s.Commit();
            }
        }

        for (int i = 0; i < 20; i++)
        {
            map = s.OpenMap<int, string>("data" + i);
            for (int j = 0; j < i * i; j++)
            {
                map.Put(j, "Hello World " + (j * 10));
            }

            s.Commit();
        }

        MVRTreeMap<string> rTreeMap = s.OpenMap("rtree", new MVRTreeMap<string>.Builder<string>());
        var r = new Random(1);
        for (int i = 0; i < 10; i++)
        {
            float x = (float)r.NextDouble();
            float y = (float)r.NextDouble();
            float width = (float)r.NextDouble() / 10;
            float height = (float)r.NextDouble() / 10;
            Spatial k = new DefaultSpatial(i, x, x + width, y, y + height);
            rTreeMap.Put(k, "Hello World " + (i * 10));
            if (i % 3 == 0)
            {
                s.Commit();
            }
        }

        s.Close();

        MVStore.Compact(fileName, fileNameNew, false, null);
        MVStore.Compact(fileName, fileNameCompressed, true, null);

        long size1 = H2Sharp.Store.FileSystem.FilePath.Get(fileName).Size();
        long size2 = H2Sharp.Store.FileSystem.FilePath.Get(fileNameNew).Size();
        long size3 = H2Sharp.Store.FileSystem.FilePath.Get(fileNameCompressed).Size();
        AssertTrue("size1: " + size1 + " size2: " + size2 + " size3: " + size3,
            size2 < size1 && size3 < size2);

        MVStoreTool.Compact(fileNameNew, false);
        Assert.True(100L * Math.Abs(size2 - H2Sharp.Store.FileSystem.FilePath.Get(fileNameNew).Size())
            / size2 < 1);

        MVStoreTool.Compact(fileNameCompressed, true);
        Assert.True(100L * Math.Abs(size3 - H2Sharp.Store.FileSystem.FilePath.Get(fileNameCompressed).Size())
            / size3 < 1);

        using (MVStore s1 = new MVStore.Builder().
                FileName(fileName).ReadOnly().Open())
        using (MVStore s2 = new MVStore.Builder().
                FileName(fileNameNew).ReadOnly().Open())
        using (MVStore s3 = new MVStore.Builder().
                FileName(fileNameCompressed).ReadOnly().Open())
        {
            AssertEquals(s1, s2);
            AssertEquals(s1, s3);
        }
    }

    private static void AssertEquals(MVStore a, MVStore b)
    {
        Assert.Equal(a.GetMapNames().Count, b.GetMapNames().Count);
        foreach (string mapName in a.GetMapNames())
        {
            // All maps of this test are known: the r-tree holds spatial keys, every other
            // map holds ints to strings. A typeless open is not available, so the test
            // opens what it created.
            if (mapName.StartsWith("rtree", StringComparison.Ordinal))
            {
                MVRTreeMap<string> ma = a.OpenMap("rtree", new MVRTreeMap<string>.Builder<string>());
                MVRTreeMap<string> mb = b.OpenMap("rtree", new MVRTreeMap<string>.Builder<string>());
                Assert.Equal(ma.SizeAsLong(), mb.SizeAsLong());
                foreach (KeyValuePair<object, string> e in ma.EntrySet())
                {
                    object? x = mb.Get(e.Key);
                    Assert.Equal(e.Value, x!.ToString());
                }
            }
            else
            {
                MVMap<int, string> ma = a.OpenMap<int, string>(mapName);
                MVMap<int, string> mb = b.OpenMap<int, string>(mapName);
                Assert.Equal(ma.SizeAsLong(), mb.SizeAsLong());
                foreach (KeyValuePair<int, string> e in ma.EntrySet())
                {
                    object? x = mb.Get(e.Key);
                    Assert.Equal(e.Value, x!.ToString());
                }
            }
        }
    }

    [Fact]
    public void TestDump()
    {
        string fileName = TempFile("testDump.h3");
        DeleteFile(fileName);
        // store with a very small page size, to make sure
        // there are many leaf pages
        MVStore s = new MVStore.Builder().
                PageSplitSize(1000).
                FileName(fileName).AutoCommitDisabled().Open();
        s.SetRetentionTime(0);
        MVMap<int, string> map = s.OpenMap<int, string>("data");

        // Insert some data. Using big strings with "H" and "c" to validate the fix of #3931
        const int nbEntries = 20_000;
        for (int i = 0; i < nbEntries; i++)
        {
            map.Put(i, i % 2 == 0 ? BigStringWithC : BigStringWithH);
        }

        s.Commit();
        // Let's rewrite the data to trigger some chunk compaction & drop
        for (int i = 0; i < nbEntries; i++)
        {
            map.Put(i, i % 2 == 0 ? BigStringWithH : BigStringWithC);
        }

        s.Commit();
        s.Close();
        var dumpWriter = new StringWriter();
        MVStoreTool.Dump(fileName, dumpWriter, true);

        int nbFileHeaders = NbOfOccurrences(dumpWriter.ToString(), "fileHeader");
        Assert.Equal(2, nbFileHeaders);
    }

    [Fact]
    public void TestRollback()
    {
        string fileName = TempFile("testDump.h4");
        DeleteFile(fileName);
        // store with a very small page size, to make sure
        // there are many leaf pages
        MVStore s = new MVStore.Builder().
                PageSplitSize(1000).
                FileName(fileName).AutoCommitDisabled().Open();
        s.SetRetentionTime(0);
        MVMap<int, string> map = s.OpenMap<int, string>("data");

        // Insert some data. Using big strings with "H" and "c" to validate the fix of #3931
        const int nbEntries = 20_000;
        for (int i = 0; i < nbEntries; i++)
        {
            map.Put(i, i % 2 == 0 ? BigStringWithC : BigStringWithH);
        }

        s.Commit();
        // Let's rewrite the data to trigger some chunk compaction & drop
        for (int i = 0; i < nbEntries; i++)
        {
            map.Put(i, i % 2 == 0 ? BigStringWithH : BigStringWithC);
        }

        s.Commit();
        s.Close();
        var dumpWriter = new StringWriter();
        try
        {
            _ = MVStoreTool.Rollback(fileName, long.MaxValue, dumpWriter);
        }
        catch (NullReferenceException)
        {
            Fail("No NullPointerException expected");
        }
    }

    private static int NbOfOccurrences(string str, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = str.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }
}
