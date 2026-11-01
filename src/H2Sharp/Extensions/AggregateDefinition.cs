/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Data.Extensions;

namespace H2Sharp.Extensions;

/// <summary>One registered host-provided aggregate.</summary>
internal sealed class AggregateDefinition
{
    internal AggregateDefinition(string name, IH2AggregateProvider provider)
    {
        Name = name;
        Provider = provider;
        Options = provider.Options ?? AggregateOptions.Default;
    }

    internal string Name { get; }

    internal IH2AggregateProvider Provider { get; }

    internal AggregateOptions Options { get; }
}
