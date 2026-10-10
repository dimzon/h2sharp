/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * The provider's factory.
 *
 * The reference has no counterpart because it has no need for one: JDBC reaches the driver
 * through DriverManager, and .NET reaches a provider through exactly this object. It is the
 * first thing an embedder looks for, and the ADO.NET conformance suites
 * (AdoNet.Specification.Tests) cannot be wired up without it, which is why it exists here
 * rather than at the end of the provider's story.
 *
 * One instance is enough and is exposed as Instance, the way SqlClient and Npgsql expose theirs:
 * a factory has no state, so a second one would only be a second place to be wrong. CreateDataAdapter
 * and CreateCommandBuilder are refused rather than returned as stubs that throw later — the two
 * say the same thing, and saying it here keeps the failure next to the thing that is missing.
 */
using System;
using System.Data.Common;

namespace H2Sharp.Data;

/// <summary>
/// The factory that creates the objects of this provider.
/// </summary>
public sealed class H2ProviderFactory : DbProviderFactory
{
    private H2ProviderFactory()
    {
    }

    /// <summary>
    /// Gets the single instance of this factory.
    /// </summary>
    public static H2ProviderFactory Instance { get; } = new();

    /// <summary>
    /// Creates a new connection.
    /// </summary>
    public override DbConnection CreateConnection()
    {
        return new H2Connection();
    }

    /// <summary>
    /// Creates a new command.
    /// </summary>
    public override DbCommand CreateCommand()
    {
        return new H2Command();
    }

    /// <summary>
    /// Creates a new parameter.
    /// </summary>
    public override DbParameter CreateParameter()
    {
        return new H2Parameter();
    }

    /// <summary>
    /// Creates a new connection string builder.
    /// </summary>
    public override DbConnectionStringBuilder CreateConnectionStringBuilder()
    {
        return new H2ConnectionStringBuilder();
    }

    /// <summary>
    /// Creates a new data adapter.
    /// </summary>
    public override DbDataAdapter CreateDataAdapter()
        => throw new NotSupportedException("This provider has no data adapter; use Dapper or read the reader directly");

    /// <summary>
    /// Creates a new command builder.
    /// </summary>
    public override DbCommandBuilder CreateCommandBuilder()
        => throw new NotSupportedException("This provider has no command builder");

    /// <summary>
    /// Creates a new data source enumerator, which this provider does not have: it has no
    /// registry of data sources to enumerate.
    /// </summary>
    public override DbDataSourceEnumerator? CreateDataSourceEnumerator()
        => null;

    /// <summary>
    /// Whether this provider can create a data source enumerator.
    /// </summary>
    public override bool CanCreateDataSourceEnumerator => false;

    /// <summary>
    /// Whether this provider can create a batch.
    /// </summary>
    public override bool CanCreateBatch => false;

    /// <summary>
    /// Whether this provider can create a command builder.
    /// </summary>
    public override bool CanCreateCommandBuilder => false;

    /// <summary>
    /// Whether this provider can create a data adapter.
    /// </summary>
    public override bool CanCreateDataAdapter => false;
}
