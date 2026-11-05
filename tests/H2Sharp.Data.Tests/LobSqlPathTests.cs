/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The SQL path of a large LOB parameter: a stream beyond the inline size is routed into
 * the database's LOB storage, persisted in the row as a reference, and read back.
 */
using System;
using System.Data.Common;
using System.IO;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class LobSqlPathTests
{
    [Fact]
    public void LargeBlobStreamGoesThroughStorageAndReadsBack()
    {
        using H2Connection connection = new("Data Source=lob_sql_path;Storage=Memory");
        connection.Open();
        using (H2Command create = new("CREATE TABLE T(B BLOB)", connection))
        {
            create.ExecuteNonQuery();
        }

        byte[] payload = new byte[512 * 1024];
        new Random(7).NextBytes(payload);
        using (H2Command insert = new("INSERT INTO T VALUES(?)", connection))
        {
            DbParameter parameter = insert.CreateParameter();
            parameter.Value = new MemoryStream(payload);
            _ = insert.Parameters.Add(parameter);
            insert.ExecuteNonQuery();
        }

        using H2Command select = new("SELECT B FROM T", connection);
        using DbDataReader reader = select.ExecuteReader();
        Assert.True(reader.Read());

        byte[] read = new byte[payload.Length];
        Assert.Equal(payload.Length, reader.GetBytes(0, 0, read, 0, read.Length));
        Assert.Equal(payload, read);
    }
}
