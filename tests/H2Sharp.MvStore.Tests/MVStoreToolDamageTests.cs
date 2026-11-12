/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * MVStoreTool over stores that were made to exercise its rarely walked paths: compressed
 * pages for the dump, corrupted chunks for the error lines, older versions for the rollback,
 * a broken file for the repair, and a leftover temp file for the clean-up. The answers are
 * the reference's: a dump never throws on a corrupt file, a repair lands the newest version
 * that opens, and a rollback writes the *.temp file the repair expects.
 */
using System;
using System.IO;
using H2Sharp.Engine;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class MVStoreToolDamageTests : IDisposable
{
    private readonly string fileName;

    public MVStoreToolDamageTests()
    {
        fileName = Path.Combine(Path.GetTempPath(), "mvstore_tool_dmg_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        foreach (string f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(fileName) + "*"))
        {
            try { File.Delete(f); } catch { }
        }
    }

    private void BuildStore(bool compress, int rounds)
    {
        MVStore.Builder builder = new MVStore.Builder().FileName(fileName);
        if (compress)
        {
            builder = builder.CompressHigh();
        }

        using MVStore store = builder.Open();
        for (int round = 0; round < rounds; round++)
        {
            MVMap<int, string> map = store.OpenMap<int, string>("data" + round);
            for (int i = 0; i < 500; i++)
            {
                map.Put(round * 1000 + i, new string('x', i % 200));
            }

            _ = store.Commit();
        }
    }

    [Fact]
    public void DumpWalksCompressedPages()
    {
        BuildStore(compress: true, rounds: 2);
        StringWriter writer = new();
        MVStoreTool.Dump(fileName, writer, true);
        string dump = writer.ToString();
        Assert.Contains("chunk", dump);
        Assert.DoesNotContain("ERROR", dump);
    }

    [Fact]
    public void DumpSurvivesCorruptedChunks()
    {
        BuildStore(compress: false, rounds: 2);
        byte[] bytes = File.ReadAllBytes(fileName);
        // Scratch the middle of the file: the chunk walk must skip the damage and carry on.
        for (int i = bytes.Length / 3; i < bytes.Length / 2; i++)
        {
            bytes[i] = 0x5A;
        }

        File.WriteAllBytes(fileName, bytes);
        StringWriter writer = new();
        MVStoreTool.Dump(fileName, writer, true);
        Assert.Contains("chunk", writer.ToString());
    }

    [Fact]
    public void InfoReportsNonStoreFile()
    {
        // A file that has neither header chunks nor a footer: recovery mode cannot open it,
        // and the reference's info answers the error message as a string.
        File.WriteAllBytes(fileName, new byte[16_384]);
        string? error = MVStoreTool.Info(fileName);
        Assert.NotNull(error);
    }

    [Fact]
    public void RollbackWritesTempFileOfOlderVersion()
    {
        BuildStore(compress: false, rounds: 3);
        StringWriter writer = new();
        long version = MVStoreTool.Rollback(fileName, 1, writer);
        Assert.True(version >= 0, writer.ToString());
        Assert.True(File.Exists(fileName + ".temp"));
    }

    [Fact]
    public void RepairOfHealthyStoreKeepsItOpenable()
    {
        BuildStore(compress: false, rounds: 2);
        // The reference's repair walks versions down and lands the newest one whose .temp
        // opens; for a healthy store that is the only version it has.
        MVStoreTool.Repair(fileName);
        Assert.True(File.Exists(fileName + ".back"));
        Assert.Null(MVStoreTool.Info(fileName));
    }

    [Fact]
    public void CompactKeepsTheStoreReadableAndCleanUpRemovesTemp()
    {
        BuildStore(compress: false, rounds: 2);
        MVStoreTool.Compact(fileName, true);
        Assert.Null(MVStoreTool.Info(fileName));

        // A leftover temp file without a store is deleted; with a store it is promoted.
        File.WriteAllText(fileName + Constants.SuffixMvStoreTempFile, "junk");
        MVStoreTool.CompactCleanUp(fileName);
        Assert.False(File.Exists(fileName + Constants.SuffixMvStoreTempFile));

        File.WriteAllText(fileName + Constants.SuffixMvStoreNewFile, "junk");
        MVStoreTool.CompactCleanUp(fileName);
        Assert.False(File.Exists(fileName + Constants.SuffixMvStoreNewFile));
    }
}
