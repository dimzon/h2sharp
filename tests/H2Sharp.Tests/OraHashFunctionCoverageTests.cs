/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of OraHashFunction's null/bucket/seed validation via reflection.
 */
using System;
using System.Reflection;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class OraHashFunctionCoverageTests
{
    private static object? InvokeHash(H2Sharp.Values.Value value, long bucket, long seed)
    {
        Type t = Type.GetType("H2Sharp.JavaLegacy.OraHashFunction, H2Sharp.JavaLegacy")!;
        MethodInfo m = t.GetMethod("Hash", BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            return m.Invoke(null, new object[] { value, bucket, seed });
        }
        catch (TargetInvocationException e)
        {
            throw e.InnerException!;
        }
    }

    [Fact]
    public void NullValueReturnsNull()
    {
        Assert.Same(H2Sharp.Values.ValueNull.Instance, InvokeHash(H2Sharp.Values.ValueNull.Instance, 0, 0));
    }

    [Fact]
    public void BadBucketThrows()
    {
        Assert.Throws<H2Sharp.H2SharpException>(() => InvokeHash(H2Sharp.Values.ValueInteger.Get(1), 1L << 40, 0));
    }

    [Fact]
    public void BadSeedThrows()
    {
        Assert.Throws<H2Sharp.H2SharpException>(() => InvokeHash(H2Sharp.Values.ValueInteger.Get(1), 0, 1L << 40));
    }

    [Fact]
    public void HappyPathReturnsBigint()
    {
        H2Sharp.Values.Value r = (H2Sharp.Values.Value)InvokeHash(H2Sharp.Values.ValueInteger.Get(1), 10, 0)!;
        Assert.Equal(H2Sharp.Values.Value.Bigint, r.GetValueType());
    }
}