/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;

namespace H2Sharp.Data.Extensions;

/// <summary>
/// An aggregate provider backed by a delegate.
/// </summary>
public sealed class AggregateProvider : IH2AggregateProvider
{
    private readonly Func<IH2Aggregate> factory;

    /// <summary>
    /// Creates a provider from a factory.
    /// </summary>
    /// <param name="factory">the aggregate factory</param>
    /// <param name="options">optimizer metadata, or null for the conservative defaults</param>
    public AggregateProvider(Func<IH2Aggregate> factory, AggregateOptions? options = null)
    {
        this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
        Options = options ?? AggregateOptions.Default;
    }

    /// <inheritdoc/>
    public AggregateOptions Options { get; }

    /// <inheritdoc/>
    public IH2Aggregate CreateAggregate() => factory();
}

/// <summary>
/// An aggregate provider backed by a parameterless aggregate type.
/// </summary>
/// <typeparam name="T">the aggregate type</typeparam>
public sealed class AggregateProvider<T> : IH2AggregateProvider
    where T : IH2Aggregate, new()
{
    /// <summary>
    /// Creates a provider for <typeparamref name="T"/>.
    /// </summary>
    /// <param name="options">optimizer metadata, or null for the conservative defaults</param>
    public AggregateProvider(AggregateOptions? options = null)
    {
        Options = options ?? AggregateOptions.Default;
    }

    /// <inheritdoc/>
    public AggregateOptions Options { get; }

    /// <inheritdoc/>
    public IH2Aggregate CreateAggregate() => new T();
}
