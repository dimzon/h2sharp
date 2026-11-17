/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 251: handmade store headers and version rollback. The reference validates the
 * block size and the write format before it trusts any chunk; these cases reach the refusals
 * and the special rollback-to-zero version that a normal store never asks for.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class MvStoreFaultSurfaceTests : IDisposable
{
    private const int BlockSize = 4 * 1024;

    private readonly string file = Path.Combine(
        Path.GetTempPath(), "mv_fault_surface_" + Guid.NewGuid().ToString("N") + ".mv");

    public void Dispose()
    {
        foreach (string f in Directory.GetFiles(
            Path.GetTempPath(), Path.GetFileNameWithoutExtension(file) + "*"))
        {
            try { File.Delete(f); } catch { }
        }
    }

    [Fact]
    public void ABlockSizeOtherThanFourKilobytesIsRefused()
    {
        WriteHandmadeHeader(("blockSize", 8192));
        MvStoreException e = Assert.ThrowsAny<MvStoreException>(() => MVStore.Open(file));
        Assert.Contains("Block size 8192", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWriteFormatFromTheFutureIsRefused()
    {
        WriteHandmadeHeader(("format", 4));
        MvStoreException e = Assert.ThrowsAny<MvStoreException>(() => MVStore.Open(file));
        Assert.Contains("larger than the supported format", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadFormatFromTheFutureIsRefused()
    {
        WriteHandmadeHeader(("format", 3), ("formatRead", 4));
        MvStoreException e = Assert.ThrowsAny<MvStoreException>(() => MVStore.Open(file));
        Assert.Contains("read format 4", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RollbackToZeroRemovesTheStoredData()
    {
        using (MVStore store = MVStore.Open(file))
        {
            MVMap<int, string> map = store.OpenMap<int, string>("data");
            map.Put(1, "one");
            _ = store.Commit();
            store.GetFileStore()!.RollbackTo(0);
            _ = store.Commit();
        }

        using MVStore reopened = MVStore.Open(file);
        Assert.Empty(reopened.GetMapNames());
    }

    /// <summary>Writes a two-block header with the writer's own checksum.</summary>
    private void WriteHandmadeHeader(params (string Key, object Value)[] fields)
    {
        StringBuilder buff = new(112);
        var map = new Dictionary<string, object?> { ["H"] = 2, ["blockSize"] = BlockSize };
        foreach ((string key, object value) in fields)
        {
            map[key] = value;
        }

        DataUtils.AppendMap(buff, map);
        byte[] bytes = Encoding.Latin1.GetBytes(buff.ToString());
        int checksum = DataUtils.GetFletcher32(bytes, 0, bytes.Length);
        DataUtils.AppendMap(buff, "fletcher", checksum);
        _ = buff.Append('\n');

        byte[] text = Encoding.Latin1.GetBytes(buff.ToString());
        byte[] header = new byte[2 * BlockSize];
        Array.Copy(text, 0, header, 0, text.Length);
        Array.Copy(text, 0, header, BlockSize, text.Length);
        File.WriteAllBytes(file, header);
    }
}
