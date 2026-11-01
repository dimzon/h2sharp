/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Collections.Generic;
using H2Sharp.Values;

namespace H2Sharp.Data.Extensions;

/// <summary>
/// A minimal skeleton for a host-provided aggregate.
/// </summary>
public abstract class AggregateBase : IH2Aggregate
{
    /// <inheritdoc/>
    public virtual void Init()
    {
    }

    /// <inheritdoc/>
    public abstract TypeInfo GetResultType(IReadOnlyList<TypeInfo> inputTypes);

    /// <inheritdoc/>
    public abstract void Accumulate(object? value);

    /// <inheritdoc/>
    public abstract object? GetResult();
}
