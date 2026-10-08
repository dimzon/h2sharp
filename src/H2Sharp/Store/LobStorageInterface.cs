/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.store.LobStorageInterface.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.IO;
using H2Sharp.Values;

namespace H2Sharp.Store;

/// <summary>
/// Where LOBs bigger than a value are kept.
/// </summary>
/// <remarks>
/// A LOB that fits in the record is kept in the record; one that does not is handed here, and the
/// two must come back the same. That is the whole contract, and it is why nothing else about the
/// storage leaks into the values.
/// </remarks>
public interface LobStorageInterface
{
    /// <summary>
    /// Create a CLOB from text.
    /// </summary>
    /// <param name="reader">the text</param>
    /// <param name="maxLength">how many characters at most, or not more than that when it is not more than zero</param>
    /// <returns>the value</returns>
    ValueClob CreateClob(TextReader reader, long maxLength);

    /// <summary>
    /// Create a BLOB from bytes.
    /// </summary>
    /// <param name="input">the bytes</param>
    /// <param name="maxLength">how many bytes at most, or not more than that when it is not more than zero</param>
    /// <returns>the value</returns>
    ValueBlob CreateBlob(Stream input, long maxLength);

    /// <summary>
    /// Copy a LOB to another table.
    /// </summary>
    /// <param name="old">the value</param>
    /// <param name="tableId">the table it is going to</param>
    /// <returns>the copy, or the value itself when it needs none</returns>
    ValueLob CopyLob(ValueLob old, int tableId);

    /// <summary>
    /// A stream to read one LOB through.
    /// </summary>
    /// <param name="lobId">which LOB</param>
    /// <param name="byteCount">how many bytes, or not more than that when it is not more than zero</param>
    /// <returns>the stream</returns>
    Stream GetInputStream(long lobId, long byteCount);

    /// <summary>
    /// A stream to read one LOB of one table through.
    /// </summary>
    /// <param name="lobId">which LOB</param>
    /// <param name="tableId">which table it is in</param>
    /// <param name="byteCount">how many bytes, or not more than that when it is not more than zero</param>
    /// <returns>the stream</returns>
    Stream GetInputStream(long lobId, int tableId, long byteCount);

    /// <summary>
    /// Forget one LOB, including the copy the database keeps of it.
    /// </summary>
    /// <param name="lob">the value</param>
    void RemoveLob(ValueLob lob);

    /// <summary>
    /// Forget every LOB of one table, which is what dropping the table means.
    /// </summary>
    /// <param name="tableId">the table</param>
    void RemoveAllForTable(int tableId);

    /// <summary>
    /// Whether this storage refuses to be written to.
    /// </summary>
    /// <returns>true if it is read-only</returns>
    bool IsReadOnly { get; }

    /// <summary>
    /// Let go of whatever this storage holds.
    /// </summary>
    void Close()
    {
    }
}
