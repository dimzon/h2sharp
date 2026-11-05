/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A configured owner of one embedded database. The data source is the lifecycle holder: it
 * creates the Database, keeps it while the data source is alive, and hands out connections to
 * it. Raw H2Connection instances remain self-contained and do not share a database.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using H2Sharp.Engine;
using H2Sharp.Extensions;

namespace H2Sharp.Data;

/// <summary>
/// A configured data source for one H2Sharp database.
/// </summary>
public sealed class H2DataSource : DbDataSource
{
    private readonly ConnectionInfo connectionInfo;

    private readonly string connectionString;

    private readonly Action<IH2Extensions>[] configureExtensions;

    private readonly object sync = new();

    private Database? database;

    private bool disposed;

    private IH2DatabaseTraceListener? traceListener;

    private IH2DatabaseEventListener? databaseEventListener;

    /// <summary>
    /// The trigger registrations of this data source, by exact trigger name: they apply to
    /// the database it owns, now and after it is opened.
    /// </summary>
    private readonly Dictionary<string, H2Sharp.Tables.TriggerRegistration> triggerRegistrations = new(StringComparer.Ordinal);

    /// <summary>
    /// Sets the database event listener, the reference's event listener supplied as an
    /// object instead of a class name in the connection string.
    /// </summary>
    /// <remarks>
    /// The listener is database-wide: it applies to the database this data source owns, now
    /// and after it is opened, and is cleared by passing null. The reference loads the
    /// listener's class from the URL; the embedder hands the instance over here.
    /// </remarks>
    /// <param name="listener">the listener, or null</param>
    public void SetDatabaseEventListener(IH2DatabaseEventListener? listener)
    {
        lock (sync)
        {
            databaseEventListener = listener;
            database?.SetDatabaseEventListener(listener);
        }
    }

    /// <summary>
    /// Registers a trigger for the tables the predicate accepts.
    /// </summary>
    /// <remarks>
    /// The trigger is database-wide: it applies to the database this data source owns, now
    /// and after it is opened. The predicate receives the raw stored schema and table names
    /// and decides applicability itself — the engine folds nothing, and the trigger maps
    /// column names by its own policy. One registration may attach to several tables, and a
    /// table created after the registration is picked up on its first fire.
    /// </remarks>
    /// <param name="name">the trigger name, the exact registration key</param>
    /// <param name="appliesTo">whether the trigger applies to the given schema and table</param>
    /// <param name="events">the statement types the trigger listens to</param>
    /// <param name="before">whether the trigger fires before rather than after the operation</param>
    /// <param name="rowBased">whether the trigger is row-based rather than statement-based</param>
    /// <param name="factory">creates the trigger instance</param>
    public void RegisterTrigger(
        string name,
        Func<string, string, bool> appliesTo,
        H2TriggerEvent events,
        bool before,
        bool rowBased,
        Func<IH2Trigger> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(appliesTo);
        ArgumentNullException.ThrowIfNull(factory);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (triggerRegistrations.ContainsKey(name))
            {
                throw new InvalidOperationException($"A trigger named '{name}' is already registered");
            }

            H2Sharp.Tables.TriggerRegistration registration = new(
                name, appliesTo, events, before, rowBased, factory);
            triggerRegistrations.Add(name, registration);
            database?.AddTriggerRegistration(registration);
        }
    }

    /// <summary>
    /// Unregisters a trigger by its exact name: detaches it from every table it was
    /// attached to and calls the trigger's Remove.
    /// </summary>
    /// <param name="name">the trigger name</param>
    /// <returns>true if a trigger with that name was registered</returns>
    public bool UnregisterTrigger(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (sync)
        {
            if (!triggerRegistrations.Remove(name))
            {
                return false;
            }

            database?.RemoveTriggerRegistration(name);
            return true;
        }
    }

    /// <summary>
    /// Sets the listener the whole database reports its trace lines to.
    /// </summary>
    /// <remarks>
    /// The listener is database-wide: it applies to the database this data source owns, now
    /// and after it is opened, and is cleared by passing null. An embedder that wants the
    /// engine's lines (errors, statements, cache reports) supplies one here instead of
    /// collecting a trace file.
    /// </remarks>
    /// <param name="listener">the listener, or null</param>
    public void SetTraceListener(IH2DatabaseTraceListener? listener)
    {
        lock (sync)
        {
            traceListener = listener;
            database?.SetTraceListener(listener);
        }
    }

    internal H2DataSource(
        ConnectionInfo connectionInfo,
        string connectionString,
        Action<IH2Extensions>[] configureExtensions)
    {
        this.connectionInfo = connectionInfo;
        this.connectionString = connectionString;
        this.configureExtensions = configureExtensions;
    }

    /// <inheritdoc/>
    public override string ConnectionString => connectionString;

    internal string DatabaseName => connectionInfo.Name;

    /// <inheritdoc/>
    protected override DbConnection CreateDbConnection()
        => CreateConnection(connectionString);

    internal H2Connection CreateConnection(string connectionString)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            H2Connection connection = new(this, connectionString);
            try
            {
                foreach (Action<IH2Extensions> configure in configureExtensions)
                {
                    configure(connection);
                }

                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }
    }

    internal SessionLocal OpenSession(
        ConnectionInfo sessionConnectionInfo,
        Func<DateTimeOffset>? systemClockProvider,
        Func<long?, Random>? randomGeneratorProvider,
        Func<Guid>? uuidProvider,
        Func<Util.TimeZoneProvider>? timeZoneProviderProvider,
        ExtensionConfiguration? extensions)
    {
        Database current;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (database is null)
            {
                database = H2Sharp.Engine.Engine.OpenDatabase(connectionInfo);
                // The data source, not the last connection, owns the database. H2's
                // DB_CLOSE_DELAY=-1 expresses the same rule for a database without sessions.
                database.SetCloseDelay(-1);
                database.SetTraceListener(traceListener);
                database.SetDatabaseEventListener(databaseEventListener);
                foreach (H2Sharp.Tables.TriggerRegistration registration in triggerRegistrations.Values)
                {
                    database.AddTriggerRegistration(registration);
                }
            }

            current = database;
        }

        return H2Sharp.Engine.Engine.CreateSession(current, sessionConnectionInfo,
            systemClockProvider, randomGeneratorProvider, uuidProvider, timeZoneProviderProvider,
            extensions);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            base.Dispose(disposing);
            return;
        }

        Database? current;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            current = database;
            database = null;
        }

        current?.Close();
        base.Dispose(disposing);
    }
}
