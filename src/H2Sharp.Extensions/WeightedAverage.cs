/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using H2Sharp.Data.Extensions;
using H2Sharp.Values;

namespace H2Sharp.Extensions;

/// <summary>
/// Computes a weighted arithmetic mean.
/// </summary>
/// <remarks>
/// Arguments are the value and its non-negative weight. Rows with a NULL value or weight
/// are ignored. The result is NULL when the total weight is zero.
/// </remarks>
public sealed class WeightedAverage : AggregateBase
{
    private double weightedSum;
    private double totalWeight;

    /// <inheritdoc/>
    public override TypeInfo GetResultType(IReadOnlyList<TypeInfo> inputTypes)
    {
        if (inputTypes.Count != 2)
        {
            throw new ArgumentException("WEIGHTED_AVERAGE requires two arguments");
        }

        return TypeInfo.TypeDouble;
    }

    /// <inheritdoc/>
    public override void Accumulate(object? value)
    {
        if (value is not object?[] row || row.Length != 2)
        {
            throw new ArgumentException(
                "WEIGHTED_AVERAGE requires value and weight arguments", nameof(value));
        }

        if (row[0] is null || row[1] is null)
        {
            return;
        }

        double number = Convert.ToDouble(row[0], CultureInfo.InvariantCulture);
        double weight = Convert.ToDouble(row[1], CultureInfo.InvariantCulture);
        if (weight < 0)
        {
            throw new ArgumentException("WEIGHTED_AVERAGE requires non-negative weights", nameof(value));
        }

        weightedSum += number * weight;
        totalWeight += weight;
    }

    /// <inheritdoc/>
    public override object? GetResult() => totalWeight == 0 ? null : weightedSum / totalWeight;
}
