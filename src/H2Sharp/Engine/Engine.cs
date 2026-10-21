/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.engine.Engine.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Partially ported: the registry of open databases and the rule for handing out a session.
 *
 * The registry is the whole trick behind "mem:", and it is two steps. A *named* in-memory
 * database goes into a static map, so the second connection to the same name finds the first
 * one's database instead of making its own; an *unnamed* one ("mem:" with nothing after it) is
 * never entered, so it belongs to the connection that asked for it and dies with it. Both cases
 * are in the differential test, because the second is the one a port gets wrong by leaving the
 * map out and every test that uses a name still passes.
 *
 * The holder lock and the map lock are kept apart as in the reference: the map is locked to find
 * or create a holder, and the holder is locked while the database inside it is created. The
 * reference starts the database's threads after releasing the holder lock, with a comment about
 * the writer thread deadlocking against recovery; nothing starts here yet, and the comment is
 * carried so the reason the ordering is what it is survives the port.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Util;
using H2Sharp.Api;
using H2Sharp.Message;

namespace H2Sharp.Engine;

/// <summary>
/// The engine contains a map of all open databases.
/// It is also responsible for opening and creating new databases.
/// This is a singleton class.
/// </summary>
public static class Engine
{
    private static readonly Dictionary<string, DatabaseHolder> Databases = [];

    private static readonly object DatabasesLock = new();

    /// <summary>
    /// Opens a session for the given connection information.
    /// </summary>
    /// <remarks>
    /// The persistent, remote and authenticated branches of the reference are not here yet and
    /// refuse rather than pretend: opening a file database without the file store would give a
    /// database that answers every query from an empty in-memory store.
    /// <para>
    /// The three delegates are the seams a test needs; the reference has no such a member, and
    /// a delegate that is not set leaves the session as it is without a test.
    /// </para>
    /// </remarks>
    /// <param name="connectionInfo">the connection information</param>
    /// <param name="systemClockProvider">the clock, or null for the machine's clock</param>
    /// <param name="randomGeneratorProvider">the generator of random numbers, or null for the
    /// session's own</param>
    /// <param name="uuidProvider">the generator of UUIDs, or null for the ones the port builds
    /// itself</param>
    /// <returns>the session</returns>
    public static SessionLocal OpenSession(
        ConnectionInfo connectionInfo,
        Func<DateTimeOffset>? systemClockProvider = null,
        Func<JavaRandom>? randomGeneratorProvider = null,
        Func<Guid>? uuidProvider = null)
    {
        string name = connectionInfo.GetName();
        if (connectionInfo.IsRemote())
        {
            throw new TODOException("Engine.openSession: remote connections");
        }

        if (connectionInfo.IsPersistent())
        {
            throw new TODOException("Engine.openSession: persistent databases need the file store");
        }

        bool openNew = connectionInfo.GetBooleanProperty("OPEN_NEW", false);
        DatabaseHolder holder;
        if (!connectionInfo.IsUnnamedInMemory())
        {
            lock (DatabasesLock)
            {
                if (!Databases.TryGetValue(name, out holder!))
                {
                    holder = new DatabaseHolder();
                    Databases[name] = holder;
                }
            }
        }
        else
        {
            holder = new DatabaseHolder();
        }

        Database? database;
        lock (holder)
        {
            database = holder.Database;
            if (database is null || openNew)
            {
                if (!connectionInfo.GetBooleanProperty("IFEXISTS", false))
                {
                    database = new Database(connectionInfo);
                }
                else
                {
                    ThrowNotFound(name);
                    throw new TODOException("Engine.openSession: IFEXISTS without a registry lookup");
                }

                holder.Database = database;
            }
        }

        if (database.IsClosing())
        {
            return null!;
        }

        // Start the threads while already synchronizing on the holder, so that a database which
        // starts one cannot deadlock against another database opening here.
        database.Opened();
        return new SessionLocal(database, connectionInfo.GetUserName())
        {
            CastDataProvider = systemClockProvider is null
                ? DefaultCastDataProvider.Instance
                : new DelegatedCastDataProvider(systemClockProvider),
            RandomGeneratorProvider = randomGeneratorProvider,
            UuidProvider = uuidProvider,
        };
    }

    /// <summary>
    /// Removes a database from the registry when it closes.
    /// </summary>
    /// <remarks>
    /// The reference drops the registry entry from Database.close itself, through the holder it
    /// was created in. That holder is private to this class, so the removal goes through here:
    /// the caller names the database and this class decides whether it was still the open one,
    /// which matters because a database that closed and was reopened under the same name must
    /// not have the new one removed by the old one's late close.
    /// </remarks>
    /// <param name="name">the database name</param>
    /// <returns>whether a database was removed</returns>
    internal static bool RemoveIfClosed(string name)
    {
        lock (DatabasesLock)
        {
            if (!Databases.TryGetValue(name, out DatabaseHolder? holder)
                || holder.Database is null
                || !holder.Database.IsClosing())
            {
                return false;
            }

            return Databases.Remove(name);
        }
    }

    /// <summary>
    /// Throws the "not found" error unless creation was forbidden.
    /// </summary>
    /// <param name="name">the database name</param>
    private static void ThrowNotFound(string name)
    {
        throw DbException.Get(ErrorCode.DatabaseNotFound1, name);
    }

    /// <summary>
    /// A slot in the registry, so that two connections racing for the same name create one
    /// database rather than two.
    /// </summary>
    private sealed class DatabaseHolder
    {
        internal Database? Database { get; set; }
    }
}
