/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The database as a DataHandler: what the store asks of it. Part of the LOB storage
 * wiring (branch lob-storage-native); the storage itself is provided lazily by
 * GetLobStorage.
 */
using H2Sharp.Store;
using H2Sharp.Store.FileSystem;
using H2Sharp.Util;

namespace H2Sharp.Engine;

public sealed partial class Database : DataHandler
{
    /// <inheritdoc/>
    public FileStore OpenFile(string name, string mode, bool mustExist)
        => FileStore.Open(this, name, mode);

    /// <inheritdoc/>
    public void CheckPowerOff()
    {
        // The power-loss simulation is a test hook the store configures; the database itself
        // does not simulate failures.
    }

    /// <inheritdoc/>
    public object GetLobSyncObject() => this;

    /// <inheritdoc/>
    public SmallLRUCache<string, string[]>? GetLobFileListCache() => null;

    private LobStorageInterface? lobStorage;

    /// <inheritdoc/>
    public LobStorageInterface GetLobStorage()
        => lobStorage ??= new MvStore.Db.LobStorageMap(this);
}
