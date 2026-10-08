/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.store.RangeInputStream.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.IO;
using H2Sharp.Util;

namespace H2Sharp.Store;

/// <summary>
/// A window of a stream: a place to start and how far it goes.
/// </summary>
/// <remarks>
/// This is what a LOB is: bytes in the middle of a file, with a length of their own. The reference
/// keeps the window by counting down, and so does this, because the end of the window is not the
/// end of the stream and the two must not be confused: reading at the end of the window is the end
/// of the data, and reading at the end of the stream is a failure.
/// </remarks>
public sealed class RangeInputStream : Stream
{
    private readonly Stream input;

    private long limit;

    /// <summary>
    /// A window of a stream.
    /// </summary>
    /// <param name="input">the stream</param>
    /// <param name="offset">where the window starts</param>
    /// <param name="limit">how long it is</param>
    /// <exception cref="IOException">if the stream ends before the offset</exception>
    public RangeInputStream(Stream input, long offset, long limit)
    {
        ArgumentNullException.ThrowIfNull(input);
        this.input = input;
        this.limit = limit;
        IOUtils.SkipFully(input, offset);
    }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    /// <remarks>
    /// The length of the whole stream, not of the window. That is the reference's answer rather than
    /// an oversight on our part: its window class is a filter over another stream and does not
    /// override the length, so a window says how long the file is. A caller that wants the window
    /// knows the window, because it is the one that opened it.
    /// </remarks>
    public override long Length => input.CanSeek ? input.Length : throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    public override int ReadByte()
    {
        if (limit <= 0)
        {
            return -1;
        }

        int b = input.ReadByte();
        if (b >= 0)
        {
            limit--;
        }

        return b;
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        if (limit <= 0)
        {
            return 0;
        }

        int count = (int)Math.Min(buffer.Length, limit);
        int read = input.Read(buffer[..count]);
        if (read > 0)
        {
            limit -= read;
        }

        return read;
    }

    /// <inheritdoc/>
    public long Skip(long n)
    {
        n = Math.Min(n, limit);
        if (n <= 0)
        {
            return 0;
        }

        long skipped = 0;
        if (input.CanSeek)
        {
            long before = input.Position;
            skipped = input.Seek(n, SeekOrigin.Current) - before;
        }
        else
        {
            skipped = input.ReadByte() < 0 ? 0 : 1;
        }

        limit -= skipped;
        return skipped;
    }

    /// <inheritdoc/>
    public int Available()
    {
        int available = input.CanSeek
            ? (int)Math.Min(input.Length - input.Position, int.MaxValue)
            : 0;
        return (int)Math.Min(available, limit);
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void WriteByte(byte value) => throw new NotSupportedException();

    /// <inheritdoc/>
    /// <remarks>
    /// The reference has mark and reset refused with a message; here they are refused the way every
    /// .NET stream refuses them, by not supporting them at all, which is the same answer with one
    /// mechanism instead of two.
    /// </remarks>
    public bool MarkSupported => false;

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
