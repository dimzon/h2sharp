/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The optional H2Sharp.Extensions aggregate package.
 */
using System;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Extensions;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2AggregatePackageTests
{
    [Fact]
    public void RegisterH2AggregatesAddsThePackageAggregates()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterH2Aggregates();
        connection.Open();
        Execute(connection, "CREATE TABLE T(V INT)");
        Execute(connection, "INSERT INTO T VALUES (1), (2), (3), (4)");

        Assert.Equal("1,2,3,4", Scalar(connection, "SELECT H2_STRING_AGG(V, ',') FROM T"));
        Assert.Equal("2", Scalar(connection, "SELECT H2_GEOMETRIC_MEAN(V) FROM T WHERE V IN (1, 4)"));
        Assert.Equal("24", Scalar(connection, "SELECT H2_PRODUCT(V) FROM T"));
        Assert.Equal("1.6", Scalar(connection, "SELECT H2_HARMONIC_MEAN(V) FROM T WHERE V IN (1, 4)"));
        Assert.Equal("2.5", Scalar(connection, "SELECT H2_WEIGHTED_AVERAGE(V, 2) FROM T"));
        Assert.Equal("2", Scalar(connection, "SELECT H2_COUNT_IF(V > 2) FROM T"));
    }

    [Fact]
    public void StringAggSkipsNullsAndHandlesAnEmptyGroup()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterH2Aggregates();
        connection.Open();
        Execute(connection, "CREATE TABLE T(V INT)");
        Execute(connection, "INSERT INTO T VALUES (1), (NULL), (3)");

        Assert.Equal("1,3", Scalar(connection, "SELECT H2_STRING_AGG(V, ',') FROM T"));
        Assert.Equal("-", Scalar(connection, "SELECT H2_STRING_AGG(V, ',') FROM T WHERE FALSE"));
        Assert.Equal("-", Scalar(connection, "SELECT H2_STRING_AGG(V, ',') FROM T WHERE V IS NULL"));
    }

    [Fact]
    public void StringAggSupportsDistinctFilterAndACustomName()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterStringAgg("CUSTOM_STRAGG");
        connection.Open();
        Execute(connection, "CREATE TABLE T(V INT)");
        Execute(connection, "INSERT INTO T VALUES (1), (1), (2), (3)");

        Assert.Equal("1,2,3", Scalar(connection, "SELECT CUSTOM_STRAGG(DISTINCT V, ',') FROM T"));
        Assert.Equal("2,3", Scalar(connection, "SELECT CUSTOM_STRAGG(V, ',') FILTER (WHERE V > 1) FROM T"));
    }

    [Fact]
    public void ProductWeightedAverageAndCountIfHandleEmptyAndNullInputs()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterH2Aggregates();
        connection.Open();
        Execute(connection, "CREATE TABLE T(V INT, W INT)");
        Execute(connection, "INSERT INTO T VALUES (1, 1), (NULL, 1), (3, NULL), (3, 1)");

        Assert.Equal("9", Scalar(connection, "SELECT H2_PRODUCT(V) FROM T"));
        Assert.Equal("2", Scalar(connection, "SELECT H2_WEIGHTED_AVERAGE(V, W) FROM T"));
        Assert.Equal("2", Scalar(connection, "SELECT H2_COUNT_IF(V > 1) FROM T"));
        Assert.Equal("-", Scalar(connection, "SELECT H2_PRODUCT(V) FROM T WHERE FALSE"));
        Assert.Equal("0", Scalar(connection, "SELECT H2_COUNT_IF(V > 1) FROM T WHERE FALSE"));
        Assert.Equal("-", Scalar(connection, "SELECT H2_WEIGHTED_AVERAGE(V, W) FROM T WHERE FALSE"));
    }

    [Fact]
    public void WeightedAverageRejectsNegativeWeights()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterH2Aggregates();
        connection.Open();
        Execute(connection, "CREATE TABLE T(V INT, W INT)");
        Execute(connection, "INSERT INTO T VALUES (1, -1)");

        Assert.ThrowsAny<Exception>(
            () => Scalar(connection, "SELECT H2_WEIGHTED_AVERAGE(V, W) FROM T"));
    }

    [Fact]
    public void GeometricMeanHandlesZeroAndRejectsNegativeValues()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterH2Aggregates();
        connection.Open();
        Execute(connection, "CREATE TABLE T(V DOUBLE)");
        Execute(connection, "INSERT INTO T VALUES (0), (4)");

        Assert.Equal("0", Scalar(connection, "SELECT H2_GEOMETRIC_MEAN(V) FROM T"));

        Execute(connection, "DELETE FROM T");
        Execute(connection, "INSERT INTO T VALUES (-1)");
        Assert.ThrowsAny<Exception>(
            () => Scalar(connection, "SELECT H2_GEOMETRIC_MEAN(V) FROM T"));
    }

    private static H2Connection NewConnection()
        => new($"Data Source=agg_pkg_{Guid.NewGuid():N};IGNORE_UNKNOWN_SETTINGS=TRUE;Storage=Memory");

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
}
