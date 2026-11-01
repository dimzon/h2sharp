/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp.Data.Extensions;
using H2Sharp.Values;

namespace H2Sharp.Extensions;

/// <summary>
/// Concatenates the non-NULL values of a group with a separator.
/// </summary>
/// <remarks>
/// The aggregate takes two arguments: the value and the separator. The separator is read
/// from the first row; NULL values are skipped, matching LISTAGG / STRING_AGG. The result is
/// NULL when every value is NULL.
/// </remarks>
public sealed class StringAgg : CollectingAggregateBase
{
    /// <summary>
    /// Creates the aggregate.
    /// </summary>
    public StringAgg()
        : base(2)
    {
    }

    /// <inheritdoc/>
    public override TypeInfo GetResultType(IReadOnlyList<TypeInfo> inputTypes)
    {
        if (inputTypes.Count != 2)
        {
            throw new ArgumentException("STRING_AGG requires two arguments");
        }

        return TypeInfo.TypeVarchar;
    }

    /// <inheritdoc/>
    protected override object? Compute(IReadOnlyList<object?> values, int argumentCount)
    {
        if (values.Count == 0)
        {
            return null;
        }

        string separator = Convert.ToString(values[1], CultureInfo.InvariantCulture) ?? string.Empty;
        StringBuilder builder = new();
        bool first = true;
        for (int i = 0; i < values.Count; i += argumentCount)
        {
            object? value = values[i];
            if (value is null)
            {
                continue;
            }

            if (!first)
            {
                builder.Append(separator);
            }

            builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            first = false;
        }

        return first ? null : builder.ToString();
    }
}
