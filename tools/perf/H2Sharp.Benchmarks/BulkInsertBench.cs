/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Bulk insert of small rows: exercises page splits, WriteBuffer pooling and chunk writes
 * (the S4 territory). The allocation numbers here are dominated by page materialization,
 * so a regression in page handling shows immediately.
 */
using BenchmarkDotNet.Attributes;
using H2Sharp.Data;

namespace H2Sharp.Benchmarks;

public class BulkInsertBench
{
    private H2Connection connection = null!;

    [GlobalSetup]
    public void Setup()
    {
        connection = PerfDb.Open("perf_bulk");
        PerfDb.CreateTestTable(connection);
    }

    [GlobalCleanup]
    public void Cleanup() => connection.Dispose();

    [Benchmark]
    public int Insert1000Rows()
    {
        PerfDb.FillTestTable(connection, 1000);
        return 1000;
    }
}
