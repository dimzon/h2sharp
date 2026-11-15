/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 222: table-valued .NET UDFs — registration, the FROM position, the column list the
 * function answers, and its use next to a regular table.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using H2Sharp.Data;
using H2Sharp.Data.Extensions;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2TableUdfTests
{
    [Fact]
    public void ARegisteredTableFunctionAnswersItsColumnsAndRows()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterTableUdf<int>("GEN", n =>
            new H2TableUdfResult(
                [new H2TableUdfColumn("N", typeof(int)), new H2TableUdfColumn("S", typeof(string))],
                Rows(n)));
        connection.Open();

        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT N, S FROM GEN(3) ORDER BY N";
        using DbDataReader reader = command.ExecuteReader();
        List<string> rows = [];
        while (reader.Read())
        {
            rows.Add(reader.GetInt32(0) + ":" + reader.GetString(1));
        }

        Assert.Equal(["1:1", "2:2", "3:3"], rows);
    }

    [Fact]
    public void TheFunctionRunsInAJoinWithItsOwnAlias()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterTableUdf<int, int>("MULT", (a, b) =>
            new H2TableUdfResult(
                [new H2TableUdfColumn("V", typeof(int))],
                new[] { new object?[] { a * b } }));
        connection.Open();
        Execute(connection, "CREATE TABLE T(ID INT)");
        Execute(connection, "INSERT INTO T VALUES (3), (6)");

        using DbCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT T.ID, M.V FROM T JOIN MULT(2, 3) M ON M.V = T.ID ORDER BY T.ID";
        using DbDataReader reader = command.ExecuteReader();
        List<int> values = [];
        while (reader.Read())
        {
            values.Add(reader.GetInt32(1));
        }

        Assert.Equal([6], values);
    }

    [Fact]
    public void AZeroArgumentFunctionAndTheArityError()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterTableUdf("ONE", () =>
            new H2TableUdfResult(
                [new H2TableUdfColumn("N", typeof(int))],
                new[] { new object?[] { 7 } }));
        connection.Open();

        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM ONE()";
            Assert.Equal(7, Convert.ToInt32(command.ExecuteScalar(),
                System.Globalization.CultureInfo.InvariantCulture));
        }

        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM ONE(1)";
            H2SharpException e = Assert.Throws<H2SharpException>(() => command.ExecuteNonQuery());
            Assert.Equal(90087, e.H2ErrorCode);
        }
    }

    [Fact]
    public void ARegistrationReplacesTheDefaultCsvread()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterTableUdf<string>("CSVREAD", _ =>
            new H2TableUdfResult(
                [new H2TableUdfColumn("X", typeof(string))],
                new[] { new object?[] { "replaced" } }));
        connection.Open();

        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT X FROM CSVREAD('ignored.csv')";
        Assert.Equal("replaced", command.ExecuteScalar());
    }

    [Fact]
    public void TheFunctionFailuresSurfaceAsSqlErrors()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterTableUdf<int>("BAD", _ =>
            throw new InvalidOperationException("boom"));
        ((IH2Extensions)connection).RegisterTableUdf<int>("SHORT", _ =>
            new H2TableUdfResult(
                [new H2TableUdfColumn("N", typeof(int))],
                new[] { System.Array.Empty<object?>() }));
        connection.Open();

        Assert.Equal(90105, ErrorCode(connection, "SELECT * FROM BAD(1)"));
        Assert.Equal(21002, ErrorCode(connection, "SELECT * FROM SHORT(1)"));
        Assert.Equal(90105, ErrorCode(connection, "SELECT * FROM BAD(CAST(NULL AS INT))"));
    }

    private static int ErrorCode(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Assert.Throws<H2SharpException>(() => command.ExecuteNonQuery()).H2ErrorCode;
    }

    private static IEnumerable<object?[]> Rows(int n)
    {
        for (int i = 1; i <= n; i++)
        {
            yield return [i, i.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        }
    }

    private static H2Connection NewConnection()
        => new($"Data Source=table_udf_{Guid.NewGuid():N};IGNORE_UNKNOWN_SETTINGS=TRUE;Storage=Memory");

    private static void Execute(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        _ = command.ExecuteNonQuery();
    }
}
