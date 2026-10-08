/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.lob.LobData.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: only the two kinds that exist in the port — in memory, and everything else that this
 * class can answer without a database — are here. The reference has four: in memory, in a file, in
 * the database, and fetched from a server on demand. The last three come with the LOB storage and
 * the network, and the answers that ask for a handler say so by not overriding it.
 */
using System;
using System.IO;
using H2Sharp.Store;

namespace H2Sharp.Values.Lobs;

/// <summary>
/// Where the bytes of a LOB are.
/// </summary>
/// <remarks>
/// The value itself is small: a length, a type, and this. Everything expensive — the bytes, and
/// their length in characters rather than in bytes — lives behind here, because where the bytes are
/// decides how they are got and how much memory the value costs.
/// </remarks>
public abstract class LobData
{
    /// <summary>
    /// A stream to read the data from.
    /// </summary>
    /// <param name="precision">how many bytes, or not more than that when it is not more than zero</param>
    /// <returns>the stream</returns>
    public abstract Stream GetInputStream(long precision);

    /// <summary>
    /// The database this data belongs to.
    /// </summary>
    /// <returns>the handler, or null when there is no database involved</returns>
    public virtual DataHandler? GetDataHandler() => null;

    /// <summary>
    /// Whether this value is tied to one table. For data kept wholly in memory, no.
    /// </summary>
    /// <returns>true if it is tied to a table</returns>
    public virtual bool IsLinkedToTable() => false;

    /// <summary>
    /// Remove whatever is behind the value, if there is anything. For data kept wholly in memory
    /// there is nothing to remove.
    /// </summary>
    /// <param name="value">the value</param>
    public virtual void Remove(ValueLob value)
    {
    }

    /// <summary>
    /// How much memory this costs.
    /// </summary>
    /// <returns>the number of bytes</returns>
    public virtual int GetMemory() => 140;
}

/// <summary>
/// A LOB that is small enough to keep in the value itself.
/// </summary>
public sealed class LobDataInMemory : LobData
{
    private readonly byte[] small;

    /// <summary>
    /// Data that is kept where it is.
    /// </summary>
    /// <param name="small">the bytes, which are not copied and must not be changed afterwards</param>
    /// <exception cref="InvalidOperationException">if there are no bytes at all</exception>
    public LobDataInMemory(byte[] small)
    {
        ArgumentNullException.ThrowIfNull(small);
        this.small = small;
    }

    /// <summary>
    /// The data.
    /// </summary>
    /// <returns>the bytes, not a copy of them</returns>
    public byte[] GetSmall() => small;

    /// <inheritdoc/>
    public override Stream GetInputStream(long precision) => new MemoryStream(small, writable: false);

    /// <inheritdoc/>
    /// <remarks>
    /// The number is the reference's own measurement: Java 11 without compressed references takes
    /// 120 bytes for the object and 128 for one byte of data.
    /// </remarks>
    public override int GetMemory() => small.Length + 127;
}
