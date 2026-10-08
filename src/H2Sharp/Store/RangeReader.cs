/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.store.RangeReader.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.IO;
using H2Sharp.Util;

namespace H2Sharp.Store;

/// <summary>
/// A window of text: a place to start and how far it goes.
/// </summary>
/// <remarks>
/// The character-shaped twin of <see cref="RangeInputStream"/>, and needed for the same reason: a
/// CLOB is characters in the middle of a file, and its length is its own.
/// </remarks>
public sealed class RangeReader : TextReader
{
    private readonly TextReader reader;

    private long limit;

    /// <summary>
    /// A window of text.
    /// </summary>
    /// <param name="reader">the text</param>
    /// <param name="offset">where the window starts</param>
    /// <param name="limit">how long it is</param>
    /// <exception cref="IOException">if the text ends before the offset</exception>
    public RangeReader(TextReader reader, long offset, long limit)
    {
        ArgumentNullException.ThrowIfNull(reader);
        this.reader = reader;
        this.limit = limit;
        IOUtils.SkipFully(reader, offset);
    }

    /// <inheritdoc/>
    public override int Read()
    {
        if (limit <= 0)
        {
            return -1;
        }

        int c = reader.Read();
        if (c >= 0)
        {
            limit--;
        }

        return c;
    }

    /// <inheritdoc/>
    public override int Read(char[] buffer, int index, int count) => Read(buffer.AsSpan(index, count));

    /// <inheritdoc/>
    public override int Read(Span<char> buffer)
    {
        if (limit <= 0)
        {
            return 0;
        }

        int count = (int)Math.Min(buffer.Length, limit);
        int read = reader.Read(buffer[..count]);
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

        // A reader has no place to jump to, so skipping is reading and throwing away.
        char[] one = new char[1];
        long skipped = 0;
        while (skipped < n && reader.Read(one, 0, 1) > 0)
        {
            skipped++;
        }

        limit -= skipped;
        return skipped;
    }

    /// <inheritdoc/>
    public bool Ready() => limit > 0 && reader.Peek() >= 0;

    /// <inheritdoc/>
    /// <remarks>
    /// A LOB is read once, from the start, and is not re-read from the middle; the reference
    /// refuses mark and reset for that reason, and a .NET reader refuses them by not having them.
    /// </remarks>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            reader.Dispose();
        }

        base.Dispose(disposing);
    }
}
