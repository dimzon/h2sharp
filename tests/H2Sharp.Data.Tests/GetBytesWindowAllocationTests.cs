/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Pins the windowed GetBytes behavior over a large LOB: each window read used to
 * materialize the whole value (a full copy per window), which made the canonical
 * ADO.NET read loop quadratic in the LOB size. The gate is a generous ceiling — the
 * whole 1 MiB payload fits in it — so the test fails only when per-window whole-value
 * copies are reintroduced.
 */
using System;
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class GetBytesWindowAllocationTests
{
    private const int PayloadLength = 1024 * 1024;

    private const int WindowLength = 64 * 1024;

    private static byte[] MakePayload()
    {
        byte[] payload = new byte[PayloadLength];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i * 31);
        }

        return payload;
    }

    [Fact]
    public void GetBytes_WindowReadsMatchPayloadAndDoNotCopyWholeLobPerWindow()
    {
        using H2Connection connection = new("Data Source=perf_blob_window;Storage=Memory");
        connection.Open();
        using (H2Command create = new("CREATE TABLE T(B BLOB)", connection))
        {
            create.ExecuteNonQuery();
        }

        byte[] payload = MakePayload();
        using (H2Command insert = new("INSERT INTO T VALUES(?)", connection))
        {
            DbParameter parameter = insert.CreateParameter();
            parameter.Value = payload;
            _ = insert.Parameters.Add(parameter);
            insert.ExecuteNonQuery();
        }

        using H2Command select = new("SELECT B FROM T", connection);
        using DbDataReader reader = select.ExecuteReader();
        Assert.True(reader.Read());

        // The length question: no buffer means the whole value length.
        Assert.Equal(PayloadLength, reader.GetBytes(0, 0, null, 0, 0));

        byte[] window = new byte[WindowLength];
        VerifyWalk(reader, payload, window);

        // Warm the blob read path, then measure the same walk. The verification walk is
        // separate so that the measurement sees the reader alone, not the assertions.
        VerifyWalk(reader, payload, window);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long start = GC.GetAllocatedBytesForCurrentThread();
        int windows = MeasureWalk(reader, window);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;

        // 16 windows; a whole-value copy per window would allocate ~16 MiB here.
        Assert.Equal(PayloadLength / WindowLength, windows);
        Assert.InRange(allocated, 0, PayloadLength);
    }

    private static int MeasureWalk(DbDataReader reader, byte[] window)
    {
        int windows = 0;
        long pos = 0;
        while (true)
        {
            long n = reader.GetBytes(0, pos, window, 0, window.Length);
            if (n <= 0)
            {
                break;
            }

            pos += n;
            windows++;
        }

        Assert.Equal(PayloadLength, pos);
        return windows;
    }

    private static void VerifyWalk(DbDataReader reader, byte[] payload, byte[] window)
    {
        long pos = 0;
        while (true)
        {
            long n = reader.GetBytes(0, pos, window, 0, window.Length);
            if (n <= 0)
            {
                break;
            }

            for (int i = 0; i < n; i++)
            {
                Assert.Equal(payload[pos + i], window[i]);
            }

            pos += n;
        }

        Assert.Equal(PayloadLength, pos);
    }
}
