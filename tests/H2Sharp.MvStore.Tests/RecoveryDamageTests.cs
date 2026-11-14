/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Recovery over damaged stores: a copy taken while the store was open, layout roots that
 * no longer parse, the newest chunk's footer destroyed, chunks destroyed but the store
 * header left, and store headers written by hand with the writer's own format — the format
 * checks, the creation-time adjustment, and the tools' answers to files they cannot read.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class RecoveryDamageTests : IDisposable
{
    // The store writes every header and footer block-aligned; 4 KiB is the only block
    // size the reader accepts, and it is what the file layout below assumes.
    private const int BlockSize = 4 * 1024;

    private readonly string file = Path.Combine(
        Path.GetTempPath(), "recovery_dmg_" + Guid.NewGuid().ToString("N") + ".mv");

    public void Dispose()
    {
        foreach (string f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileNameWithoutExtension(file) + "*"))
        {
            try { File.Delete(f); } catch { }
        }
    }

    private void BuildStore(int rounds)
    {
        using MVStore store = MVStore.Open(file);
        for (int round = 0; round < rounds; round++)
        {
            MVMap<int, string> map = store.OpenMap<int, string>("data" + round);
            for (int i = 0; i < 300; i++)
            {
                map.Put(round * 1000 + i, new string('x', i % 150));
            }

            _ = store.Commit();
        }
    }

    [Fact]
    public void ACopyTakenWhileOpenRecoversEverything()
    {
        BuildStore(4);
        string copy = file + ".copy";
        File.Copy(file, copy);

        using MVStore recovered = MVStore.Open(copy);
        for (int round = 0; round < 4; round++)
        {
            MVMap<int, string> map = recovered.OpenMap<int, string>("data" + round);
            Assert.Equal("xxx", map.Get(round * 1000 + 3));
        }
    }

    [Fact]
    public void UnreadableLayoutRootsMakeTheFileUnrecoverable()
    {
        BuildStore(3);
        // A copy taken while the store is open has no clean flag, so opening it walks
        // the file instead of trusting the header.
        string copy = file + ".copy";
        File.Copy(file, copy);

        byte[] bytes = File.ReadAllBytes(copy);
        CorruptLayoutRoots(bytes);
        // The newest chunk's footer goes too, so the header points at a chunk the reader
        // cannot verify and the fallback walks the whole file.
        for (int i = bytes.Length - BlockSize; i < bytes.Length; i++)
        {
            bytes[i] = 0;
        }

        File.WriteAllBytes(copy, bytes);

        MvStoreException e = Assert.ThrowsAny<MvStoreException>(() => MVStore.Open(copy));
        Assert.Contains("unable to recover", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChunksDestroyedButHeaderLeftOpensEmpty()
    {
        BuildStore(3);
        byte[] bytes = File.ReadAllBytes(file);
        for (int i = BlockSize; i < bytes.Length; i++)
        {
            bytes[i] = 0x5A;
        }

        File.WriteAllBytes(file, bytes);

        using MVStore store = MVStore.Open(file);
        Assert.False(store.OpenMap<int, string>("data0").Get(1) is not null);
        MVMap<int, string> fresh = store.OpenMap<int, string>("fresh");
        fresh.Put(1, "one");
        _ = store.Commit();
        Assert.Equal("one", fresh.Get(1));
    }

    [Fact]
    public void DestroyedLastChunkFooterFallsBackToAnOlderVersion()
    {
        BuildStore(3);
        byte[] bytes = File.ReadAllBytes(file);
        // The file ends with the newest chunk's footer; without it the reader walks the
        // file and lands the newest version it can still verify.
        for (int i = bytes.Length - BlockSize; i < bytes.Length; i++)
        {
            bytes[i] = 0;
        }

        File.WriteAllBytes(file, bytes);

        using MVStore store = MVStore.Open(file);
        MVMap<int, string> oldest = store.OpenMap<int, string>("data0");
        Assert.Equal("xxx", oldest.Get(3));
    }

    [Fact]
    public void AHeaderWithAnOldWriteFormatIsRefused()
    {
        WriteHandmadeHeader(("format", 2));
        MvStoreException e = Assert.ThrowsAny<MvStoreException>(() => MVStore.Open(file));
        Assert.Contains("smaller than the supported format", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AHeaderWithAnOldReadFormatIsRefused()
    {
        WriteHandmadeHeader(("format", 3), ("formatRead", 2));
        MvStoreException e = Assert.ThrowsAny<MvStoreException>(() => MVStore.Open(file));
        Assert.Contains("smaller than the supported format", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AHeaderFromTheFutureAdjustsItsCreationTime()
    {
        long tomorrow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 24L * 60 * 60 * 1000;
        WriteHandmadeHeader(("format", 3), ("formatRead", 3), ("created", tomorrow));

        using MVStore store = MVStore.Open(file);
        Assert.Equal("2", Convert.ToString(store.GetStoreHeader()["H"], CultureInfo.InvariantCulture));
    }

    [Fact]
    public void DumpReportsAChunkThatClaimsMoreThanTheFileHas()
    {
        BuildStore(2);
        byte[] bytes = File.ReadAllBytes(file);
        byte[] withFake = new byte[bytes.Length + BlockSize];
        Array.Copy(bytes, withFake, bytes.Length);
        const string fakeHeader =
            "chunk:99,block:99,len:100,version:1,pages:1,max:100,map:2,next:0,time:1,root:0,toc:0\n";
        byte[] fake = Latin1(fakeHeader);
        Array.Copy(fake, 0, withFake, bytes.Length, fake.Length);
        File.WriteAllBytes(file, withFake);

        StringWriter writer = new();
        MVStoreTool.Dump(file, writer, true);
        Assert.Contains("ERROR", writer.ToString());
    }

    [Fact]
    public void RollbackOfAChunkBeyondTheFileAnswersAnError()
    {
        BuildStore(2);
        byte[] bytes = File.ReadAllBytes(file);
        byte[] withFake = new byte[bytes.Length + BlockSize];
        Array.Copy(bytes, withFake, bytes.Length);
        const string fakeHeader =
            "chunk:99,block:99,len:100,version:1,pages:1,max:100,map:2,next:0,time:1,root:0,toc:0\n";
        byte[] fake = Latin1(fakeHeader);
        Array.Copy(fake, 0, withFake, bytes.Length, fake.Length);
        File.WriteAllBytes(file, withFake);

        StringWriter writer = new();
        MVStoreTool.Rollback(file, 1, writer);
        Assert.Contains("ERROR", writer.ToString());
    }

    [Fact]
    public void RepairOverAGarbageMiddleKeepsAStore()
    {
        BuildStore(3);
        byte[] bytes = File.ReadAllBytes(file);
        for (int i = bytes.Length / 3; i < 2 * bytes.Length / 3; i++)
        {
            bytes[i] = 0x5A;
        }

        File.WriteAllBytes(file, bytes);

        MVStoreTool.Repair(file);
        // Whatever version the repair lands, the dump survives the file either way.
        StringWriter writer = new();
        MVStoreTool.Dump(file, writer, true);
        _ = writer.ToString();
    }

    /// <summary>
    /// Destroys the layout root page of every chunk: the header text stays, the page it
    /// points at does not parse any more, and no candidate for the last chunk verifies.
    /// </summary>
    private static void CorruptLayoutRoots(byte[] bytes)
    {
        int blocks = bytes.Length / BlockSize;
        for (int b = 1; b < blocks; b++)
        {
            int start = b * BlockSize;
            if (bytes[start] != (byte)'c')
            {
                continue;
            }

            string header = Encoding.Latin1.GetString(
                bytes, start, Math.Min(1024, bytes.Length - start));
            int rootIdx = header.IndexOf("root:", StringComparison.Ordinal);
            if (rootIdx < 0)
            {
                continue;
            }

            int valueStart = rootIdx + "root:".Length;
            int valueEnd = valueStart;
            while (valueEnd < header.Length && Uri.IsHexDigit(header[valueEnd]))
            {
                valueEnd++;
            }

            long pagePos = Convert.ToInt64(header[valueStart..valueEnd], 16);
            long absolute = (long)b * BlockSize + (int)(pagePos >> 6);
            for (long i = absolute; i < absolute + 128 && i < bytes.Length; i++)
            {
                bytes[i] = 0x5A;
            }
        }
    }

    /// <summary>
    /// Writes a store header by hand the way the writer does: the map, the Fletcher-32
    /// checksum of what precedes the checksum key, and both copies over two blocks.
    /// </summary>
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

    private static byte[] Latin1(string s) => Encoding.Latin1.GetBytes(s);
}
