/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The typed description of a connection. It is produced by H2ConnectionStringBuilder and
 * consumed by Engine. It does not parse any connection string.
 */
namespace H2Sharp.Engine;

/// <summary>
/// The kind of storage behind a connection.
/// </summary>
public enum StorageKind
{
    /// <summary>A file-backed database.</summary>
    File,

    /// <summary>An in-memory database.</summary>
    Memory,
}

/// <summary>
/// The typed information needed to open a database.
/// </summary>
public sealed class ConnectionInfo
{
    /// <summary>
    /// Creates an empty connection description using the regular mode and file storage.
    /// </summary>
    public ConnectionInfo()
    {
    }

    /// <summary>
    /// Creates a connection description with the given storage and data source.
    /// </summary>
    /// <param name="storage">the storage kind</param>
    /// <param name="dataSource">the memory key or file path</param>
    /// <param name="mode">the compatibility mode, or null for regular</param>
    public ConnectionInfo(StorageKind storage, string? dataSource, Mode? mode = null)
    {
        Storage = storage;
        DataSource = dataSource;
        Mode = mode ?? Mode.GetRegular();
    }

    /// <summary>
    /// Gets or sets the storage kind.
    /// </summary>
    public StorageKind Storage { get; set; } = StorageKind.File;

    /// <summary>
    /// Gets or sets the memory key or file path.
    /// </summary>
    public string? DataSource { get; set; }

    /// <summary>
    /// Gets or sets the compatibility mode.
    /// </summary>
    public Mode Mode { get; set; } = Mode.GetRegular();

    /// <summary>
    /// Gets or sets the typed connection options.
    /// </summary>
    public ConnectionOptions Options { get; set; } = new();

    /// <summary>
    /// Gets the database name used by the engine.
    /// </summary>
    public string Name => DataSource ?? string.Empty;

    /// <summary>
    /// Whether the storage is persistent.
    /// </summary>
    public bool IsPersistent => Storage == StorageKind.File;

    /// <summary>
    /// Whether this is an unnamed in-memory database.
    /// </summary>
    public bool IsUnnamedInMemory => Storage == StorageKind.Memory && string.IsNullOrEmpty(DataSource);

    /// <summary>
    /// Creates a named or unnamed in-memory connection.
    /// </summary>
    /// <param name="name">the memory key, or null for an unnamed database</param>
    /// <param name="mode">the compatibility mode, or null for regular</param>
    /// <returns>the connection information</returns>
    public static ConnectionInfo Memory(string? name, Mode? mode = null)
        => new(StorageKind.Memory, name, mode);

    /// <summary>
    /// Creates a file-backed connection.
    /// </summary>
    /// <param name="path">the file path</param>
    /// <param name="mode">the compatibility mode, or null for regular</param>
    /// <returns>the connection information</returns>
    public static ConnectionInfo File(string path, Mode? mode = null)
        => new(StorageKind.File, path, mode);
}
