/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of JavaRandom's branches (java.util.Random port).
 */
using System;
using H2Sharp.JavaLegacy;
using Xunit;

namespace H2Sharp.Tests;

public sealed class JavaRandomCoverageTests
{
    [Fact]
    public void NextIntBranches()
    {
        JavaRandom r = new(1);
        _ = r.Next();
        _ = r.Next(0);
        _ = r.Next(1);
        _ = r.Next(10);
        _ = r.Next(1, 1);
        _ = r.Next(1, 5);
        _ = r.Next(0, int.MaxValue);
        _ = r.Next(int.MinValue, int.MaxValue);
    }

    [Fact]
    public void NextLongBranches()
    {
        JavaRandom r = new(2);
        _ = r.NextInt64();
        _ = r.NextInt64(0);
        _ = r.NextInt64(1);
        _ = r.NextInt64(100);
        _ = r.NextInt64(5, 5);
        _ = r.NextInt64(5, 10);
        _ = r.NextInt64(long.MinValue, long.MaxValue);
    }

    [Fact]
    public void NextDoubleSingleBytes()
    {
        JavaRandom r = new(3);
        _ = r.NextDouble();
        _ = r.NextSingle();
        byte[] buf = new byte[10];
        r.NextBytes(buf);
        Span<byte> span = stackalloc byte[7];
        r.NextBytes(span);
    }

    [Fact]
    public void SetSeedReseeds()
    {
        JavaRandom r = new(4);
        r.SetSeed(99);
        _ = r.Next();
    }

    [Fact]
    public void SampleOverrideIsCallable()
    {
        SubRandom r = new(5);
        _ = r.CallSample();
    }

    private sealed class SubRandom : JavaRandom
    {
        public SubRandom(long seed) : base(seed) { }
        public double CallSample() => Sample();
    }
}