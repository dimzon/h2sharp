/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A LOB whose bytes live in the database's LOB storage (branch lob-storage-native):
 * the value keeps a reference, the blocks are read on demand through the storage.
 */
using System;
using System.IO;
using H2Sharp.MvStore.Db;
using H2Sharp.Store;

namespace H2Sharp.Values.Lobs;

/// <summary>
/// A LOB stored in the database.
/// </summary>
public sealed class LobDataStore : LobData
{
    private readonly LobStorageMap storage;

    /// <summary>
    /// The LOB id in the storage.
    /// </summary>
    public long LobId { get; }

    /// <summary>
    /// The table the LOB belongs to.
    /// </summary>
    public int TableId { get; internal set; }

    /// <summary>
    /// How many bytes the LOB holds.
    /// </summary>
    public long OctetLength { get; }

    /// <summary>
    /// Creates a stored LOB reference.
    /// </summary>
    /// <param name="storage">the storage</param>
    /// <param name="lobId">the LOB id</param>
    /// <param name="octetLength">the number of bytes</param>
    public LobDataStore(LobStorageMap storage, long lobId, long octetLength)
    {
        this.storage = storage;
        LobId = lobId;
        OctetLength = octetLength;
    }

    /// <inheritdoc/>
    public override Stream GetInputStream(long precision)
        => storage.GetInputStream(LobId, TableId, precision > 0 ? precision : OctetLength);

    /// <inheritdoc/>
    public override bool IsLinkedToTable() => true;

    /// <inheritdoc/>
    public override void Remove(ValueLob value) => storage.RemoveLob(value);

    /// <inheritdoc/>
    public override int GetMemory() => 64;
}
