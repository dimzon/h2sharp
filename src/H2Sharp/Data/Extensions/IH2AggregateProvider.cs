/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
namespace H2Sharp.Data.Extensions;

/// <summary>
/// Creates host-provided aggregate instances and supplies their optimizer metadata.
/// </summary>
/// <remarks>
/// The provider is the primary registration contract. An instance is created for every
/// group, window partition or transient empty-group evaluation; <see cref="CreateAggregate"/>
/// must return a fresh object each time. <see cref="Options"/> is read once at registration.
/// </remarks>
public interface IH2AggregateProvider
{
    /// <summary>
    /// The optimizer metadata of the aggregate.
    /// </summary>
    AggregateOptions Options { get; }

    /// <summary>
    /// Creates a new aggregate instance.
    /// </summary>
    /// <returns>a fresh aggregate instance</returns>
    IH2Aggregate CreateAggregate();
}
