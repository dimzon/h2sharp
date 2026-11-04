/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * LOB inserts of 1 MiB each: the StreamStore block path and the large chunk buffer. Both
 * allocate > 85 KiB byte[] today, so this is the LOH probe for S4/S5. The allocated bytes
 * number will stay high while the LOB lives in managed heap and drop when the blocks move
 * off-heap.
 */
using System.Data.Common;
using BenchmarkDotNet.Attributes;
using H2Sharp.Data;

namespace H2Sharp.Benchmarks;

public class BlobInsertBench
{
    private static readonly byte[] Blob = new byte[1024 * 1024];

    private H2Connection connection = null!;

    [GlobalSetup]
    public void Setup()
    {
        connection = PerfDb.Open("perf_blob");
        PerfDb.ExecuteNonQuery(connection, "CREATE TABLE LOB_T(B BLOB)");
    }

    [GlobalCleanup]
    public void Cleanup() => connection.Dispose();

    [Benchmark]
    public int InsertBlob()
    {
        using H2Command command = new("INSERT INTO LOB_T(B) VALUES (?)", connection);
        DbParameter parameter = command.CreateParameter();
        parameter.Value = Blob;
        _ = command.Parameters.Add(parameter);
        command.ExecuteNonQuery();
        return Blob.Length;
    }
}
