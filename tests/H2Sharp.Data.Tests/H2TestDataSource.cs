// Copyright 2026 H2Sharp contributors.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
using System;
using System.Collections.Concurrent;
using H2Sharp.Data;

namespace H2Sharp.Data.Tests;

/// <summary>
/// A test-only cache of data sources for tests that need several connections to one database.
/// </summary>
internal static class H2TestDataSource
{
    private static readonly ConcurrentDictionary<string, H2DataSource> Sources =
        new(StringComparer.Ordinal);

    internal static H2DataSource Get(string connectionString)
        => Sources.GetOrAdd(connectionString,
            static cs => new H2DataSourceBuilder(cs).Build());

    internal static H2Connection Open(string connectionString)
    {
        H2Connection connection = (H2Connection)Get(connectionString).CreateConnection();
        connection.Open();
        return connection;
    }
}
