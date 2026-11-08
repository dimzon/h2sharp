/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of SpatialDataType: the null branches of IsOverlap, IncreaseBounds,
 * GetAreaIncrease, Contains and IsInside, GetExtremes on an empty and a null-containing
 * list, and CreateStorage.
 */
using System;
using System.Collections.Generic;
using H2Sharp.MvStore.RTree;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class SpatialDataTypeCoverageTests
{
    [Fact]
    public void SpatialDataTypeAnswersItsSurface()
    {
        SpatialDataType type = new(2);
        DefaultSpatial a = new(1, 0, 2, 0, 2);
        DefaultSpatial b = new(2, 1, 3, 1, 3);
        DefaultSpatial bounds = new(9, 0, 1, 0, 1);
        DefaultSpatial empty = new(7);

        // CreateStorage.
        Spatial[] storage = type.CreateStorage(3);
        Assert.Equal(3, storage.Length);

        // IsOverlap: null branches and a real overlap.
        Assert.False(type.IsOverlap(empty, empty));
        Assert.False(type.IsOverlap(a, empty));
        Assert.False(type.IsOverlap(empty, b));
        Assert.True(type.IsOverlap(a, b));

        // IncreaseBounds: null branches.
        type.IncreaseBounds(empty, empty);
        type.IncreaseBounds(bounds, empty);
        type.IncreaseBounds(empty, a);

        // GetAreaIncrease: null branches.
        Assert.Equal(0f, type.GetAreaIncrease(empty, empty));
        Assert.Equal(0f, type.GetAreaIncrease(bounds, empty));
        Assert.Equal(0f, type.GetAreaIncrease(empty, a));

        // Contains: null branches.
        Assert.False(type.Contains(empty, empty));
        Assert.False(type.Contains(bounds, empty));
        Assert.False(type.Contains(empty, a));

        // IsInside: null branches.
        Assert.False(type.IsInside(empty, empty));
        Assert.False(type.IsInside(a, empty));
        Assert.False(type.IsInside(empty, bounds));

        // GetExtremes: empty list and a list with a null spatial.
        Assert.Null(type.GetExtremes(new List<Spatial>()));
        List<Spatial> withNull = new() { empty, a };
        _ = type.GetExtremes(withNull);
    }
}
