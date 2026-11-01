/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using H2Sharp.Data.Extensions;
using H2Sharp.Values;

namespace H2Sharp.Extensions;

/// <summary>
/// Counts rows for which a boolean argument is TRUE.
/// </summary>
/// <remarks>
/// NULL and FALSE are not counted. An empty group returns zero, like COUNT.
/// </remarks>
public sealed class CountIf : AggregateBase
{
    private long count;

    /// <inheritdoc/>
    public override TypeInfo GetResultType(IReadOnlyList<TypeInfo> inputTypes)
    {
        if (inputTypes.Count != 1)
        {
            throw new ArgumentException("COUNT_IF requires one argument");
        }

        return TypeInfo.TypeBigint;
    }

    /// <inheritdoc/>
    public override void Accumulate(object? value)
    {
        if (value is null)
        {
            return;
        }

        if (value is not bool condition)
        {
            throw new ArgumentException("COUNT_IF requires a boolean argument", nameof(value));
        }

        if (condition)
        {
            count++;
        }
    }

    /// <inheritdoc/>
    public override object? GetResult() => count;
}
