/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.store.FileLockMethod.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */

namespace H2Sharp.Store;

/// <summary>
/// The way a database file is locked against other processes.
/// </summary>
public enum FileLockMethod
{
    /// <summary>
    /// No locking is used at all.
    /// </summary>
    No,

    /// <summary>
    /// The cooperative file locking protocol should be used.
    /// </summary>
    File,

    /// <summary>
    /// A socket is created on the given machine.
    /// </summary>
    Socket,

    /// <summary>
    /// Use the file system to lock the file; don't use a separate lock file.
    /// </summary>
    Fs,
}
