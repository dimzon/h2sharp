/*
 * Ported from org.h2.compress.LZFInputStream.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.IO;
using H2Sharp.Util;

namespace H2Sharp.Compression;

/// <summary>
/// Reads an LZF stream: a header, and then blocks that are either compressed or, when compression
/// did not pay, stored as they are.
/// </summary>
/// <remarks>
/// A block starts with its length, and a negative length means the block is stored as it is. The
/// end of the stream is not marked: reading past the last block is how the reader learns that there
/// is nothing more, which is why the decompressor is set aside rather than asked for a block it
/// cannot have.
/// </remarks>
public sealed class LZFInputStream : Stream
{
    private readonly Stream input;

    private CompressLZF? decompress = new();

    private int pos;

    private int bufferLength;

    private byte[]? inBuffer;

    private byte[]? buffer;

    /// <summary>
    /// A reader over the given stream.
    /// </summary>
    /// <param name="input">where to read</param>
    /// <exception cref="IOException">if the stream is not an LZF stream</exception>
    public LZFInputStream(Stream input)
    {
        this.input = input;
        if (ReadInt() != LZFOutputStream.Magic)
        {
            throw new IOException("Not an LZFInputStream");
        }
    }

    private static byte[] EnsureSize(byte[]? bytes, int len)
        => bytes is null || bytes.Length < len ? Utils.NewBytes(len) : bytes;

    private void FillBuffer()
    {
        if (buffer is not null && pos < bufferLength)
        {
            return;
        }

        int len = ReadInt();
        if (decompress is null)
        {
            // The end of the stream.
            bufferLength = 0;
        }
        else if (len < 0)
        {
            len = -len;
            buffer = EnsureSize(buffer, len);
            ReadFully(buffer, len);
            bufferLength = len;
        }
        else
        {
            inBuffer = EnsureSize(inBuffer, len);
            int size = ReadInt();
            ReadFully(inBuffer, len);
            buffer = EnsureSize(buffer, size);
            try
            {
                decompress.Expand(inBuffer, 0, len, buffer, 0, size);
            }
            catch (IndexOutOfRangeException e)
            {
                // The reference answers a damaged block with an I/O error rather than with the
                // exception that the expansion itself raises.
                throw new IOException("Corrupt LZF block", e);
            }

            bufferLength = size;
        }

        pos = 0;
    }

    private void ReadFully(byte[] bytes, int len)
    {
        int offset = 0;
        while (len > 0)
        {
            int read = input.Read(bytes, offset, len);
            if (read <= 0)
            {
                throw new IOException("Unexpected end of LZF stream");
            }

            len -= read;
            offset += read;
        }
    }

    private int ReadInt()
    {
        int x = input.ReadByte();
        if (x < 0)
        {
            decompress = null;
            return 0;
        }

        x = (x << 24) + (input.ReadByte() << 16) + (input.ReadByte() << 8) + input.ReadByte();
        return x;
    }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override int ReadByte()
    {
        FillBuffer();
        return pos >= bufferLength ? -1 : buffer![pos++] & 255;
    }

    /// <summary>
    /// A block read, or a negative answer when there is nothing left.
    /// </summary>
    /// <remarks>
    /// The reference's <c>read</c> answers -1 at the end of the stream because that is what
    /// <c>InputStream</c> answers, and this port keeps -1 inside the reader where it belongs. What
    /// leaves the reader is a .NET stream, and a .NET stream answers 0 at the end: <c>CopyTo</c>
    /// loops until it gets a 0 and writes whatever count it was given, so a -1 that got out of here
    /// is an argument error one frame further up.
    /// </remarks>
    /// <param name="bytes">where to put the block</param>
    /// <param name="offset">where in it</param>
    /// <param name="count">how much room there is</param>
    /// <returns>how many bytes were read, or 0 at the end</returns>
    public override int Read(byte[] bytes, int offset, int count)
    {
        if (count == 0)
        {
            return 0;
        }

        int read = 0;
        while (count > 0)
        {
            int r = ReadBlock(bytes, offset, count);
            if (r < 0)
            {
                break;
            }

            read += r;
            offset += r;
            count -= r;
        }

        return read;
    }

    /// <inheritdoc/>
    public override int Read(Span<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return 0;
        }

        FillBuffer();
        if (pos >= bufferLength)
        {
            return 0;
        }

        int max = Math.Min(bytes.Length, bufferLength - pos);
        buffer.AsSpan(pos, max).CopyTo(bytes);
        pos += max;
        return max;
    }

    private int ReadBlock(byte[] bytes, int offset, int count)
    {
        FillBuffer();
        if (pos >= bufferLength)
        {
            return -1;
        }

        int max = Math.Min(count, bufferLength - pos);
        max = Math.Min(max, bytes.Length - offset);
        Array.Copy(buffer!, pos, bytes, offset, max);
        pos += max;
        return max;
    }

    /// <inheritdoc/>
    public override void Flush()
    {
        // Nothing of our own to push.
    }

    /// <inheritdoc/>
    public override void Write(byte[] bytes, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            input.Dispose();
        }

        base.Dispose(disposing);
    }
}
