/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A TextReader over a slice of a string that does not copy the slice.
 *
 * The reference's Value.getReader(offset, length) is
 * new StringReader(string.substring(offset, offset + length)): the window is copied into a
 * second string, which for a large CLOB means another string of the window's size in the
 * heap. This reader answers the same characters out of the original string's memory.
 * Approved deviation D-043.
 */
using System;
using System.IO;

namespace H2Sharp.Values;

/// <summary>
/// A reader over a range of characters in an existing string.
/// </summary>
internal sealed class CharSliceReader : TextReader
{
    private readonly ReadOnlyMemory<char> slice;

    private int position;

    private bool disposed;

    /// <summary>
    /// Creates a reader over the given characters.
    /// </summary>
    /// <param name="slice">the characters, which are not copied</param>
    internal CharSliceReader(ReadOnlyMemory<char> slice)
    {
        this.slice = slice;
    }

    /// <inheritdoc/>
    public override int Peek()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return position < slice.Length ? slice.Span[position] : -1;
    }

    /// <inheritdoc/>
    public override int Read()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return position < slice.Length ? slice.Span[position++] : -1;
    }

    /// <inheritdoc/>
    public override int Read(char[] buffer, int index, int count)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (buffer.Length - index < count)
        {
            throw new ArgumentException("Offset and length were out of bounds for the array.", nameof(count));
        }

        return ReadCore(buffer.AsSpan(index, count));
    }

    /// <inheritdoc/>
    public override int Read(Span<char> buffer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return ReadCore(buffer);
    }

    /// <inheritdoc/>
    public override int ReadBlock(char[] buffer, int index, int count)
        => Read(buffer, index, count);

    /// <inheritdoc/>
    public override int ReadBlock(Span<char> buffer) => Read(buffer);

    /// <inheritdoc/>
    public override string ReadToEnd()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        string result = slice.Span[position..].ToString();
        position = slice.Length;
        return result;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        disposed = true;
        base.Dispose(disposing);
    }

    private int ReadCore(Span<char> buffer)
    {
        int count = Math.Min(buffer.Length, slice.Length - position);
        if (count <= 0)
        {
            return 0;
        }

        slice.Span.Slice(position, count).CopyTo(buffer);
        position += count;
        return count;
    }
}
