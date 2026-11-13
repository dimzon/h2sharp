/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * IH2DataFormat getters, multi-argument UDF registration and GetSchema guards
 * on H2Connection.
 */
using System;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2ConnectionExtensionTests
{
    [Fact]
    public void DataFormatGettersAnswer()
    {
        using H2Connection connection = new("Data Source=fmt;Storage=Memory");
        connection.Open();
        IH2DataFormat format = connection;
        Assert.True(Enum.IsDefined(format.BlobFormat));
        Assert.True(Enum.IsDefined(format.ClobFormat));
        Assert.True(Enum.IsDefined(format.JsonFormat));
        Assert.True(Enum.IsDefined(format.ArrayFormat));
        Assert.True(Enum.IsDefined(format.DateFormat));
        Assert.True(Enum.IsDefined(format.TimeFormat));
        Assert.True(Enum.IsDefined(format.TimeTzFormat));
        Assert.True(Enum.IsDefined(format.TimestampFormat));
        Assert.True(Enum.IsDefined(format.TimestampTzFormat));
    }

    [Fact]
    public void MultiArgumentUdfsAreCallable()
    {
        using H2Connection connection = new("Data Source=udf_multi;Storage=Memory");
        ((IH2Extensions)connection).RegisterUdf<int, int, int>("ADD", (a, b) => a + b);
        ((IH2Extensions)connection).RegisterUdf<int, int, int, int>("ADD3", (a, b, c) => a + b + c);
        ((IH2Extensions)connection).RegisterUdf<int, int, int, int, int>("ADD4", (a, b, c, d) => a + b + c + d);
        connection.Open();

        Assert.Equal("5", Scalar(connection, "SELECT ADD(2,3)"));
        Assert.Equal("6", Scalar(connection, "SELECT ADD3(1,2,3)"));
        Assert.Equal("10", Scalar(connection, "SELECT ADD4(1,2,3,4)"));
    }

    [Fact]
    public void SchemaCollectionGuardsAnswer()
    {
        using H2Connection connection = new("Data Source=schema_guards;Storage=Memory");
        connection.Open();
        Assert.Throws<ArgumentException>(() => connection.GetSchema(null!));
        Assert.Throws<ArgumentException>(() => connection.GetSchema("NOPE"));
        Assert.NotNull(connection.GetSchemaAsync("TABLES").GetAwaiter().GetResult());
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
