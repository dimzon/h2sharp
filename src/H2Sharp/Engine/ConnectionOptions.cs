/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Typed connection options. Values are converted by H2ConnectionStringBuilder; nothing here
 * parses a connection string.
 */
using H2Sharp.Store;

namespace H2Sharp.Engine;

/// <summary>
/// The typed options that may be supplied with a connection.
/// </summary>
public sealed class ConnectionOptions
{
    /// <summary>
    /// Gets or sets whether opening an existing database is required.
    /// </summary>
    public bool IfExists { get; set; }

    /// <summary>
    /// Gets or sets whether creation of a database is forbidden.
    /// </summary>
    public bool ForbidCreation { get; set; }

    /// <summary>
    /// Gets or sets the cipher name, when specified.
    /// </summary>
    public string? Cipher { get; set; }

    /// <summary>
    /// Gets or sets the INIT script, when specified.
    /// </summary>
    public string? Init { get; set; }

    /// <summary>
    /// Gets or sets whether a new database instance is requested.
    /// </summary>
    public bool OpenNew { get; set; }

    /// <summary>
    /// Gets or sets whether unknown settings are ignored.
    /// </summary>
    public bool IgnoreUnknownSettings { get; set; }

    /// <summary>
    /// Gets or sets the lock mode.
    /// </summary>
    public int LockMode { get; set; } = Constants.DefaultLockMode;

    /// <summary>
    /// Gets or sets the page size.
    /// </summary>
    public int PageSize { get; set; } = Constants.DefaultPageSize;

    /// <summary>
    /// Gets or sets the access mode data value.
    /// </summary>
    public string AccessModeData { get; set; } = "rw";

    /// <summary>
    /// Gets or sets whether automatic server mode was requested.
    /// </summary>
    public bool AutoServer { get; set; }

    /// <summary>
    /// Gets or sets the requested file lock method, or null for the default.
    /// </summary>
    public FileLockMethod? FileLock { get; set; }
}
