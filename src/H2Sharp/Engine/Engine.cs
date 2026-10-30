/*
 * Copyright 2004-2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.engine.Engine, but without the reference's global database registry.
 * An embedded application owns a database through DbDataSource (or one raw H2Connection owns
 * its own database). There is no name-to-database map and no key by mode or identifier
 * settings; the storage lock is what prevents two owners of the same file.
 */
using System;
using H2Sharp.Api;
using H2Sharp.Extensions;
using H2Sharp.Message;
using H2Sharp.Store.FileSystem;

namespace H2Sharp.Engine;

/// <summary>
/// Opening a database and creating a session on it.
/// </summary>
public static class Engine
{
    /// <summary>
    /// Opens a new database and a session on it.
    /// </summary>
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
        Database database = OpenDatabase(connectionInfo);
        try
        {
            return CreateSession(database, connectionInfo, systemClockProvider,
                randomGeneratorProvider, uuidProvider, timeZoneProviderProvider, extensions);
        }
        catch
        {
            database.Close();
            throw;
        }
    }

    /// <summary>
    /// Opens a database without a session. The caller owns the returned database.
    /// </summary>
    internal static Database OpenDatabase(ConnectionInfo connectionInfo)
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
            // The reference refuses a name with no file stem (such as a path ending
            // in a separator) in ConnectionInfo.getName: the file name must be longer
            // than the bare ".mv.db" suffix.
            string stem = System.IO.Path.GetFileName(
                Store.FileSystem.FileUtils.ToRealPath(name + Constants.SuffixMvFile));
            if (stem.Length < Constants.SuffixMvFile.Length + 1)
            {
                throw Message.DbException.Get(Api.ErrorCode.InvalidDatabaseName1, name);
            }

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

        return new Database(connectionInfo);
    }

    /// <summary>
    /// Creates a session on an already opened database.
    /// </summary>
    internal static SessionLocal CreateSession(
        Database database,
        ConnectionInfo connectionInfo,
        Func<DateTimeOffset>? systemClockProvider,
        Func<long?, Random>? randomGeneratorProvider,
        Func<Guid>? uuidProvider,
        Func<Util.TimeZoneProvider>? timeZoneProviderProvider,
        ExtensionConfiguration? extensions)
    {
        _ = connectionInfo;
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
}
