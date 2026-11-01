/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Host-provided aggregate functions through the ADO.NET provider.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Text;
using H2Sharp.Data;
using H2Sharp.Data.Extensions;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2AggregateTests
{
    [Fact]
    public void ARegisteredAggregateIsCallableFromSql()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterAggregate("CUSTOM_GEO", () => new GeometricMean());
        connection.Open();
        Execute(connection, "CREATE TABLE T(V DOUBLE)");
        Execute(connection, "INSERT INTO T VALUES (1), (4)");

        Assert.Equal("2", Scalar(connection, "SELECT CUSTOM_GEO(V) FROM T"));
    }

    [Fact]
    public void RegistrationIsOnlyAllowedWhileClosed()
    {
        using H2Connection connection = NewConnection();
        connection.Open();

        Assert.Throws<InvalidOperationException>(
            () => ((IH2Extensions)connection).RegisterAggregate("CUSTOM_GEO", () => new GeometricMean()));
    }

    [Fact]
    public void RegistrationAfterDisposeIsRefused()
    {
        H2Connection connection = NewConnection();
        connection.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => ((IH2Extensions)connection).RegisterAggregate("CUSTOM_GEO", () => new GeometricMean()));
    }

    [Fact]
    public void IndependentDatabasesDoNotShareAggregates()
    {
        using H2Connection first = NewConnection();
        ((IH2Extensions)first).RegisterAggregate("CUSTOM_GEO", () => new GeometricMean());
        first.Open();

        using H2Connection second = NewConnection();
        second.Open();

        Assert.Equal("4", Scalar(first, "SELECT CUSTOM_GEO(X) FROM SYSTEM_RANGE(4, 4)"));
        Assert.ThrowsAny<Exception>(
            () => Scalar(second, "SELECT CUSTOM_GEO(X) FROM SYSTEM_RANGE(4, 4)"));
    }

    [Fact]
    public void CollectingAggregateSupportsSingleAndMultipleArguments()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterAggregate("CUSTOM_MEDIAN", () => new Median());
        ((IH2Extensions)connection).RegisterAggregate("CUSTOM_STRAGG", () => new StrAgg());
        connection.Open();
        Execute(connection, "CREATE TABLE T(V INT)");
        Execute(connection, "INSERT INTO T VALUES (3), (1), (2)");

        Assert.Equal("2", Scalar(connection, "SELECT CUSTOM_MEDIAN(V) FROM T"));
        Assert.Equal("3,1,2", Scalar(connection, "SELECT CUSTOM_STRAGG(V, ',') FROM T"));
    }

    [Fact]
    public void DistinctFilterAndOverUseTheReferencePaths()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterAggregate("CUSTOM_MEDIAN", () => new Median());
        connection.Open();
        Execute(connection, "CREATE TABLE T(V INT)");
        Execute(connection, "INSERT INTO T VALUES (1), (1), (1), (2), (3)");

        Assert.Equal("1", Scalar(connection, "SELECT CUSTOM_MEDIAN(V) FROM T"));
        Assert.Equal("2", Scalar(connection, "SELECT CUSTOM_MEDIAN(DISTINCT V) FROM T"));
        Assert.Equal("3", Scalar(connection, "SELECT CUSTOM_MEDIAN(V) FILTER (WHERE V > 2) FROM T"));

        Execute(connection, "CREATE TABLE W(V INT)");
        Execute(connection, "INSERT INTO W VALUES (1), (2), (3), (4), (5)");
        string[] window = Column(connection, "SELECT CUSTOM_MEDIAN(V) OVER (ORDER BY V) FROM W ORDER BY V");
        Assert.Equal(["1", "2", "2", "3", "3"], window);
    }

    [Fact]
    public void InvariantToDuplicatesSkipsDistinctCollection()
    {
        MaxWithCounter.Reset();
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterAggregate("CUSTOM_MAX", () => new MaxWithCounter());
        connection.Open();
        Execute(connection, "CREATE TABLE T(V INT)");
        Execute(connection, "INSERT INTO T VALUES (1), (1), (2), (2), (3)");

        Assert.Equal("3", Scalar(connection, "SELECT CUSTOM_MAX(DISTINCT V) FROM T"));
        Assert.Equal(3, MaxWithCounter.AccumulateCalls);

        MaxWithCounter.Reset();
        using H2Connection invariantConnection = NewConnection();
        ((IH2Extensions)invariantConnection).RegisterAggregate(
            "CUSTOM_MAX",
            () => new MaxWithCounter(),
            new AggregateOptions { InvariantToDuplicates = true });
        invariantConnection.Open();
        Execute(invariantConnection, "CREATE TABLE T(V INT)");
        Execute(invariantConnection, "INSERT INTO T VALUES (1), (1), (2), (2), (3)");

        Assert.Equal("3", Scalar(invariantConnection, "SELECT CUSTOM_MAX(DISTINCT V) FROM T"));
        Assert.Equal(5, MaxWithCounter.AccumulateCalls);
    }

    [Fact]
    public void GetResultCanBeEvaluatedMoreThanOnce()
    {
        Median median = new();
        median.Accumulate(3);
        median.Accumulate(1);
        median.Accumulate(2);

        Assert.Equal("2", Convert.ToString(median.GetResult(), CultureInfo.InvariantCulture));
        Assert.Equal("2", Convert.ToString(median.GetResult(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void EmptyGroupCallsGetResultOnAFreshInstance()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterAggregate("CUSTOM_MEDIAN", () => new Median());
        connection.Open();

        Assert.Equal("-", Scalar(connection, "SELECT CUSTOM_MEDIAN(X) FROM SYSTEM_RANGE(1, 3) WHERE FALSE"));
    }

    [Fact]
    public void NullArgumentsArePassedToTheAggregate()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterAggregate("CUSTOM_GEO", () => new GeometricMean());
        connection.Open();
        Execute(connection, "CREATE TABLE T(V DOUBLE)");
        Execute(connection, "INSERT INTO T VALUES (1), (NULL), (4)");

        Assert.Equal("2", Scalar(connection, "SELECT CUSTOM_GEO(V) FROM T"));
    }

    [Fact]
    public void WrongArityIsReportedByTheAggregate()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterAggregate("CUSTOM_MEDIAN", () => new Median());
        connection.Open();

        Assert.ThrowsAny<Exception>(() => Scalar(connection, "SELECT CUSTOM_MEDIAN(1, 2)"));
        Assert.ThrowsAny<Exception>(() => Scalar(connection, "SELECT CUSTOM_MEDIAN()"));
    }

    [Fact]
    public void BuiltinNamesWinOverRegisteredAggregates()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterAggregate("MEDIAN", () => new ConstantZeroAggregate());
        connection.Open();

        Assert.Equal("2", Scalar(connection, "SELECT MEDIAN(X) FROM SYSTEM_RANGE(1, 3)"));
    }

    [Fact]
    public void UdfAndAggregateNamesCannotCollide()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterAggregate("SAME_NAME", () => new GeometricMean());

        Assert.Throws<InvalidOperationException>(
            () => ((IH2Extensions)connection).RegisterUdf<int, int>("SAME_NAME", value => value));
    }

    [Fact]
    public void DistinctReplaysValuesOnlyWhenTheResultIsRequested()
    {
        DistinctCounter.Reset();
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterAggregate("CUSTOM_COUNTER", () => new DistinctCounter());
        connection.Open();
        Execute(connection, "CREATE TABLE T(V INT)");
        Execute(connection, "INSERT INTO T VALUES (1), (1), (1), (2), (3)");

        Assert.Equal("3", Scalar(connection, "SELECT CUSTOM_COUNTER(DISTINCT V) FROM T"));
        Assert.Equal(3, DistinctCounter.AccumulateCalls);
    }

    private static H2Connection NewConnection()
        => new($"Data Source=agg_{Guid.NewGuid():N};IGNORE_UNKNOWN_SETTINGS=TRUE;Storage=Memory");

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

    private static string[] Column(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using DbDataReader reader = command.ExecuteReader();
        List<string> values = [];
        while (reader.Read())
        {
            values.Add(reader.IsDBNull(0)
                ? "-"
                : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? "-");
        }

        return [.. values];
    }

    private sealed class GeometricMean : AggregateBase
    {
        private double product = 1;
        private long count;

        public override TypeInfo GetResultType(IReadOnlyList<TypeInfo> inputTypes)
        {
            if (inputTypes.Count != 1)
            {
                throw new ArgumentException("CUSTOM_GEO requires one argument");
            }

            return TypeInfo.TypeDouble;
        }

        public override void Accumulate(object? value)
        {
            if (value is null)
            {
                return;
            }

            product *= Convert.ToDouble(value, CultureInfo.InvariantCulture);
            count++;
        }

        public override object? GetResult()
            => count == 0 ? null : Math.Pow(product, 1.0 / count);
    }

    private sealed class Median : CollectingAggregateBase
    {
        public Median()
            : base(1)
        {
        }

        public override TypeInfo GetResultType(IReadOnlyList<TypeInfo> inputTypes)
        {
            if (inputTypes.Count != 1)
            {
                throw new ArgumentException("CUSTOM_MEDIAN requires one argument");
            }

            return TypeInfo.TypeDouble;
        }

        protected override object? Compute(IReadOnlyList<object?> values, int argumentCount)
        {
            if (values.Count == 0)
            {
                return null;
            }

            double[] sorted = new double[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                sorted[i] = Convert.ToDouble(values[i], CultureInfo.InvariantCulture);
            }

            Array.Sort(sorted);
            return sorted[sorted.Length / 2];
        }
    }

    private sealed class StrAgg : CollectingAggregateBase
    {
        public StrAgg()
            : base(2)
        {
        }

        public override TypeInfo GetResultType(IReadOnlyList<TypeInfo> inputTypes)
            => TypeInfo.TypeVarchar;

        protected override object? Compute(IReadOnlyList<object?> values, int argumentCount)
        {
            if (values.Count == 0)
            {
                return null;
            }

            string separator = Convert.ToString(values[1], CultureInfo.InvariantCulture) ?? string.Empty;
            StringBuilder builder = new();
            for (int i = 0; i < values.Count; i += argumentCount)
            {
                if (i > 0)
                {
                    builder.Append(separator);
                }

                builder.Append(Convert.ToString(values[i], CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
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

    private sealed class ConstantZeroAggregate : AggregateBase
    {
        public override TypeInfo GetResultType(IReadOnlyList<TypeInfo> inputTypes)
            => TypeInfo.TypeInteger;

        public override void Accumulate(object? value)
        {
        }

        public override object? GetResult() => 0;
    }

    private sealed class DistinctCounter : AggregateBase
    {
        internal static int AccumulateCalls;

        internal static void Reset() => AccumulateCalls = 0;

        private long count;

        public override TypeInfo GetResultType(IReadOnlyList<TypeInfo> inputTypes)
            => TypeInfo.TypeBigint;

        public override void Accumulate(object? value)
        {
            count++;
            AccumulateCalls++;
        }

        public override object? GetResult() => count;
    }
}
