/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 93: the LZF stream pair — the header, the block framing, both Write
 * overloads crossing the 128 KiB buffer, WriteByte flushing a full buffer, the
 * .NET stream contract (0 at the end, not -1), the byte and span readers, the
 * damaged-block IOException, and the members a read-only or write-only stream
 * refuses.
 */
using System;
using System.IO;
using System.Linq;
using H2Sharp.Compression;
using H2Sharp.Engine;
using Xunit;

namespace H2Sharp.Tests;

public sealed class LzfStreamCoverageTests
{
    private static byte[] Pattern(int length)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < length; i++)
        {
            data[i] = (byte)(i * 31 % 251);
        }

        return data;
    }

    [Fact]
    public void BothWritersAndReadersRoundTrip()
    {
        byte[] data = Pattern(300_000);
        byte[] zeros = new byte[Constants.IoBufferSizeCompress + 10];
        byte[] tail = Pattern(10);

        using MemoryStream memory = new();
        using (LZFOutputStream output = new(memory))
        {
            Assert.False(output.CanRead);
            Assert.False(output.CanSeek);
            Assert.True(output.CanWrite);
            Assert.Throws<NotSupportedException>(() => _ = output.Length);
            Assert.Throws<NotSupportedException>(() => _ = output.Position);
            Assert.Throws<NotSupportedException>(() => output.Position = 0);
            Assert.Throws<NotSupportedException>(() => output.Read(new byte[1], 0, 1));
            Assert.Throws<NotSupportedException>(() => output.Seek(0, SeekOrigin.Begin));
            Assert.Throws<NotSupportedException>(() => output.SetLength(0));

            output.WriteByte(1);
            output.Write(data, 0, data.Length);
            output.Write(data.AsSpan());
            output.WriteByte(2);
            output.Flush();

            // A full buffer: the next WriteByte flushes it before it writes.
            for (int i = 0; i < Constants.IoBufferSizeCompress; i++)
            {
                output.WriteByte((byte)i);
            }

            output.WriteByte(9);

            // Both overloads cross the buffer boundary; the span reader is fed below.
            output.Write(zeros, 0, zeros.Length);
            output.Write(zeros.AsSpan());
            output.Write(tail, 0, tail.Length);
        }

        byte[] expected = [1, .. data, .. data, 2, .. Enumerable.Range(0, Constants.IoBufferSizeCompress)
            .Select(i => (byte)i), 9, .. zeros, .. zeros, .. tail];

        // The writer owns and closes the stream it was given; its bytes are still there.
        using MemoryStream readBack = new(memory.ToArray());
        using LZFInputStream input = new(readBack);
        Assert.True(input.CanRead);
        Assert.False(input.CanSeek);
        Assert.False(input.CanWrite);
        Assert.Throws<NotSupportedException>(() => _ = input.Length);
        Assert.Throws<NotSupportedException>(() => _ = input.Position);
        Assert.Throws<NotSupportedException>(() => input.Position = 0);
        Assert.Throws<NotSupportedException>(() => input.Write(new byte[1], 0, 1));
        Assert.Throws<NotSupportedException>(() => input.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => input.SetLength(0));

        // The byte reader and the span reader see the same bytes the block reader sees.
        Assert.Equal(1, input.ReadByte());
        Assert.Equal(0, input.Read([], 0, 0));
        byte[] firstSpan = new byte[3];
        Assert.Equal(3, input.Read(firstSpan));
        Assert.Equal(data.AsSpan(0, 3).ToArray(), firstSpan);
        input.Flush();

        using MemoryStream collected = new();
        input.CopyTo(collected);
        Assert.Equal(expected.Length - 4, collected.Length);
        Assert.Equal(expected[4..], collected.ToArray());

        // Past the end: -1 from the byte reader, 0 from both block readers.
        Assert.Equal(-1, input.ReadByte());
        Assert.Equal(0, input.Read(new byte[1], 0, 1));
        Assert.Equal(0, input.Read(Span<byte>.Empty));
        Assert.Equal(0, input.Read(new byte[1]));
    }

    [Fact]
    public void ADamagedBlockIsAnIoError()
    {
        using MemoryStream corrupt = new();
        WriteInt(corrupt, LZFOutputStream.Magic);
        WriteInt(corrupt, 1);
        WriteInt(corrupt, 100);
        corrupt.WriteByte(0x20); // a literal run of 33 bytes with no data behind it
        corrupt.Position = 0;

        using LZFInputStream input = new(corrupt);
        IOException e = Assert.Throws<IOException>(() => input.ReadByte());
        Assert.Equal("Corrupt LZF block", e.Message);
    }

    [Fact]
    public void AStreamWithoutTheHeaderIsRefused()
        => Assert.Throws<IOException>(
            () => new LZFInputStream(new MemoryStream([1, 2, 3, 4])));

    private static void WriteInt(Stream stream, int value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }
}
