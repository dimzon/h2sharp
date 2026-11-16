/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 229: the internal lookup surface of the extension snapshot — arities, case-insensitive
 * names, missing entries and hash-name normalisation.
 */
using System;
using System.Linq;
using H2Sharp.Api;
using H2Sharp.Data.Extensions;
using H2Sharp.Extensions;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class ExtensionRuntimeSurfaceTests
{
    private static H2TableUdfResult EmptyResult()
        => new([new H2TableUdfColumn("V", typeof(int))], Array.Empty<object?[]>());

    [Fact]
    public void FindsUdfsAndTableUdfsByArity()
    {
        ExtensionConfiguration configuration = new();
        configuration.Add(UdfDefinition.Create("F", (int x) => x + 1, deterministic: true));
        configuration.Add(UdfDefinition.Create("F", (int x, int y) => x + y, deterministic: true));
        configuration.AddTableUdf(TableUdfDefinition.Create("T", (int x) => EmptyResult(), true));
        configuration.AddTableUdf(TableUdfDefinition.Create("T", EmptyResult, true));
        ExtensionRuntime runtime = ExtensionRuntime.Create(configuration);

        Assert.True(runtime.HasUdf("f"));
        Assert.True(runtime.HasTableUdf("t"));
        Assert.Equal([1, 2], runtime.GetUdfArities("F"));
        Assert.Equal([0, 1], runtime.GetTableUdfArities("T"));
        Assert.NotNull(runtime.FindUdf("f", 2));
        Assert.NotNull(runtime.FindTableUdf("t", 1));
        Assert.Null(runtime.FindUdf("f", 7));
        Assert.Null(runtime.FindTableUdf("t", 7));
        Assert.False(runtime.HasUdf("missing"));
        Assert.False(runtime.HasTableUdf("missing"));
        Assert.Empty(runtime.GetUdfArities("missing"));
        Assert.Empty(runtime.GetTableUdfArities("missing"));
        Assert.Null(runtime.FindAggregate("missing"));
    }

    [Fact]
    public void NormalizesHashNamesAndRefusesUnknownAlgorithms()
    {
        Assert.Equal("SHA-1", ExtensionRuntime.NormalizeHashName("sha1"));
        Assert.Equal("SHA-256", ExtensionRuntime.NormalizeHashName("SHA256"));
        Assert.Equal("SHA-384", ExtensionRuntime.NormalizeHashName("Sha384"));
        Assert.Equal("SHA-512", ExtensionRuntime.NormalizeHashName("sha512"));
        Assert.Equal("MD5", ExtensionRuntime.NormalizeHashName("md5"));

        ExtensionRuntime runtime = ExtensionRuntime.Create(new ExtensionConfiguration());
        H2SharpException error = Assert.Throws<H2SharpException>(
            () => runtime.CreateHashAlgorithm("not-a-hash"));
        Assert.Equal(ErrorCode.InvalidValue2, error.H2ErrorCode);
        Assert.NotNull(runtime.CreateHashAlgorithm("sha1"));
    }
}
