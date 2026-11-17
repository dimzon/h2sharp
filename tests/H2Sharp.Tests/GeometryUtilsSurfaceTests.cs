/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 282: the geometry envelope helpers on EWKB produced by the parser, including Z/M
 * dimension bits and the null-envelope cases that SQL rarely asks about.
 */
using System;
using H2Sharp.Util.Geometry;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class GeometryUtilsSurfaceTests
{
    [Fact]
    public void EnvelopeIsReadFromEwkbWithExtraDimensions()
    {
        foreach (string text in new[]
        {
            "POINT (1 2)",
            "POINT Z (1 2 3)",
            "POINT M (1 2 4)",
            "POINT ZM (1 2 3 4)",
        })
        {
            ValueGeometry geometry = (ValueGeometry)ValueGeometry.Get(text);
            double[]? envelope = GeometryUtils.GetEnvelope(geometry.GetBytesNoCopy()!);
            Assert.NotNull(envelope);
            Assert.True(envelope.Length >= 4);
        }
    }

    [Fact]
    public void EnvelopeIntersectsAndUnionAnswerNullAndNormalCases()
    {
        double[] first = [0, 2, 0, 2];
        double[] second = [1, 3, 1, 3];
        double[] apart = [10, 11, 10, 11];

        Assert.True(GeometryUtils.Intersects(first, second));
        Assert.False(GeometryUtils.Intersects(first, apart));
        Assert.False(GeometryUtils.Intersects(null, first));
        Assert.False(GeometryUtils.Intersects(first, null));

        Assert.NotNull(GeometryUtils.Union(first, second));
        Assert.Equal(first, GeometryUtils.Union(null, first));
        Assert.Equal(first, GeometryUtils.Union(first, null));
    }
}
