/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Full table scans. Two flavors, because they measure two different paths:
 *
 * * SelectAllRows — plain SELECT: the direct cursor path, which streams rows off pages
 *   without materializing a LocalResult. This is the page-decode allocation probe.
 * * SelectOrderedRows — ORDER BY: forces a LocalResult with a sort, which is the S1
 *   target (LocalResult.AddRowForTable adds one Value[] per row today).
 */
using System.Data.Common;
using BenchmarkDotNet.Attributes;
using H2Sharp.Data;

namespace H2Sharp.Benchmarks;

public class SelectRowsBench
{
    private H2Connection connection = null!;

    [GlobalSetup]
    public void Setup()
    {
        connection = PerfDb.Open("perf_select");
        PerfDb.CreateTestTable(connection);
        PerfDb.FillTestTable(connection, 10_000);
    }

    [GlobalCleanup]
    public void Cleanup() => connection.Dispose();

    [Benchmark]
    public long SelectAllRows()
    {
        using H2Command command = new("SELECT A, B, C FROM TEST", connection);
        using DbDataReader reader = command.ExecuteReader();
        long sum = 0;
        while (reader.Read())
        {
            sum += reader.GetInt32(0);
        }

        return sum;
    }

    [Benchmark]
    public long SelectOrderedRows()
    {
        using H2Command command = new("SELECT A, B, C FROM TEST ORDER BY C", connection);
        using DbDataReader reader = command.ExecuteReader();
        long sum = 0;
        while (reader.Read())
        {
            sum += reader.GetInt32(0);
        }

        return sum;
    }
}
