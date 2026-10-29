/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The ADO.NET connection string builder. The syntax and the quoting are handled by
 * DbConnectionStringBuilder; this type only maps the known keys to typed values.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Store;
using H2Sharp.Util;
using DbException = H2Sharp.Message.DbException;

namespace H2Sharp.Data;

/// <summary>
/// Builds and parses connection strings.
/// </summary>
public sealed class H2ConnectionStringBuilder : DbConnectionStringBuilder
{
    private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Data Source", "DataSource", "Storage", "Mode",
        "ACCESS_MODE_DATA", "AUTO_SERVER", "CASE_INSENSITIVE_IDENTIFIERS", "CIPHER",
        "DATABASE_TO_LOWER", "DATABASE_TO_UPPER", "FILE_LOCK",
        "FORBID_CREATION", "IFEXISTS", "IGNORE_UNKNOWN_SETTINGS", "INIT",
        "LOCK_MODE", "OPEN_NEW", "PAGE_SIZE",
    };

    private static readonly HashSet<string> CredentialKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "User ID", "UserId", "Password", "USER", "PASSWORD",
    };

    /// <summary>
    /// Whether a name is a setting understood by this builder.
    /// </summary>
    /// <param name="name">the setting name</param>
    /// <returns>true if it is known</returns>
    public static bool IsKnownSetting(string name)
        => KnownKeys.Contains(name) || CredentialKeys.Contains(name);

    /// <summary>
    /// Creates a builder with no connection string.
    /// </summary>
    public H2ConnectionStringBuilder()
    {
    }

    /// <summary>
    /// Creates a builder for the given connection string.
    /// </summary>
    /// <param name="connectionString">the connection string</param>
    public H2ConnectionStringBuilder(string? connectionString)
    {
        ConnectionString = connectionString;
    }

    /// <summary>
    /// Gets or sets the memory key or file path.
    /// </summary>
    public string DataSource
    {
        get => GetString("Data Source") ?? GetString("DataSource") ?? string.Empty;
        set => this["Data Source"] = value;
    }

    /// <summary>
    /// Gets or sets the storage kind.
    /// </summary>
    public StorageKind Storage
    {
        get => GetStorage();
        set => this["Storage"] = value.ToString();
    }

    /// <summary>
    /// Gets or sets the compatibility mode.
    /// </summary>
    public Mode Mode
    {
        get => GetMode();
        set => this["Mode"] = value.Name;
    }

    /// <summary>
    /// Gets or sets whether an existing database is required.
    /// </summary>
    public bool IfExists
    {
        get => GetBoolean("IFEXISTS");
        set => this["IFEXISTS"] = value;
    }

    /// <summary>
    /// Gets or sets whether creation is forbidden.
    /// </summary>
    public bool ForbidCreation
    {
        get => GetBoolean("FORBID_CREATION");
        set => this["FORBID_CREATION"] = value;
    }

    /// <summary>
    /// Gets or sets the cipher name.
    /// </summary>
    public string? Cipher
    {
        get => GetString("CIPHER");
        set => SetOptional("CIPHER", value);
    }

    /// <summary>
    /// Gets or sets the INIT script.
    /// </summary>
    public string? Init
    {
        get => GetString("INIT");
        set => SetOptional("INIT", value);
    }

    /// <summary>
    /// Gets or sets whether a new database is requested.
    /// </summary>
    public bool OpenNew
    {
        get => GetBoolean("OPEN_NEW");
        set => this["OPEN_NEW"] = value;
    }

    /// <summary>
    /// Gets or sets whether unknown settings are ignored.
    /// </summary>
    public bool IgnoreUnknownSettings
    {
        get => GetBoolean("IGNORE_UNKNOWN_SETTINGS");
        set => this["IGNORE_UNKNOWN_SETTINGS"] = value;
    }

    /// <summary>
    /// Gets or sets whether identifiers compare without case.
    /// </summary>
    public bool CaseInsensitiveIdentifiers
    {
        get => GetBoolean("CASE_INSENSITIVE_IDENTIFIERS");
        set => this["CASE_INSENSITIVE_IDENTIFIERS"] = value;
    }

    /// <summary>
    /// Gets or sets the lock mode.
    /// </summary>
    public int LockMode
    {
        get => GetInt32("LOCK_MODE", Constants.DefaultLockMode);
        set => this["LOCK_MODE"] = value;
    }

    /// <summary>
    /// Gets or sets the page size.
    /// </summary>
    public int PageSize
    {
        get => GetInt32("PAGE_SIZE", Constants.DefaultPageSize);
        set => this["PAGE_SIZE"] = value;
    }

    /// <summary>
    /// Gets or sets the access mode data value.
    /// </summary>
    public string AccessModeData
    {
        get => GetString("ACCESS_MODE_DATA") ?? "rw";
        set => this["ACCESS_MODE_DATA"] = value;
    }

    /// <summary>
    /// Gets or sets whether automatic server mode is requested.
    /// </summary>
    public bool AutoServer
    {
        get => GetBoolean("AUTO_SERVER");
        set => this["AUTO_SERVER"] = value;
    }

    /// <summary>
    /// Gets or sets the requested file lock method.
    /// </summary>
    public FileLockMethod? FileLock
    {
        get
        {
            string? value = GetString("FILE_LOCK");
            return value is null ? null : H2Sharp.Store.FileLock.GetFileLockMethod(value);
        }
        set => SetOptional("FILE_LOCK", value?.ToString());
    }

    /// <summary>
    /// Converts this builder into typed connection information.
    /// </summary>
    /// <returns>the connection information</returns>
    public ConnectionInfo ToConnectionInfo()
    {
        ValidateKeys();
        string dataSource = DataSource;
        StorageKind storage = Storage;
        if (storage == StorageKind.File && dataSource.Length == 0)
        {
            throw DbException.GetInvalidValueException("Data Source", dataSource);
        }

        return new ConnectionInfo
        {
            Storage = storage,
            DataSource = dataSource.Length == 0 ? null : dataSource,
            Mode = Mode,
            Options = new ConnectionOptions
            {
                IfExists = IfExists,
                ForbidCreation = ForbidCreation,
                Cipher = Cipher,
                Init = Init,
                OpenNew = OpenNew,
                IgnoreUnknownSettings = IgnoreUnknownSettings,
                CaseInsensitiveIdentifiers = CaseInsensitiveIdentifiers,
                LockMode = LockMode,
                PageSize = PageSize,
                AccessModeData = AccessModeData,
                AutoServer = AutoServer,
                FileLock = FileLock,
            },
        };
    }

    /// <summary>
    /// Replaces this builder's values with the given connection information.
    /// </summary>
    /// <param name="info">the connection information</param>
    public void LoadFrom(ConnectionInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        Clear();
        Storage = info.Storage;
        if (!string.IsNullOrEmpty(info.DataSource))
        {
            DataSource = info.DataSource;
        }

        Mode = info.Mode;
        ConnectionOptions options = info.Options;
        IfExists = options.IfExists;
        ForbidCreation = options.ForbidCreation;
        Cipher = options.Cipher;
        Init = options.Init;
        OpenNew = options.OpenNew;
        IgnoreUnknownSettings = options.IgnoreUnknownSettings;
        CaseInsensitiveIdentifiers = options.CaseInsensitiveIdentifiers;
        LockMode = options.LockMode;
        PageSize = options.PageSize;
        AccessModeData = options.AccessModeData;
        AutoServer = options.AutoServer;
        FileLock = options.FileLock;
    }

    private StorageKind GetStorage()
    {
        string? value = GetString("Storage");
        if (string.IsNullOrEmpty(value))
        {
            return StorageKind.File;
        }

        if (Enum.TryParse(value, ignoreCase: true, out StorageKind storage))
        {
            return storage;
        }

        throw DbException.GetInvalidValueException("Storage", value);
    }

    private Mode GetMode()
    {
        string? value = GetString("Mode");
        if (string.IsNullOrEmpty(value))
        {
            return Mode.GetRegular();
        }

        return Mode.GetInstance(value)
            ?? throw DbException.Get(ErrorCode.UnknownMode1, value);
    }

    private string? GetString(string key)
        => TryGetValue(key, out object? value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;

    private bool GetBoolean(string key)
        => Utils.ParseBoolean(GetString(key), false, false);

    private int GetInt32(string key, int defaultValue)
    {
        string? value = GetString(key);
        if (value is null)
        {
            return defaultValue;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
        {
            return result;
        }

        throw DbException.GetInvalidValueException(key, value);
    }

    private void SetOptional(string key, string? value)
    {
        if (value is null)
        {
            _ = Remove(key);
        }
        else
        {
            this[key] = value;
        }
    }

    private void ValidateKeys()
    {
        bool ignoreUnknown = IgnoreUnknownSettings;
        foreach (string key in Keys)
        {
            if (CredentialKeys.Contains(key))
            {
                throw DbException.GetUnsupportedException(key);
            }

            if (!KnownKeys.Contains(key) && !ignoreUnknown)
            {
                throw DbException.Get(ErrorCode.UnsupportedSetting1, key);
            }
        }
    }
}
