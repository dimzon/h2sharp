/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 265: a store must answer a damaged file with a database exception or recover it, not
 * leak a platform exception or hang. One byte is changed at each interesting position of a
 * valid file (header fields, chunk headers, footers and page bodies) and the file is reopened.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class StoreCorruptionSurfaceTests : IDisposable
{
    private const int BlockSize = 4 * 1024;
    private readonly string file = Path.Combine(
        Path.GetTempPath(), "store_corrupt_" + Guid.NewGuid().ToString("N") + ".mv");

    public void Dispose()
    {
        foreach (string f in Directory.GetFiles(
            Path.GetTempPath(), Path.GetFileNameWithoutExtension(file) + "*"))
        {
            try { File.Delete(f); } catch { }
        }
    }

    [Fact]
    public void EverySingleByteDamageEitherOpensOrFailsWithAStoreException()
    {
        byte[] original = BuildStore();
        List<int> offsets = [];
        offsets.AddRange(Enumerable.Range(0, 256));
        for (int pos = BlockSize; pos < original.Length; pos += 512)
        {
            offsets.Add(pos);
        }

        int opened = 0;
        int refused = 0;
        foreach (int offset in offsets)
        {
            byte[] damaged = (byte[])original.Clone();
            damaged[offset] ^= 0xFF;
            File.WriteAllBytes(file, damaged);
            try
            {
                using MVStore store = MVStore.Open(file);
                _ = store.GetMapNames();
                _ = store.OpenMap<int, string>("data").Get(3);
                opened++;
            }
            catch (MvStoreException)
            {
                refused++;
            }
            catch (H2Sharp.H2SharpException)
            {
                refused++;
            }
        }

        Assert.True(opened + refused == offsets.Count);
        Assert.True(refused > 0);
    }

    [Fact]
    public void TruncatedFilesEitherOpenOrFailWithAStoreException()
    {
        byte[] original = BuildStore();
        List<int> lengths = [0, 1, 100, 4095, 4096, 4097, 8191, 8192,
            original.Length / 2, original.Length - 1];
        foreach (int length in lengths)
        {
            File.WriteAllBytes(file, original.Take(length).ToArray());
            try
            {
                using MVStore store = MVStore.Open(file);
                _ = store.GetMapNames();
            }
            catch (MvStoreException)
            {
            }
            catch (H2Sharp.H2SharpException)
            {
            }
        }
    }

    [Fact]
    public void RandomSingleByteDamageNeverLeaksAPlatformException()
    {
        byte[] original = BuildStore();
        Random random = new(1234);
        for (int i = 0; i < 250; i++)
        {
            byte[] damaged = (byte[])original.Clone();
            int offset = random.Next(damaged.Length);
            damaged[offset] ^= (byte)(1 << random.Next(8));
            File.WriteAllBytes(file, damaged);
            try
            {
                using MVStore store = MVStore.Open(file);
                _ = store.GetMapNames();
            }
            catch (MvStoreException)
            {
            }
            catch (H2Sharp.H2SharpException)
            {
            }
        }
    }

    private byte[] BuildStore()
    {
        using (MVStore store = MVStore.Open(file))
        {
            MVMap<int, string> map = store.OpenMap<int, string>("data");
            for (int i = 0; i < 40; i++)
            {
                map.Put(i, new string('x', 40 + i));
            }

            _ = store.Commit();
        }

        return File.ReadAllBytes(file);
    }
}
