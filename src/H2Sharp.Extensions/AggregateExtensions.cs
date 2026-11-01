/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using H2Sharp.Data;
using H2Sharp.Data.Extensions;

namespace H2Sharp.Extensions;

/// <summary>
/// Registers the aggregate examples and utilities supplied by H2Sharp.Extensions.
/// </summary>
public static class AggregateExtensions
{
    /// <summary>The SQL name of <see cref="StringAgg"/> by default.</summary>
    public const string DefaultStringAggName = "H2_STRING_AGG";

    /// <summary>The SQL name of <see cref="GeometricMean"/> by default.</summary>
    public const string DefaultGeometricMeanName = "H2_GEOMETRIC_MEAN";

    /// <summary>The SQL name of <see cref="Product"/> by default.</summary>
    public const string DefaultProductName = "H2_PRODUCT";

    /// <summary>The SQL name of <see cref="HarmonicMean"/> by default.</summary>
    public const string DefaultHarmonicMeanName = "H2_HARMONIC_MEAN";

    /// <summary>The SQL name of <see cref="WeightedAverage"/> by default.</summary>
    public const string DefaultWeightedAverageName = "H2_WEIGHTED_AVERAGE";

    /// <summary>The SQL name of <see cref="CountIf"/> by default.</summary>
    public const string DefaultCountIfName = "H2_COUNT_IF";

    /// <summary>
    /// Registers every package aggregate under its default name.
    /// </summary>
    /// <param name="extensions">the closed connection's extension surface</param>
    public static void RegisterH2Aggregates(this IH2Extensions extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        extensions.RegisterStringAgg();
        extensions.RegisterGeometricMean();
        extensions.RegisterProduct();
        extensions.RegisterHarmonicMean();
        extensions.RegisterWeightedAverage();
        extensions.RegisterCountIf();
    }

    /// <summary>
    /// Registers <see cref="StringAgg"/>.
    /// </summary>
    /// <param name="extensions">the closed connection's extension surface</param>
    /// <param name="name">the SQL name, <c>H2_STRING_AGG</c> by default</param>
    /// <remarks>
    /// The built-in <c>STRING_AGG</c> keeps that name; register this aggregate under a
    /// different name if both should be available.
    /// </remarks>
    public static void RegisterStringAgg(this IH2Extensions extensions, string name = DefaultStringAggName, AggregateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        extensions.RegisterAggregate(name, new StringAggProvider(options));
    }

    /// <summary>
    /// Registers <see cref="GeometricMean"/>.
    /// </summary>
    /// <param name="extensions">the closed connection's extension surface</param>
    /// <param name="name">the SQL name, <c>H2_GEOMETRIC_MEAN</c> by default</param>
    public static void RegisterGeometricMean(this IH2Extensions extensions, string name = DefaultGeometricMeanName, AggregateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        extensions.RegisterAggregate(name, new GeometricMeanProvider(options));
    }

    /// <summary>
    /// Registers <see cref="Product"/>.
    /// </summary>
    /// <param name="extensions">the closed connection's extension surface</param>
    /// <param name="name">the SQL name, <c>H2_PRODUCT</c> by default</param>
    public static void RegisterProduct(this IH2Extensions extensions, string name = DefaultProductName, AggregateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        extensions.RegisterAggregate(name, new ProductProvider(options));
    }

    /// <summary>
    /// Registers <see cref="HarmonicMean"/>.
    /// </summary>
    /// <param name="extensions">the closed connection's extension surface</param>
    /// <param name="name">the SQL name, <c>H2_HARMONIC_MEAN</c> by default</param>
    public static void RegisterHarmonicMean(this IH2Extensions extensions, string name = DefaultHarmonicMeanName, AggregateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        extensions.RegisterAggregate(name, new HarmonicMeanProvider(options));
    }

    /// <summary>
    /// Registers <see cref="WeightedAverage"/>.
    /// </summary>
    /// <param name="extensions">the closed connection's extension surface</param>
    /// <param name="name">the SQL name, <c>H2_WEIGHTED_AVERAGE</c> by default</param>
    public static void RegisterWeightedAverage(this IH2Extensions extensions, string name = DefaultWeightedAverageName, AggregateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        extensions.RegisterAggregate(name, new WeightedAverageProvider(options));
    }

    /// <summary>
    /// Registers <see cref="CountIf"/>.
    /// </summary>
    /// <param name="extensions">the closed connection's extension surface</param>
    /// <param name="name">the SQL name, <c>H2_COUNT_IF</c> by default</param>
    public static void RegisterCountIf(this IH2Extensions extensions, string name = DefaultCountIfName, AggregateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        extensions.RegisterAggregate(name, new CountIfProvider(options));
    }
}
