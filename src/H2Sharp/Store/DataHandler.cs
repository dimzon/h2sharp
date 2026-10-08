/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.store.DataHandler.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: the two members that speak about LOB data — getLobStorage and readLob — are not here
 * yet, because they answer with types that are not ported (LobStorageInterface and ValueLob). They
 * are only asked for by the LOB storage itself, so nothing that exists today can tell the
 * difference; they come with the values.
 */
using H2Sharp.Store.FileSystem;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Store;

/// <summary>
/// What a file store asks of the database it belongs to.
/// </summary>
/// <remarks>
/// The store does not own the answers: whether writing is allowed, whether the machine is supposed
/// to have lost power just now, and where temporary files go are all the database's business. That is
/// why this is an interface and not a set of settings.
/// </remarks>
public interface DataHandler
{
    /// <summary>
    /// Where the database lives.
    /// </summary>
    /// <returns>the path</returns>
    string GetDatabasePath();

    /// <summary>
    /// Open a file at the given place.
    /// </summary>
    /// <param name="name">the file</param>
    /// <param name="mode">the access mode</param>
    /// <param name="mustExist">whether the file has to be there already</param>
    /// <returns>the open file</returns>
    FileStore OpenFile(string name, string mode, bool mustExist);

    /// <summary>
    /// Check whether the power is supposed to have failed. This counts down: the test that asks for
    /// a simulated failure gets one on the call it was set for.
    /// </summary>
    /// <exception cref="H2SharpException">if the power is supposed to have failed</exception>
    void CheckPowerOff();

    /// <summary>
    /// Check whether writing is allowed.
    /// </summary>
    /// <exception cref="H2SharpException">if it is not</exception>
    void CheckWritingAllowed();

    /// <summary>
    /// The largest LOB that may be written in place rather than into the LOB storage.
    /// </summary>
    /// <returns>the size</returns>
    int GetMaxLengthInplaceLob();

    /// <summary>
    /// Who deletes temporary files.
    /// </summary>
    /// <returns>the deleter</returns>
    TempFileDeleter GetTempFileDeleter();

    /// <summary>
    /// The object LOB operations are held under.
    /// </summary>
    /// <returns>the object</returns>
    object GetLobSyncObject();

    /// <summary>
    /// The cache of LOB file names, when the database keeps one.
    /// </summary>
    /// <returns>the cache, or null when there is none</returns>
    SmallLRUCache<string, string[]>? GetLobFileListCache();

    /// <summary>
    /// How strings are compared.
    /// </summary>
    /// <returns>the compare mode</returns>
    CompareMode GetCompareMode();
}
