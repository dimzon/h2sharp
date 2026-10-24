/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueBlob.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: the temporary-file and storage paths of the reference are not ported here, because they
 * need the LOB storage to have an implementation; what is here is the small case and everything a
 * value answers from it.
 */
using System;
using System.IO;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Store;
using H2Sharp.Util;
using H2Sharp.Values.Lobs;

namespace H2Sharp.Values;

/// <summary>
/// The BINARY LARGE OBJECT type: bytes, of a length the value keeps, wherever the bytes are.
/// </summary>
public sealed class ValueBlob : ValueLob
{
    /// <summary>
    /// A BLOB of bytes held somewhere else.
    /// </summary>
    /// <param name="lobData">where they are</param>
    /// <param name="octetLength">how many</param>
    public ValueBlob(LobData lobData, long octetLength)
        : base(lobData, octetLength, -1L)
    {
    }

    /// <summary>
    /// A BLOB kept in the value.
    /// </summary>
    /// <param name="data">the bytes, which are not copied and must not be changed afterwards</param>
    /// <returns>the value</returns>
    public static ValueBlob CreateSmall(byte[] data)
        => new(new LobDataInMemory(data), data.Length);



    /// <inheritdoc/>
    public override int GetValueType() => Value.Blob;

    /// <inheritdoc/>
    /// <remarks>
    /// The length in characters is not known until the bytes have been read as text, so it is
    /// remembered here: the first call decodes, and the rest are free.
    /// </remarks>
    public override string GetString()
    {
        long p = LobCharLength;
        if (p >= 0L)
        {
            if (p > Constants.MaxStringLength)
            {
                throw GetStringTooLong(p);
            }

            return ReadString((int)p);
        }

        // One character takes at most three bytes, so more than that many bytes cannot be a string.
        if (LobOctetLength > Constants.MaxStringLength * 3L)
        {
            throw GetStringTooLong(CharLength());
        }

        string text;
        if (LobData is LobDataInMemory)
        {
            text = IOUtils.StreamEncoding.GetString(((LobDataInMemory)LobData).GetSmall());
        }
        else
        {
            text = ReadString(int.MaxValue);
        }

        LobCharLength = p = text.Length;
        if (p > Constants.MaxStringLength)
        {
            throw GetStringTooLong(p);
        }

        return text;
    }

    /// <inheritdoc/>
    protected override byte[]? GetBytesInternal()
    {
        if (LobOctetLength > Constants.MaxStringLength)
        {
            throw GetBinaryTooLong(LobOctetLength);
        }

        return ReadBytes((int)LobOctetLength);
    }

    /// <summary>
    /// Truncates this value to the given number of octets, if it is longer.
    /// </summary>
    /// <remarks>
    /// Internal rather than private because <c>Value.ConvertTo</c> calls it for a cast: casting
    /// to a narrower BINARY keeps as much as fits, and this is where "keeps as much as fits" is
    /// decided. Only the in-memory case is answered, because a value held elsewhere would need
    /// the handler to write a new one.
    /// </remarks>
    /// <param name="precision">the number of octets to keep</param>
    /// <returns>this value, or a shorter one</returns>
    internal ValueBlob ConvertPrecision(long precision)
    {
        if (LobOctetLength <= precision)
        {
            return this;
        }

        return CreateSmall(IOUtils.ReadBytesAndClose(
            GetInputStream(), MathUtils.ConvertLongToInt(precision)));
    }

    /// <inheritdoc/>
    public override Stream GetInputStream() => LobData.GetInputStream(LobOctetLength);

    /// <inheritdoc/>
    public override Stream GetInputStream(long oneBasedOffset, long length)
        => RangeInputStream(LobData.GetInputStream(LobOctetLength), oneBasedOffset, length, LobOctetLength);

    /// <inheritdoc/>
    public override TextReader GetReader(long oneBasedOffset, long length)
        => RangeReader(GetReader(), oneBasedOffset, length, -1L);

    /// <inheritdoc/>
    public override int CompareTypeSafe(Value v, CompareMode mode, CastDataProvider? provider)
    {
        if (ReferenceEquals(v, this))
        {
            return 0;
        }

        ValueBlob other = (ValueBlob)v;
        LobData mine = LobData;
        LobData theirs = other.LobData;

        if (mine.GetType() == theirs.GetType())
        {
            // Two values held the same way are compared without reading either one, and two that
            // name the same thing in the storage are equal without being read at all.
            if (mine is LobDataInMemory)
            {
                return Math.Sign(CompareUnsigned(
                    ((LobDataInMemory)mine).GetSmall(), ((LobDataInMemory)theirs).GetSmall()));
            }
        }

        return Compare(this, other);
    }

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => Dialect.AppendBlob(builder, this, sqlFlags);

    /// <inheritdoc/>
    public override long CharLength()
    {
        long p = LobCharLength;
        if (p >= 0L)
        {
            return p;
        }

        if (LobData is LobDataInMemory inMemory)
        {
            p = IOUtils.StreamEncoding.GetString(inMemory.GetSmall()).Length;
        }
        else
        {
            using TextReader reader = GetReader();
            p = 0;
            for (int c; (c = reader.Read()) >= 0;)
            {
                p++;
            }
        }

        LobCharLength = p;
        return p;
    }

    /// <inheritdoc/>
    public override ValueLob Copy(DataHandler database, int tableId)
    {
        if (LobData is not LobDataInMemory)
        {
            throw DbException.GetUnsupportedException("Copying a large value needs the LOB storage");
        }

        return this;
    }

    /// <inheritdoc/>
    public override bool ContainsNull() => false;

    private static int Compare(ValueBlob one, ValueBlob other)
    {
        long minPrecision = Math.Min(one.LobOctetLength, other.LobOctetLength);
        try
        {
            using Stream oneStream = one.GetInputStream();
            using Stream otherStream = other.GetInputStream();
            // Small and short-lived: two stack buffers instead of two heap arrays on every
            // comparison. The frames are transient — freed on return, never held across calls.
            Span<byte> oneBuffer = stackalloc byte[BlockComparisonSize];
            Span<byte> otherBuffer = stackalloc byte[BlockComparisonSize];
            for (; minPrecision >= BlockComparisonSize; minPrecision -= BlockComparisonSize)
            {
                if (IOUtils.ReadFully(oneStream, oneBuffer, BlockComparisonSize) != BlockComparisonSize
                    || IOUtils.ReadFully(otherStream, otherBuffer, BlockComparisonSize) != BlockComparisonSize)
                {
                    throw DbException.GetUnsupportedException("Invalid LOB");
                }

                int comparison = Math.Sign(CompareUnsigned(oneBuffer, otherBuffer));
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            for (; ; )
            {
                int b1 = oneStream.ReadByte();
                int b2 = otherStream.ReadByte();
                if (b1 < 0)
                {
                    return b2 < 0 ? 0 : -1;
                }

                if (b2 < 0)
                {
                    return 1;
                }

                if (b1 != b2)
                {
                    return b1 < b2 ? -1 : 1;
                }
            }
        }
        catch (IOException e)
        {
            throw DbException.ConvertIOException(e, "Compare");
        }
    }

    /// <summary>
    /// Java's <c>Arrays.compareUnsigned</c>: the bytes as numbers, the bigger byte the later one.
    /// </summary>
    private static int CompareUnsigned(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        => a.SequenceCompareTo(b);
}
