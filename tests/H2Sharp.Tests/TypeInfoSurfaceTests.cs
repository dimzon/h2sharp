/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 277: the data-type descriptor's own answers — unknown type, precision/scale defaults,
 * numeric widening and its rendered form — covered directly rather than through CAST.
 */
using H2Sharp.Api;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class TypeInfoSurfaceTests
{
    [Fact]
    public void UnknownTypeIsRefused()
        => Assert.ThrowsAny<H2SharpException>(() => TypeInfo.GetTypeInfo(-1));

    [Fact]
    public void PrecisionAndScaleHaveTheirDefaults()
    {
        Assert.Equal(int.MaxValue, TypeInfo.TypeRowEmpty.GetPrecision());
        Assert.True(TypeInfo.TypeUuid.GetPrecision() > 0);
        Assert.Equal(1L, TypeInfo.TypeNull.GetPrecision());
        Assert.Equal(0, TypeInfo.TypeNull.GetScale());
        Assert.Equal("INTEGER(-1, -1)", TypeInfo.TypeInteger.ToString());
    }

    [Fact]
    public void NumericWideningUsesTheFloatingPointBounds()
    {
        TypeInfo real = TypeInfo.TypeReal.ToNumericType();
        Assert.Equal(Value.Numeric, real.GetValueType());
        Assert.Equal(85, real.GetPrecision());
        Assert.Equal(46, real.GetScale());

        TypeInfo doubled = TypeInfo.TypeDouble.ToNumericType();
        Assert.Equal(634, doubled.GetPrecision());
        Assert.Equal(325, doubled.GetScale());

        Assert.Equal(Value.Decfloat, TypeInfo.TypeInteger.ToDecfloatType().GetValueType());
        Assert.Equal(Value.Numeric, TypeInfo.TypeNull.ToNumericType().GetValueType());
    }
}
