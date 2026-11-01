/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using H2Sharp.Data.Extensions;

namespace H2Sharp.Extensions;

/// <summary>One registered host-provided aggregate.</summary>
internal sealed class AggregateDefinition
{
    internal AggregateDefinition(string name, Func<IH2Aggregate> factory)
    {
        Name = name;
        Factory = factory;
    }

    internal string Name { get; }

    internal Func<IH2Aggregate> Factory { get; }
}
