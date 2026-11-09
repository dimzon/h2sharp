/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 100: the native LOB block and its map data type. The block copies in, is read
 * from a stream (shrinking to the bytes actually read, refusing at the end), exposes a
 * writable span, and frees idempotently while the live counter follows. The data type
 * keeps the tagged-array wire form for short and long blocks and for plain arrays.
 */
using System;
using System.IO;
using System.Linq;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Db;
using Xunit;

namespace H2Sharp.Tests;

public sealed class NativeLobBlockTests
{
    [Fact]
    public void ABlockCopiesInAndFreesOnce()
    {
        long before = NativeLobBlock.LiveAllocations;

        NativeLobBlock fromBytes = NativeLobBlock.FromBytes([1, 2, 3]);
        Assert.Equal(3, fromBytes.Length);
        Assert.Equal(new byte[] { 1, 2, 3 }, fromBytes.Bytes.ToArray());

        NativeLobBlock fromSpan = NativeLobBlock.FromSpan([4, 5]);
        Assert.Equal(new byte[] { 4, 5 }, fromSpan.Bytes.ToArray());

        fromBytes.WritableSpan[0] = 9;
        Assert.Equal(9, fromBytes.Bytes[0]);

        Assert.Equal(before + 2, NativeLobBlock.LiveAllocations);
        fromBytes.Free();
        fromBytes.Free();
        fromSpan.Free();
        Assert.Equal(before, NativeLobBlock.LiveAllocations);
    }

    [Fact]
    public void ReadingFromAStreamTakesOnlyWhatIsThere()
    {
        using MemoryStream full = new([1, 2, 3, 4]);
        NativeLobBlock? exact = NativeLobBlock.ReadFrom(full, 4);
        Assert.NotNull(exact);
        Assert.Equal(4, exact!.Length);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, exact.Bytes.ToArray());
        exact.Free();
        // A seekable stream at its end allocates nothing.
        Assert.Null(NativeLobBlock.ReadFrom(full, 4));

        using MemoryStream shortStream = new([1, 2, 3]);
        NativeLobBlock? shrunk = NativeLobBlock.ReadFrom(shortStream, 16);
        Assert.NotNull(shrunk);
        Assert.Equal(3, shrunk!.Length);
        Assert.Equal(new byte[] { 1, 2, 3 }, shrunk.Bytes.ToArray());
        shrunk.Free();

        // A stream that answers zero without being at a known end frees the block.
        using ZeroStream zero = new();
        Assert.Null(NativeLobBlock.ReadFrom(zero, 8));
    }

    [Fact]
    public void TheDataTypeKeepsTheTaggedArrayForm()
    {
        LobBlockDataType type = LobBlockDataType.Instance;

        NativeLobBlock shortBlock = NativeLobBlock.FromBytes([1, 2, 3]);
        WriteBuffer shortBuffer = new(16);
        type.Write(shortBuffer, shortBlock);
        byte[] shortBytes = shortBuffer.ToArray();
        int shortPos = 0;
        NativeLobBlock shortBack = Assert.IsType<NativeLobBlock>(type.Read(shortBytes, ref shortPos));
        Assert.Equal(shortBytes.Length, shortPos);
        Assert.Equal(new byte[] { 1, 2, 3 }, shortBack.Bytes.ToArray());
        Assert.Equal((64 + 3) * 2, type.GetMemory(shortBlock));

        byte[] longData = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray();
        NativeLobBlock longBlock = NativeLobBlock.FromBytes(longData);
        WriteBuffer longBuffer = new(512);
        type.Write(longBuffer, longBlock);
        byte[] longBytes = longBuffer.ToArray();
        int longPos = 0;
        NativeLobBlock longBack = Assert.IsType<NativeLobBlock>(type.Read(longBytes, ref longPos));
        Assert.Equal(longBytes.Length, longPos);
        Assert.Equal(longData, longBack.Bytes.ToArray());

        // The same form takes a managed array, which is what a value not yet moved to
        // native memory is written as.
        WriteBuffer arrayBuffer = new(16);
        type.Write(arrayBuffer, new byte[] { 7, 8 });
        Assert.Equal((64 + 2) * 2, type.GetMemory(new byte[] { 7, 8 }));
        int arrayPos = 0;
        NativeLobBlock arrayBack = Assert.IsType<NativeLobBlock>(type.Read(arrayBuffer.ToArray(), ref arrayPos));
        Assert.Equal(new byte[] { 7, 8 }, arrayBack.Bytes.ToArray());

        Assert.Equal(3, type.CreateStorage(3).Length);
        shortBlock.Free();
        longBlock.Free();
        shortBack.Free();
        longBack.Free();
        arrayBack.Free();
    }

    /// <summary>A non-seekable stream that is immediately at its end.</summary>
    private sealed class ZeroStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
