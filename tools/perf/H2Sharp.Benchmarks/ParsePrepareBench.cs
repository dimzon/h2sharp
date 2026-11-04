/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The SQL front path: tokenizer + parser + plan. H2Command.Prepare() only checks state
 * here — the statement is parsed when it runs — so this benchmark executes sixteen rotating
 * statements that share the same shape and match zero rows: the scan cost is negligible and
 * the measured time is the parse+plan pipeline. The rotating list owns the strings so no
 * string building happens inside the measured region, and distinct texts defeat any
 * statement cache.
 */
using System.Data.Common;
using BenchmarkDotNet.Attributes;
using H2Sharp.Data;

namespace H2Sharp.Benchmarks;

public class ParsePrepareBench
{
    private static readonly string[] Statements = BuildStatements();

    private static string[] BuildStatements()
    {
        string[] statements = new string[16];
        for (int i = 0; i < statements.Length; i++)
        {
            statements[i] = "SELECT A, B, C FROM TEST WHERE A > " + i + " AND B LIKE 'x" + i + "%' ORDER BY C";
        }

        return statements;
    }

    private H2Connection connection = null!;

    private int index;

    [GlobalSetup]
    public void Setup()
    {
        connection = PerfDb.Open("perf_parse");
        PerfDb.CreateTestTable(connection);
    }

    [GlobalCleanup]
    public void Cleanup() => connection.Dispose();

    [Benchmark]
    public int ExecuteSelectNoRows()
    {
        string statement = Statements[index];
        index = (index + 1) & 15;
        using H2Command command = new(statement, connection);
        using DbDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            // zero rows: never entered
        }

        return 1;
    }
}
