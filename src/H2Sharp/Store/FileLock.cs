/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.store.FileLock.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Only the method-name resolver is here. The lock protocol itself — the .lock.db file,
 * the properties written into it, the heartbeat thread, the FILE and SOCKET methods —
 * is not ported: the port takes the default FS lock on the data file instead. A
 * database URL that asks for FILE or SOCKET must say so, not be silently downgraded,
 * which is why the resolver arrived before the rest.
 */
using System;
using H2Sharp.Message;

namespace H2Sharp.Store;

/// <summary>
/// The lock file protocol of the reference: who holds the database, since when, and how
/// to find out that a holder died. Only <see cref="GetFileLockMethod"/> is ported.
/// </summary>
public sealed class FileLock
{
    private FileLock()
    {
    }

    /// <summary>
    /// Resolves the FILE_LOCK value of a connection URL.
    /// </summary>
    /// <param name="method">the value, or null for the default</param>
    /// <returns>the lock method</returns>
    /// <exception cref="H2SharpException">if the value is not known</exception>
    public static FileLockMethod GetFileLockMethod(string? method)
    {
        if (method is null || method.Equals("FILE", StringComparison.OrdinalIgnoreCase))
        {
            return FileLockMethod.File;
        }

        if (method.Equals("NO", StringComparison.OrdinalIgnoreCase))
        {
            return FileLockMethod.No;
        }

        if (method.Equals("SOCKET", StringComparison.OrdinalIgnoreCase))
        {
            return FileLockMethod.Socket;
        }

        if (method.Equals("FS", StringComparison.OrdinalIgnoreCase))
        {
            return FileLockMethod.Fs;
        }

        throw DbException.Get(Api.ErrorCode.UnsupportedLockMethod1, method);
    }
}
