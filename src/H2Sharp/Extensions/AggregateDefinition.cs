/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Data.Extensions;
using H2Sharp.Expressions.Aggregate;

namespace H2Sharp.Extensions;

/// <summary>One registered host-provided aggregate.</summary>
internal sealed class AggregateDefinition
{
    internal AggregateDefinition(string name, IH2AggregateProvider provider)
    {
        Name = name;
        Provider = provider;
        Options = provider.Options ?? AggregateOptions.Default;
        Registration = new ClrAggregateRegistration(this, isBuiltIn: false);
    }

    internal string Name { get; }

    internal IH2AggregateProvider Provider { get; }

    internal AggregateOptions Options { get; }

    /// <summary>
    /// The parser-facing registration of this aggregate, built once here instead of on
    /// every name resolution: registering an aggregate is a rare event and resolving its
    /// name happens on every statement that uses it.
    /// </summary>
    internal ClrAggregateRegistration Registration { get; }
}
