using System;
using System.IO;

namespace H2Sharp.Compression;

/// <summary>
/// A read-only stream over a slice of memory.
/// </summary>
/// <remarks>
/// A stream cannot wrap a span, and copying the slice into an array just to hand it to a
/// stream defeats the span it came from. This one reads straight from the memory: an
/// offset, a length, and no copy. Writing, seeking and timeouts are refused, which is
/// what a deflate reader needs and nothing it does not.
/// </remarks>
internal sealed class ReadOnlyMemoryStream : Stream
{
    private readonly ReadOnlyMemory<byte> data;

    private int position;

    /// <summary>
    /// A stream reading the memory from its start.
    /// </summary>
    /// <param name="data">the bytes</param>
    internal ReadOnlyMemoryStream(ReadOnlyMemory<byte> data) => this.data = data;

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => data.Length;

    /// <inheritdoc/>
    public override long Position
    {
        get => position;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        int available = data.Length - position;
        if (available <= 0)
        {
            return 0;
        }

        int count = Math.Min(buffer.Length, available);
        data.Span.Slice(position, count).CopyTo(buffer);
        position += count;
        return count;
    }

    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
