/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 225: the ADO.NET conversion and enlist edges — typed reader getters over numeric,
 * text and binary columns, GetFieldValue shapes, the canceled schema task, the enumerator,
 * the ambient transaction refusals, an unknown schema collection and an sbyte parameter.
 */
using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Transactions;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class AdoMappingEdgeTests
{
    [Fact]
    public void TheTypedGettersConvertAndRefuse()
    {
        using H2Connection connection = Open("ado_getters");
        Execute(connection, "CREATE TABLE T(R REAL, B BOOLEAN, S VARCHAR(30), I INT)");
        Execute(connection, "INSERT INTO T VALUES (1.5, TRUE, '2026-01-02 03:04:05', 7)");

        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT R, B, S, I FROM T";
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1.5f, reader.GetFloat(0));
        Assert.Equal(1.5d, reader.GetDouble(0));
        Assert.Equal(1.5m, reader.GetDecimal(0));
        _ = Assert.Throws<InvalidCastException>(() => reader.GetDecimal(1));
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5), reader.GetDateTime(2));
        _ = Assert.Throws<InvalidCastException>(() => reader.GetBytes(3, 0, null, 0, 0));
        Assert.Equal(1L, reader.GetChars(3, 0, null, 0, 0));
        _ = Assert.Throws<InvalidOperationException>(() => reader.GetStream(3));
        _ = Assert.Throws<InvalidOperationException>(() => reader.GetTextReader(3));
        _ = Assert.Throws<InvalidCastException>(() => reader.GetChar(2));
        _ = Assert.Throws<InvalidCastException>(() => reader.GetGuid(3));
        Assert.Equal(1.5f, reader.GetFieldValue<float>(0));
        Assert.Equal("2026-01-02 03:04:05", reader.GetFieldValue<string>(2));
        Assert.Equal(7, reader.GetFieldValue<int>(3));
    }

    [Fact]
    public void TheCancelledSchemaTaskAndTheEnumeratorAnswer()
    {
        using H2Connection connection = Open("ado_schema_task");
        Execute(connection, "CREATE TABLE T(ID INT)");
        Execute(connection, "INSERT INTO T VALUES (1), (2)");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ID FROM T ORDER BY ID";
        using DbDataReader reader = command.ExecuteReader();

        _ = Assert.ThrowsAny<OperationCanceledException>(() =>
            reader.GetColumnSchemaAsync(new System.Threading.CancellationToken(canceled: true))
                .GetAwaiter().GetResult());

        int rows = 0;
        foreach (DbDataReader row in reader)
        {
            Assert.Equal(++rows, row.GetInt32(0));
        }

        Assert.Equal(2, rows);
    }

    [Fact]
    public void TheAmbientTransactionCannotBeReplaced()
    {
        using H2Connection connection = Open("ado_enlist");
        using var first = new CommittableTransaction();
        using var second = new CommittableTransaction();

        connection.EnlistTransaction(first);
        connection.EnlistTransaction(first);
        _ = Assert.Throws<InvalidOperationException>(() => connection.EnlistTransaction(second));
        _ = Assert.Throws<InvalidOperationException>(() => connection.EnlistTransaction(null));
        _ = Assert.Throws<InvalidOperationException>(() => connection.BeginTransaction());
        first.Commit();
        connection.EnlistTransaction(null);
        connection.EnlistTransaction(second);
        second.Commit();
    }

    [Fact]
    public void AnUnknownSchemaCollectionIsRefused()
    {
        using H2Connection connection = Open("ado_schema_unknown");
        _ = Assert.Throws<ArgumentException>(() => connection.GetSchema("NOPE"));
    }

    [Fact]
    public void AnSbyteParameterRoundsThroughTheMapper()
    {
        using H2Connection connection = Open("ado_sbyte");
        Execute(connection, "CREATE TABLE T(ID INT, V TINYINT)");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO T VALUES (1, $v)";
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "v";
        parameter.Value = (sbyte)-5;
        command.Parameters.Add(parameter);
        Assert.Equal(1, command.ExecuteNonQuery());

        using DbCommand select = connection.CreateCommand();
        select.CommandText = "SELECT V FROM T WHERE ID = 1";
        Assert.Equal(-5, Convert.ToInt32(select.ExecuteScalar(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void TemporalValuesTakeTheConfiguredShapes()
    {
        using H2Connection connection = new("Data Source=ado_temporal;Storage=Memory");
        IH2DataFormat format = connection;
        format.DateFormat = H2TemporalFormat.DateTimeOffset;
        format.TimeFormat = H2TimeFormat.DateTimeOffset;
        format.TimeTzFormat = H2TemporalFormat.DateTimeOffset;
        format.TimestampFormat = H2TemporalFormat.DateTimeOffset;
        format.TimestampTzFormat = H2TemporalFormat.DateTimeOffset;
        connection.Open();
        Execute(connection, "CREATE TABLE T(D DATE, TI TIME, TITZ TIME WITH TIME ZONE, "
            + "TS TIMESTAMP, TSTZ TIMESTAMP WITH TIME ZONE, IV INTERVAL DAY TO SECOND)");

        using (DbCommand insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO T VALUES ($d, $ti, $titz, $ts, $tstz, $iv)";
            Add(insert, "d", new DateOnly(2026, 1, 2));
            Add(insert, "ti", new TimeOnly(3, 4, 5));
            Add(insert, "titz", new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)));
            Add(insert, "ts", new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)));
            Add(insert, "tstz", new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)));
            Add(insert, "iv", new TimeSpan(1, 2, 3, 4));
            Assert.Equal(1, insert.ExecuteNonQuery());
        }

        using DbCommand select = connection.CreateCommand();
        select.CommandText = "SELECT D, TI, TITZ, TS, TSTZ, IV FROM T";
        using DbDataReader reader = select.ExecuteReader();
        Assert.True(reader.Read());
        Assert.IsType<DateTimeOffset>(reader.GetValue(0));
        Assert.IsType<DateTimeOffset>(reader.GetValue(1));
        Assert.IsType<DateTimeOffset>(reader.GetValue(2));
        Assert.IsType<DateTimeOffset>(reader.GetValue(3));
        Assert.IsType<DateTimeOffset>(reader.GetValue(4));
        Assert.Equal(new TimeSpan(1, 2, 3, 4), reader.GetValue(5));
    }

    private static void Add(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static H2Connection Open(string name)
    {
        H2Connection connection = new($"Data Source={name};Storage=Memory");
        connection.Open();
        return connection;
    }

    private static void Execute(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        _ = command.ExecuteNonQuery();
    }
}
