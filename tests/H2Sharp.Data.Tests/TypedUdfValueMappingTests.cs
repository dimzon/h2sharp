/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 243: the CLR-to-SQL value mapping used by typed UDFs — every type the registration
 * surface supports, and the errors for the ones it does not.
 */
using System;
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class TypedUdfValueMappingTests
{
    [Fact]
    public void AllMappedClrTypesRoundTripThroughAUdf()
    {
        H2Connection connection = new("Data Source=typed_udf;Storage=Memory");
        IH2Extensions extensions = connection;
        Guid guid = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        extensions.RegisterUdf<Guid, Guid>("ID_GUID", value => value, deterministic: true);
        extensions.RegisterUdf<byte[], long>("LEN_BYTES", value => value.Length, deterministic: true);
        extensions.RegisterUdf<DateTime, int>("YEAR_DT", value => value.Year, deterministic: true);
        extensions.RegisterUdf<DateTimeOffset, int>("YEAR_DTO", value => value.Year, deterministic: true);
        connection.Open();
        try
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT ID_GUID(CAST('" + guid + "' AS UUID))";
            Assert.Equal(guid, command.ExecuteScalar());
            command.CommandText = "SELECT LEN_BYTES(X'0102')";
            Assert.Equal(2L, Convert.ToInt64(command.ExecuteScalar()));
            command.CommandText = "SELECT YEAR_DT(DATE '2020-05-06')";
            Assert.Equal(2020L, Convert.ToInt64(command.ExecuteScalar()));
            command.CommandText = "SELECT YEAR_DTO(TIMESTAMP WITH TIME ZONE '2021-05-06 00:00:00+00')";
            Assert.Equal(2021L, Convert.ToInt64(command.ExecuteScalar()));
        }
        finally
        {
            connection.Dispose();
        }
    }

    [Fact]
    public void UnsupportedClrTypesAreNamedByTheRegistration()
    {
        H2Connection connection = new("Data Source=typed_udf_bad;Storage=Memory");
        IH2Extensions extensions = connection;
        extensions.RegisterUdf<Uri, long>("BAD_UDF", value => value.Port, true);
        connection.Open();
        try
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT BAD_UDF(1)";
            H2SharpException error = Assert.Throws<H2SharpException>(() => command.ExecuteScalar());
            Assert.IsType<TODOException>(error.InnerException);
            Assert.Contains("Uri", error.InnerException.Message, StringComparison.Ordinal);
        }
        finally
        {
            connection.Dispose();
        }
    }
}
