/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The ADO.NET answers of issues 94 and 95: GetByte returns the .NET byte the value's
 * two's-complement holds, and a parameterized catalog query answers the current state
 * after the metadata changed.
 */
using System;
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2ReaderAndCatalogTests
{
    [Fact]
    public void AnUnsignedByteIsReadAsANetByte()
    {
        using H2Connection connection = H2TestDataSource.Open("Data Source=issue94;Storage=Memory");
        Execute(connection, "CREATE TABLE T(ID INT PRIMARY KEY, DATA VARBINARY NOT NULL)");
        Execute(connection, "INSERT INTO T VALUES (1, X'FF')");

        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(SUBSTRING(DATA FROM 1 FOR 1) AS TINYINT), 255, CAST(300 AS SMALLINT) FROM T";
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());

        // The SQL value keeps the reference's signed byte; the ADO.NET read is unsigned.
        Assert.Equal((sbyte)-1, Convert.ToSByte(reader.GetValue(0)));
        Assert.Equal((byte)255, reader.GetByte(0));
        Assert.Equal((byte)255, reader.GetByte(1));

        // A number that does not fit a signed byte is still a byte after truncation.
        Assert.Equal(unchecked((byte)300), reader.GetByte(2));
    }

    [Fact]
    public void AParameterizedSchemataQuerySeesASchemaCreatedLater()
    {
        // Issue 95: the session's query cache must not answer a parameterized catalog
        // query from before a CREATE SCHEMA — a metadata change changes the data id.
        using H2Connection connection = H2TestDataSource.Open("Data Source=issue95;Storage=Memory");
        Assert.Equal(0L, CountSchemas(connection, "forum_notes"));

        Execute(connection, "CREATE SCHEMA \"forum_notes\"");

        Assert.Equal(1L, CountSchemas(connection, "forum_notes"));
        Assert.Equal(1L, CountSchemas(connection, "PUBLIC"));
        Assert.Equal(1L, CountSchemasLiteral(connection, "forum_notes"));
    }

    private static long CountSchemas(H2Connection connection, string schema)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = $p";
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "p";
        parameter.Value = schema;
        command.Parameters.Add(parameter);
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static long CountSchemasLiteral(H2Connection connection, string schema)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = '" + schema + "'";
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void Execute(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        _ = command.ExecuteNonQuery();
    }
}
