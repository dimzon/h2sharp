/*
 * Ported from org.h2.compress.LZFOutputStream.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.IO;
using H2Sharp.Engine;

namespace H2Sharp.Compression;

/// <summary>
/// Writes an LZF stream: a header, and then blocks of compressed data, each of which says how long
/// it is both compressed and not, so that a block which did not get smaller is stored as it is.
/// </summary>
/// <remarks>
/// The reference grows its output buffer to twice the block, which it admits is a guess; the guess
/// is kept, because the size of the buffer is not in the file and the only thing that has to agree
/// between the two sides is what the file says.
/// </remarks>
public sealed class LZFOutputStream : Stream
{
    /// <summary>
    /// The header of an LZF stream.
    /// </summary>
    internal const int Magic = ('H' << 24) | ('2' << 16) | ('I' << 8) | 'S';

    private readonly Stream output;

    private readonly CompressLZF compress = new();

    private readonly byte[] buffer;

    private int pos;

    private byte[]? outBuffer;

    /// <summary>
    /// A writer over the given stream.
    /// </summary>
    /// <param name="output">where to write</param>
    public LZFOutputStream(Stream output)
    {
        this.output = output;
        int len = Constants.IoBufferSizeCompress;
        buffer = new byte[len];
        EnsureOutput(len);
        WriteInt(Magic);
    }

    private void EnsureOutput(int len)
    {
        int outputLen = (len < 100 ? len + 100 : len) * 2;
        if (outBuffer is null || outBuffer.Length < outputLen)
        {
            outBuffer = new byte[outputLen];
        }
    }

    /// <inheritdoc/>
    public override bool CanRead => false;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => true;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void WriteByte(byte value)
    {
        if (pos >= buffer.Length)
        {
            Flush();
        }

        buffer[pos++] = value;
    }

    /// <inheritdoc/>
    public override void Write(byte[] bytes, int offset, int count)
    {
        while (count > 0)
        {
            int copy = Math.Min(buffer.Length - pos, count);
            Array.Copy(bytes, offset, buffer, pos, copy);
            pos += copy;
            if (pos >= buffer.Length)
            {
                Flush();
            }

            offset += copy;
            count -= copy;
        }
    }

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> bytes)
    {
        int offset = 0;
        while (offset < bytes.Length)
        {
            int copy = Math.Min(buffer.Length - pos, bytes.Length - offset);
            bytes.Slice(offset, copy).CopyTo(buffer.AsSpan(pos));
            pos += copy;
            if (pos >= buffer.Length)
            {
                Flush();
            }

            offset += copy;
        }
    }

    private void CompressAndWrite(int len)
    {
        if (len <= 0)
        {
            return;
        }

        EnsureOutput(len);
        int compressed = compress.Compress(buffer, 0, len, outBuffer!, 0);
        if (compressed > len)
        {
            WriteInt(-len);
            output.Write(buffer, 0, len);
        }
        else
        {
            WriteInt(compressed);
            WriteInt(len);
            output.Write(outBuffer!, 0, compressed);
        }
    }

    private void WriteInt(int x)
    {
        output.WriteByte((byte)(x >> 24));
        output.WriteByte((byte)(x >> 16));
        output.WriteByte((byte)(x >> 8));
        output.WriteByte((byte)x);
    }

    /// <inheritdoc/>
    public override void Flush()
    {
        CompressAndWrite(pos);
        pos = 0;
    }

    /// <inheritdoc/>
    public override int Read(byte[] bytes, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Flush();
            output.Dispose();
        }

        base.Dispose(disposing);
    }
}
