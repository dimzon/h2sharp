/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 119: the extension assemblies' cheap cluster — COUNT_IF's argument refusals and the
 * crypto registration's refusal of an extensions surface that is not the connection's own.
 */
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using H2Sharp.Crypto;
using H2Sharp.Data;
using H2Sharp.Data.Extensions;
using H2Sharp.Engine;
using H2Sharp.Extensions;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class ExtensionEdgeClusterTests
{
    /// <summary>An extensions surface that is not the connection's raw one.</summary>
    private sealed class NotRawExtensions : IH2Extensions
    {
        public void RegisterUdf<TResult>(string name, Func<TResult> implementation, bool deterministic = false)
        {
        }

        public void RegisterUdf<T1, TResult>(string name, Func<T1, TResult> implementation, bool deterministic = false)
        {
        }

        public void RegisterUdf<T1, T2, TResult>(string name, Func<T1, T2, TResult> implementation, bool deterministic = false)
        {
        }

        public void RegisterUdf<T1, T2, T3, TResult>(string name, Func<T1, T2, T3, TResult> implementation, bool deterministic = false)
        {
        }

        public void RegisterUdf<T1, T2, T3, T4, TResult>(string name, Func<T1, T2, T3, T4, TResult> implementation, bool deterministic = false)
        {
        }

        public void RegisterAggregate(string name, IH2AggregateProvider provider)
        {
        }

        public void RegisterHashAlgorithm(string name, Func<HashAlgorithm> factory)
        {
        }

        public void RegisterRandomGenerator(Func<long?, Random> factory)
        {
        }

        public void RegisterUuidGenerator(Func<int, Guid> factory)
        {
        }
    }

    [Fact]
    public void CountIfRefusesItsArgumentShapes()
    {
        CountIf count = new();
        Assert.Throws<ArgumentException>(() =>
            count.GetResultType([TypeInfo.TypeInteger, TypeInfo.TypeInteger]));
        Assert.Throws<ArgumentException>(() => count.Accumulate(ValueInteger.Get(1)));
        count.Accumulate(null);
        Assert.Equal(0L, (long)count.GetResult()!);
    }

    [Fact]
    public void UseCryptoRefusesANonRawExtensionsSurface()
    {
        Assert.Throws<InvalidOperationException>(() => new NotRawExtensions().UseCrypto());
    }

    [Fact]
    public void WeightedAverageRefusesItsArgumentShapes()
    {
        WeightedAverage average = new();
        Assert.Throws<ArgumentException>(() => average.GetResultType([TypeInfo.TypeInteger]));
        Assert.Throws<ArgumentException>(() => average.Accumulate(1));
        average.Accumulate(new object?[] { 1.0, 2.0 });
        Assert.NotNull(average.GetResult());
    }
}
