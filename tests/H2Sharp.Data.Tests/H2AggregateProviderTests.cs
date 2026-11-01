/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Provider-first aggregate registration, metadata, and opt-in disposal.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Data.Extensions;
using H2Sharp.Extensions;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2AggregateProviderTests
{
    [Fact]
    public void ProviderRegistrationUsesCreateAggregate()
    {
        using H2Connection connection = NewConnection();
        CountingProvider provider = new();
        ((IH2Extensions)connection).RegisterAggregate("CUSTOM_GEO", provider);
        connection.Open();

        Assert.Equal("4", Scalar(connection, "SELECT CUSTOM_GEO(X) FROM SYSTEM_RANGE(4, 4)"));
        Assert.Equal(1, provider.OptionsReads);
        Assert.Equal(2, provider.Created);
    }

    [Fact]
    public void GenericRegistrationCreatesInstancesWithoutADelegate()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterAggregate<GeometricMean>("CUSTOM_GEO");
        connection.Open();

        Assert.Equal("4", Scalar(connection, "SELECT CUSTOM_GEO(X) FROM SYSTEM_RANGE(4, 4)"));
    }

    [Fact]
    public void ProviderOptionsAreSnapshottedAtRegistration()
    {
        using H2Connection connection = NewConnection();
        MutableOptionsProvider provider = new(new AggregateOptions { Deterministic = true });
        ((IH2Extensions)connection).RegisterAggregate("CUSTOM_GEO", provider);
        provider.Options = AggregateOptions.Default;
        connection.Open();

        Assert.Equal("4", Scalar(connection, "SELECT CUSTOM_GEO(X) FROM SYSTEM_RANGE(4, 4)"));
    }

    [Fact]
    public void ProviderInvariantToDuplicatesSkipsDistinctCollection()
    {
        MaxWithCounter.Reset();
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterAggregate(
            "CUSTOM_MAX",
            new DelegateProvider(
                () => new MaxWithCounter(),
                new AggregateOptions { InvariantToDuplicates = true }));
        connection.Open();
        Execute(connection, "CREATE TABLE T(V INT)");
        Execute(connection, "INSERT INTO T VALUES (1), (1), (2), (2), (3)");

        Assert.Equal("3", Scalar(connection, "SELECT CUSTOM_MAX(DISTINCT V) FROM T"));
        Assert.Equal(5, MaxWithCounter.AccumulateCalls);
    }

    [Fact]
    public void DisposableAggregateIsDisposedAtQueryEnd()
    {
        foreach (string sql in new[]
        {
            "SELECT D(V) FROM T",
            "SELECT D(DISTINCT V) FROM T",
            "SELECT D(V) FROM T WHERE FALSE",
            "SELECT D(V) OVER () FROM T",
            "SELECT D(V) OVER (ORDER BY V) FROM T",
        })
        {
            DisposableCounter.Reset();
            using H2Connection connection = NewConnection();
            ((IH2Extensions)connection).RegisterAggregate("D", new DelegateProvider(() => new DisposableCounter()));
            connection.Open();
            Execute(connection, "CREATE TABLE T(V INT)");
            Execute(connection, "INSERT INTO T VALUES (1), (2), (3)");

            using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            using DbDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
            }

            Assert.Equal(1, DisposableCounter.DisposeCalls);
        }
    }

    [Fact]
    public void DisposableAggregateIsDisposedWhenAccumulateThrows()
    {
        DisposableThrower.Reset();
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterAggregate("D", new DelegateProvider(() => new DisposableThrower()));
        connection.Open();
        Execute(connection, "CREATE TABLE T(V INT)");
        Execute(connection, "INSERT INTO T VALUES (1)");

        Assert.ThrowsAny<Exception>(() => Scalar(connection, "SELECT D(V) FROM T"));
        Assert.Equal(1, DisposableThrower.DisposeCalls);
    }

    private static H2Connection NewConnection()
        => new($"Data Source=agg_provider_{Guid.NewGuid():N};IGNORE_UNKNOWN_SETTINGS=TRUE;Storage=Memory");

    private static void Execute(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Scalar(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value is null or DBNull
            ? "-"
            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "-";
    }

    private sealed class CountingProvider : IH2AggregateProvider
    {
        internal int OptionsReads;
        internal int Created;

        public AggregateOptions Options
        {
            get
            {
                OptionsReads++;
                return AggregateOptions.Default;
            }
        }

        public IH2Aggregate CreateAggregate()
        {
            Created++;
            return new GeometricMean();
        }
    }

    private sealed class MutableOptionsProvider : IH2AggregateProvider
    {
        public MutableOptionsProvider(AggregateOptions options) => Options = options;

        public AggregateOptions Options { get; set; }

        public IH2Aggregate CreateAggregate() => new GeometricMean();
    }

    private sealed class DelegateProvider : IH2AggregateProvider
    {
        private readonly Func<IH2Aggregate> factory;

        internal DelegateProvider(Func<IH2Aggregate> factory, AggregateOptions? options = null)
        {
            this.factory = factory;
            Options = options ?? AggregateOptions.Default;
        }

        public AggregateOptions Options { get; }

        public IH2Aggregate CreateAggregate() => factory();
    }

    private sealed class MaxWithCounter : AggregateBase
    {
        internal static int AccumulateCalls;
        internal static void Reset() => AccumulateCalls = 0;

        private int? max;

        public override TypeInfo GetResultType(IReadOnlyList<TypeInfo> inputTypes)
            => TypeInfo.TypeInteger;

        public override void Accumulate(object? value)
        {
            AccumulateCalls++;
            if (value is null)
            {
                return;
            }

            int number = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            if (max is null || number > max)
            {
                max = number;
            }
        }

        public override object? GetResult() => max;
    }

    private sealed class DisposableCounter : AggregateBase, IDisposable
    {
        internal static int DisposeCalls;
        internal static void Reset() => DisposeCalls = 0;

        private long count;

        public override TypeInfo GetResultType(IReadOnlyList<TypeInfo> inputTypes)
            => TypeInfo.TypeBigint;

        public override void Accumulate(object? value) => count++;

        public override object? GetResult() => count;

        public void Dispose() => DisposeCalls++;
    }

    private sealed class DisposableThrower : AggregateBase, IDisposable
    {
        internal static int DisposeCalls;
        internal static void Reset() => DisposeCalls = 0;

        public override TypeInfo GetResultType(IReadOnlyList<TypeInfo> inputTypes)
            => TypeInfo.TypeBigint;

        public override void Accumulate(object? value)
            => throw new InvalidOperationException("accumulate failed");

        public override object? GetResult() => 0L;

        public void Dispose() => DisposeCalls++;
    }
}
