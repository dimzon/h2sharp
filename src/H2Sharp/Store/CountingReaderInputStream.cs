/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.store.CountingReaderInputStream.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: the reference encodes with a CharsetEncoder into a ByteBuffer and keeps a CharBuffer
 * with its position, limit and flip dance. A stream encoder does the same work in one place, and it
 * can hand back the characters it was given rather than counting them here, which is what
 * GetLength then answers.
 */
using System;
using System.IO;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Util;

namespace H2Sharp.Store;

/// <summary>
/// Turns text into bytes, up to a known number of characters, and counts what it read.
/// </summary>
/// <remarks>
/// This is how a CLOB of known length is written: the store knows how many characters it is, so it
/// stops there rather than reading to the end and hoping. The count it keeps is in characters, not
/// bytes, because that is what the length was in.
/// </remarks>
public sealed class CountingReaderInputStream : Stream
{
    private readonly TextReader reader;

    private readonly Encoder encoder;

    private long length;

    private long remaining;

    /// <summary>
    /// Bytes that were encoded and not yet read.
    /// </summary>
    private byte[] pending = [];

    private int pendingAt;

    private int pendingLength;

    /// <summary>
    /// The character window, kept for the instance's life as the reference keeps its buffer:
    /// every fetch reads into the same array instead of allocating one per call.
    /// </summary>
    private readonly char[] characters = new char[Constants.IoBufferSize];

    /// <summary>
    /// A stream over some text, of at most so many characters.
    /// </summary>
    /// <param name="reader">the text</param>
    /// <param name="maxLength">how many characters at most, or not more than that when it is not more than zero</param>
    public CountingReaderInputStream(TextReader reader, long maxLength)
    {
        ArgumentNullException.ThrowIfNull(reader);
        this.reader = reader;
        remaining = maxLength <= 0 ? long.MaxValue : maxLength;

        // The reference replaces what cannot be encoded and what has no character for it, and what
        // its UTF-8 encoder replaces with is a question mark. .NET's own replacement would be U+FFFD,
        // which is the difference the port's byte encoding avoids everywhere else; here it would
        // otherwise be the one place that quietly differs. So the replacement is named outright.
        encoder = IOUtils.StreamEncoding.GetEncoder();
        encoder.Fallback = JavaReplacementFallback();
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

    /// <summary>
    /// How many characters have been read. There may still be bytes of them in the buffer, so this
    /// runs ahead of what the reader has been handed.
    /// </summary>
    /// <returns>the number of characters</returns>
    public long GetLength() => length;

    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    public override int ReadByte()
    {
        if (!Fetch())
        {
            return -1;
        }

        return pending[pendingAt++];
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        if (!Fetch())
        {
            return 0;
        }

        int count = Math.Min(buffer.Length, pendingLength - pendingAt);
        pending.AsSpan(pendingAt, count).CopyTo(buffer);
        pendingAt += count;
        return count;
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            reader.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Whether there is a byte to hand, encoding more text if there is not.
    /// </summary>
    /// <returns>true when there is</returns>
    private bool Fetch()
    {
        if (pendingAt < pendingLength)
        {
            return true;
        }

        pendingAt = 0;
        pendingLength = 0;
        if (remaining == 0)
        {
            return false;
        }

        int want = (int)Math.Min(characters.Length, remaining);
        int read = reader.Read(characters, 0, want);
        if (read <= 0)
        {
            remaining = 0;
            return false;
        }

        remaining -= read;
        length += read;

        byte[] bytes = new byte[Constants.IoBufferSize * 4];
        encoder.Convert(characters, 0, read, bytes, 0, bytes.Length, flush: remaining == 0, out _, out int used, out _);
        pending = bytes;
        pendingLength = used;
        return used > 0 || Fetch();
    }

    private static EncoderFallback JavaReplacementFallback() => new JavaFallback();

    /// <summary>
    /// The replacement the reference's encoder writes for what it cannot encode.
    /// </summary>
    /// <remarks>
    /// .NET's own replacement is U+FFFD, and the reference's is a question mark: that is the
    /// difference the port's byte encoding avoids everywhere else, and here it would otherwise be the
    /// one place that quietly differs.
    /// <para>
    /// The protocol is not "write these bytes": the encoder asks that a replacement is needed and
    /// then takes the replacement <em>characters</em> one at a time and encodes them itself. So the
    /// replacement is a string, and its bytes are whatever encoding it gives — which for a question
    /// mark is one byte.
    /// </para>
    /// </remarks>
    private sealed class JavaFallback : EncoderFallback
    {
        public override int MaxCharCount => 1;

        public override EncoderFallbackBuffer CreateFallbackBuffer() => new JavaFallbackBuffer();
    }

    /// <summary>
    /// Hands out one question mark and then nothing more.
    /// </summary>
    private sealed class JavaFallbackBuffer : EncoderFallbackBuffer
    {
        private const string Replacement = "?";

        private int left;

        public override int Remaining => 0;

        public override bool Fallback(char charUnknown, int index)
        {
            left = Replacement.Length;
            return true;
        }

        public override bool Fallback(char charUnknownHigh, char charUnknownLow, int index)
        {
            // A pair that is not a character at all is still one replacement, not two.
            left = Replacement.Length;
            return true;
        }

        public override char GetNextChar()
        {
            if (left == 0)
            {
                return '\0';
            }

            left--;
            return Replacement[Replacement.Length - 1 - left];
        }

        public override bool MovePrevious() => false;
    }
}
