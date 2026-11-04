/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The reader's column schema: the synchronous interface and its agreement with the
 * asynchronous method.
 */
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2DataReaderSchemaTests
{
    [Fact]
    public void TheSynchronousSchemaMatchesTheAsynchronousOne()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1 AS ID, CAST('x' AS VARCHAR(10)) AS NAME, CAST(NULL AS INT) AS OPTIONAL";
        using DbDataReader reader = command.ExecuteReader();

        IDbColumnSchemaGenerator generator = Assert.IsAssignableFrom<IDbColumnSchemaGenerator>(reader);
        ReadOnlyCollection<DbColumn> sync = generator.GetColumnSchema();
        IReadOnlyList<DbColumn> async = reader.GetColumnSchemaAsync().GetAwaiter().GetResult();

        Assert.Equal(async.Count, sync.Count);
        for (int i = 0; i < sync.Count; i++)
        {
            Assert.Equal(async[i].ColumnName, sync[i].ColumnName);
            Assert.Equal(async[i].ColumnOrdinal, sync[i].ColumnOrdinal);
            Assert.Equal(async[i].DataType, sync[i].DataType);
            Assert.Equal(async[i].DataTypeName, sync[i].DataTypeName);
            Assert.Equal(async[i].NumericPrecision, sync[i].NumericPrecision);
            Assert.Equal(async[i].NumericScale, sync[i].NumericScale);
            Assert.Equal(async[i].AllowDBNull, sync[i].AllowDBNull);
        }

        Assert.Equal("ID", sync[0].ColumnName);
        Assert.Equal("NAME", sync[1].ColumnName);
        Assert.Equal("OPTIONAL", sync[2].ColumnName);
    }

    [Fact]
    public void TheSchemaIsEmptyWhenTheStatementHasNoColumns()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE T(ID INT)";
        using DbDataReader reader = command.ExecuteReader();

        Assert.Equal(0, reader.FieldCount);
        IDbColumnSchemaGenerator generator = Assert.IsAssignableFrom<IDbColumnSchemaGenerator>(reader);
        Assert.Empty(generator.GetColumnSchema());
    }

    private static H2Connection NewConnection()
        => new($"Data Source=reader_schema_{Guid.NewGuid():N};Storage=Memory");
}
