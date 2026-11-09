/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of JavaLegacyExtensions.UseJavaLegacy's non-raw guard.
 */
using System;
using H2Sharp.Data;
using H2Sharp.Data.Extensions;
using H2Sharp.JavaLegacy;
using Xunit;

namespace H2Sharp.Tests;

public sealed class JavaLegacyExtensionsCoverageTests
{
    private sealed class FakeExtensions : IH2Extensions
    {
        public void RegisterUdf<TResult>(string name, Func<TResult> implementation, bool deterministic = false) { }
        public void RegisterUdf<T1, TResult>(string name, Func<T1, TResult> implementation, bool deterministic = false) { }
        public void RegisterUdf<T1, T2, TResult>(string name, Func<T1, T2, TResult> implementation, bool deterministic = false) { }
        public void RegisterUdf<T1, T2, T3, TResult>(string name, Func<T1, T2, T3, TResult> implementation, bool deterministic = false) { }
        public void RegisterUdf<T1, T2, T3, T4, TResult>(string name, Func<T1, T2, T3, T4, TResult> implementation, bool deterministic = false) { }
        public void RegisterAggregate(string name, IH2AggregateProvider provider) { }
        public void RegisterHashAlgorithm(string name, Func<System.Security.Cryptography.HashAlgorithm> factory) { }
        public void RegisterRandomGenerator(Func<long?, Random> factory) { }
        public void RegisterUuidGenerator(Func<int, Guid> factory) { }
    }

    [Fact]
    public void NonRawExtensionsThrow()
    {
        Assert.Throws<InvalidOperationException>(() => new FakeExtensions().UseJavaLegacy());
    }
}