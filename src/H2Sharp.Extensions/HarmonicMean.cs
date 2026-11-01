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
/// Computes the harmonic mean of non-negative numeric values.
/// </summary>
/// <remarks>
/// NULL values are ignored. A negative value is an error; a zero value makes the result
/// zero. The result is NULL when every value is NULL.
/// </remarks>
public sealed class HarmonicMean : AggregateBase
{
    private double reciprocalSum;
    private long count;
    private bool hasZero;

    /// <inheritdoc/>
    public override TypeInfo GetResultType(IReadOnlyList<TypeInfo> inputTypes)
    {
        if (inputTypes.Count != 1)
        {
            throw new ArgumentException("HARMONIC_MEAN requires one argument");
        }

        return TypeInfo.TypeDouble;
    }

    /// <inheritdoc/>
    public override void Accumulate(object? value)
    {
        if (value is null)
        {
            return;
        }

        double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (number < 0)
        {
            throw new ArgumentException("Harmonic mean requires non-negative values", nameof(value));
        }

        if (number == 0)
        {
            hasZero = true;
        }
        else
        {
            reciprocalSum += 1.0 / number;
        }

        count++;
    }

    /// <inheritdoc/>
    public override object? GetResult()
    {
        if (count == 0)
        {
            return null;
        }

        return hasZero ? 0.0 : count / reciprocalSum;
    }
}
