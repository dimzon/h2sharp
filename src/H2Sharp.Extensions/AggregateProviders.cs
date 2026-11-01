/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Data.Extensions;

namespace H2Sharp.Extensions;

/// <summary>Provider for <see cref="StringAgg"/>.</summary>
public sealed class StringAggProvider : IH2AggregateProvider
{
    private static readonly AggregateOptions Defaults = new() { Deterministic = true };

    /// <summary>Creates the provider.</summary>
    /// <param name="options">metadata overriding the defaults, or null</param>
    public StringAggProvider(AggregateOptions? options = null) => Options = options ?? Defaults;

    /// <inheritdoc/>
    public AggregateOptions Options { get; }

    /// <inheritdoc/>
    public IH2Aggregate CreateAggregate() => new StringAgg();
}

/// <summary>Provider for <see cref="GeometricMean"/>.</summary>
public sealed class GeometricMeanProvider : IH2AggregateProvider
{
    private static readonly AggregateOptions Defaults = new() { Deterministic = true };

    /// <summary>Creates the provider.</summary>
    /// <param name="options">metadata overriding the defaults, or null</param>
    public GeometricMeanProvider(AggregateOptions? options = null) => Options = options ?? Defaults;

    /// <inheritdoc/>
    public AggregateOptions Options { get; }

    /// <inheritdoc/>
    public IH2Aggregate CreateAggregate() => new GeometricMean();
}

/// <summary>Provider for <see cref="HarmonicMean"/>.</summary>
public sealed class HarmonicMeanProvider : IH2AggregateProvider
{
    private static readonly AggregateOptions Defaults = new() { Deterministic = true };

    /// <summary>Creates the provider.</summary>
    /// <param name="options">metadata overriding the defaults, or null</param>
    public HarmonicMeanProvider(AggregateOptions? options = null) => Options = options ?? Defaults;

    /// <inheritdoc/>
    public AggregateOptions Options { get; }

    /// <inheritdoc/>
    public IH2Aggregate CreateAggregate() => new HarmonicMean();
}

/// <summary>Provider for <see cref="Product"/>.</summary>
public sealed class ProductProvider : IH2AggregateProvider
{
    private static readonly AggregateOptions Defaults = new() { Deterministic = true };

    /// <summary>Creates the provider.</summary>
    /// <param name="options">metadata overriding the defaults, or null</param>
    public ProductProvider(AggregateOptions? options = null) => Options = options ?? Defaults;

    /// <inheritdoc/>
    public AggregateOptions Options { get; }

    /// <inheritdoc/>
    public IH2Aggregate CreateAggregate() => new Product();
}

/// <summary>Provider for <see cref="WeightedAverage"/>.</summary>
public sealed class WeightedAverageProvider : IH2AggregateProvider
{
    private static readonly AggregateOptions Defaults = new() { Deterministic = true };

    /// <summary>Creates the provider.</summary>
    /// <param name="options">metadata overriding the defaults, or null</param>
    public WeightedAverageProvider(AggregateOptions? options = null) => Options = options ?? Defaults;

    /// <inheritdoc/>
    public AggregateOptions Options { get; }

    /// <inheritdoc/>
    public IH2Aggregate CreateAggregate() => new WeightedAverage();
}

/// <summary>Provider for <see cref="CountIf"/>.</summary>
public sealed class CountIfProvider : IH2AggregateProvider
{
    private static readonly AggregateOptions Defaults = new() { Deterministic = true };

    /// <summary>Creates the provider.</summary>
    /// <param name="options">metadata overriding the defaults, or null</param>
    public CountIfProvider(AggregateOptions? options = null) => Options = options ?? Defaults;

    /// <inheritdoc/>
    public AggregateOptions Options { get; }

    /// <inheritdoc/>
    public IH2Aggregate CreateAggregate() => new CountIf();
}
