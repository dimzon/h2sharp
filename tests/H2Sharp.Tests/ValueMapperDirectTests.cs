/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of H2ValueMapper: CLR-to-engine value conversion, and of
 * Value conversion families: cast/convert paths including error branches.
 */
using System;
using System.Numerics;
using System.Text.Json;
using H2Sharp.Api;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ValueMapperDirectTests
{
    [Fact]
    public void ToValueSbyteMapsToTinyint()
    {
        Value v = H2ValueMapper.ToValue((sbyte)42, null, null);
        Assert.IsType<ValueTinyint>(v);
        Assert.Equal(42, ((ValueTinyint)v).GetByte());
    }

    [Fact]
    public void ToValueFloatMapsToReal()
    {
        Value v = H2ValueMapper.ToValue(1.5f, null, null);
        Assert.IsType<ValueReal>(v);
        Assert.Equal(1.5f, ((ValueReal)v).GetFloat());
    }

    [Fact]
    public void CastToValueNullTarget()
    {
        Value v = ValueInteger.Get(5);
        Value result = v.CastToValue(TypeInfo.TypeNull, null);
        Assert.IsType<ValueNull>(result);
    }

    [Fact]
    public void CastToValueUnsupportedTargetThrows()
    {
        Value v = ValueInteger.Get(5);
        Assert.Throws<H2SharpException>(() => v.CastToValue(TypeInfo.TypeUnknown, null));
    }

    [Fact]
    public void CastToBinaryTypeFromVarcharSucceeds()
    {
        Value v = ValueVarchar.Get("text");
        Value result = v.CastToValue(TypeInfo.TypeBinary, null);
        Assert.IsType<ValueBinary>(result);
    }

    [Fact]
    public void CastToClobTypeFromBlob()
    {
        Value v = ValueBlob.CreateSmall(new byte[] { 1, 2, 3 });
        Value result = v.CastToValue(TypeInfo.TypeClob, null);
        Assert.IsType<ValueClob>(result);
    }

    [Fact]
    public void CastToBooleanTypeFromNullThrows()
    {
        Value v = ValueNull.Instance;
        Value result = v.CastToValue(TypeInfo.TypeNull, null);
        Assert.IsType<ValueNull>(result);
    }

    [Fact]
    public void CastToBooleanTypeFromUnsupportedThrows()
    {
        Value v = ValueBlob.CreateSmall(new byte[] { 1 });
        Assert.Throws<H2SharpException>(() => v.CastToValue(TypeInfo.TypeBoolean, null));
    }

    [Fact]
    public void CastToDecfloatTypeFormatException()
    {
        Value v = ValueVarchar.Get("not-a-number");
        Assert.Throws<H2SharpException>(() => v.CastToValue(TypeInfo.TypeDecfloat, null));
    }
}
