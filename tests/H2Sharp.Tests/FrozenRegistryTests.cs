/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The read-only registries are probed on every parsed name — function, type, schema,
 * aggregate — so their lookups must answer without allocating and with the answers the
 * reference gives. The behaviour itself is oracle-covered through the SQL that resolves
 * these names; these tests pin what the frozen copies must keep: the comparers, the
 * instances they hand out, and the zero-allocation lookup.
 */
using System;
using System.Collections.Generic;
using H2Sharp;
using H2Sharp.Data;
using H2Sharp.Data.Extensions;
using H2Sharp.Engine;
using H2Sharp.Expressions.Aggregate;
using H2Sharp.Expressions.Functions;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// Pins the lookup behaviour of the frozen read-only registries.
/// </summary>
[Collection(MeasurementCollection.Name)]
public sealed class FrozenRegistryTests
{
    private static long Measure(Action action)
    {
        for (int i = 0; i < 3; i++)
        {
            action();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static H2Connection OpenMemoryConnection(string name)
    {
        H2Connection connection = new("Data Source=" + name + ";Storage=Memory");
        connection.Open();
        return connection;
    }

    [Fact]
    public void FunctionLookupIsCaseInsensitiveAndAllocationFree()
    {
        Assert.True(FunctionRegistry.TryGet("ABS", out FunctionRegistration upper));
        Assert.True(FunctionRegistry.TryGet("abs", out FunctionRegistration lower));
        Assert.Same(upper, lower);
        Assert.False(FunctionRegistry.TryGet("NOT_A_BUILT_IN_FUNCTION", out _));

        Assert.Equal(0, Measure(() => FunctionRegistry.TryGet("abs", out _)));
    }

    [Fact]
    public void TypeNameLookupIsOrdinalAndAllocationFree()
    {
        DataType? upper = DataType.GetTypeByName("VARCHAR");
        DataType? exact = DataType.GetTypeByName("VARCHAR2");
        Assert.NotNull(upper);
        Assert.NotNull(exact);

        // Ordinal: the reference matches type names as the standard writes them, and a
        // lowercase spelling is a different string that the parser folds before it arrives.
        Assert.Null(DataType.GetTypeByName("varchar"));

        Assert.Equal(0, Measure(() => DataType.GetTypeByName("VARCHAR")));
    }

    [Fact]
    public void SchemaLookupSeesAddedRenamedAndRemovedSchemas()
    {
        using H2Connection connection = OpenMemoryConnection("frozen_schemas_" + Guid.NewGuid().ToString("N"));
        Database database = connection.Session!.GetDatabase();

        Assert.NotNull(database.FindSchema("PUBLIC"));

        database.AddSchema(new H2Sharp.Schemas.Schema(database, 1000, "EXTRA", system: false));
        Assert.NotNull(database.FindSchema("EXTRA"));

        database.RenameDatabaseObject(
            connection.Session, database.FindSchema("EXTRA")!, "RENAMED");
        Assert.Null(database.FindSchema("EXTRA"));
        Assert.NotNull(database.FindSchema("RENAMED"));

        database.RemoveSchema(connection.Session, database.FindSchema("RENAMED")!);
        Assert.Null(database.FindSchema("RENAMED"));

        // And the hot read stays allocation free: every parsed schema name goes through here.
        Assert.Equal(0, Measure(() => database.FindSchema("PUBLIC")));
    }

    [Fact]
    public void RegisteredAggregateResolvesToTheSameRegistrationEveryTime()
    {
        // create → configure → Open: the ADR-001 registration order.
        using H2Connection connection = new("Data Source=frozen_aggregates_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        ((IH2Extensions)connection).RegisterAggregate("M", () => new TestAggregate());
        connection.Open();

        Assert.True(AggregateRegistry.TryGet(connection.Session!, "M", out AggregateRegistration? first));
        Assert.True(AggregateRegistry.TryGet(connection.Session, "m", out AggregateRegistration? second));

        // One registration per registered aggregate, not one per resolution: the old shape
        // built a ClrAggregateRegistration inside TryGet, which was an object on every
        // statement that named the aggregate.
        Assert.Same(first, second);
        Assert.Equal(0, Measure(() => AggregateRegistry.TryGet(connection.Session, "M", out _)));
    }

    /// <summary>
    /// The smallest host aggregate: it answers as an integer and aggregates nothing.
    /// </summary>
    private sealed class TestAggregate : IH2Aggregate
    {
        public void Init() { }

        public TypeInfo GetResultType(IReadOnlyList<TypeInfo> inputTypes) => TypeInfo.TypeInteger;

        public void Accumulate(object? value) { }

        public object? GetResult() => 0;
    }
}
