/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 221: the remaining DbBatch surface — the empty shapes, the async wrappers, the
 * timeout and cancel hooks, the command's RecordsAffected, and the base command collection.
 */
using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2BatchSurfaceTests
{
    [Fact]
    public void TheEmptyAndAsyncShapesAnswer()
    {
        using H2Connection connection = H2TestDataSource.Open("Data Source=batch_surface;Storage=Memory");
        Execute(connection, "CREATE TABLE T(ID INT)");
        using DbBatch batch = connection.CreateBatch();

        Assert.Null(batch.ExecuteScalar());
        Assert.Throws<InvalidOperationException>(() => batch.ExecuteReader());
        batch.Cancel();
        batch.PrepareAsync(CancellationToken.None).GetAwaiter().GetResult();
        batch.Timeout = 30;
        Assert.Equal(30, batch.Timeout);
        Assert.Same(connection, batch.Connection);
        Assert.Null(batch.Transaction);
        Assert.Empty(batch.BatchCommands);
        Assert.Null(batch.ExecuteScalarAsync(CancellationToken.None).GetAwaiter().GetResult());

        DbBatchCommand item = batch.CreateBatchCommand();
        item.CommandText = "SELECT 7 FROM SYSTEM_RANGE(1, 1)";
        Assert.Equal(7, Convert.ToInt32(batch.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(7, Convert.ToInt32(
            batch.ExecuteScalarAsync(CancellationToken.None).GetAwaiter().GetResult(),
            System.Globalization.CultureInfo.InvariantCulture));

        using DbDataReader reader = batch.ExecuteReaderAsync(CommandBehavior.Default, CancellationToken.None)
            .GetAwaiter().GetResult();
        Assert.True(reader.Read());
        Assert.Equal(7, reader.GetInt32(0));
        Assert.True(((H2Batch.H2BatchCommand)item).RecordsAffected >= 0);
    }

    [Fact]
    public void TheBaseCommandCollectionMutatesTheBatch()
    {
        using H2Connection connection = H2TestDataSource.Open("Data Source=batch_collection;Storage=Memory");
        using DbBatch batch = connection.CreateBatch();
        DbBatchCommand one = batch.CreateBatchCommand();
        one.CommandText = "SELECT 1 FROM SYSTEM_RANGE(1, 1)";
        DbBatchCommand two = batch.CreateBatchCommand();
        two.CommandText = "SELECT 2 FROM SYSTEM_RANGE(1, 1)";

        DbBatchCommandCollection collection = ((DbBatch)batch).BatchCommands;
        Assert.False(collection.IsReadOnly);
        Assert.Equal(2, collection.Count);
        Assert.Same(one, collection[0]);
        Assert.Same(two, collection[1]);
        Assert.Equal(0, collection.IndexOf(one));
        Assert.Contains(two, collection);

        DbBatchCommand[] copy = new DbBatchCommand[2];
        collection.CopyTo(copy, 0);
        Assert.Same(one, copy[0]);
        Assert.Same(two, copy[1]);
        int enumerated = 0;
        foreach (DbBatchCommand command in collection)
        {
            Assert.NotNull(command.CommandText);
            enumerated++;
        }

        Assert.Equal(2, enumerated);

        collection[0] = two;
        Assert.Same(two, collection[0]);
        collection.Insert(0, one);
        Assert.Equal(3, collection.Count);
        Assert.True(collection.Remove(one));
        Assert.Equal(2, collection.Count);
        collection.RemoveAt(0);
        Assert.Single(collection);
        collection.Clear();
        Assert.Empty(collection);
        Assert.Empty(batch.BatchCommands);
    }

    private static void Execute(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        _ = command.ExecuteNonQuery();
    }
}
