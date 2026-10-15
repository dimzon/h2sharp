/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Pins the span page read: the chunk's in-memory image is deserialized as a view.
 *
 * The first fact is the load-bearing one: the buffer handed back for a page of a chunk
 * that was just serialized is the chunk's own array, not a copy of a slice of it. A
 * reader that retained the span, or a path that copied again, fails it.
 *
 * The second fact is the one the issue asks for: readers racing a writer that commits,
 * which is what moves chunks under reads and drives the retry loop. A torn or retained
 * view answers a wrong value or throws here.
 *
 * The third fact covers the occupancy codec next to it: the span form of ValueOf answers
 * what the array form does, on every length including the ragged tail.
 */
using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.Tests;

public sealed class PageReadSpanTests
{
    [Fact]
    public void BufferedImageIsReadAsAView()
    {
        byte[] image = new byte[4096];
        for (int i = 0; i < image.Length; i++)
        {
            image[i] = (byte)(i * 31);
        }

        TestChunk chunk = new(7);
        chunk.SetBlock(2);
        chunk.Len = 10;
        chunk.Buffer = image;

        const int offset = 100;
        const int length = 512;
        long pos = DataUtils.ComposePagePos(7, offset, length, DataUtils.PageTypeLeaf);
        using RentedBuffer rented = chunk.ReadBufferForPage(null!, offset, pos);
        Memory<byte> memory = rented.Memory;

        Assert.True(MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment));
        Assert.Same(image, segment.Array);
        Assert.Equal(offset, segment.Offset);
        Assert.Equal(DataUtils.GetPageMaxLength(pos), segment.Count);
    }

    [Fact]
    public async Task ConcurrentReadersAndWriterAgree()
    {
        string file = Path.Combine(
            Path.GetTempPath(), "h2sharp-page-span-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using MVStore store = new MVStore.Builder().FileName(file).Open();
            MVMap<int, string> map = store.OpenMap<int, string>("data");

            const int count = 1000;
            Task writer = Task.Run(() =>
            {
                for (int i = 0; i < count; i++)
                {
                    map.Put(i, "value-" + i);
                    if (i % 50 == 0)
                    {
                        store.Commit();
                    }
                }

                store.Commit();
            });

            Task[] readers =
            [
                Task.Run(() => ReadWhileWriting(map, count)),
                Task.Run(() => ReadWhileWriting(map, count)),
                Task.Run(() => ReadWhileWriting(map, count)),
                Task.Run(() => ReadWhileWriting(map, count)),
            ];

            await Task.WhenAll([writer, .. readers]);

            for (int i = 0; i < count; i++)
            {
                Assert.Equal("value-" + i, map.Get(i));
            }
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void ReadWhileWriting(MVMap<int, string> map, int count)
    {
        for (int i = 0; i < count; i++)
        {
            string? value = map.Get(i);
            Assert.True(
                value is null || value == "value-" + i,
                $"key {i} answered '{value}'");
        }
    }

    [Fact]
    public void LongBitSetValueOfSpanMatchesArray()
    {
        byte[][] corpus =
        [
            [],
            [0],
            [0xFF],
            [1, 2, 3, 4, 5, 6, 7],
            [1, 2, 3, 4, 5, 6, 7, 8],
            [1, 2, 3, 4, 5, 6, 7, 8, 9],
            [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
            [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1],
        ];
        foreach (byte[] bytes in corpus)
        {
            LongBitSet fromArray = LongBitSet.ValueOf(bytes);
            LongBitSet fromSpan = LongBitSet.ValueOf(bytes.AsSpan());
            Assert.Equal(fromArray.Cardinality(), fromSpan.Cardinality());
            Assert.Equal(fromArray.ToByteArray(), fromSpan.ToByteArray());
        }
    }

    private sealed class TestChunk(int id) : Chunk(id)
    {
    }
}
