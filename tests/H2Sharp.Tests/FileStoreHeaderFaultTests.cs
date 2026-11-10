/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 124: the store-header validation a corrupt file walks into — the block size and the
 * write/read formats — crafted as a checksummed header map exactly as the store writes it,
 * plus the counters reset of Clear().
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.Tests;

public sealed class FileStoreHeaderFaultTests : IDisposable
{
    private readonly string directory;

    public FileStoreHeaderFaultTests()
    {
        directory = Path.Combine(Path.GetTempPath(), "h2sharp-mvheader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Somebody still holds a file; the next run makes another directory.
        }
    }

    private string Disk(string name) => Path.Combine(directory, name).Replace('\\', '/');

    private static Dictionary<string, object?> Header(int blockSize, int format, int formatRead) => new()
    {
        ["H"] = "2",
        ["blockSize"] = blockSize,
        ["format"] = format,
        ["formatRead"] = formatRead,
    };

    private static void WriteHeader(string path, Dictionary<string, object?> map)
    {
        StringBuilder text = new();
        DataUtils.AppendMap(text, map);
        byte[] body = Encoding.Latin1.GetBytes(text.ToString());
        DataUtils.AppendMap(text, "fletcher", DataUtils.GetFletcher32(body, 0, body.Length));
        _ = text.Append('\n');
        byte[] header = Encoding.Latin1.GetBytes(text.ToString());
        byte[] file = new byte[2 * 4096];
        Array.Copy(header, 0, file, 0, header.Length);
        Array.Copy(header, 0, file, 4096, header.Length);
        File.WriteAllBytes(path, file);
    }

    private void ExpectOpenFailure(string name, Dictionary<string, object?> map, string messagePart)
    {
        string path = Disk(name);
        WriteHeader(path, map);
        MvStoreException e = Assert.Throws<MvStoreException>(
            () => new MVStore.Builder().FileName(path).Open());
        Assert.Contains(messagePart, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StoreHeaderRefusesUnsupportedFormats()
    {
        ExpectOpenFailure("block.db", Header(1024, 3, 3), "Block size");
        ExpectOpenFailure("new.db", Header(4096, 4, 3), "write format");
        ExpectOpenFailure("old.db", Header(4096, 2, 3), "write format");
        ExpectOpenFailure("read.db", Header(4096, 3, 4), "read format");
    }

    [Fact]
    public void ClearResetsTheStoreCounters()
    {
        SingleFileStore store = new(new Dictionary<string, object?>());
        store.Open(Disk("clear.db"), false);
        store.Clear();
        store.Close();
    }
}
