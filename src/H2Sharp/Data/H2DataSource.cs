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
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(H2DataSource));
            }

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
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(H2DataSource));
            }

            if (database is null)
            {
                database = H2Sharp.Engine.Engine.OpenDatabase(connectionInfo);
                // The data source, not the last connection, owns the database. H2's
                // DB_CLOSE_DELAY=-1 expresses the same rule for a database without sessions.
                database.SetCloseDelay(-1);
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
