/*
 * Copyright 2004-2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Ported from org.h2.util.IOUtils.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: the stream and reader parts only. The rest of the reference is about its own file
 * systems and about mvstore.DataUtils, which is not ported: copyFiles, nameSeparatorsToNative,
 * getInputStreamFromString and the FileChannel overloads of copy come with them.
 *
 * What is different besides that, and has to be: a Java AutoCloseable is not a thing here, so
 * CloseSilently takes a delegate; and a failure that the reference turns into an IOException
 * through DataUtils is left as the exception it already is, because a wrapping that only renames
 * it would lose the type the caller wants to catch.
 */
using System;
using System.Buffers;
using System.IO;
using System.Text;
using H2Sharp.Engine;

namespace H2Sharp.Util;

/// <summary>
/// Reading and writing streams, and closing things that may not be there.
/// </summary>
public static class IOUtils
{
    /// <summary>
    /// Close something, and do not mind if it fails.
    /// </summary>
    /// <remarks>
    /// The reference takes an <c>AutoCloseable</c>, which .NET does not have: there is no interface
    /// that a <c>Stream</c>, a <c>StreamReader</c> and an <c>IDisposable</c> all implement. So this
    /// takes what to close, and the call sites pass the close itself.
    /// </remarks>
    /// <param name="close">how to close it, or null if there is nothing to close</param>
    public static void CloseSilently(Action? close)
    {
        if (close is null)
        {
            return;
        }

        try
        {
            close();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or NotSupportedException
            or InvalidOperationException or UnauthorizedAccessException)
        {
            // Closing is the last thing that happens, and a failure here has nothing left to spoil.
        }
    }

    /// <summary>
    /// Skip so many bytes in a stream, and keep asking until they are all gone.
    /// </summary>
    /// <param name="input">the stream</param>
    /// <param name="skip">how many bytes</param>
    /// <exception cref="IOException">if the stream ends before that</exception>
    /// <remarks>
    /// A single <c>Skip</c> is allowed to skip nothing, and on a stream that has nothing buffered it
    /// usually does, so one call is not enough — and a skip of nothing when bytes were asked for is
    /// an end of the stream rather than a no-op.
    /// </remarks>
    public static void SkipFully(Stream input, long skip)
    {
        ArgumentNullException.ThrowIfNull(input);
        while (skip > 0)
        {
            long skipped;
            if (input.CanSeek)
            {
                // The position is read before the jump, not after: reading it afterwards would give
                // the difference as zero and skip one byte too many.
                long before = input.Position;
                skipped = input.Seek(skip, SeekOrigin.Current) - before;
            }
            else
            {
                // Not every stream can seek, so for those skipping is reading and throwing away.
                skipped = input.ReadByte() < 0 ? 0 : 1;
            }

            if (skipped <= 0)
            {
                throw new EndOfStreamException();
            }

            skip -= skipped;
        }
    }

    /// <summary>
    /// Skip so many characters in a reader, and keep asking until they are all gone.
    /// </summary>
    /// <param name="reader">the reader</param>
    /// <param name="skip">how many characters</param>
    /// <exception cref="IOException">if the text ends before that</exception>
    public static void SkipFully(TextReader reader, long skip)
    {
        ArgumentNullException.ThrowIfNull(reader);
        while (skip > 0)
        {
            long skipped = reader.Read() < 0 ? 0 : 1;
            if (skipped <= 0)
            {
                throw new EndOfStreamException();
            }

            skip -= skipped;
        }
    }

    /// <summary>
    /// Read what is there into the buffer, up to its length or up to the end of the stream.
    /// </summary>
    /// <param name="input">the stream</param>
    /// <param name="buffer">where to read</param>
    /// <param name="max">the most to read</param>
    /// <returns>how many bytes were read</returns>
    public static int ReadFully(Stream input, byte[] buffer, int max)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(buffer);
        return ReadFully(input, buffer.AsSpan(), max);
    }

    /// <summary>
    /// Read what is there into the buffer, up to its length or up to the end of the stream.
    /// </summary>
    /// <param name="input">the stream</param>
    /// <param name="buffer">where to read</param>
    /// <param name="max">the most to read</param>
    /// <returns>how many bytes were read</returns>
    public static int ReadFully(Stream input, Span<byte> buffer, int max)
    {
        ArgumentNullException.ThrowIfNull(input);
        int result = 0;
        int len = Math.Min(max, buffer.Length);
        while (len > 0)
        {
            int read = input.Read(buffer.Slice(result, len));
            if (read <= 0)
            {
                break;
            }

            result += read;
            len -= read;
        }

        return result;
    }

    /// <summary>
    /// Read what is there into the buffer, up to its length or up to the end of the text.
    /// </summary>
    /// <param name="reader">the reader</param>
    /// <param name="buffer">where to read</param>
    /// <param name="max">the most to read</param>
    /// <returns>how many characters were read</returns>
    public static int ReadFully(TextReader reader, char[] buffer, int max)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(buffer);
        return ReadFully(reader, buffer.AsSpan(), max);
    }

    /// <summary>
    /// Read what is there into the buffer, up to its length or up to the end of the text.
    /// </summary>
    /// <param name="reader">the reader</param>
    /// <param name="buffer">where to read</param>
    /// <param name="max">the most to read</param>
    /// <returns>how many characters were read</returns>
    public static int ReadFully(TextReader reader, Span<char> buffer, int max)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int result = 0;
        int len = Math.Min(max, buffer.Length);
        while (len > 0)
        {
            int read = reader.Read(buffer.Slice(result, len));
            if (read <= 0)
            {
                break;
            }

            result += read;
            len -= read;
        }

        return result;
    }

    /// <summary>
    /// Copy what is there from one stream to another.
    /// </summary>
    /// <param name="input">where from</param>
    /// <param name="output">where to</param>
    /// <returns>how many bytes went</returns>
    public static long Copy(Stream input, Stream output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        return Copy(input, output, long.MaxValue);
    }

    /// <summary>
    /// Copy up to so many bytes from one stream to another.
    /// </summary>
    /// <param name="input">where from</param>
    /// <param name="output">where to</param>
    /// <param name="length">the most to copy, or a length that is not less than what is there</param>
    /// <returns>how many bytes went</returns>
    public static long Copy(Stream input, Stream output, long length)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        if (length <= 0)
        {
            return 0;
        }

        byte[] buffer = new byte[(int)Math.Min(Constants.IoBufferSize, length)];
        long copied = 0;
        while (length > 0)
        {
            int want = (int)Math.Min(buffer.Length, length);
            int read = input.Read(buffer, 0, want);
            if (read <= 0)
            {
                break;
            }

            output.Write(buffer, 0, read);
            copied += read;
            length -= read;
        }

        return copied;
    }

    /// <summary>
    /// Read a stream to its end and close it.
    /// </summary>
    /// <param name="input">the stream</param>
    /// <param name="length">how many bytes, or not more than that when it is not more than zero</param>
    /// <returns>the bytes</returns>
    public static byte[] ReadBytesAndClose(Stream input, int length)
    {
        ArgumentNullException.ThrowIfNull(input);
        try
        {
            if (length <= 0)
            {
                using MemoryStream outStream = new(Constants.IoBufferSize);
                _ = Copy(input, outStream);
                return outStream.ToArray();
            }

            // Exact size: the length is an upper bound the stream usually fills, so one
            // allocation does it. A short stream still ends correctly, through one more copy
            // on a path that means the input was malformed anyway.
            byte[] result = new byte[length];
            int copied = ReadFully(input, result, length);
            return copied == length ? result : result[..copied];
        }
        finally
        {
            CloseSilently(input.Dispose);
        }
    }

    /// <summary>
    /// Read text to its end and close it.
    /// </summary>
    /// <param name="reader">the reader</param>
    /// <param name="length">how many characters, or not more than that when it is not more than zero</param>
    /// <returns>the text</returns>
    public static string ReadStringAndClose(TextReader reader, int length)
    {
        ArgumentNullException.ThrowIfNull(reader);
        try
        {
            if (length <= 0)
            {
                length = int.MaxValue;
            }

            // Sized from the length when it is known: a builder that starts at 16 doubles
            // its way up, copying everything so far on each growth. The cap keeps a lying
            // length from preallocating the world; the buffer rents for the same reason the
            // map string reader rents its characters.
            StringBuilder text = new(Math.Min(length, 16 * Constants.IoBufferSize));
            char[] buffer = ArrayPool<char>.Shared.Rent(Math.Min(Constants.IoBufferSize, length));
            try
            {
                while (length > 0)
                {
                    int want = Math.Min(buffer.Length, length);
                    int read = reader.Read(buffer, 0, want);
                    if (read <= 0)
                    {
                        break;
                    }

                    _ = text.Append(buffer, 0, read);
                    length -= read;
                }
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }

            return text.ToString();
        }
        finally
        {
            CloseSilently(reader.Dispose);
        }
    }

    /// <summary>
    /// The encoding a stream of text is read and written in.
    /// </summary>
    /// <remarks>
    /// UTF-8 without a byte order mark. The reference builds its readers without naming an
    /// encoding at all, so it takes whatever the platform's default is, and on a modern JVM that is
    /// UTF-8; naming it here is the same thing without the ambiguity. What UTF-8 cannot do is
    /// unpaired surrogates, and for those <see cref="JavaUtf8"/> has the reference's answer, which
    /// is a question mark rather than the replacement character.
    /// </remarks>
    public static Encoding StreamEncoding { get; } = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);

    /// <summary>
    /// A reader over a stream, in <see cref="StreamEncoding"/>.
    /// </summary>
    /// <param name="input">the stream</param>
    /// <returns>the reader</returns>
    public static TextReader GetReader(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new StreamReader(input, StreamEncoding, detectEncodingFromByteOrderMarks: false);
    }

    /// <summary>
    /// A writer over a stream, in <see cref="StreamEncoding"/>.
    /// </summary>
    /// <param name="output">the stream</param>
    /// <returns>the writer</returns>
    public static TextWriter GetBufferedWriter(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return new StreamWriter(output, StreamEncoding, Constants.IoBufferSize, leaveOpen: true);
    }
}
