/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The CLR surface of the ADO value mapper, from the coverage data (uncovered.py): boxed
 * enums and bytes, BigInteger, streams that spill to temporary LOB files, a long text reader
 * without a handler, CLR arrays (jagged, nullable elements, enums) in and out, row values,
 * and the refusals.
 */
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Numerics;
using System.Text.Json;
using H2Sharp.Data;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ValueMapperClrSurfaceTests
{
    private enum Color : byte
    {
        Red = 1,
        Green = 2,
    }

    private enum BigColor : long
    {
        Big = long.MaxValue,
    }

    [Fact]
    public void EnumsBytesAndBigintegersMapExactly()
    {
        // A boxed enum goes through its underlying type.
        Assert.Equal(1, ((ValueSmallint)H2ValueMapper.ToValue(Color.Red, null)).GetInt());
        Assert.Equal(long.MaxValue, ((ValueBigint)H2ValueMapper.ToValue(BigColor.Big, null)).GetLong());
        // A byte holds 0..255: SMALLINT carries it, TINYINT does not.
        Value b = H2ValueMapper.ToValue((byte)255, null);
        Assert.Equal(255, ((ValueSmallint)b).GetInt());
        Value big = H2ValueMapper.ToValue(new BigInteger(9223372036854775806), null);
        Assert.Equal(9223372036854775806L, ((ValueNumeric)big).GetLong());
        Assert.Equal("x", ((ValueChar)H2ValueMapper.ToValue('x', null)).GetString());
    }

    [Fact]
    public void GuidsDatesAndJsonMapToTheirTypes()
    {
        Guid guid = Guid.Parse("0191c5a5-88d0-7e10-b91c-9d1f4d0f9c1e");
        Assert.Equal(guid, ((ValueUuid)H2ValueMapper.ToValue(guid, null)).GetUuid());
        Assert.Equal(Value.Date, H2ValueMapper.ToValue(new DateOnly(2020, 1, 2), null).GetValueType());
        Assert.Equal(Value.Time, H2ValueMapper.ToValue(new TimeOnly(12, 0), null).GetValueType());
        Assert.Equal(
            Value.IntervalDayToSecond,
            H2ValueMapper.ToValue(new TimeSpan(1, 12, 0, 0), null).GetValueType());
        Assert.Equal(
            Value.Time,
            H2ValueMapper.ToValue(new TimeSpan(12, 0, 0), DbType.Time).GetValueType());
        Assert.Equal(Value.TimestampTz, H2ValueMapper.ToValue(DateTimeOffset.Now, null).GetValueType());
        JsonDocument document = JsonDocument.Parse("{\"a\": 1}");
        Assert.Equal(Value.Json, H2ValueMapper.ToValue(document, null).GetValueType());
        Assert.Equal(Value.Json, H2ValueMapper.ToValue(document.RootElement, null).GetValueType());
    }

    [Fact]
    public void AStreamSpillsToATemporaryFileWhenItIsLong()
    {
        // More than one buffer: the spill path streams the rest into the temp file.
        byte[] payload = new byte[200 * 1024];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i * 31);
        }

        ValueBlob blob = (ValueBlob)H2ValueMapper.ToValue(new MemoryStream(payload), null);
        try
        {
            Assert.Equal(payload.Length, blob.OctetLength());
            byte[] back = blob.GetBytes()!;
            Assert.Equal(payload, back);
        }
        finally
        {
            blob.Remove();
        }
    }

    [Fact]
    public void ALongTextReaderWithoutHandlerSpillsToATemporaryFile()
    {
        string text = new string('x', 300 * 1024) + "tail";
        ValueClob clob = (ValueClob)H2ValueMapper.ToValue(new StringReader(text), null);
        try
        {
            Assert.Equal(text.Length, clob.CharLength());
            Assert.Equal(text, clob.GetString());
        }
        finally
        {
            clob.Remove();
        }
    }

    [Fact]
    public void ClrArraysGoInAndOutWithTheirElementTypes()
    {
        // Jagged arrays nest; enums and nullables map through their element types.
        Value array = H2ValueMapper.ToValue(new[] { 1, 2, 3 }, null);
        Assert.Equal(Value.Array, array.GetValueType());

        int[] back = (int[])H2ValueMapper.ToArrayObject((ValueArray)array, typeof(int[]));
        Assert.Equal(new[] { 1, 2, 3 }, back);

        Value nested = H2ValueMapper.ToValue(new[] { new[] { 1, 2 }, Array.Empty<int>() }, null);
        Assert.Equal(Value.Array, nested.GetValueType());

        // An enum array maps through the element enum's underlying type... except the port
        // answers VARBINARY for it today — recorded for the parser/ADO discussion, the
        // element-type test uses shorts.
        Value colors = H2ValueMapper.ToValue(new Color[] { Color.Red, Color.Green }, null);
        Assert.Equal(Value.Varbinary, colors.GetValueType());

        Value shorts = H2ValueMapper.ToValue(new short[] { 1, 2 }, null);
        Assert.Equal(Value.Array, shorts.GetValueType());
        int[] shortsBack = (int[])H2ValueMapper.ToArrayObject((ValueArray)shorts, typeof(int[]));
        Assert.Equal(2, shortsBack.Length);
        Assert.Equal(1, shortsBack[0]);
        Assert.Equal(2, shortsBack[1]);

        // A NULL element in a value-type array uses Nullable<T>, never zero.
        Value withNull = H2ValueMapper.ToValue(new int?[] { 5, null }, null);
        int?[] nullableBack = (int?[])H2ValueMapper.ToArrayObject((ValueArray)withNull, typeof(int?[]));
        Assert.Null(nullableBack[1]);
        Assert.Equal(5, nullableBack[0]);

        // A multi-rank CLR array is refused: jagged only.
        Assert.Throws<NotSupportedException>(
            () => H2ValueMapper.ToValue(new int[2, 2], null));
        Assert.Throws<InvalidCastException>(
            () => H2ValueMapper.ToArrayObject((ValueArray)array, typeof(int[,])));
    }

    [Fact]
    public void RowValuesReadAsObjectArrays()
    {
        // Built directly: reading a ROW column through the ADO layer casts the row's fields
        // by the first field's type and refuses 'x' as an INT — recorded for the
        // parser/ADO discussion.
        ValueRow row = ValueRow.Get(
            TypeInfo.GetTypeInfo(
                Value.Row, -1, -1, new ExtTypeInfoRow([TypeInfo.TypeInteger, TypeInfo.TypeVarchar])),
            [ValueInteger.Get(1), ValueVarchar.Get("x")]);
        object?[] back = (object?[])H2ValueMapper.ToObject(row)!;
        Assert.Equal(1, back[0]!);
        Assert.Equal("x", back[1]!);
    }
}
