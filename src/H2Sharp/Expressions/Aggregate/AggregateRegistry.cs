using System.Collections.Frozen;
/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using H2Sharp.Command;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Extensions;

namespace H2Sharp.Expressions.Aggregate;

/// <summary>
/// One aggregate name and the parser entry that reads its invocation.
/// </summary>
internal abstract class AggregateRegistration
{
    protected AggregateRegistration(string name, bool isBuiltIn)
    {
        Name = name;
        IsBuiltIn = isBuiltIn;
    }

    internal string Name { get; }

    internal bool IsBuiltIn { get; }

    internal abstract Expression Read(Parser parser, Select select);
}

internal sealed class BuiltInAggregateRegistration : AggregateRegistration
{
    private readonly AggregateType aggregateType;

    internal BuiltInAggregateRegistration(string name, AggregateType aggregateType)
        : base(name, isBuiltIn: true)
    {
        this.aggregateType = aggregateType;
    }

    internal override Expression Read(Parser parser, Select select)
        => parser.ReadBuiltInAggregateCore(aggregateType, Name, select);
}

internal sealed class ClrAggregateRegistration : AggregateRegistration
{
    private readonly int baseCost;
    private readonly bool rendersDistinct;
    private readonly bool readsDistinct;

    internal ClrAggregateRegistration(
        AggregateDefinition definition,
        bool isBuiltIn,
        int baseCost = 5,
        bool rendersDistinct = false,
        bool readsDistinct = true)
        : base(definition.Name, isBuiltIn)
    {
        Definition = definition;
        this.baseCost = baseCost;
        this.rendersDistinct = rendersDistinct;
        this.readsDistinct = readsDistinct;
    }

    internal AggregateDefinition Definition { get; }

    internal override Expression Read(Parser parser, Select select)
        => parser.ReadClrAggregateCore(Definition, select, baseCost, rendersDistinct, readsDistinct);
}

/// <summary>
/// The aggregate names of one engine: built-ins are static, registered aggregates come
/// from the opened session. A registered aggregate wins over a built-in with the same name.
/// </summary>
internal static class AggregateRegistry
{
    private static readonly FrozenDictionary<string, AggregateRegistration> BuiltIns = CreateBuiltIns();

    internal static bool TryGet(
        SessionLocal? session, string name, out AggregateRegistration? registration)
    {
        if (session?.Extensions.FindAggregate(name) is { } custom)
        {
            registration = custom.Registration;
            return true;
        }

        return BuiltIns.TryGetValue(name, out registration);
    }

    private static FrozenDictionary<string, AggregateRegistration> CreateBuiltIns()
    {
        Dictionary<string, AggregateRegistration> registrations = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, AggregateType type) in Aggregate.GetAggregateTypes())
        {
            registrations[name] = BuiltInClrAggregates.Get(type) is { } clr
                ? new ClrAggregateRegistration(
                    clr,
                    isBuiltIn: true,
                    baseCost: 1,
                    rendersDistinct: true,
                    readsDistinct: type != AggregateType.RegrCount)
                : new BuiltInAggregateRegistration(name, type);
        }

        return registrations.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
