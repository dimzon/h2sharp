/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Pins the allocation behaviour of a full-table UPDATE over 10 000 rows: the MVStore
 * write path (page copy-on-write, undo records). The bound is a ceiling over the current
 * baseline — it does not force the number down, it guards against structural regressions
 * while the MVStore write path is under work.
 */
using System;
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Tests;

public sealed class MvMapPutAllocationTests
{
    [Fact]
    public void UpdateAllRowsOver10kAllocatesWithinCeiling()
    {
        using H2Connection connection = new("Data Source=put_alloc;Storage=Memory");
        connection.Open();

        using (H2Command create = new("CREATE TABLE TEST(A INT, B INT, C VARCHAR(50))", connection))
        {
            create.ExecuteNonQuery();
        }

        for (int batch = 0; batch < 10_000; batch += 500)
        {
            System.Text.StringBuilder sb = new("INSERT INTO TEST(A, B, C) VALUES ");
            int end = Math.Min(batch + 500, 10_000);
            for (int i = batch; i < end; i++)
            {
                if (i > batch)
                {
                    sb.Append(',');
                }

                sb.Append('(').Append(i).Append(',').Append(i % 100)
                    .Append(",'name").Append(i).Append("')");
            }

            using H2Command insert = new(sb.ToString(), connection);
            insert.ExecuteNonQuery();
        }

        long bytes = Measure(() =>
        {
            using H2Command command = new("UPDATE TEST SET B = B + 1", connection);
            command.ExecuteNonQuery();
        });

        Assert.InRange(bytes, 0, 60_000_000);
    }

    private static long Measure(Action action)
    {
        for (int i = 0; i < 3; i++)
        {
            action();
        }

        long smallest = long.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            action();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (allocated < smallest)
            {
                smallest = allocated;
            }
        }

        return smallest;
    }
}
