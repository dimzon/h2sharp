/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Off-heap memory gates (SP-27+). Chunk images and (later) LOB blocks live in native
 * memory; the gates assert that repeated work neither balloons private memory nor drives
 * the managed heap. Bounds are deliberately loose: the machine under CI is shared, so the
 * gates catch structural leaks (per-chunk Alloc/Free churn, unreturned pools), not exact
 * byte counts.
 */
using System;
using System.Data.Common;
using System.IO;
using H2Sharp.Data;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Db;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class OffHeapMemoryGateTests
{
    [Fact]
    public void FileDbRepeatedLargeCommitsKeepChunkImagesBounded()
    {
        string path = Path.Combine(Path.GetTempPath(), "h2sharp-offheap-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (H2Connection connection = new("Data Source=" + path + ";Storage=File;FILE_LOCK=NO"))
            {
                connection.Open();
                using (H2Command create = new("CREATE TABLE T(A INT, B VARCHAR(2000))", connection))
                {
                    create.ExecuteNonQuery();
                }

                // Warm up (JIT, pools, caches).
                WriteBatches(connection, 10);

                // Many chunk writes: the chunk-image pool must keep native allocations bounded
                // (pool cap + in-flight), not grow per chunk.
                long maxLive = 0;
                for (int b = 0; b < 60; b++)
                {
                    WriteBatches(connection, 1);
                    maxLive = Math.Max(maxLive, NativeChunkImage.LiveAllocations);
                }

                Assert.InRange(maxLive, 0, 16);
            }
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public void FileDbStoredLobSurvivesCloseAndReopen()
    {
        string path = Path.Combine(Path.GetTempPath(), "h2sharp-lob-" + Guid.NewGuid().ToString("N"));
        byte[] payload = new byte[300 * 1024];
        new Random(42).NextBytes(payload);
        try
        {
            string url = "Data Source=" + path + ";Storage=File;FILE_LOCK=NO";
            using (H2Connection connection = new(url))
            {
                connection.Open();
                using (H2Command create = new("CREATE TABLE LOB_T(B BLOB)", connection))
                {
                    create.ExecuteNonQuery();
                }

                using (H2Command insert = new("INSERT INTO LOB_T(B) VALUES (?)", connection))
                {
                    DbParameter parameter = insert.CreateParameter();
                    parameter.Value = payload;
                    _ = insert.Parameters.Add(parameter);
                    insert.ExecuteNonQuery();
                }
            }

            // Reopen the file: blocks deserialize from disk as sbyte[] (ObjectDataType's
            // signed-byte slot). The read path must tolerate that.
            using (H2Connection reopened = new(url))
            {
                reopened.Open();
                using H2Command select = new("SELECT B FROM LOB_T", reopened);
                using DbDataReader reader = select.ExecuteReader();
                Assert.True(reader.Read());
                using MemoryStream stream = new();
                using (Stream blob = reader.GetStream(0))
                {
                    blob.CopyTo(stream);
                }

                Assert.Equal(payload, stream.ToArray());
            }
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public void StoredLobBlocksAreNativeAndFreedOnClose()
    {
        string path = Path.Combine(Path.GetTempPath(), "h2sharp-lob-native-" + Guid.NewGuid().ToString("N"));
        byte[] payload = new byte[1024 * 1024];
        new Random(42).NextBytes(payload);
        long baseline = NativeLobBlock.LiveAllocations;
        long peak = baseline;
        try
        {
            H2Sharp.Engine.Database database = new(H2Sharp.Engine.ConnectionInfo.File(path));
            try
            {
                LobStorageMap storage = (LobStorageMap)database.GetLobStorage();
                for (int i = 0; i < 5; i++)
                {
                    ValueBlob blob = storage.CreateBlob(new MemoryStream(payload), payload.Length);
                    peak = Math.Max(peak, NativeLobBlock.LiveAllocations);
                    storage.RemoveLob(blob);
                }

                // Remove frees blocks synchronously; nothing should stay alive in this test.
                Assert.InRange(NativeLobBlock.LiveAllocations, 0, baseline + 2);
                Assert.True(peak > baseline, "LOB blocks should live in native memory");
            }
            finally
            {
                database.Close();
            }
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static void WriteBatches(H2Connection connection, int batches)
    {
        for (int b = 0; b < batches; b++)
        {
            System.Text.StringBuilder sb = new("INSERT INTO T(A, B) VALUES ");
            for (int i = 0; i < 500; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append('(').Append(b * 500 + i).Append(",'").Append(new string((char)('a' + b % 26), 2000)).Append("')");
            }

            using H2Command insert = new(sb.ToString(), connection);
            insert.ExecuteNonQuery();
        }
    }

    private static void DeleteDatabase(string path)
    {
        foreach (string suffix in new[] { "", ".mv.db", ".lock.db", ".trace.db" })
        {
            try
            {
                if (File.Exists(path + suffix))
                {
                    File.Delete(path + suffix);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
