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
/// Multiplies the non-NULL numeric values of a group.
/// </summary>
/// <remarks>
/// NULL values are ignored. The result is NULL when every value is NULL.
/// </remarks>
public sealed class Product : AggregateBase
{
    private double product = 1;
    private long count;

    /// <inheritdoc/>
    public override TypeInfo GetResultType(IReadOnlyList<TypeInfo> inputTypes)
    {
        if (inputTypes.Count != 1)
        {
            throw new ArgumentException("PRODUCT requires one argument");
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

        product *= Convert.ToDouble(value, CultureInfo.InvariantCulture);
        count++;
    }

    /// <inheritdoc/>
    public override object? GetResult() => count == 0 ? null : product;
}
