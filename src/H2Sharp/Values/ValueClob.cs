/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueClob.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: as with ValueBlob, the paths that write into a file or into the storage are not here.
 */
using System;
using System.IO;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Store;
using H2Sharp.Util;
using H2Sharp.Values.Lobs;

namespace H2Sharp.Values;

/// <summary>
/// The CHARACTER LARGE OBJECT type: text, of a length in characters the value keeps, encoded as
/// bytes wherever they are.
/// </summary>
public sealed class ValueClob : ValueLob
{
    /// <summary>
    /// A CLOB kept in the value, with its length in characters not yet known.
    /// </summary>
    /// <param name="data">the bytes, which are not copied and must not be changed afterwards</param>
    /// <returns>the value</returns>
    public static ValueClob CreateSmall(byte[] data)
        => new(new LobDataInMemory(data), data.Length, -1L);

    /// <summary>
    /// A CLOB kept in the value, whose length in characters is already known.
    /// </summary>
    /// <param name="data">the bytes, which are not copied and must not be changed afterwards</param>
    /// <param name="charLength">how many characters they are</param>
    /// <returns>the value</returns>
    public static ValueClob CreateSmall(byte[] data, long charLength)
        => new(new LobDataInMemory(data), data.Length, charLength);

    /// <summary>
    /// A CLOB kept in the value.
    /// </summary>
    /// <param name="value">the text</param>
    /// <returns>the value</returns>
    public static ValueClob CreateSmall(string value) => CreateSmall(IOUtils.StreamEncoding.GetBytes(value), value.Length);

    /// <summary>
    /// A CLOB of text held somewhere else.
    /// </summary>
    /// <param name="lobData">where the bytes are</param>
    /// <param name="octetLength">how many bytes</param>
    /// <param name="charLength">how many characters, or -1</param>
    /// <returns>the value</returns>
    public ValueClob(LobData lobData, long octetLength, long charLength)
        : base(lobData, octetLength, charLength)
    {
    }

    /// <inheritdoc/>
    public override int GetValueType() => Value.Clob;

    /// <inheritdoc/>
    public override string GetString()
    {
        if (LobCharLength > Constants.MaxStringLength)
        {
            throw GetStringTooLong(LobCharLength);
        }

        if (LobData is LobDataInMemory inMemory)
        {
            return IOUtils.StreamEncoding.GetString(inMemory.GetSmall());
        }

        return ReadString((int)LobCharLength);
    }

    /// <inheritdoc/>
    protected override byte[]? GetBytesInternal()
    {
        long p = LobOctetLength;
        if (p >= 0L)
        {
            if (p > Constants.MaxStringLength)
            {
                throw GetBinaryTooLong(p);
            }

            return ReadBytes((int)p);
        }

        if (LobOctetLength > Constants.MaxStringLength)
        {
            throw GetBinaryTooLong(OctetLength());
        }

        byte[] bytes = ReadBytes(int.MaxValue);
        LobOctetLength = p = bytes.Length;
        if (p > Constants.MaxStringLength)
        {
            throw GetBinaryTooLong(p);
        }

        return bytes;
    }

    /// <summary>
    /// Truncates this value to the given number of characters, if it is longer.
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="ValueBlob.ConvertPrecision"/> for characters rather than
    /// octets, and internal for the same reason: a cast to a narrower CLOB keeps as many
    /// characters as fit, and only the in-memory case can answer that without a handler.
    /// </remarks>
    /// <param name="precision">the number of characters to keep</param>
    /// <returns>this value, or a shorter one</returns>
    internal ValueClob ConvertPrecision(long precision)
    {
        if (LobCharLength <= precision)
        {
            return this;
        }

        return CreateSmall(IOUtils.ReadStringAndClose(
            GetReader(), MathUtils.ConvertLongToInt(precision)));
    }

    /// <inheritdoc/>
    public override Stream GetInputStream() => LobData.GetInputStream(-1L);

    /// <inheritdoc/>
    public override Stream GetInputStream(long oneBasedOffset, long length)
        => RangeInputStream(LobData.GetInputStream(-1L), oneBasedOffset, length, -1L);

    /// <inheritdoc/>
    public override TextReader GetReader(long oneBasedOffset, long length)
        => RangeReader(GetReader(), oneBasedOffset, length, LobCharLength);

    /// <inheritdoc/>
    public override int CompareTypeSafe(Value v, CompareMode mode, CastDataProvider? provider)
    {
        if (ReferenceEquals(v, this))
        {
            return 0;
        }

        ValueClob other = (ValueClob)v;
        LobData mine = LobData;
        LobData theirs = other.LobData;

        if (mine.GetType() == theirs.GetType() && mine is LobDataInMemory)
        {
            // Two values held the same way are compared as the text they are, which is not the same
            // thing as comparing their bytes: the same text in two encodings is one value.
            return Math.Sign(string.CompareOrdinal(GetString(), other.GetString()));
        }

        return Compare(this, other);
    }

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        if ((sqlFlags & HasSql.ReplaceLobsForTrace) != 0
            && (LobData is not LobDataInMemory || LobCharLength > SysProperties.MaxTraceDataLength))
        {
            // The text is not put into a trace: how long it is and where it is will do.
            _ = builder.Append("SPACE(").Append(LobCharLength);
            FormatLobDataComment(builder);
        }
        else if ((sqlFlags & (HasSql.ReplaceLobsForTrace | HasSql.NoCasts)) == 0)
        {
            _ = StringUtils.QuoteStringSql(_ = builder.Append("CAST("), GetString())
                .Append(" AS CHARACTER LARGE OBJECT(")
                .Append(LobCharLength)
                .Append("))");
        }
        else
        {
            _ = StringUtils.QuoteStringSql(builder, GetString());
        }

        return builder;
    }

    /// <inheritdoc/>
    public override long CharLength() => LobCharLength;

    /// <inheritdoc/>
    /// <remarks>
    /// The length in bytes is not known until the text has been encoded, so it is remembered here.
    /// </remarks>
    public override long OctetLength()
    {
        long p = LobOctetLength;
        if (p >= 0L)
        {
            return p;
        }

        byte[]? bytes = GetBytesInternal();
        p = LobOctetLength = bytes?.Length ?? 0;
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

    private static int Compare(ValueClob one, ValueClob other)
    {
        long minPrecision = Math.Min(one.LobCharLength, other.LobCharLength);
        try
        {
            using TextReader oneReader = one.GetReader();
            using TextReader otherReader = other.GetReader();
            char[] oneBuffer = new char[BlockComparisonSize];
            char[] otherBuffer = new char[BlockComparisonSize];
            for (; minPrecision >= BlockComparisonSize; minPrecision -= BlockComparisonSize)
            {
                if (IOUtils.ReadFully(oneReader, oneBuffer, BlockComparisonSize) != BlockComparisonSize
                    || IOUtils.ReadFully(otherReader, otherBuffer, BlockComparisonSize) != BlockComparisonSize)
                {
                    throw DbException.GetUnsupportedException("Invalid LOB");
                }

                int comparison = Math.Sign(string.CompareOrdinal(new string(oneBuffer), new string(otherBuffer)));
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            for (; ; )
            {
                int c1 = oneReader.Read();
                int c2 = otherReader.Read();
                if (c1 < 0)
                {
                    return c2 < 0 ? 0 : -1;
                }

                if (c2 < 0)
                {
                    return 1;
                }

                if (c1 != c2)
                {
                    return c1 < c2 ? -1 : 1;
                }
            }
        }
        catch (IOException e)
        {
            throw DbException.ConvertIOException(e, "Compare");
        }
    }
}
