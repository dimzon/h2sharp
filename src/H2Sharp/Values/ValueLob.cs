/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueLob.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: the parts that write a LOB into a temporary file or into the database are not here —
 * they need the LOB storage, which is an interface now and has no implementation until the store
 * is ported. What is here is everything a small LOB answers, plus the two things both kinds need:
 * a window over the data, and a hash that is cheap for a big value.
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
/// What a BINARY LARGE OBJECT and a CHARACTER LARGE OBJECT have in common.
/// </summary>
/// <remarks>
/// A small one is kept in the record it is in; a big one is somewhere else, and this value says
/// where and how long. Two lengths are kept and they are not the same: bytes for a BLOB, characters
/// for a CLOB, and each kind has to answer the other's question by reading.
/// </remarks>
public abstract class ValueLob : Value
{
    /// <summary>
    /// How much of two values is read at once when they are compared and neither fits in memory.
    /// </summary>
    internal const int BlockComparisonSize = 512;

    /// <summary>
    /// The bytes, or wherever they are.
    /// </summary>
    internal readonly LobData LobData;

    /// <summary>
    /// How many bytes.
    /// </summary>
    protected long LobOctetLength;

    /// <summary>
    /// How many characters, or not more than that when it is not more than zero.
    /// </summary>
    protected long LobCharLength;

    private TypeInfo? type;

    private int hash;

    /// <summary>
    /// A large value.
    /// </summary>
    /// <param name="lobData">where the data is</param>
    /// <param name="octetLength">how many bytes</param>
    /// <param name="charLength">how many characters, or -1</param>
    protected ValueLob(LobData lobData, long octetLength, long charLength)
    {
        LobData = lobData;
        LobOctetLength = octetLength;
        LobCharLength = charLength;
    }

    /// <summary>
    /// The data behind this value.
    /// </summary>
    /// <returns>where it is</returns>
    public LobData GetLobData() => LobData;

    /// <summary>
    /// Whether this value is tied to one table. For a value kept wholly in memory, no.
    /// </summary>
    /// <returns>true if it is tied to a table</returns>
    public bool IsLinkedToTable() => LobData.IsLinkedToTable();

    /// <summary>
    /// Forget whatever is behind this value, if there is anything.
    /// </summary>
    public void Remove() => LobData.Remove(this);

    /// <summary>
    /// Copy a large value to another table.
    /// </summary>
    /// <param name="database">the database</param>
    /// <param name="tableId">the table it is going to</param>
    /// <returns>the copy, or this value when it needs none</returns>
    public abstract ValueLob Copy(DataHandler database, int tableId);

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo()
    {
        TypeInfo? cached = type;
        if (cached is not null)
        {
            return cached;
        }

        int valueType = GetValueType();
        type = cached = new TypeInfo(
            valueType,
            valueType == Value.Clob ? LobCharLength : LobOctetLength,
            0,
            null);
        return cached;
    }

    /// <inheritdoc/>
    public override TextReader GetReader() => IOUtils.GetReader(GetInputStream());

    /// <inheritdoc/>
    public override byte[]? GetBytes()
    {
        if (LobData is LobDataInMemory)
        {
            // A copy, because the caller may change what it gets and the value may not.
            return [.. GetSmall()];
        }

        return GetBytesInternal();
    }

    /// <inheritdoc/>
    public override byte[]? GetBytesNoCopy()
        => LobData is LobDataInMemory ? GetSmall() : GetBytesInternal();

    /// <inheritdoc/>
    /// <remarks>
    /// A big value is not read to be hashed: its length is used instead, which is cheap and, as the
    /// reference notes, not as good as a hash of the contents — but reading a gigabyte to answer a
    /// question about a key is worse. Two big values of the same length therefore compare equal by
    /// hash, and the comparison that follows settles it.
    /// </remarks>
    public override int GetHashCode()
    {
        int result = hash;
        if (result != 0)
        {
            return result;
        }

        int valueType = GetValueType();
        long length = valueType == Value.Clob ? LobCharLength : LobOctetLength;
        if (length > 4096)
        {
            return (int)(length ^ (long)((ulong)length >> 32));
        }

        hash = result = Utils.GetByteArrayHash(GetBytesNoCopy() ?? []);
        return result;
    }

    /// <inheritdoc/>
    public override bool Equals(object? other)
    {
        if (other is not ValueLob otherLob)
        {
            return false;
        }

        if (GetHashCode() != otherLob.GetHashCode())
        {
            return false;
        }

        // The reference passes no compare mode here, because equality does not need one: it decides
        // for itself, byte by byte or character by character, which is also the only way two values
        // held in different places can be equal at all.
        return CompareTypeSafe(otherLob, CompareMode.GetInstance(null, 0), null) == 0;
    }

    /// <inheritdoc/>
    public override int GetMemory() => LobData.GetMemory();

    /// <summary>
    /// The bytes, read from wherever they are.
    /// </summary>
    /// <returns>the bytes</returns>
    protected abstract byte[]? GetBytesInternal();

    /// <summary>
    /// The bytes of a small value.
    /// </summary>
    /// <returns>the bytes</returns>
    /// <exception cref="H2SharpException">if there are more than a value may hold</exception>
    protected byte[] GetSmall()
    {
        byte[] small = ((LobDataInMemory)LobData).GetSmall();
        if (small.Length > Constants.MaxStringLength)
        {
            throw DbException.GetValueTooLongException(
                "BINARY VARYING", StringUtils.ConvertBytesToHex(small, 41), small.Length);
        }

        return small;
    }

    /// <summary>
    /// The text of this value is too long to be a string.
    /// </summary>
    /// <param name="precision">the length it has</param>
    /// <returns>the error</returns>
    protected H2SharpException GetStringTooLong(long precision)
        => DbException.GetValueTooLongException("CHARACTER VARYING", ReadString(81), precision);

    /// <summary>
    /// The bytes of this value are too many to be a string.
    /// </summary>
    /// <param name="precision">the length it has</param>
    /// <returns>the error</returns>
    protected H2SharpException GetBinaryTooLong(long precision)
        => DbException.GetValueTooLongException(
            "BINARY VARYING", StringUtils.ConvertBytesToHex(ReadBytes(41)), precision);

    /// <summary>
    /// Read this value as text, at most so many characters.
    /// </summary>
    /// <param name="len">the most to read</param>
    /// <returns>the text</returns>
    protected string ReadString(int len)
    {
        try
        {
            return IOUtils.ReadStringAndClose(GetReader(), len);
        }
        catch (IOException e)
        {
            throw DbException.ConvertIOException(e, ToString()!);
        }
    }

    /// <summary>
    /// Read this value as bytes, at most so many.
    /// </summary>
    /// <param name="len">the most to read</param>
    /// <returns>the bytes</returns>
    protected byte[] ReadBytes(int len)
    {
        try
        {
            return IOUtils.ReadBytesAndClose(GetInputStream(), len);
        }
        catch (IOException e)
        {
            throw DbException.ConvertIOException(e, ToString()!);
        }
    }

    /// <summary>
    /// A window of a stream: so much of it, from there.
    /// </summary>
    /// <param name="inputStream">the stream</param>
    /// <param name="oneBasedOffset">where, where one means the beginning</param>
    /// <param name="length">how many bytes</param>
    /// <param name="dataSize">how long the stream is, or not more than that when it is not more than zero</param>
    /// <returns>the window</returns>
    /// <exception cref="H2SharpException">if the window is not inside the data</exception>
    protected static Stream RangeInputStream(
        Stream inputStream,
        long oneBasedOffset,
        long length,
        long dataSize)
    {
        if (dataSize > 0)
        {
            RangeCheck(oneBasedOffset - 1, length, dataSize);
        }
        else
        {
            RangeCheckUnknown(oneBasedOffset - 1, length);
        }

        try
        {
            return new RangeInputStream(inputStream, oneBasedOffset - 1, length);
        }
        catch (IOException)
        {
            throw DbException.GetInvalidValueException("offset", oneBasedOffset);
        }
    }

    /// <summary>
    /// A window of text: so much of it, from there.
    /// </summary>
    /// <param name="reader">the text</param>
    /// <param name="oneBasedOffset">where, where one means the beginning</param>
    /// <param name="length">how many characters</param>
    /// <param name="dataSize">how long the text is, or not more than that when it is not more than zero</param>
    /// <returns>the window</returns>
    /// <exception cref="H2SharpException">if the window is not inside the text</exception>
    protected static TextReader RangeReader(
        TextReader reader,
        long oneBasedOffset,
        long length,
        long dataSize)
    {
        if (dataSize > 0)
        {
            RangeCheck(oneBasedOffset - 1, length, dataSize);
        }
        else
        {
            RangeCheckUnknown(oneBasedOffset - 1, length);
        }

        try
        {
            return new RangeReader(reader, oneBasedOffset - 1, length);
        }
        catch (IOException)
        {
            throw DbException.GetInvalidValueException("offset", oneBasedOffset);
        }
    }

    private static void RangeCheckUnknown(long zeroBasedOffset, long length)
    {
        if (zeroBasedOffset < 0)
        {
            throw DbException.GetInvalidValueException("offset", zeroBasedOffset + 1);
        }

        if (length < 0)
        {
            throw DbException.GetInvalidValueException("length", length);
        }
    }



    /// <summary>
    /// A comment saying where the data is, for the SQL of a value whose data is not in the text.
    /// </summary>
    /// <param name="builder">where to write</param>
    protected void FormatLobDataComment(StringBuilder builder)
    {
        if (LobData is LobDataInMemory inMemory)
        {
            // The data is in the value, so the comment names it and cannot be closed by it.
            _ = builder.Append(" /* ").Append(inMemory.ToString()!.Replace("*/", "\\*/")).Append(" */");
        }
        else
        {
            _ = builder.Append(" /* ").Append(LobData.ToString()).Append(" */");
        }
    }
}
