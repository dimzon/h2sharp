/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Covers the rarely-traversed MVStore paths: stream id skipping, block stores,
 * immediate close, and corrupted-file open errors. Lives in the core suite because
 * only the core coverage run instruments the engine assembly.
 */
using System;
using System.Collections.Generic;
using System.IO;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.Tests;

public sealed class TestStoreCoverage
{
    [Fact]
    public void IdStreamReadsWithSkips()
    {
        var map = new Dictionary<long, byte[]>();
        StreamStore store = new(map);
        byte[] payload = new byte[300];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 251);
        }

        byte[] id = store.Put(new MemoryStream(payload));
        using (Stream s = store.Get(id))
        {
            byte[] all = new byte[payload.Length];
            int off = 0;
            while (off < all.Length)
            {
                int n = s.Read(all, off, all.Length - off);
                Assert.True(n > 0);
                off += n;
            }

            Assert.Equal(payload, all);
        }

        using (Stream s = store.Get(id))
        {
            byte[] chunk = new byte[17];
            int total = 0;
            int n;
            while ((n = s.Read(chunk, 0, chunk.Length)) > 0)
            {
                total += n;
                Assert.True(n <= chunk.Length);
            }

            Assert.Equal(payload.Length, total);
        }

        using (Stream s = store.Get(id))
        {
            byte[] big = new byte[1000];
            int n = s.Read(big, 0, big.Length);
            Assert.Equal(payload.Length, n);
        }
    }

    [Fact]
    public void StreamStoreBlocksAndLength()
    {
        var map = new Dictionary<long, byte[]>();
        StreamStore store = new(map, 16, 32);
        byte[] payload = new byte[500];
        new Random(7).NextBytes(payload);
        byte[] id = store.Put(new MemoryStream(payload));
        Assert.Equal(payload.Length, store.Length(id));
        Assert.False(store.IsInPlace(id));
        Assert.True(store.GetMaxBlockKey(id) > 0);
        Assert.NotNull(StreamStore.Describe(id));
        byte[] round = new byte[payload.Length];
        using (Stream s = store.Get(id))
        {
            int off = 0;
            while (off < round.Length)
            {
                int n = s.Read(round, off, round.Length - off);
                if (n <= 0) break;
                off += n;
            }
        }

        Assert.Equal(payload, round);
        store.Remove(id);
    }

    [Fact]
    public void ImmediateCloseAndCorruptedFile()
    {
        using (MVStore store = new MVStore.Builder().Open())
        {
            store.CloseImmediately();
        }

        string fileName = Path.Combine(Path.GetTempPath(), "CorruptedTest_" + Guid.NewGuid().ToString("N") + ".h3");
        File.WriteAllBytes(fileName, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        try
        {
            Assert.ThrowsAny<Exception>(() =>
            {
                using MVStore bad = new MVStore.Builder().FileName(fileName).Open();
            });
        }
        finally
        {
            File.Delete(fileName);
        }
    }
}
