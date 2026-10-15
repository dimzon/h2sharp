using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Store.FileSystem;
using H2Sharp.Util;

namespace H2Sharp.MvStore;

/// <summary>
/// Reading and writing the compact encodings the store is built from: variable length integers, UTF-8
/// strings that stop at a byte count, and the packed page positions.
/// </summary>
/// <remarks>
/// Two things here are worth reading before changing them.
/// <para>
/// The string encoding is hand-written and stops at a <em>character</em> count, not a byte count, and
/// it does not handle anything outside the basic multilingual plane. It is kept because the store
/// writes its own metadata with it and has to read back what the reference wrote.
/// </para>
/// <para>
/// A page position packs four things into one long: the chunk id in the top bits, an offset, a
/// length that is stored as a code rather than as a number, and a type in the lowest bit. The lowest
/// bit is the type, so a page position is always even, and that is what
/// <see cref="IsPageSaved(long)"/> asks about.
/// </para>
/// </remarks>
public static class DataUtils
{
    /// <summary>
    /// Error code for a read that failed.
    /// </summary>
    public const int ErrorReadingFailed = 1;

    /// <summary>
    /// Error code for a write that failed.
    /// </summary>
    public const int ErrorWritingFailed = 2;

    /// <summary>
    /// Error code for an internal problem.
    /// </summary>
    public const int ErrorInternal = 3;

    /// <summary>
    /// Error code for use after close.
    /// </summary>
    public const int ErrorClosed = 4;

    /// <summary>
    /// Error code for a format this build does not read.
    /// </summary>
    public const int ErrorUnsupportedFormat = 5;

    /// <summary>
    /// Error code for a file that does not hold what it should.
    /// </summary>
    public const int ErrorFileCorrupt = 6;

    /// <summary>
    /// Error code for a file locked by someone else.
    /// </summary>
    public const int ErrorFileLocked = 7;

    /// <summary>
    /// Error code for a value that would not serialize.
    /// </summary>
    public const int ErrorSerialization = 8;

    /// <summary>
    /// Error code for a chunk that is not there.
    /// </summary>
    public const int ErrorChunkNotFound = 9;

    /// <summary>
    /// Error code for a block that is not there.
    /// </summary>
    public const int ErrorBlockNotFound = 50;

    /// <summary>
    /// Error code for a corrupt transaction.
    /// </summary>
    public const int ErrorTransactionCorrupt = 100;

    /// <summary>
    /// Error code for a locked transaction.
    /// </summary>
    public const int ErrorTransactionLocked = 101;

    /// <summary>
    /// Error code for too many open transactions.
    /// </summary>
    public const int ErrorTooManyOpenTransactions = 102;

    /// <summary>
    /// Error code for a transaction used in a state it is not in.
    /// </summary>
    public const int ErrorTransactionIllegalState = 103;

    /// <summary>
    /// Error code for a transaction that grew too large.
    /// </summary>
    public const int ErrorTransactionTooBig = 104;

    /// <summary>
    /// Error code for transactions deadlocked against each other.
    /// </summary>
    public const int ErrorTransactionsDeadlock = 105;

    /// <summary>
    /// Error code for a value of a type that is not stored.
    /// </summary>
    public const int ErrorUnknownDataType = 106;

    /// <summary>
    /// A page holding values.
    /// </summary>
    public const int PageTypeLeaf = 0;

    /// <summary>
    /// A page holding page positions.
    /// </summary>
    public const int PageTypeNode = 1;

    /// <summary>
    /// A page whose contents were compressed.
    /// </summary>
    public const int PageCompressed = 2;

    /// <summary>
    /// A page whose contents were compressed, with high bits.
    /// </summary>
    public const int PageCompressedHigh = 2 + 4;

    /// <summary>
    /// The largest value a compressed variable length integer can carry.
    /// </summary>
    public const int CompressedVarIntMax = 0x1fffff;

    /// <summary>
    /// The largest value a compressed variable length long can carry.
    /// </summary>
    public const long CompressedVarLongMax = 0x1ffffffffffffL;

    /// <summary>
    /// The size above which a page is stored on its own.
    /// </summary>
    public const int PageLarge = 2 * 1024 * 1024;

    /// <summary>
    /// Layout prefix for a chunk.
    /// </summary>
    public const string LayoutChunk = "chunk.";

    /// <summary>
    /// Layout prefix for a root.
    /// </summary>
    public const string LayoutRoot = "root.";

    /// <summary>
    /// Meta key for the name.
    /// </summary>
    public const string MetaName = "name.";

    /// <summary>
    /// Meta key for a map.
    /// </summary>
    public const string MetaMap = "map.";

    /// <summary>
    /// How many bytes a variable length integer of this value takes.
    /// </summary>
    /// <param name="x">the value</param>
    /// <returns>the length in bytes</returns>
    public static int GetVarIntLen(int x)
    {
        if ((x & (-1 << 7)) == 0)
        {
            return 1;
        }

        if ((x & (-1 << 14)) == 0)
        {
            return 2;
        }

        if ((x & (-1 << 21)) == 0)
        {
            return 3;
        }

        if ((x & (-1 << 28)) == 0)
        {
            return 4;
        }

        return 5;
    }

    /// <summary>
    /// How many bytes a variable length long of this value takes.
    /// </summary>
    /// <param name="x">the value</param>
    /// <returns>the length in bytes</returns>
    public static int GetVarLongLen(long x)
    {
        int i = 1;
        while (true)
        {
            // The shift has to be the 64 bit one. Shifting a uint instead truncates the value to 32
            // bits, and every long above 2^32 then reports a length two bytes short.
            x = (long)((ulong)x >> 7);
            if (x == 0)
            {
                return i;
            }

            i++;
        }
    }

    /// <summary>
    /// Read a variable length integer.
    /// </summary>
    /// <param name="buff">the buffer</param>
    /// <returns>the value</returns>
    public static int ReadVarInt(Span<byte> buff, ref int position)
        => ReadVarInt((ReadOnlySpan<byte>)buff, ref position);

    /// <summary>
    /// Read a variable length integer.
    /// </summary>
    /// <param name="buff">the buffer</param>
    /// <returns>the value</returns>
    public static int ReadVarInt(ReadOnlySpan<byte> buff, ref int position)
    {
        int b = buff[position++];
        if (b < 0x80)
        {
            return b;
        }

        // a separate function so that this one can be inlined
        return ReadVarIntRest(buff, ref position, b);
    }

    private static int ReadVarIntRest(ReadOnlySpan<byte> buff, ref int position, int b)
    {
        int x = b & 0x7f;
        b = buff[position++];
        if (b < 0x80)
        {
            return x | (b << 7);
        }

        x |= (b & 0x7f) << 7;
        b = buff[position++];
        if (b < 0x80)
        {
            return x | (b << 14);
        }

        x |= (b & 0x7f) << 14;
        b = buff[position++];
        if (b < 0x80)
        {
            return x | b << 21;
        }

        x |= ((b & 0x7f) << 21) | (buff[position++] << 28);
        return x;
    }

    /// <summary>
    /// Read a variable length long.
    /// </summary>
    /// <param name="buff">the buffer</param>
    /// <returns>the value</returns>
    public static long ReadVarLong(Span<byte> buff, ref int position)
        => ReadVarLong((ReadOnlySpan<byte>)buff, ref position);

    /// <summary>
    /// Read a variable length long.
    /// </summary>
    /// <param name="buff">the buffer</param>
    /// <returns>the value</returns>
    public static long ReadVarLong(ReadOnlySpan<byte> buff, ref int position)
    {
        long x = buff[position++];
        if (x < 0x80)
        {
            return x;
        }

        x &= 0x7f;
        for (int s = 7; s < 64; s += 7)
        {
            long b = buff[position++];
            x |= (b & 0x7f) << s;
            if (b < 0x80)
            {
                break;
            }
        }

        return x;
    }

    /// <summary>
    /// Reads eight raw bytes as a long, big endian, advancing past them: the shape the row
    /// and object types both read fixed longs in.
    /// </summary>
    /// <param name="buff">the buffer</param>
    /// <param name="position">where to read, advanced past the value</param>
    /// <returns>the value</returns>
    public static long ReadFixedLong(Span<byte> buff, ref int position)
    {
        long result = BinaryPrimitives.ReadInt64BigEndian(buff.Slice(position, 8));
        position += 8;
        return result;
    }

    /// <summary>
    /// Write a variable length integer.
    /// </summary>
    /// <param name="out">where to write</param>
    /// <param name="x">the value</param>
    public static void WriteVarInt(Stream @out, int x)
    {
        while ((x & ~0x7f) != 0)
        {
            @out.WriteByte((byte)(x | 0x80));
            x = (int)((uint)x >>> 7);
        }

        @out.WriteByte((byte)x);
    }

    /// <summary>
    /// Write a variable length integer.
    /// </summary>
    /// <param name="buff">the buffer</param>
    /// <param name="position">where to write</param>
    /// <param name="x">the value</param>
    public static void WriteVarInt(Span<byte> buff, ref int position, int x)
    {
        while ((x & ~0x7f) != 0)
        {
            buff[position++] = (byte)(x | 0x80);
            x = (int)((uint)x >>> 7);
        }

        buff[position++] = (byte)x;
    }

    /// <summary>
    /// Write the first <paramref name="len"/> characters of a string as UTF-8.
    /// </summary>
    /// <remarks>
    /// The count is characters, not bytes, and nothing outside the basic multilingual plane is
    /// handled. Both are true of the reference: the store only ever writes its own metadata with
    /// this, and it has to read back exactly what the reference wrote.
    /// </remarks>
    /// <param name="buff">the buffer</param>
    /// <param name="position">where to write</param>
    /// <param name="s">the string</param>
    /// <param name="len">how many characters of it</param>
    public static void WriteStringData(Span<byte> buff, ref int position, string s, int len)
    {
        for (int i = 0; i < len; i++)
        {
            int c = s[i];
            if (c < 0x80)
            {
                buff[position++] = (byte)c;
            }
            else if (c >= 0x800)
            {
                buff[position++] = (byte)(0xe0 | (c >> 12));
                buff[position++] = (byte)((c >> 6) & 0x3f);
                buff[position++] = (byte)(c & 0x3f);
            }
            else
            {
                buff[position++] = (byte)(0xc0 | (c >> 6));
                buff[position++] = (byte)(c & 0x3f);
            }
        }
    }

    /// <summary>
    /// Read a string written as a variable length integer count followed by UTF-8.
    /// </summary>
    /// <param name="buff">the buffer</param>
    /// <param name="position">where to read</param>
    /// <returns>the string</returns>
    public static string ReadString(Span<byte> buff, ref int position) => ReadString(buff, ref position, ReadVarInt(buff, ref position));

    /// <summary>
    /// Read a string of the given number of characters.
    /// </summary>
    /// <param name="buff">the buffer</param>
    /// <param name="position">where to read</param>
    /// <param name="len">how many characters</param>
    /// <returns>the string</returns>
    public static string ReadString(Span<byte> buff, ref int position, int len)
    {
        char[] chars = new char[len];
        for (int i = 0; i < len; i++)
        {
            int x = buff[position++];
            if (x < 0x80)
            {
                chars[i] = (char)x;
            }
            else if (x >= 0xe0)
            {
                chars[i] = (char)(((x & 0xf) << 12)
                    + ((buff[position++] & 0x3f) << 6) + (buff[position++] & 0x3f));
            }
            else
            {
                chars[i] = (char)(((x & 0x1f) << 6) + (buff[position++] & 0x3f));
            }
        }

        return new string(chars);
    }

    /// <summary>
    /// Write a variable length long.
    /// </summary>
    /// <param name="buff">the buffer</param>
    /// <param name="position">where to write</param>
    /// <param name="x">the value</param>
    public static void WriteVarLong(Span<byte> buff, ref int position, long x)
    {
        while ((x & ~0x7fL) != 0)
        {
            buff[position++] = (byte)(x | 0x80);
            x = (long)((ulong)x >>> 7);
        }

        buff[position++] = (byte)x;
    }

    /// <summary>
    /// Write a variable length long.
    /// </summary>
    /// <param name="out">where to write</param>
    /// <param name="x">the value</param>
    public static void WriteVarLong(Stream @out, long x)
    {
        while ((x & ~0x7fL) != 0)
        {
            @out.WriteByte((byte)(x | 0x80));
            x = (long)((ulong)x >>> 7);
        }

        @out.WriteByte((byte)x);
    }

    /// <summary>
    /// Copy an array, leaving a gap at the given index.
    /// </summary>
    /// <typeparam name="T">the element type</typeparam>
    /// <param name="src">the source</param>
    /// <param name="dst">the destination, one longer than the source</param>
    /// <param name="oldSize">how much of the source there is</param>
    /// <param name="gapIndex">where the gap goes</param>
    public static void CopyWithGap<T>(T[] src, T[] dst, int oldSize, int gapIndex)
    {
        if (gapIndex > 0)
        {
            Array.Copy(src, 0, dst, 0, gapIndex);
        }

        if (gapIndex < oldSize)
        {
            Array.Copy(src, gapIndex, dst, gapIndex + 1, oldSize - gapIndex);
        }
    }

    /// <summary>
    /// Copy an array, leaving out the element at the given index.
    /// </summary>
    /// <typeparam name="T">the element type</typeparam>
    /// <param name="src">the source</param>
    /// <param name="dst">the destination, one shorter than the source</param>
    /// <param name="oldSize">how much of the source there is</param>
    /// <param name="removeIndex">which element to leave out</param>
    public static void CopyExcept<T>(T[] src, T[] dst, int oldSize, int removeIndex)
    {
        if (removeIndex > 0 && oldSize > 0)
        {
            Array.Copy(src, 0, dst, 0, removeIndex);
        }

        if (removeIndex < oldSize)
        {
            Array.Copy(src, removeIndex + 1, dst, removeIndex, oldSize - removeIndex - 1);
        }
    }

    /// <summary>
    /// Read until the buffer is full, or the file ends early.
    /// </summary>
    /// <remarks>
    /// The channel reads at the absolute position and leaves its own where it found it, as
    /// the reference's FileChannel does: concurrent reads need no lock between them, and the
    /// save-seek-use-restore this replaces needed one or interleaved readers garbled each
    /// other into phantom "file corrupted" errors.
    /// </remarks>
    /// <param name="file">the file</param>
    /// <param name="pos">where to read from</param>
    /// <param name="dst">the buffer to fill</param>
    /// <exception cref="EndOfStreamException">the file ended first</exception>
    public static void ReadFully(FileChannel file, long pos, Memory<byte> dst)
    {
        int read = 0;
        try
        {
            while (read < dst.Length)
            {
                int len = file.ReadAt(pos + read, dst.Span[read..]);
                if (len <= 0)
                {
                    throw new EndOfStreamException();
                }

                read += len;
            }
        }
        catch (IOException e)
        {
            long size;
            try
            {
                size = file.Length;
            }
            catch (IOException)
            {
                size = -1;
            }

            throw NewMvStoreException(
                ErrorReadingFailed,
                "Reading from file {0} failed at {1} (length {2}), read {3}, remaining {4}",
                file, pos, size, dst.Length, 0, e);
        }
    }

    /// <summary>
    /// Write everything.
    /// </summary>
    /// <remarks>
    /// One positional write: the channel takes the whole buffer or throws, and concurrent
    /// writers need no lock between them.
    /// </remarks>
    /// <param name="file">the file</param>
    /// <param name="pos">where to write</param>
    /// <param name="src">what to write</param>
    public static void WriteFully(FileChannel file, long pos, ReadOnlyMemory<byte> src)
    {
        try
        {
            file.WriteAt(pos, src.Span);
        }
        catch (IOException e)
        {
            throw NewMvStoreException(
                ErrorWritingFailed,
                "Writing to {0} failed; length {1} at {2}",
                file, src.Length, pos, e);
        }
    }

    /// <summary>
    /// The code a length is stored as.
    /// </summary>
    /// <remarks>
    /// The bits of the code say how many bits of the length are significant and whether the top one is
    /// set, so the length comes back rounded up to a power of two. That is what makes it fit in five
    /// bits of a page position.
    /// <para>
    /// The reference carries a slower version of this in a comment, kept here because it is the
    /// clearest statement of what the code means.
    /// </para>
    /// </remarks>
    /// <param name="len">the length</param>
    /// <returns>the code</returns>
    public static int EncodeLength(int len)
    {
        System.Diagnostics.Debug.Assert(len >= 0);
        if (len <= 32)
        {
            return 0;
        }

        int code = System.Numerics.BitOperations.LeadingZeroCount((uint)len);
        int remaining = len << (code + 1);
        code += code;
        if ((remaining & (1 << 31)) != 0)
        {
            code--;
        }

        if ((remaining << 1) != 0)
        {
            code--;
        }

        // alternative code (slower):
        // int x = len;
        // int shift = 0;
        // while (x > 3) {
        //    shift++;
        //    x = (x >>> 1) + (x & 1);
        // }
        // shift = Math.Max(0,  shift - 4);
        // int code = (shift << 1) + (x & 1);
        // code = Math.min(31, code);
        return Math.Min(31, 52 - code);
    }

    /// <summary>
    /// The chunk a page position points into.
    /// </summary>
    /// <param name="pos">the page position</param>
    /// <returns>the chunk id</returns>
    public static int GetPageChunkId(long pos) => (int)((ulong)pos >> 38);

    /// <summary>
    /// The map a table of contents element belongs to.
    /// </summary>
    /// <param name="tocElement">the element</param>
    /// <returns>the map id</returns>
    public static int GetPageMapId(long tocElement) => (int)((ulong)tocElement >> 38);

    /// <summary>
    /// The largest length a page at this position could have.
    /// </summary>
    /// <param name="pos">the page position</param>
    /// <returns>the length</returns>
    public static int GetPageMaxLength(long pos) => DecodePageLength((int)((pos >> 1) & 31));

    /// <summary>
    /// The length a length code stands for.
    /// </summary>
    /// <param name="code">the code</param>
    /// <returns>the length</returns>
    public static int DecodePageLength(int code)
    {
        if (code == 31)
        {
            return PageLarge;
        }

        return (2 + (code & 1)) << ((code >> 1) + 4);
    }

    /// <summary>
    /// The offset within the chunk.
    /// </summary>
    /// <param name="tocElement">the element</param>
    /// <returns>the offset</returns>
    public static int GetPageOffset(long tocElement) => (int)(tocElement >> 6);

    /// <summary>
    /// Whether a page position points at a leaf or at a node.
    /// </summary>
    /// <param name="pos">the page position</param>
    /// <returns>the page type</returns>
    public static int GetPageType(long pos) => ((int)pos) & 1;

    /// <summary>
    /// Whether a page position points at a leaf.
    /// </summary>
    /// <param name="pos">the page position</param>
    /// <returns>true for a leaf</returns>
    public static bool IsLeafPosition(long pos) => GetPageType(pos) == PageTypeLeaf;

    /// <summary>
    /// Whether a page position points at a page that exists.
    /// </summary>
    /// <remarks>
    /// The type is in the lowest bit, so every real position is even and a cleared low bit means the
    /// page was never written.
    /// </remarks>
    /// <param name="pos">the page position</param>
    /// <returns>true if the page was saved</returns>
    public static bool IsPageSaved(long pos) => (pos & ~1L) != 0;

    /// <summary>
    /// Whether a page position is the one that stands for "removed".
    /// </summary>
    /// <param name="pos">the page position</param>
    /// <returns>true if removed</returns>
    internal static bool IsPageRemoved(long pos) => pos == 1L;

    /// <summary>
    /// Pack a page position out of its parts.
    /// </summary>
    /// <param name="chunkId">the chunk</param>
    /// <param name="offset">the offset within it</param>
    /// <param name="length">the length</param>
    /// <param name="type">the page type</param>
    /// <returns>the page position</returns>
    public static long ComposePagePos(int chunkId, int offset, int length, int type)
    {
        System.Diagnostics.Debug.Assert(offset >= 0);
        System.Diagnostics.Debug.Assert(type == PageTypeLeaf || type == PageTypeNode);
        long pos = (long)chunkId << 38;
        pos |= (long)offset << 6;
        pos |= (long)EncodeLength(length) << 1;
        pos |= (uint)type;
        return pos;
    }

    /// <summary>
    /// Move a page position into another chunk.
    /// </summary>
    /// <param name="chunkId">the chunk</param>
    /// <param name="tocElement">the element to move</param>
    /// <returns>the page position</returns>
    public static long ComposePagePos(int chunkId, long tocElement)
        => (tocElement & 0x3FFFFFFFFFL) | ((long)chunkId << 38);

    /// <summary>
    /// Pack a table of contents element out of its parts.
    /// </summary>
    /// <param name="mapId">the map</param>
    /// <param name="offset">the offset</param>
    /// <param name="length">the length</param>
    /// <param name="type">the page type</param>
    /// <returns>the element</returns>
    public static long ComposeTocElement(int mapId, int offset, int length, int type)
    {
        System.Diagnostics.Debug.Assert(mapId >= 0);
        System.Diagnostics.Debug.Assert(offset >= 0);
        System.Diagnostics.Debug.Assert(type == PageTypeLeaf || type == PageTypeNode);
        long pos = (long)mapId << 38;
        pos |= (long)offset << 6;
        pos |= (long)EncodeLength(length) << 1;
        pos |= (uint)type;
        return pos;
    }

    /// <summary>
    /// A checksum short for an int.
    /// </summary>
    /// <param name="x">the value</param>
    /// <returns>the check value</returns>
    public static short GetCheckValue(int x) => (short)((x >> 16) ^ x);

    /// <summary>
    /// Append a whole map, sorted by key.
    /// </summary>
    /// <param name="buff">where to append</param>
    /// <param name="map">the map, whose values may be boxed numbers</param>
    /// <returns>the builder</returns>
    public static StringBuilder AppendMap(StringBuilder buff, IReadOnlyDictionary<string, object?> map)
    {
        foreach (string key in map.Keys.Order(StringComparer.Ordinal))
        {
            object? value = map[key];
            switch (value)
            {
                case long l:
                    AppendMap(buff, key, l);
                    break;

                case int i:
                    AppendMap(buff, key, i);
                    break;

                default:
                    AppendMap(buff, key, value?.ToString() ?? string.Empty);
                    break;
            }
        }

        return buff;
    }

    private static StringBuilder AppendMapKey(StringBuilder buff, string key)
    {
        if (buff.Length > 0)
        {
            _ = buff.Append(',');
        }

        return _ = buff.Append(key).Append(':');
    }

    /// <summary>
    /// Append one entry with a string value.
    /// </summary>
    /// <param name="buff">where to append</param>
    /// <param name="key">the key</param>
    /// <param name="value">the value</param>
    public static void AppendMap(StringBuilder buff, string key, string value)
    {
        _ = AppendMapKey(buff, key);
        if (value.IndexOf(',') < 0 && value.IndexOf('"') < 0)
        {
            _ = buff.Append(value);
        }
        else
        {
            _ = buff.Append('"');
            for (int i = 0, size = value.Length; i < size; i++)
            {
                char c = value[i];
                if (c == '"')
                {
                    _ = buff.Append('\\');
                }

                _ = buff.Append(c);
            }

            _ = buff.Append('"');
        }
    }

    /// <summary>
    /// Append one entry with a long value, in hex.
    /// </summary>
    /// <param name="buff">where to append</param>
    /// <param name="key">the key</param>
    /// <param name="value">the value</param>
    public static void AppendMap(StringBuilder buff, string key, long value)
        => _ = AppendMapKey(buff, key).Append(value.ToString("x", CultureInfo.InvariantCulture));

    /// <summary>
    /// Append one entry with an int value, in hex.
    /// </summary>
    /// <param name="buff">where to append</param>
    /// <param name="key">the key</param>
    /// <param name="value">the value</param>
    public static void AppendMap(StringBuilder buff, string key, int value)
        => _ = AppendMapKey(buff, key).Append(value.ToString("x", CultureInfo.InvariantCulture));

    private static int ParseMapValue(StringBuilder buff, string s, int i, int size)
    {
        while (i < size)
        {
            char c = s[i++];
            if (c == ',')
            {
                break;
            }

            if (c == '"')
            {
                while (i < size)
                {
                    c = s[i++];
                    if (c == '\\')
                    {
                        if (i == size)
                        {
                            throw NewMvStoreException(ErrorFileCorrupt, "Not a map: {0}", s);
                        }

                        c = s[i++];
                    }
                    else if (c == '"')
                    {
                        break;
                    }

                    _ = buff.Append(c);
                }
            }
            else
            {
                _ = buff.Append(c);
            }
        }

        return i;
    }

    /// <summary>
    /// Parse a map out of its text form.
    /// </summary>
    /// <param name="s">the text</param>
    /// <returns>the map</returns>
    public static Dictionary<string, string> ParseMap(string s)
    {
        Dictionary<string, string> map = [];
        StringBuilder buff = new();
        for (int i = 0, size = s.Length; i < size;)
        {
            int startKey = i;
            i = s.IndexOf(':', i);
            if (i < 0)
            {
                throw NewMvStoreException(ErrorFileCorrupt, "Not a map: {0}", s);
            }

            string key = s[startKey..i];
            i++;
            i = ParseMapValue(buff, s, i, size);
            map[key] = buff.ToString();
            _ = buff.Clear();
        }

        return map;
    }

    /// <summary>
    /// Parse a map that carries its own checksum, and check it.
    /// </summary>
    /// <remarks>
    /// A map whose checksum does not match is not a map this store wrote, and the answer is null
    /// rather than a refusal: the caller has a newer file, not a broken one.
    /// </remarks>
    /// <param name="bytes">the bytes</param>
    /// <returns>the map, or null</returns>
    internal static Dictionary<string, string>? ParseChecksummedMap(ReadOnlySpan<byte> bytes)
    {
        int start = 0, end = bytes.Length;
        while (start < end && bytes[start] <= ' ')
        {
            start++;
        }

        while (start < end && bytes[end - 1] <= ' ')
        {
            end--;
        }

        string s = Encoding.Latin1.GetString(bytes[start..end]);
        Dictionary<string, string> map = [];
        StringBuilder buff = new();
        for (int i = 0, size = s.Length; i < size;)
        {
            int startKey = i;
            i = s.IndexOf(':', i);
            if (i < 0)
            {
                // Corrupted map
                return null;
            }

            if (i - startKey == 8 && string.CompareOrdinal(s, startKey, "fletcher", 0, 8) == 0)
            {
                _ = ParseMapValue(buff, s, i + 1, size);
                int check = (int)long.Parse(buff.ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return check == GetFletcher32(bytes.Slice(start, startKey - 1)) ? map : null;
            }

            string key = s[startKey..i];
            i++;
            i = ParseMapValue(buff, s, i, size);
            map[key] = buff.ToString();
            _ = buff.Clear();
        }

        // Corrupted map
        return null;
    }

    /// <summary>
    /// The name in a map's text form.
    /// </summary>
    /// <param name="s">the text</param>
    /// <returns>the name</returns>
    public static string? GetMapName(string s) => GetFromMap(s, "name");

    /// <summary>
    /// One value out of a map's text form.
    /// </summary>
    /// <param name="s">the text</param>
    /// <param name="key">the key</param>
    /// <returns>the value, or null</returns>
    public static string? GetFromMap(string s, string key)
    {
        int keyLength = key.Length;
        for (int i = 0, size = s.Length; i < size;)
        {
            int startKey = i;
            i = s.IndexOf(':', i);
            if (i < 0)
            {
                throw NewMvStoreException(ErrorFileCorrupt, "Not a map: {0}", s);
            }

            i++;
            if (i - startKey - 1 == keyLength && string.CompareOrdinal(s, startKey, key, 0, keyLength) == 0)
            {
                StringBuilder buff = new();
                _ = ParseMapValue(buff, s, i, size);
                return buff.ToString();
            }

            while (i < size)
            {
                char c = s[i++];
                if (c == ',')
                {
                    break;
                }

                if (c == '"')
                {
                    while (i < size)
                    {
                        c = s[i++];
                        if (c == '\\')
                        {
                            if (i++ == size)
                            {
                                throw NewMvStoreException(ErrorFileCorrupt, "Not a map: {0}", s);
                            }
                        }
                        else if (c == '"')
                        {
                            break;
                        }
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The Fletcher-32 checksum of a range of bytes.
    /// </summary>
    /// <remarks>
    /// The sums are reduced every 360 words so that neither can overflow an int over a long file, and
    /// an odd length has a zero byte appended before the last reduction.
    /// </remarks>
    /// <param name="bytes">the bytes</param>
    /// <param name="offset">where to start</param>
    /// <param name="length">how many</param>
    /// <returns>the checksum</returns>
    public static int GetFletcher32(byte[] bytes, int offset, int length)
        => GetFletcher32(new ReadOnlySpan<byte>(bytes, offset, length));

    /// <summary>
    /// The Fletcher-32 checksum of the bytes.
    /// </summary>
    /// <remarks>
    /// The sums are reduced every 360 words so that neither can overflow an int over a long file, and
    /// an odd length has a zero byte appended before the last reduction.
    /// </remarks>
    /// <param name="bytes">the bytes</param>
    /// <returns>the checksum</returns>
    public static int GetFletcher32(ReadOnlySpan<byte> bytes)
    {
        int length = bytes.Length;
        int s1 = 0xffff, s2 = 0xffff;
        int i = 0, len = length & ~1;
        while (i < len)
        {
            // reduce after 360 words (each word is two bytes)
            for (int end = Math.Min(i + 720, len); i < end;)
            {
                int x = (bytes[i++] << 8) | bytes[i++];
                s2 += s1 += x;
            }

            s1 = (s1 & 0xffff) + (s1 >> 16);
            s2 = (s2 & 0xffff) + (s2 >> 16);
        }

        if ((length & 1) != 0)
        {
            // odd length: append 0
            int x = bytes[i] << 8;
            s2 += s1 += x;
        }

        s1 = (s1 & 0xffff) + (s1 >> 16);
        s2 = (s2 & 0xffff) + (s2 >> 16);
        return (s2 << 16) | s1;
    }

    /// <summary>
    /// Fail with an argument error unless the test holds.
    /// </summary>
    /// <param name="test">the test</param>
    /// <param name="message">the message</param>
    /// <param name="arguments">the message arguments</param>
    public static void CheckArgument(bool test, string message, params object?[] arguments)
    {
        if (!test)
        {
            throw NewIllegalArgumentException(message, arguments);
        }
    }

    /// <summary>
    /// An argument error with the message already formatted.
    /// </summary>
    /// <param name="message">the message</param>
    /// <param name="arguments">the message arguments</param>
    /// <returns>the exception, for a throw</returns>
    public static ArgumentException NewIllegalArgumentException(string message, params object?[] arguments)
        => InitCause(new ArgumentException(FormatMessage(0, message, arguments)), arguments);

    /// <summary>
    /// An unsupported operation error with the message already formatted.
    /// </summary>
    /// <param name="message">the message</param>
    /// <returns>the exception, for a throw</returns>
    public static NotSupportedException NewUnsupportedOperationException(string message)
        => new(FormatMessage(0, message));

    /// <summary>
    /// A store error with the message already formatted.
    /// </summary>
    /// <param name="errorCode">the error code</param>
    /// <param name="message">the message</param>
    /// <param name="arguments">the message arguments</param>
    /// <returns>the exception, for a throw</returns>
    public static MvStoreException NewMvStoreException(int errorCode, string message, params object?[] arguments)
        => InitCause(new MvStoreException(errorCode, FormatMessage(errorCode, message, arguments)), arguments);

    /// <summary>
    /// Set the cause, if the last argument is one.
    /// </summary>
    /// <remarks>
    /// The reference passes the cause among the message arguments and picks it off the end, which
    /// reads as a mistake until you notice it is deliberate: the message keeps the exception's text
    /// and the exception keeps the cause.
    /// </remarks>
    /// <typeparam name="T">the exception type</typeparam>
    /// <param name="e">the exception</param>
    /// <param name="arguments">the message arguments</param>
    /// <returns>the same exception</returns>
    private static T InitCause<T>(T e, params object?[] arguments)
        where T : Exception
    {
        int size = arguments.Length;
        if (size > 0 && arguments[size - 1] is Exception cause)
        {
            e.Data["InnerException"] = cause;
        }

        return e;
    }

    /// <summary>
    /// Format a store message, with the version and the error code appended.
    /// </summary>
    /// <remarks>
    /// Arguments are converted to strings first, so that the result does not depend on the locale,
    /// and a very long one is cut: a value that goes into a message can be a whole page.
    /// </remarks>
    /// <param name="errorCode">the error code</param>
    /// <param name="message">the message</param>
    /// <param name="arguments">the message arguments</param>
    /// <returns>the message</returns>
    public static string FormatMessage(int errorCode, string message, params object?[] arguments)
    {
        // convert arguments to strings, to avoid locale specific formatting
        object?[] copy = (object?[])arguments.Clone();
        for (int i = 0; i < copy.Length; i++)
        {
            object? a = copy[i];
            if (a is not Exception)
            {
                string s = a?.ToString() ?? "null";
                if (s.Length > 1000)
                {
                    s = string.Concat(s.AsSpan(0, 1000), "...");
                }

                copy[i] = s;
            }
        }

        return ErrorMessages.Format(message, copy)
            + " [" + Constants.VersionMajor.ToString(CultureInfo.InvariantCulture) + "."
            + Constants.VersionMinor.ToString(CultureInfo.InvariantCulture) + "."
            + Constants.BuildId.ToString(CultureInfo.InvariantCulture)
            + "/" + errorCode.ToString(CultureInfo.InvariantCulture) + "]";
    }

    /// <summary>
    /// A value out of a map of mixed types, or null.
    /// </summary>
    /// <remarks>
    /// Not <c>GetValueOrDefault</c>: that is an extension on <c>IReadOnlyDictionary</c>, the reference
    /// takes a plain <c>Map</c>, and the extension is not reachable from
    /// <see cref="IDictionary{TKey, TValue}"/> on one of the two targets this assembly builds for.
    /// </remarks>
    /// <param name="map">the map</param>
    /// <param name="key">the key</param>
    /// <returns>the value, or null</returns>
    private static object? Get(IDictionary<string, object?> map, string key)
        => map.TryGetValue(key, out object? value) ? value : null;

    /// <summary>
    /// Read a hex long out of a map of mixed values.
    /// </summary>
    /// <param name="map">the map</param>
    /// <param name="key">the key</param>
    /// <param name="defaultValue">what to answer when the key is not there</param>
    /// <returns>the value</returns>
    public static long ReadHexLong(IDictionary<string, object?> map, string key, long defaultValue)
    {
        object? v = Get(map, key);
        return v switch
        {
            null => defaultValue,
            long l => l,
            _ => ParseHexLong((string)v),
        };
    }

    /// <summary>
    /// Parse a hex long.
    /// </summary>
    /// <param name="x">the text</param>
    /// <returns>the value</returns>
    public static long ParseHexLong(string x)
    {
        try
        {
            if (x.Length == 16)
            {
                // avoid problems with overflow
                return (long.Parse(x[..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture) << 32)
                    | long.Parse(x[8..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            return long.Parse(x, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        catch (FormatException e)
        {
            throw NewMvStoreException(ErrorFileCorrupt, "Error parsing the value {0}", x, e);
        }
    }

    /// <summary>
    /// Parse a hex int.
    /// </summary>
    /// <param name="x">the text</param>
    /// <returns>the value</returns>
    public static int ParseHexInt(string x)
    {
        try
        {
            // avoid problems with overflow
            return (int)long.Parse(x, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        catch (FormatException e)
        {
            throw NewMvStoreException(ErrorFileCorrupt, "Error parsing the value {0}", x, e);
        }
    }

    /// <summary>
    /// Read a hex int out of a map of mixed values, supporting the unsigned range.
    /// </summary>
    /// <param name="map">the map</param>
    /// <param name="key">the key</param>
    /// <param name="defaultValue">what to answer when the key is not there</param>
    /// <returns>the value</returns>
    internal static int ReadHexInt(IDictionary<string, object?> map, string key, int defaultValue)
    {
        object? v = Get(map, key);
        if (v is null)
        {
            return defaultValue;
        }

        if (v is int i)
        {
            return i;
        }

        try
        {
            // support unsigned hex value
            return (int)long.Parse((string)v, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        catch (FormatException e)
        {
            throw NewMvStoreException(ErrorFileCorrupt, "Error parsing the value {0}", v, e);
        }
    }

    /// <summary>
    /// Read hex bytes out of a map.
    /// </summary>
    /// <param name="map">the map</param>
    /// <param name="key">the key</param>
    /// <returns>the bytes, or null</returns>
    internal static byte[]? ParseHexBytes(IDictionary<string, object?> map, string key)
    {
        object? v = Get(map, key);

        // The cast is deliberate. The reference casts and raises ClassCastException when the value is
        // not a string, and a map holding a number under a key someone then reads as hex is a mistake
        // worth reporting rather than answering "no bytes" about.
        return v is null ? null : StringUtils.ConvertHexToBytes((string)v);
    }

    /// <summary>
    /// Read a configuration parameter, which may have been stored as a number or as text.
    /// </summary>
    /// <param name="config">the configuration</param>
    /// <param name="key">the key</param>
    /// <param name="defaultValue">what to answer when there is nothing usable</param>
    /// <returns>the value</returns>
    internal static int GetConfigParam(IDictionary<string, object?> config, string key, int defaultValue)
    {
        object? o = Get(config, key);
        if (o is not null)
        {
            // Java asks `instanceof Number`, and in .NET every type is IConvertible, string
            // included, so the test has to be spelled out: a string is not a number here and goes to
            // the text branch.
            switch (o)
            {
                case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                    return Convert.ToInt32(o, CultureInfo.InvariantCulture);

                default:
                    try
                    {
                        return DecodeInteger(o.ToString() ?? string.Empty);
                    }
                    catch (FormatException)
                    {
                        // ignore
                    }

                    break;
            }
        }

        return defaultValue;
    }

    /// <summary>
    /// <c>Integer.decode</c>, which .NET has no equivalent of.
    /// </summary>
    /// <remarks>
    /// A call-through, not a second implementation: <see cref="Utils.TryParseInt"/> follows the
    /// JDK branch for branch (including the lone-"0"-stays-decimal guard this method used to
    /// miss, and the sign handling that used to reject every "#..."), so this only translates
    /// its boolean into the throw the caller catches.
    /// </remarks>
    /// <param name="s">the text</param>
    /// <returns>the value</returns>
    /// <exception cref="FormatException">the text is not a number in any of those radixes</exception>
    private static int DecodeInteger(string s)
    {
        if (Utils.TryParseInt(s, out int value))
        {
            return value;
        }

        throw new FormatException("For input string: \"" + s + "\"");
    }

    /// <summary>
    /// Turn any failure into an IO exception, unwrapping the wrapper the JDBC layer puts around a
    /// database exception first.
    /// </summary>
    /// <remarks>
    /// The reference unwraps a <c>JdbcException</c> and this unwraps <see cref="H2SharpException"/>,
    /// which is what the JDBC layer raises here. The reason for the step is the same either way: the
    /// wrapper's message describes the connection, not the failure.
    /// </remarks>
    /// <param name="e">the root cause</param>
    /// <returns>the IO exception</returns>
    public static IOException ConvertToIoException(Exception e)
    {
        if (e is IOException io)
        {
            return io;
        }

        if (e is H2SharpException && e.InnerException is not null)
        {
            e = e.InnerException;
        }

        return new IOException(e.ToString(), e);
    }
}
