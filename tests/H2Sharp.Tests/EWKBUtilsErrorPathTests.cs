/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Error paths and dimension conversions of EWKBUtils that the flat coverage tests do not
 * reach: PostGIS-style type words inside a parse, the refusal of coordinates that the
 * target dimension system has no room for, big-endian inputs cut mid-number, and every
 * envelope shape the reference draws (point, segment, area).
 */
using System;
using H2Sharp.Util;
using H2Sharp.Util.Geometry;
using Xunit;

namespace H2Sharp.Tests;

public sealed class EWKBUtilsErrorPathTests
{
    [Fact]
    public void ParseReadsPostgisStyleTypeWords()
    {
        // PostGIS writes the dimension system into the type word (1001 = POINT Z), without
        // the OGC flag. The parser reads both spellings, as the reference does.
        byte[] ewkb = new byte[29];
        ewkb[0] = 1; // little endian
        BitConverter.GetBytes(1001).CopyTo(ewkb, 1);
        BitConverter.GetBytes(1.0).CopyTo(ewkb, 5);
        BitConverter.GetBytes(2.0).CopyTo(ewkb, 13);
        BitConverter.GetBytes(3.0).CopyTo(ewkb, 21);

        Assert.Equal(GeometryUtils.DimensionSystemXyz, EWKBUtils.GetDimensionSystem(ewkb));

        RecordingTarget target = new();
        EWKBUtils.ParseEWKB(ewkb, target);
        Assert.True(target.Z);
        Assert.False(target.M);
        (double X, double Y, double Z, double M) first = target.Coordinates[0];
        Assert.Equal((1.0, 2.0, 3.0, double.NaN), first);
    }

    [Fact]
    public void ParseReadsPostgisXyzmTypeWords()
    {
        byte[] ewkb = new byte[37];
        ewkb[0] = 1;
        BitConverter.GetBytes(3001).CopyTo(ewkb, 1);
        double[] values = new double[] { 1, 2, 3, 4 };
        for (int i = 0; i < 4; i++)
        {
            BitConverter.GetBytes(values[i]).CopyTo(ewkb, 5 + 8 * i);
        }

        RecordingTarget target = new();
        EWKBUtils.ParseEWKB(ewkb, target);
        Assert.True(target.Z);
        Assert.True(target.M);
        Assert.Equal((1.0, 2.0, 3.0, 4.0), target.Coordinates[0]);
    }

    [Fact]
    public void CanonicalisationRefusesMismatchedDimensions()
    {
        // The reference's addCoordinate runs the coordinates it has through the target's
        // dimension system: a coordinate the target has no room for is refused, and a
        // missing one (NaN) is refused as well.
        byte[] pointZ = EWKTUtils.EWKT2EWKB("POINT Z(1 2 3)");
        Assert.Throws<ArgumentException>(() => EWKBUtils.EWKB2EWKB(pointZ, GeometryUtils.DimensionSystemXy));
        byte[] xy = EWKTUtils.EWKT2EWKB("POINT(1 2)");
        Assert.Throws<ArgumentException>(() => EWKBUtils.EWKB2EWKB(xy, GeometryUtils.DimensionSystemXyzm));

        // Matching systems canonicalise.
        byte[] canonical = EWKBUtils.EWKB2EWKB(pointZ, GeometryUtils.DimensionSystemXyz);
        Assert.Equal(GeometryUtils.DimensionSystemXyz, EWKBUtils.GetDimensionSystem(canonical));
        Assert.Equal(pointZ.Length, canonical.Length);
    }

    [Fact]
    public void BigEndianInputCutMidNumberIsRefused()
    {
        // Big-endian numbers are read by hand, and a cut lands in IndexOutOfRangeException,
        // which ParseEWKB translates to the same ArgumentException as the little-endian case.
        byte[] ewkb = new byte[] { 0, 0, 0, 0, 1, 0x40 };
        Assert.Throws<ArgumentException>(() => EWKBUtils.ParseEWKB(ewkb, new NullTarget()));
    }

    [Fact]
    public void Envelope2WkbDrawsEveryShape()
    {
        Assert.Null(EWKBUtils.Envelope2WKB(null));

        // A single point. The envelope WKB writes the type and the counts as single bytes,
        // exactly where the reference sets result[4], result[8] and result[12].
        byte[] point = EWKBUtils.Envelope2WKB(new double[] { 1, 1, 2, 2 })!;
        Assert.Equal(21, point.Length);
        Assert.Equal(GeometryUtils.Point, point[4]);
        Assert.Equal(1.0, BitConverter.Int64BitsToDouble(Bits.LongFromBigEndian(point, 5)));

        // A horizontal segment.
        byte[] line = EWKBUtils.Envelope2WKB(new double[] { 1, 3, 2, 2 })!;
        Assert.Equal(41, line.Length);
        Assert.Equal(GeometryUtils.LineString, line[4]);
        Assert.Equal(2, line[8]);
        Assert.Equal(1.0, BitConverter.Int64BitsToDouble(Bits.LongFromBigEndian(line, 9)));
        Assert.Equal(2.0, BitConverter.Int64BitsToDouble(Bits.LongFromBigEndian(line, 17)));

        // An area.
        byte[] polygon = EWKBUtils.Envelope2WKB(new double[] { 0, 2, 0, 3 })!;
        Assert.Equal(93, polygon.Length);
        Assert.Equal(GeometryUtils.Polygon, polygon[4]);
        Assert.Equal(1, polygon[8]);
        Assert.Equal(5, polygon[12]);
    }

    private sealed class NullTarget : GeometryUtils.Target
    {
        protected internal override void AddCoordinate(double x, double y, double z, double m, int index, int total)
        {
        }
    }

    private sealed class RecordingTarget : GeometryUtils.Target
    {
        internal bool Z;

        internal bool M;

        internal System.Collections.Generic.List<(double X, double Y, double Z, double M)> Coordinates { get; } = new();

        protected internal override void DimensionSystem(int dimensionSystem)
        {
            Z = (dimensionSystem & GeometryUtils.DimensionSystemXyz) != 0;
            M = (dimensionSystem & GeometryUtils.DimensionSystemXym) != 0;
        }

        protected internal override void AddCoordinate(double x, double y, double z, double m, int index, int total)
        {
            Coordinates.Add((x, y, z, m));
        }
    }
}
