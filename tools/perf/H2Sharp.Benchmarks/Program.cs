/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The benchmark runner. Runs every benchmark in this assembly with memory diagnostics on
 * and a short job preset (one launch, two warmups, five iterations): the comparison this
 * project cares about is between two builds of the same engine, not an absolute number.
 *
 * Run with:  dotnet run -c Release --project tools/perf/H2Sharp.Benchmarks
 *            --exporters json --artifacts artifacts/perf/<label>
 */
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace H2Sharp.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        IConfig config = DefaultConfig.Instance
            .AddJob(Job.Default
                .WithLaunchCount(1)
                .WithWarmupCount(3)
                .WithIterationCount(10))
            .AddDiagnoser(MemoryDiagnoser.Default);

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }
}
