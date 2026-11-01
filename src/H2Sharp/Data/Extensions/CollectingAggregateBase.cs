/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;

namespace H2Sharp.Data.Extensions;

/// <summary>
/// An aggregate that keeps every input row and computes its result from the whole set.
/// </summary>
/// <remarks>
/// Values are stored flat: a single-argument aggregate adds one value per row, while a
/// multi-argument aggregate adds one array per row and appends its elements. The argument
/// count is supplied by the concrete aggregate because a single SQL ARRAY argument and a
/// multi-argument row are both represented by an array.
/// </remarks>
public abstract class CollectingAggregateBase : AggregateBase
{
    private readonly List<object?> values = [];
    private readonly int argumentCount;

    /// <summary>
    /// Creates a collecting aggregate.
    /// </summary>
    /// <param name="argumentCount">the number of arguments, at least one</param>
    protected CollectingAggregateBase(int argumentCount)
    {
        if (argumentCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(argumentCount));
        }

        this.argumentCount = argumentCount;
    }

    /// <summary>
    /// The number of arguments.
    /// </summary>
    protected int ArgumentCount => argumentCount;

    /// <summary>
    /// The number of collected rows.
    /// </summary>
    protected int RowCount => values.Count / argumentCount;

    /// <inheritdoc/>
    public sealed override void Accumulate(object? value)
    {
        if (argumentCount == 1)
        {
            values.Add(value);
            return;
        }

        if (value is not object?[] row)
        {
            throw new ArgumentException(
                "A multi-argument aggregate must receive an argument array", nameof(value));
        }

        if (row.Length != argumentCount)
        {
            throw new ArgumentException(
                "Expected " + argumentCount + " argument(s), received " + row.Length, nameof(value));
        }

        values.AddRange(row);
    }

    /// <inheritdoc/>
    public sealed override object? GetResult() => Compute(values, argumentCount);

    /// <summary>
    /// Computes the aggregate result from the collected values.
    /// </summary>
    /// <param name="values">the arguments, row after row</param>
    /// <param name="argumentCount">the number of arguments per row</param>
    /// <returns>the aggregated value</returns>
    protected abstract object? Compute(IReadOnlyList<object?> values, int argumentCount);
}
