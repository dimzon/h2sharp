/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.engine.Engine.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The registry of open databases. The storage identity is typed: a memory key or a
 * canonical file path. A database is shared only when the requested mode matches; a
 * different mode is an open conflict, not a second database on the same storage.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using H2Sharp.Api;
using H2Sharp.Extensions;
using H2Sharp.Message;
using H2Sharp.Store.FileSystem;

namespace H2Sharp.Engine;

/// <summary>
/// The engine contains a map of all open databases.
/// It is also responsible for opening and creating new databases.
/// This is a singleton class.
/// </summary>
public static class Engine
{
    private static readonly Dictionary<StorageKey, DatabaseHolder> Databases =
        new(StorageKeyComparer.Instance);

    private static readonly object DatabasesLock = new();

    /// <summary>
    /// Opens a session for the given connection information.
    /// </summary>
    /// <param name="connectionInfo">the connection information</param>
    /// <param name="systemClockProvider">the clock, or null for the machine's clock</param>
    /// <param name="randomGeneratorProvider">the generator of random numbers, or null for the
    /// session's own</param>
    /// <param name="uuidProvider">the generator of UUIDs, or null for the ones the port builds
    /// itself</param>
    /// <param name="timeZoneProviderProvider">the time zone provider, or null for the default</param>
    /// <returns>the session</returns>
    public static SessionLocal OpenSession(
        ConnectionInfo connectionInfo,
        Func<DateTimeOffset>? systemClockProvider = null,
        Func<long?, Random>? randomGeneratorProvider = null,
        Func<Guid>? uuidProvider = null,
        Func<Util.TimeZoneProvider>? timeZoneProviderProvider = null)
        => OpenSession(connectionInfo, systemClockProvider, randomGeneratorProvider,
            uuidProvider, timeZoneProviderProvider, extensions: null);

    internal static SessionLocal OpenSession(
        ConnectionInfo connectionInfo,
        Func<DateTimeOffset>? systemClockProvider,
        Func<long?, Random>? randomGeneratorProvider,
        Func<Guid>? uuidProvider,
        Func<Util.TimeZoneProvider>? timeZoneProviderProvider,
        ExtensionConfiguration? extensions)
    {
        string name = connectionInfo.Name;
        ConnectionOptions options = connectionInfo.Options;
        if (options.Cipher is not null)
        {
            throw new TODOException("Engine.openSession: file encryption (CIPHER) is not ported");
        }

        if (options.Init is not null)
        {
            throw new TODOException("Engine.openSession: INIT is not ported");
        }

        if (connectionInfo.IsPersistent)
        {
            string fileName = name + Constants.SuffixMvFile;
            if (!FileUtils.Exists(fileName))
            {
                ThrowNotFound(options.IfExists, options.ForbidCreation, name);
                fileName = name + Constants.SuffixOldDatabaseFile;
                if (FileUtils.Exists(fileName))
                {
                    throw DbException.GetFileVersionError(fileName);
                }
            }
            else if (!FileUtils.CanWrite(fileName))
            {
                options.AccessModeData = "r";
            }
        }
        else
        {
            ThrowNotFound(options.IfExists, options.ForbidCreation, name);
        }

        StorageKey key = default;
        bool named = !connectionInfo.IsUnnamedInMemory;
        if (named)
        {
            key = GetStorageKey(connectionInfo);
        }

        DatabaseHolder holder = named ? GetOrCreateHolder(key) : new DatabaseHolder();
        Database? database;
        for (;;)
        {
            lock (holder)
            {
                if (!holder.Removed)
                {
                    database = holder.Database;
                    if (database is not null && database.IsClosing())
                    {
                        database = null;
                    }
                    else
                    {
                        if (database is null)
                        {
                            if (options.OpenNew && holder.Database is not null)
                            {
                                throw DbException.Get(ErrorCode.DatabaseAlreadyOpen1, name);
                            }

                            database = new Database(connectionInfo);
                            holder.Database = database;
                        }
                        else if (options.OpenNew)
                        {
                            throw DbException.Get(ErrorCode.DatabaseAlreadyOpen1, name);
                        }
                        else if (database.GetMode() != connectionInfo.Mode)
                        {
                            throw DbException.Get(ErrorCode.DatabaseAlreadyOpen1, name);
                        }

                        break;
                    }
                }
            }

            // A database that is closing releases its registry slot from Database.Close.
            // Re-read the holder because close may have removed it from the registry.
            if (named)
            {
                holder = GetOrCreateHolder(key);
            }

            Thread.Sleep(1);
        }

        database.Opened();
        SessionLocal session = new(database, Constants.DefaultUser)
        {
            RandomGeneratorProvider = randomGeneratorProvider,
            UuidProvider = uuidProvider,
            Extensions = ExtensionRuntime.Create(extensions),
        };
        CastDataProvider inner = systemClockProvider is null && timeZoneProviderProvider is null
            ? DefaultCastDataProvider.Instance
            : new DelegatedCastDataProvider(systemClockProvider, timeZoneProviderProvider);
        session.CastDataProvider = new SessionCastDataProvider(session, inner);
        session.SetTimeZone(inner.CurrentTimeZone());
        return session;
    }

    /// <summary>
    /// Removes a closed database from the registry, waking any opener waiting for it.
    /// </summary>
    /// <param name="database">the database that finished closing</param>
    internal static void RemoveClosed(Database database)
    {
        ConnectionInfo connectionInfo = database.GetConnectionInfo();
        if (connectionInfo.IsUnnamedInMemory)
        {
            return;
        }

        StorageKey key = GetStorageKey(connectionInfo);
        lock (DatabasesLock)
        {
            if (!Databases.TryGetValue(key, out DatabaseHolder? holder)
                || !ReferenceEquals(holder.Database, database))
            {
                return;
            }

            lock (holder)
            {
                if (ReferenceEquals(holder.Database, database))
                {
                    holder.Database = null;
                    holder.Removed = true;
                }
            }

            _ = Databases.Remove(key);
        }
    }

    private static DatabaseHolder GetOrCreateHolder(StorageKey key)
    {
        lock (DatabasesLock)
        {
            if (!Databases.TryGetValue(key, out DatabaseHolder? holder))
            {
                holder = new DatabaseHolder();
                Databases[key] = holder;
            }

            return holder;
        }
    }

    private static StorageKey GetStorageKey(ConnectionInfo connectionInfo)
    {
        string name = connectionInfo.DataSource ?? string.Empty;
        if (connectionInfo.Storage == StorageKind.File)
        {
            name = Path.GetFullPath(name);
        }

        return new StorageKey(connectionInfo.Storage, name);
    }

    private static void ThrowNotFound(bool ifExists, bool forbidCreation, string name)
    {
        if (ifExists)
        {
            throw DbException.Get(ErrorCode.DatabaseNotFoundWithIfExists1, name);
        }

        if (forbidCreation)
        {
            throw DbException.Get(ErrorCode.RemoteDatabaseNotFound1, name);
        }
    }

    private readonly record struct StorageKey(StorageKind Kind, string Name);

    private sealed class StorageKeyComparer : IEqualityComparer<StorageKey>
    {
        internal static readonly StorageKeyComparer Instance = new();

        public bool Equals(StorageKey x, StorageKey y)
        {
            if (x.Kind != y.Kind)
            {
                return false;
            }

            StringComparison comparison = x.Kind == StorageKind.File && OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(x.Name, y.Name, comparison);
        }

        public int GetHashCode(StorageKey obj)
        {
            StringComparer comparer = obj.Kind == StorageKind.File && OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            return HashCode.Combine(obj.Kind, comparer.GetHashCode(obj.Name));
        }
    }

    private sealed class DatabaseHolder
    {
        internal Database? Database { get; set; }

        internal bool Removed { get; set; }
    }
}


