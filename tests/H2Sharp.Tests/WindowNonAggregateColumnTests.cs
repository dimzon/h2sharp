/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Pins the reference invariant behind error 90016: a non-aggregate column in a window
 * query is validated per source row by ExpressionColumn.UpdateAggregate, and the
 * validation relies on a fresh per-row group-data array. Reusing one array across rows
 * (e.g. an "optimization" that skips per-row allocation in the plain group walk) makes
 * the second row see a previously recorded value and throws MUST_GROUP_BY_COLUMN_1 on
 * a filled table. Java's ExpressionColumn.updateAggregate is identical. This test guards
 * the invariant so the optimization cannot silently regress.
 */
using System;
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Tests;

public sealed class WindowNonAggregateColumnTests
{
    [Fact]
    public void NonAggregateColumnWithOrderedWindowRunsOverFilledTable()
    {
        using H2Connection connection = new("Data Source=window_golden;Storage=Memory");
        connection.Open();

        using (H2Command create = new("CREATE TABLE W(ID INT, VAL INT)", connection))
        {
            create.ExecuteNonQuery();
        }

        for (int batch = 0; batch < 10_000; batch += 500)
        {
            System.Text.StringBuilder sb = new("INSERT INTO W(ID, VAL) VALUES ");
            int end = Math.Min(batch + 500, 10_000);
            for (int i = batch; i < end; i++)
            {
                if (i > batch)
                {
                    sb.Append(',');
                }

                sb.Append('(').Append(i).Append(',').Append(i % 100).Append(')');
            }

            using H2Command insert = new(sb.ToString(), connection);
            insert.ExecuteNonQuery();
        }

        using H2Command command = new(
            "SELECT ID, SUM(VAL) OVER (ORDER BY ID ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) FROM W",
            connection);
        using DbDataReader reader = command.ExecuteReader();
        long rows = 0;
        long lastId = -1;
        while (reader.Read())
        {
            rows++;
            lastId = reader.GetInt32(0);
        }

        Assert.Equal(10_000, rows);
        Assert.Equal(9_999, lastId);
    }
}
