/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Human-readable tool output over a small store: dump and info.
 */
using System.IO;
using System.Text;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class TestMVStoreToolCoverage : StoreTestBase
{
    [Fact]
    public void DumpAndInfoDescribeTheStore()
    {
        string fileName = TempFile("tool-coverage.h3");
        DeleteFile(fileName);
        using (MVStore store = new MVStore.Builder().FileName(fileName).Open())
        {
            MVMap<int, string> map = store.OpenMap<int, string>("data");
            for (int i = 0; i < 100; i++)
            {
                map.Put(i, "value-" + i);
            }

            store.Commit();
        }

        StringBuilder dump = new();
        using (StringWriter dumpWriter = new(dump))
        {
            MVStoreTool.Dump(fileName, dumpWriter, details: false);
            MVStoreTool.Dump(fileName, dumpWriter, details: true);
        }

        StringBuilder info = new();
        string? error;
        using (StringWriter infoWriter = new(info))
        {
            error = MVStoreTool.Info(fileName, infoWriter);
        }

        Assert.Contains("chunkHeader", dump.ToString());
        Assert.Null(error);
        Assert.Contains("Chunk count:", info.ToString());
    }

    [Fact]
    public void DumpOfMissingFileIsAnAnswerNotAnException()
    {
        string fileName = TempFile("tool-missing.h3");
        DeleteFile(fileName);

        StringBuilder dump = new();
        using (StringWriter writer = new(dump))
        {
            MVStoreTool.Dump(fileName, writer, details: false);
        }

        Assert.Contains("File not found", dump.ToString());
    }

    [Fact]
    public void RollbackToLatestVersionKeepsTheData()
    {
        string fileName = TempFile("tool-rollback.h3");
        DeleteFile(fileName);
        using (MVStore store = new MVStore.Builder().FileName(fileName).Open())
        {
            MVMap<int, string> map = store.OpenMap<int, string>("data");
            map.Put(1, "one");
            store.Commit();
            map.Put(2, "two");
            store.Commit();
        }

        StringBuilder log = new();
        long version;
        using (StringWriter writer = new(log))
        {
            version = MVStoreTool.Rollback(fileName, long.MaxValue, writer);
        }

        Assert.True(version >= 1, "rollback should report the newest chunk version, got " + version);
        string tempName = fileName + ".temp";
        Assert.True(File.Exists(tempName));
        using (MVStore rolled = new MVStore.Builder().FileName(tempName).ReadOnly().Open())
        {
            MVMap<int, string> map = rolled.OpenMap<int, string>("data");
            Assert.Equal("one", map.Get(1));
            Assert.Equal("two", map.Get(2));
        }
    }

    [Fact]
    public void RollbackOfMissingFileIsAnAnswerNotAnException()
    {
        string fileName = TempFile("tool-rollback-missing.h3");
        DeleteFile(fileName);

        StringBuilder log = new();
        long version;
        using (StringWriter writer = new(log))
        {
            version = MVStoreTool.Rollback(fileName, long.MaxValue, writer);
        }

        Assert.Equal(-1, version);
        Assert.Contains("File not found", log.ToString());
    }

    [Fact]
    public void CompactCleanUpRemovesLeftoverTempFiles()
    {
        string fileName = TempFile("tool-cleanup.h3");
        DeleteFile(fileName);
        File.WriteAllBytes(fileName, [1, 2, 3]);
        File.WriteAllBytes(fileName + ".tempFile", [4, 5]);
        File.WriteAllBytes(fileName + ".newFile", [6, 7]);

        MVStoreTool.CompactCleanUp(fileName);

        Assert.False(File.Exists(fileName + ".tempFile"));
        Assert.False(File.Exists(fileName + ".newFile"));
        Assert.True(File.Exists(fileName));
    }

    [Fact]
    public void CompactCleanUpPromotesTheNewFileWhenTheStoreIsGone()
    {
        string fileName = TempFile("tool-cleanup-new.h3");
        DeleteFile(fileName);
        DeleteFile(fileName + ".newFile");
        File.WriteAllBytes(fileName + ".newFile", [9]);

        MVStoreTool.CompactCleanUp(fileName);

        Assert.False(File.Exists(fileName + ".newFile"));
        Assert.Equal([9], File.ReadAllBytes(fileName));
    }

    [Fact]
    public void MoveAtomicReplaceMovesTheFile()
    {
        string source = TempFile("tool-move-src.h3");
        string destination = TempFile("tool-move-dst.h3");
        DeleteFile(source);
        DeleteFile(destination);
        File.WriteAllBytes(source, [1, 2, 3]);

        MVStoreTool.MoveAtomicReplace(source, destination);

        Assert.False(File.Exists(source));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(destination));
    }
}
