/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.test.store.TestStreamStore.
 *
 * Each Java subtest is its own fact, so the in-memory facts stay green while the file-backed
 * ones fail with TODOException until the file store is ported. The split is the only structural
 * change: Java runs everything from one test() method.
 *
 * Three things do not map one for one. System.IO.Stream has no skip, so the test carries the
 * default InputStream.skip spelled out: read and discard, stopping at the end. Dictionary has no
 * virtual get or containsKey to override for the access counters, so a counting wrapper stands
 * in, counting TryGetValue and the indexer as reads and ContainsKey as contains — the same
 * operations the reference counts. And Java's Random is not System.Random, which is fine because
 * no vector is compared across platforms: the data only has to round-trip.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using H2Sharp.MvStore;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class TestStreamStore : StoreTestBase
{
    [Fact]
    public void TestMaxBlockKey()
    {
        var map = new SortedDictionary<long, byte[]>();
        var s = new StreamStore(map, 64, 128);
        map.Clear();
        for (int len = 1; len < 1024 * 1024; len *= 2)
        {
            byte[] id = s.Put(new MemoryStream(new byte[len]));
            long max = s.GetMaxBlockKey(id);
            if (max == -1)
            {
                AssertTrue(map.Count == 0);
            }
            else
            {
                Assert.Equal(map.Keys.Last(), max);
            }
        }
    }

    [Fact]
    public void TestIOException()
    {
        IDictionary<long, byte[]> map = new Dictionary<long, byte[]>();
        var s = new StreamStore(map);
        byte[] id = s.Put(new MemoryStream(new byte[1024 * 1024]));
        Stream @in = s.Get(id);
        map.Clear();
        try
        {
            while (true)
            {
                if (@in.ReadByte() < 0)
                {
                    break;
                }
            }

            Fail("expected IOException");
        }
        catch (IOException e)
        {
            CheckErrorCode(DataUtils.ErrorBlockNotFound, e.InnerException);
        }
    }

    [Fact]
    public void TestSaveCount()
    {
        string fileName = TempFile("testSaveCount.h3");
        DeleteFile(fileName);
        MVStore s = new MVStore.Builder().FileName(fileName).Open();
        MVMap<long, byte[]> map = s.OpenMap<long, byte[]>("data");
        var streamStore = new StreamStore(new MapAdapter(map));
        int blockSize = 256 * 1024;
        Assert.Equal(blockSize, (int)streamStore.GetMaxBlockSize());
        for (int i = 0; i < 8 * 16; i++)
        {
            streamStore.Put(new RandomStream(blockSize, i));
        }

        s.Close();
        long writeCount = s.GetFileStore()!.GetWriteCount();
        AssertTrue(writeCount > 5);
    }

    [Fact]
    public void TestExceptionDuringStore()
    {
        // test that if there is an IOException while storing
        // the data, the entries in the map are "rolled back"
        IDictionary<long, byte[]> map = new Dictionary<long, byte[]>();
        var s = new StreamStore(map, 256, 1024);
        Assert.Throws<IOException>(() => { s.Put(CreateFailingStream(new IOException())); });
        Assert.Empty(map);
        // the runtime exception is converted to an IOException
        Assert.Throws<IOException>(() => { s.Put(CreateFailingStream(new InvalidOperationException())); });
        Assert.Empty(map);
    }

    [Fact]
    public void TestReadCount()
    {
        string fileName = TempFile("testReadCount.h3");
        DeleteFile(fileName);
        MVStore s = new MVStore.Builder().FileName(fileName).Open();
        IFileStore fileStore = s.GetFileStore()!;
        fileStore.SetCacheSize(1);
        StreamStore streamStore = GetAutoCommitStreamStore(s);
        long size = fileStore.GetMaxPageSize() * 2;
        for (int i = 0; i < 100; i++)
        {
            streamStore.Put(new RandomStream(size, i));
        }

        s.Commit();
        MVMap<long, byte[]> map = s.OpenMap<long, byte[]>("data");
        AssertTrue("size: " + map.Count, map.SizeAsLong() >= 100);
        s.Close();

        s = new MVStore.Builder().FileName(fileName).Open();
        streamStore = GetAutoCommitStreamStore(s);
        for (int i = 0; i < 100; i++)
        {
            streamStore.Put(new RandomStream(size, -i));
        }

        s.Commit();
        long readCount = fileStore.GetReadCount();
        // the read count should be low because new blocks
        // are appended at the end (not between existing blocks)
        AssertTrue("rc: " + readCount, readCount <= 20);
        map = s.OpenMap<long, byte[]>("data");
        AssertTrue("size: " + map.Count, map.SizeAsLong() >= 200);
        s.Close();
    }

    private static StreamStore GetAutoCommitStreamStore(MVStore s)
    {
        MVMap<long, byte[]> map = s.OpenMap<long, byte[]>("data");
        return new StreamStore(new MapAdapter(map), len =>
        {
            if (s.GetUnsavedMemory() > s.GetAutoCommitMemory() / 2)
            {
                s.Commit();
            }
        });
    }

    [Fact]
    public void TestLarge()
    {
        string fileName = TempFile("testVeryLarge.h3");
        DeleteFile(fileName);
        MVStore s = new MVStore.Builder().FileName(fileName).Open();
        MVMap<long, byte[]> map = s.OpenMap<long, byte[]>("data");
        int count = 0;
        var streamStore = new StreamStore(new MapAdapter(map), len =>
        {
            count++;
            s.Commit();
        });
        streamStore.Put(new RandomStream(1024 * 1024, 0));
        s.Close();
        Assert.Equal(4, count);
    }

    /// <summary>
    /// A stream of incompressible data.
    /// </summary>
    private sealed class RandomStream : Stream
    {
        private readonly long size;

        private long pos;

        private int seed;

        internal RandomStream(long size, int seed)
        {
            this.size = size;
            this.seed = seed;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => size;

        public override long Position
        {
            get => pos;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (pos >= size)
            {
                return -1;
            }

            int len = (int)Math.Min(size - pos, count);
            int x = seed, end = offset + len;
            // a fast and very simple pseudo-random number generator
            // with a period length of 4 GB
            // also good: x * 9 + 1, shift 6; x * 11 + 1, shift 7
            while (offset < end)
            {
                x = (x << 4) + x + 1;
                buffer[offset++] = (byte)(x >> 8);
            }

            seed = x;
            pos += len;
            return len;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void TestDetectIllegalId()
    {
        IDictionary<long, byte[]> map = new Dictionary<long, byte[]>();
        var store = new StreamStore(map);
        Assert.Throws<ArgumentException>(() => { store.Length(new byte[] { 3, 0, 0 }); });
        Assert.Throws<ArgumentException>(() => { store.Remove(new byte[] { 3, 0, 0 }); });
        map[0L] = new byte[] { 3, 0, 0 };
        Stream @in = store.Get(new byte[] { 2, 1, 0 });
        Assert.Throws<ArgumentException>(() => { @in.ReadByte(); });
    }

    [Fact]
    public void TestTreeStructure()
    {
        var map = new CountingDictionary();
        var store = new StreamStore(map, 10, 100);
        byte[] id = store.Put(new MemoryStream(new byte[10000]));
        Stream @in = store.Get(id);
        Assert.Equal(0, @in.Read(new byte[0], 0, 0));
        Assert.Equal(0, @in.ReadByte());
        Assert.Equal(3, map.ReadCount);
    }

    [Fact]
    public void TestFormat()
    {
        var store = new StreamStore(new Dictionary<long, byte[]>(), 10, 20);
        store.SetNextKey(123);

        byte[] id;

        id = store.Put(new MemoryStream(new byte[200]));
        Assert.Equal(200L, store.Length(id));
        Assert.Equal("02c8018801", Hex.BytesToHex(id));

        id = store.Put(new MemoryStream(new byte[0]));
        Assert.Equal("", Hex.BytesToHex(id));

        id = store.Put(new MemoryStream(new byte[1]));
        Assert.Equal("000100", Hex.BytesToHex(id));

        id = store.Put(new MemoryStream(new byte[3]));
        Assert.Equal("0003000000", Hex.BytesToHex(id));

        id = store.Put(new MemoryStream(new byte[10]));
        Assert.Equal("010a8901", Hex.BytesToHex(id));

        byte[] combined = Convert.FromHexString("0001aa0002bbcc");
        Assert.Equal(3L, store.Length(combined));
        Stream @in = store.Get(combined);
        Assert.Equal(1L, Skip(@in, 1));
        Assert.Equal(0xbb, @in.ReadByte());
        Assert.Equal(1L, Skip(@in, 1));
    }

    [Fact]
    public void TestWithExistingData()
    {
        var map = new CountingDictionary();
        var store = new StreamStore(map, 10, 20);
        for (int i = 0; i < 10; i++)
        {
            store.Put(new MemoryStream(new byte[20]));
        }

        Assert.Equal(10, map.Count);
        Assert.Equal(10, map.ContainsCount);
        for (int i = 0; i < 10; i++)
        {
            AssertTrue(map.ContainsKey(i));
        }

        Assert.Equal(20, map.ContainsCount);
        store = new StreamStore(map, 10, 20);
        Assert.Equal(0L, store.GetNextKey());
        for (int i = 0; i < 5; i++)
        {
            store.Put(new MemoryStream(new byte[20]));
        }

        Assert.Equal(88, map.ContainsCount);
        Assert.Equal(15L, store.GetNextKey());
        Assert.Equal(15, map.Count);
        for (int i = 0; i < 15; i++)
        {
            AssertTrue(map.ContainsKey((long)i));
        }
    }

    [Fact]
    public void TestWithFullMap()
    {
        // simulate a *very* full map
        var map = new CountingDictionary { ContainsOverride = k => k < long.MaxValue / 2 };
        var store = new StreamStore(map, 20, 100);
        store.Put(new MemoryStream(new byte[100]));
        Assert.Single(map);
        Assert.Equal(64, map.ContainsCount);
        Assert.Equal(long.MaxValue / 2 + 1, store.GetNextKey());
    }

    [Fact]
    public void TestLoop()
    {
        Test(10, 20, 1000);
        for (int i = 0; i < 20; i++)
        {
            Test(0, 128, i);
            Test(10, 128, i);
        }

        for (int i = 20; i < 200; i += 10)
        {
            Test(0, 128, i);
            Test(10, 128, i);
        }
    }

    private static void Test(int minBlockSize, int maxBlockSize, int length)
    {
        IDictionary<long, byte[]> map = new Dictionary<long, byte[]>();
        var store = new StreamStore(map, minBlockSize, maxBlockSize);
        Assert.Equal(minBlockSize, store.GetMinBlockSize());
        Assert.Equal((long)maxBlockSize, store.GetMaxBlockSize());
        long next = store.GetNextKey();
        var r = new Random(length);
        byte[] data = new byte[length];
        r.NextBytes(data);
        byte[] id = store.Put(new MemoryStream(data));
        if (length > 0 && length >= minBlockSize)
        {
            AssertFalse(store.IsInPlace(id));
        }
        else
        {
            AssertTrue(store.IsInPlace(id));
        }

        long next2 = store.GetNextKey();
        if (length > 0 && length >= minBlockSize)
        {
            AssertTrue(next2 > next);
        }
        else
        {
            Assert.Equal(next, next2);
        }

        if (length == 0)
        {
            Assert.Empty(id);
        }

        Assert.Equal((long)length, store.Length(id));

        Stream @in = store.Get(id);
        using var @out = new MemoryStream();
        IOUtils.Copy(@in, @out);
        AssertTrue(data.SequenceEqual(@out.ToArray()));

        @in = store.Get(id);
        @in.Close();
        Assert.Equal(-1, @in.ReadByte());

        @in = store.Get(id);
        Assert.Equal(0L, Skip(@in, 0));
        if (length > 0)
        {
            Assert.Equal(1L, Skip(@in, 1));
            if (length > 1)
            {
                Assert.Equal(data[1] & 255, @in.ReadByte());
                if (length > 2)
                {
                    Assert.Equal(1L, Skip(@in, 1));
                    if (length > 3)
                    {
                        Assert.Equal(data[3] & 255, @in.ReadByte());
                    }
                }
                else
                {
                    Assert.Equal(0L, Skip(@in, 1));
                }
            }
            else
            {
                Assert.Equal(-1, @in.ReadByte());
            }
        }
        else
        {
            Assert.Equal(0L, Skip(@in, 1));
        }

        if (length > 12)
        {
            @in = store.Get(id);
            Assert.Equal(12L, Skip(@in, 12));
            Assert.Equal(data[12] & 255, @in.ReadByte());
            long skipped = 0;
            while (true)
            {
                long s = Skip(@in, int.MaxValue);
                if (s == 0)
                {
                    break;
                }

                skipped += s;
            }

            Assert.Equal(length - 13, skipped);
            Assert.Equal(-1, @in.ReadByte());
        }

        store.Remove(id);
        Assert.Empty(store.GetMap());
    }

    /// <summary>
    /// System.IO.Stream has no skip; this is InputStream.skip's default spelled out: read and
    /// discard, stopping at the end.
    /// </summary>
    private static long Skip(Stream stream, long n)
    {
        if (n <= 0)
        {
            return 0;
        }

        byte[] buf = new byte[(int)Math.Min(n, 8192)];
        long skipped = 0;
        while (skipped < n)
        {
            int read = stream.Read(buf, 0, (int)Math.Min(buf.Length, n - skipped));
            if (read <= 0)
            {
                break;
            }

            skipped += read;
        }

        return skipped;
    }

    private static Stream CreateFailingStream(Exception error) => new FailingStream(error);

    private sealed class FailingStream(Exception error) : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw error;
    }

    /// <summary>
    /// An MVMap as a dictionary, standing in for Java's Map-ness: the reference passes its maps
    /// straight into StreamStore, and this port's maps are not dictionaries. The adapter adds no
    /// logic; every operation goes to the map it wraps.
    /// </summary>
    private sealed class MapAdapter(MVMap<long, byte[]> map) : IDictionary<long, byte[]>
    {
        public byte[] this[long key]
        {
            get => map.Get(key)!;
            set => map.Put(key, value);
        }

        public ICollection<long> Keys => map.KeySet();

        public ICollection<byte[]> Values => map.Select(entry => entry.Value).ToList();

        public int Count => map.Count;

        public bool IsReadOnly => false;

        public void Add(long key, byte[] value) => map.Put(key, value);

        public void Add(KeyValuePair<long, byte[]> item) => map.Put(item.Key, item.Value);

        public void Clear() => map.Clear();

        public bool Contains(KeyValuePair<long, byte[]> item)
            => map.ContainsKey(item.Key);

        public bool ContainsKey(long key) => map.ContainsKey(key);

        public void CopyTo(KeyValuePair<long, byte[]>[] array, int arrayIndex)
        {
            foreach (KeyValuePair<long, byte[]> entry in map)
            {
                array[arrayIndex++] = entry;
            }
        }

        public IEnumerator<KeyValuePair<long, byte[]>> GetEnumerator() => map.GetEnumerator();

        public bool Remove(long key) => map.Remove(key) is not null;

        public bool Remove(KeyValuePair<long, byte[]> item) => Remove(item.Key);

        public bool TryGetValue(long key, out byte[] value)
        {
            byte[]? found = map.Get(key);
            value = found!;
            return found is not null;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// A dictionary that counts reads, standing in for the reference's anonymous subclasses:
    /// Dictionary has no virtual get or containsKey to override, so TryGetValue and the indexer
    /// count as reads and ContainsKey counts as contains — the operations the reference counts.
    /// </summary>
    private sealed class CountingDictionary : IDictionary<long, byte[]>
    {
        private readonly Dictionary<long, byte[]> inner = new();

        public int ReadCount { get; private set; }

        public int ContainsCount { get; private set; }

        public Func<long, bool>? ContainsOverride { get; set; }

        public byte[] this[long key]
        {
            get
            {
                ReadCount++;
                return inner[key];
            }

            set => inner[key] = value;
        }

        public ICollection<long> Keys => inner.Keys;

        public ICollection<byte[]> Values => inner.Values;

        public int Count => inner.Count;

        public bool IsReadOnly => false;

        public void Add(long key, byte[] value) => inner.Add(key, value);

        public void Add(KeyValuePair<long, byte[]> item) => inner.Add(item.Key, item.Value);

        public void Clear() => inner.Clear();

        public bool Contains(KeyValuePair<long, byte[]> item)
            => ((ICollection<KeyValuePair<long, byte[]>>)inner).Contains(item);

        public bool ContainsKey(long key)
        {
            ContainsCount++;
            if (ContainsOverride is not null && ContainsOverride(key))
            {
                return true;
            }

            return inner.ContainsKey(key);
        }

        public void CopyTo(KeyValuePair<long, byte[]>[] array, int arrayIndex)
            => ((ICollection<KeyValuePair<long, byte[]>>)inner).CopyTo(array, arrayIndex);

        public IEnumerator<KeyValuePair<long, byte[]>> GetEnumerator() => inner.GetEnumerator();

        public bool Remove(long key) => inner.Remove(key);

        public bool Remove(KeyValuePair<long, byte[]> item)
            => ((ICollection<KeyValuePair<long, byte[]>>)inner).Remove(item);

        public bool TryGetValue(long key, out byte[] value)
        {
            ReadCount++;
            bool found = inner.TryGetValue(key, out byte[]? current);
            value = current!;
            return found;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
