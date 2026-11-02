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
}
