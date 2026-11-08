/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of EWKBUtils: the dimension-system mapping (OGC and PostGIS style),
 * the canonicalisation and the refusal of malformed input.
 */
using System;
using H2Sharp.Util.Geometry;
using Xunit;

namespace H2Sharp.Tests;

public sealed class EWKBUtilsCoverageTests
{
    [Fact]
    public void DimensionSystemMappingAnswersThePort()
    {
        // OGC style: the dimension system lives in the flags.
        Assert.Equal(0, EWKBUtils.Type2DimensionSystem(1));
        Assert.Equal(1, EWKBUtils.Type2DimensionSystem(unchecked((int)0x80000001)));
        Assert.Equal(2, EWKBUtils.Type2DimensionSystem(0x40000001));
        Assert.Equal(3, EWKBUtils.Type2DimensionSystem(unchecked((int)0xC0000001)));

        // PostGIS style: the dimension system lives in the type word.
        Assert.Equal(1, EWKBUtils.Type2DimensionSystem(1001));
        Assert.Equal(2, EWKBUtils.Type2DimensionSystem(2001));
        Assert.Equal(3, EWKBUtils.Type2DimensionSystem(3001));
    }

    [Fact]
    public void GetDimensionSystemAnswersThePort()
    {
        byte[] xy = EWKTUtils.EWKT2EWKB("POINT(1 2)");
        byte[] z = EWKTUtils.EWKT2EWKB("POINT Z(1 2 3)");
        byte[] m = EWKTUtils.EWKT2EWKB("POINT M(1 2 3)");
        byte[] zm = EWKTUtils.EWKT2EWKB("POINT ZM(1 2 3 4)");

        Assert.Equal(0, EWKBUtils.GetDimensionSystem(xy));
        Assert.Equal(1, EWKBUtils.GetDimensionSystem(z));
        Assert.Equal(2, EWKBUtils.GetDimensionSystem(m));
        Assert.Equal(3, EWKBUtils.GetDimensionSystem(zm));
    }

    [Fact]
    public void CanonicalisationAnswersThePort()
    {
        byte[] xy = EWKTUtils.EWKT2EWKB("POINT(1 2)");
        byte[] canonical = EWKBUtils.EWKB2EWKB(xy);
        Assert.NotNull(canonical);
        Assert.Equal(xy.Length, canonical.Length);

        // With an explicit dimension system.
        byte[] canonical2 = EWKBUtils.EWKB2EWKB(xy, 0);
        Assert.NotNull(canonical2);
    }

    [Fact]
    public void ParseEWKBAnswersThePort()
    {
        byte[] xy = EWKTUtils.EWKT2EWKB("POINT(1 2)");
        GeometryUtils.Target target = new NullTarget();
        EWKBUtils.ParseEWKB(xy, target);
    }

    [Fact]
    public void InvalidInputIsRefused()
    {
        Assert.Throws<ArgumentException>(() => EWKBUtils.ParseEWKB(new byte[] { 2, 1, 0, 0, 0 }, new NullTarget()));
        Assert.Throws<ArgumentException>(() => EWKBUtils.ParseEWKB(new byte[] { 1 }, new NullTarget()));
        Assert.Throws<ArgumentException>(() => EWKBUtils.GetDimensionSystem(new byte[] { 2, 1, 0, 0, 0 }));
    }

    [Fact]
    public void Envelope2WkbAnswersThePort()
    {
        double[] envelope = new double[] { 0, 1, 0, 1 };
        byte[]? result = EWKBUtils.Envelope2WKB(envelope);
        _ = result;
    }

    private sealed class NullTarget : GeometryUtils.Target
    {
        protected internal override void AddCoordinate(double x, double y, double z, double m, int index, int total)
        {
        }
    }
}