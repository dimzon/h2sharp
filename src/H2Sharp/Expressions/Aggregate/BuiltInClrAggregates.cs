/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Built-in aggregates that are simple value accumulators and therefore can use the
 * host-provided aggregate engine without losing a fast path or a precise result type.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using H2Sharp.Data.Extensions;
using H2Sharp.Extensions;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Aggregate;

internal static class BuiltInClrAggregates
{
    private static readonly AggregateDefinition AnyDefinition = Create(
        "ANY", static () => new BooleanAggregate(every: false), invariantToDuplicates: true);

    private static readonly AggregateDefinition EveryDefinition = Create(
        "EVERY", static () => new BooleanAggregate(every: true), invariantToDuplicates: true);

    private static readonly AggregateDefinition RegrCountDefinition = Create(
        "REGR_COUNT", static () => new RegrCountAggregate(), invariantToDuplicates: false);

    internal static AggregateDefinition? Get(AggregateType type) => type switch
    {
        AggregateType.Any => AnyDefinition,
        AggregateType.Every => EveryDefinition,
        AggregateType.RegrCount => RegrCountDefinition,
        _ => null,
    };

    private static AggregateDefinition Create(
        string name, Func<IH2Aggregate> factory, bool invariantToDuplicates)
    {
        AggregateOptions options = new()
        {
            Deterministic = true,
            InvariantToDuplicates = invariantToDuplicates,
        };
        return new AggregateDefinition(name, new AggregateProvider(factory, options));
    }

    private sealed class BooleanAggregate : AggregateBase
    {
        private readonly bool every;
        private bool? value;

        internal BooleanAggregate(bool every)
        {
            this.every = every;
        }

        public override TypeInfo GetResultType(IReadOnlyList<TypeInfo> inputTypes)
        {
            if (inputTypes.Count != 1)
            {
                throw new ArgumentException("ANY and EVERY require one argument");
            }

            return TypeInfo.TypeBoolean;
        }

        public override void Accumulate(object? value)
        {
            if (value is null)
            {
                return;
            }

            bool current = value is bool flag
                ? flag
                : Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            this.value = this.value is null
                ? current
                : every ? this.value.Value && current : this.value.Value || current;
        }

        public override object? GetResult() => value;
    }

    private sealed class RegrCountAggregate : AggregateBase
    {
        private long count;

        public override TypeInfo GetResultType(IReadOnlyList<TypeInfo> inputTypes)
        {
            if (inputTypes.Count != 2)
            {
                throw new ArgumentException("REGR_COUNT requires two arguments");
            }

            return TypeInfo.TypeBigint;
        }

        public override void Accumulate(object? value)
        {
            if (value is not object?[] row || row.Length != 2)
            {
                throw new ArgumentException("REGR_COUNT requires two arguments", nameof(value));
            }

            if (row[0] is null || row[1] is null)
            {
                return;
            }

            count++;
        }

        public override object? GetResult() => count;
    }
}
