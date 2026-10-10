/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.store.DataReader.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The variable size number format code is a port from SQLite,
 * but stored in reverse order (least significant bits in the first byte).
 *
 * This is the reading half of what Data writes: a byte at a time, and a UTF-8 decode that
 * trusts the writer. The characters are decoded by hand rather than through a UTF-8 decoder
 * because the reference's decoder does not validate, and the difference is observable at the
 * end of the stream: a malformed sequence runs off the end of the input instead of being
 * replaced, and the caller sees the end of the stream rather than a substitution character.
 *
 * Two transliteration notes. ReadByte is signed (sbyte), and readVarInt's "b >= 0" test is
 * how the format says "last byte", so the sign is part of the reading, not an accident of
 * the type. And the reference's FastEOFException avoids filling in a stack trace; .NET has
 * no equivalent switch to throw, so the exception is EndOfStreamException with the usual
 * trace, which costs an allocation on a path the reference deliberately kept cheap.
 */
using System;
using System.IO;

namespace H2Sharp.Store;

/// <summary>
/// This class is backed by an input stream and supports reading values and
/// variable size data.
/// </summary>
public class DataReader : TextReader
{
    private readonly Stream stream;

    /// <summary>
    /// Create a new data reader.
    /// </summary>
    /// <param name="stream">the input stream</param>
    public DataReader(Stream stream)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>
    /// Reads the next character, as <see cref="TextReader"/> spells "no more input".
    /// </summary>
    /// <returns>the character, or -1 at the end of the stream</returns>
    public override int Read()
    {
        int x = stream.ReadByte();
        if (x < 0)
        {
            return -1;
        }

        return ReadChar(x);
    }

    /// <inheritdoc/>
    public override int Peek()
    {
        if (!stream.CanSeek)
        {
            throw new NotSupportedException("Peek needs a seekable stream; the reference reads ahead by one byte");
        }

        long position = stream.Position;
        int x = stream.ReadByte();
        stream.Position = position;
        return x;
    }

    /// <summary>
    /// Read a byte.
    /// </summary>
    /// <returns>the byte</returns>
    public sbyte ReadByte()
    {
        int x = stream.ReadByte();
        if (x < 0)
        {
            throw new EndOfStreamException();
        }

        return (sbyte)x;
    }

    /// <summary>
    /// Read a variable size integer.
    /// </summary>
    /// <returns>the value</returns>
    public int ReadVarInt()
    {
        sbyte b = ReadByte();
        if (b >= 0)
        {
            return b;
        }

        int x = b & 0x7f;
        b = ReadByte();
        if (b >= 0)
        {
            return x | (b << 7);
        }

        x |= (b & 0x7f) << 7;
        b = ReadByte();
        if (b >= 0)
        {
            return x | (b << 14);
        }

        x |= (b & 0x7f) << 14;
        b = ReadByte();
        if (b >= 0)
        {
            return x | b << 21;
        }

        return x | ((b & 0x7f) << 21) | (ReadByte() << 28);
    }

    /// <summary>
    /// Read one character from the input stream, given its first byte.
    /// </summary>
    /// <param name="first">the first byte, already read and known to be 0..255</param>
    /// <returns>the character</returns>
    private char ReadChar(int first)
    {
        int x = first & 0xff;
        if (x < 0x80)
        {
            return (char)x;
        }

        if (x >= 0xe0)
        {
            return (char)(((x & 0xf) << 12) + ((ReadByte() & 0x3f) << 6) + (ReadByte() & 0x3f));
        }

        return (char)(((x & 0x1f) << 6) + (ReadByte() & 0x3f));
    }

    /// <summary>
    /// Closes the reader. The reference closes nothing here, and neither does this: the
    /// stream belongs to whoever opened it.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        // ignore
    }

    /// <summary>
    /// Reads up to <paramref name="count"/> characters.
    /// </summary>
    /// <remarks>
    /// The partial answer is the point: a stream that ends halfway through a buffer gives
    /// back what it had rather than an error, and -1 only when it had nothing at all. The
    /// reference spells that by catching its own EOF exception; here the end of the stream is
    /// a return value, which is the same answer with one exception instead of two.
    /// </remarks>
    /// <param name="buffer">the buffer to read into</param>
    /// <param name="index">the offset to start at</param>
    /// <param name="count">the number of characters to read</param>
    /// <returns>the number of characters read, or -1 at the end of the stream</returns>
    public override int Read(char[] buffer, int index, int count)
    {
        if (count == 0)
        {
            return 0;
        }

        int i = 0;
        try
        {
            for (; i < count; i++)
            {
                int c = Read();
                if (c < 0)
                {
                    throw new EndOfStreamException();
                }

                buffer[index + i] = (char)c;
            }

            return count;
        }
        catch (EndOfStreamException)
        {
            if (i == 0)
            {
                return -1;
            }

            return i;
        }
    }
}
