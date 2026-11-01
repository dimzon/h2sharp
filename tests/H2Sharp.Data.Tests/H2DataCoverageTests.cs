/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct ADO coverage: enlistment, parameter collection shapes, connection surface,
 * value mapper round trips.
 */
using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Transactions;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2DataCoverageTests
{
    [Fact]
    public void AmbientEnlistmentCommits()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE T(ID INT)";
            command.ExecuteNonQuery();
        }

        using (var scope = new TransactionScope())
        {
            connection.EnlistTransaction(Transaction.Current);
            using DbCommand command = connection.CreateCommand();
            command.CommandText = "INSERT INTO T VALUES(1)";
            command.ExecuteNonQuery();
            scope.Complete();
        }

        Assert.Equal("1", Scalar(connection, "SELECT COUNT(*) FROM T"));
    }

    [Fact]
    public void ParameterCollectionShapes()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO T(ID, NAME) VALUES(?, ?)";
        DbParameterCollection coll = command.Parameters;
        coll.Add(new H2Parameter { Value = 1 });
        coll.Add(new H2Parameter { ParameterName = "p2", Value = "x" });
        Assert.Equal(2, coll.Count);
        Assert.True(coll.Contains("p2"));
        Assert.Equal(1, coll.IndexOf("p2"));
        Assert.Same(coll[1], coll["p2"]);

        var arr = new DbParameter[2];
        coll.CopyTo(arr, 0);
        Assert.NotNull(arr[0]);

        var list = new ArrayList();
        foreach (object p in coll)
        {
            list.Add(p);
        }

        Assert.Equal(2, list.Count);
        coll.RemoveAt(0);
        Assert.Single(coll);
        coll.Clear();
        Assert.Empty(coll);
    }

    [Fact]
    public void ConnectionSurfaceProperties()
    {
        using H2Connection connection = NewConnection();
        Assert.Equal("", connection.Database);
        Assert.Equal("", connection.DataSource);
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.Throws<InvalidOperationException>(() => _ = connection.ServerVersion);
        connection.Open();
        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.NotNull(connection.Database);
        Assert.NotNull(connection.DataSource);
        Assert.NotNull(connection.ServerVersion);
        Assert.NotNull(connection.CurrentDatabase);
        connection.Close();
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void ValueMapperRoundTripsClrKinds()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE V(D DECIMAL(20,5), O TIMESTAMP WITH TIME ZONE, G UUID,"
                + " I INTERVAL DAY TO SECOND, C CHAR(3), B VARBINARY(8))";
            command.ExecuteNonQuery();
        }

        using (DbCommand insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO V VALUES(?, ?, ?, ?, ?, ?)";
            insert.Parameters.Add(new H2Parameter { Value = 123.45m });
            insert.Parameters.Add(new H2Parameter { Value = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)) });
            insert.Parameters.Add(new H2Parameter { Value = Guid.Parse("123e4567-e89b-12d3-a456-426614174000") });
            insert.Parameters.Add(new H2Parameter { Value = new TimeSpan(0, 2, 30, 0) });
            insert.Parameters.Add(new H2Parameter { Value = 'A' });
            insert.Parameters.Add(new H2Parameter { Value = new byte[] { 1, 2, 3 } });
            insert.ExecuteNonQuery();
        }

        using DbCommand select = connection.CreateCommand();
        select.CommandText = "SELECT D, O, G, I, C, B FROM V";
        using (DbDataReader reader = select.ExecuteReader())
        {
            Assert.True(reader.Read());
            Assert.Equal(123.45m, reader.GetDecimal(0));
            _ = reader.GetFieldValue<DateTimeOffset>(1);
            Assert.Equal(Guid.Parse("123e4567-e89b-12d3-a456-426614174000"), reader.GetGuid(2));
            _ = reader.GetFieldValue<TimeSpan>(3);
            Assert.Equal("A", reader.GetString(4).Trim());
            Assert.Equal(new byte[] { 1, 2, 3 }, reader.GetFieldValue<byte[]>(5));
        }
    }

    private static H2Connection NewConnection()
        => new($"Data Source=cover_{Guid.NewGuid():N};IGNORE_UNKNOWN_SETTINGS=TRUE;Storage=Memory");

    private static string Scalar(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value is null or DBNull
            ? "-"
            : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "-";
    }
}
