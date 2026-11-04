/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A predicate scan where every row is tested against a compiled LIKE pattern. The pattern
 * is prepared once and evaluated per row, so this measures per-row evaluation cost (the
 * CompareLike path).
 */
using System.Data.Common;
using BenchmarkDotNet.Attributes;
using H2Sharp.Data;

namespace H2Sharp.Benchmarks;

public class LikeBench
{
    private H2Connection connection = null!;

    [GlobalSetup]
    public void Setup()
    {
        connection = PerfDb.Open("perf_like");
        PerfDb.CreateTestTable(connection);
        PerfDb.FillTestTable(connection, 10_000);
    }

    [GlobalCleanup]
    public void Cleanup() => connection.Dispose();

    [Benchmark]
    public long SelectLike()
    {
        using H2Command command = new("SELECT A FROM TEST WHERE C LIKE 'name1%'", connection);
        using DbDataReader reader = command.ExecuteReader();
        long sum = 0;
        while (reader.Read())
        {
            sum += reader.GetInt32(0);
        }

        return sum;
    }
}
