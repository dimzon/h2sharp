/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using H2Sharp.Engine;

namespace H2Sharp.Data;

/// <summary>
/// Configures and creates one <see cref="H2DataSource"/>.
/// </summary>
public sealed class H2DataSourceBuilder
{
    private readonly ConnectionInfo connectionInfo;

    private readonly string connectionString;

    private readonly List<Action<IH2Extensions>> configureExtensions = [];

    private readonly List<Action<IH2DataFormat>> configureDataFormat = [];

    /// <summary>
    /// Creates a builder for the given connection string.
    /// </summary>
    /// <param name="connectionString">the classic ADO.NET connection string</param>
    public H2DataSourceBuilder(string connectionString)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        this.connectionString = connectionString;
        connectionInfo = new H2ConnectionStringBuilder(connectionString).ToConnectionInfo();
    }

    /// <summary>
    /// Adds an action that configures extension registrations on each new connection.
    /// </summary>
    /// <remarks>
    /// The action is invoked immediately after the connection object is created and before it
    /// is returned, so the connection cannot be open yet. Actions run in registration order.
    /// </remarks>
    /// <param name="configure">the registration action</param>
    /// <returns>this builder</returns>
    public H2DataSourceBuilder ConfigureExtensions(Action<IH2Extensions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configureExtensions.Add(configure);
        return this;
    }

    /// <summary>
    /// Adds an action that configures the ADO.NET value projection on each new connection.
    /// </summary>
    /// <remarks>
    /// The action is invoked immediately after the connection object is created and before it
    /// is returned, so the connection cannot be open yet. It is invoked for every connection
    /// this data source creates, after the extension actions and in registration order. The
    /// projection belongs to the connection, not to the database: the action must therefore
    /// set the properties, not share one mutable object between connections.
    /// </remarks>
    /// <param name="configure">the projection action</param>
    /// <returns>this builder</returns>
    public H2DataSourceBuilder ConfigureDataFormat(Action<IH2DataFormat> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configureDataFormat.Add(configure);
        return this;
    }

    /// <summary>
    /// Builds the data source.
    /// </summary>
    /// <returns>the configured data source</returns>
    public H2DataSource Build()
        => new(connectionInfo, connectionString, [.. configureExtensions], [.. configureDataFormat]);
}
