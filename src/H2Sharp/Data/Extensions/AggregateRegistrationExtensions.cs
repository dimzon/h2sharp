/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;

namespace H2Sharp.Data.Extensions;

/// <summary>
/// Delegate and generic convenience overloads over the provider-based registration.
/// </summary>
public static class AggregateRegistrationExtensions
{
    /// <summary>
    /// Registers an aggregate created by a delegate.
    /// </summary>
    /// <param name="extensions">the closed connection's extension surface</param>
    /// <param name="name">the SQL name</param>
    /// <param name="factory">the aggregate factory</param>
    /// <param name="options">optimizer metadata, or null for the conservative defaults</param>
    public static void RegisterAggregate(
        this IH2Extensions extensions,
        string name,
        Func<IH2Aggregate> factory,
        AggregateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        extensions.RegisterAggregate(name, new AggregateProvider(factory, options));
    }

    /// <summary>
    /// Registers an aggregate by its parameterless type.
    /// </summary>
    /// <typeparam name="T">the aggregate type</typeparam>
    /// <param name="extensions">the closed connection's extension surface</param>
    /// <param name="name">the SQL name</param>
    /// <param name="options">optimizer metadata, or null for the conservative defaults</param>
    public static void RegisterAggregate<T>(
        this IH2Extensions extensions,
        string name,
        AggregateOptions? options = null)
        where T : IH2Aggregate, new()
    {
        ArgumentNullException.ThrowIfNull(extensions);
        extensions.RegisterAggregate(name, new AggregateProvider<T>(options));
    }
}
