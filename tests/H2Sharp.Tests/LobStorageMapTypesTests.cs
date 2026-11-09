/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 95: the two registry types of the LOB maps. A database file names them in its
 * type registry, so their wire format is part of what an H2-written file carries —
 * BlobReference orders by stream id length, then by signed bytes, then by lob id, and
 * both types are written and read back here.
 */
using System;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Db;
using Xunit;

namespace H2Sharp.Tests;

public sealed class LobStorageMapTypesTests
{
    [Fact]
    public void BlobReferencesOrderByStreamThenId()
    {
        LobStorageMap.BlobReference a = new([1, 2], 5);
        LobStorageMap.BlobReference b = new([1, 2], 6);
        LobStorageMap.BlobReference longer = new([1, 2, 3], 1);
        LobStorageMap.BlobReference lowerByte = new([1, 1], 9);

        Assert.True(a.CompareTo(b) < 0);
        Assert.True(b.CompareTo(a) > 0);
        Assert.Equal(0, a.CompareTo(new LobStorageMap.BlobReference([1, 2], 5)));

        // A shorter stream identifier sorts first, whatever its lob id.
        Assert.True(b.CompareTo(longer) < 0);
        // The bytes are compared as Java's signed bytes: 0xFF is -1 and sorts before 1.
        Assert.True(
            new LobStorageMap.BlobReference([0xFF], 0)
                .CompareTo(new LobStorageMap.BlobReference([1], 0)) < 0);
        Assert.True(a.CompareTo(lowerByte) > 0);

        Assert.Throws<ArgumentNullException>(() => a.CompareTo(null));
    }

    [Fact]
    public void BlobReferenceTypeRoundTrips()
    {
        LobStorageMap.BlobReference.Type type = LobStorageMap.BlobReference.Type.Instance;
        LobStorageMap.BlobReference value = new([4, 5, 6], 1_234_567_890_123L);
        Assert.Equal(3 + 8, type.GetMemory(value));

        WriteBuffer buffer = new(32);
        type.Write(buffer, value);
        byte[] bytes = buffer.ToArray();

        int position = 0;
        LobStorageMap.BlobReference back = type.Read(bytes, ref position);
        Assert.Equal(bytes.Length, position);
        Assert.Equal(value.StreamStoreId, back.StreamStoreId);
        Assert.Equal(value.LobId, back.LobId);

        Assert.Equal(0, type.Compare(value, value));
        Assert.Equal(1, type.Compare(null, value));
        Assert.Equal(-1, type.Compare(value, null));
        Assert.True(type.Compare(value, new LobStorageMap.BlobReference([4, 5, 6], 1)) > 0);
        Assert.Equal(4, type.CreateStorage(4).Length);
    }

    [Fact]
    public void BlobMetaTypeRoundTrips()
    {
        LobStorageMap.BlobMeta.Type type = LobStorageMap.BlobMeta.Type.Instance;
        LobStorageMap.BlobMeta value = new([7, 8], 42, 1_000_000_000L, -123_456_789L, 99);
        Assert.Equal(2 + 20, type.GetMemory(value));

        WriteBuffer buffer = new(64);
        type.Write(buffer, value);
        byte[] bytes = buffer.ToArray();

        int position = 0;
        LobStorageMap.BlobMeta back = type.Read(bytes, ref position);
        Assert.Equal(bytes.Length, position);
        Assert.Equal(value.StreamStoreId, back.StreamStoreId);
        Assert.Equal(42, back.TableId);
        Assert.Equal(1_000_000_000L, back.ByteCount);
        Assert.Equal(-123_456_789L, back.Hash);
        Assert.Equal(99, back.CharLength);
        Assert.Equal(3, type.CreateStorage(3).Length);
    }
}
