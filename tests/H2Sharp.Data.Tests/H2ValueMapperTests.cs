/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The decimal road in both directions: exact or refused, never rounded quietly.
 */
using System;
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2ValueMapperTests
{
    [Fact]
    public void DecimalRoundTripsExactly()
    {
        using H2Connection connection = new("Data Source=decimal_roundtrip;Storage=Memory");
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE T(VAL NUMERIC(10, 2))";
        _ = command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO T(VAL) VALUES ($v)";
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "$v";
        parameter.Value = 123.45m;
        command.Parameters.Add(parameter);
        Assert.Equal(1, command.ExecuteNonQuery());

        command.Parameters.Clear();
        command.CommandText = "SELECT VAL FROM T";
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(123.45m, reader.GetDecimal(0));
    }

    [Fact]
    public void DecimalOverflowThrows()
    {
        using H2Connection connection = new("Data Source=decimal_overflow;Storage=Memory");
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(1000000000000000000000000000000 AS NUMERIC)";
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Throws<OverflowException>(() => reader.GetDecimal(0));
    }

    [Fact]
    public void DecimalWideScaleOfZerosReads()
    {
        using H2Connection connection = new("Data Source=decimal_widezero;Storage=Memory");
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(1 AS NUMERIC(38, 30))";
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1m, reader.GetDecimal(0));
    }

    [Fact]
    public void DecimalWideScaleNonzeroThrows()
    {
        using H2Connection connection = new("Data Source=decimal_widescale;Storage=Memory");
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE T(VAL NUMERIC(38, 28))";
        _ = command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO T(VAL) VALUES (1), (1), (2)";
        _ = command.ExecuteNonQuery();
        command.CommandText = "SELECT AVG(VAL) FROM T";
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Throws<OverflowException>(() => reader.GetDecimal(0));
    }
}
