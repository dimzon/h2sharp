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

    /// <summary>
    /// An empty array carries no element to infer the SQL type from, so the CLR element type
    /// is asked instead: every TypeCode the mapper knows, enums through their underlying type,
    /// jagged arrays through their element, and the unknown ones staying NULL.
    /// </summary>
    [Fact]
    public void EmptyArraysPreserveTheirElementTypes()
    {
        AssertComponent(Array.Empty<bool>(), Value.Boolean);
        AssertComponent(Array.Empty<short>(), Value.Smallint);
        AssertComponent(Array.Empty<long>(), Value.Bigint);
        AssertComponent(Array.Empty<float>(), Value.Real);
        AssertComponent(Array.Empty<double>(), Value.Double);
        AssertComponent(Array.Empty<decimal>(), Value.Numeric);
        AssertComponent(Array.Empty<char>(), Value.Char);
        AssertComponent(Array.Empty<string>(), Value.Varchar);
        AssertComponent(Array.Empty<DateTime>(), Value.Timestamp);
        AssertComponent(Array.Empty<Guid>(), Value.Uuid);
        AssertComponent(Array.Empty<DateTimeOffset>(), Value.TimestampTz);
        AssertComponent(Array.Empty<DateOnly>(), Value.Date);
        AssertComponent(Array.Empty<TimeOnly>(), Value.Time);
        AssertComponent(Array.Empty<TimeSpan>(), Value.IntervalDayToSecond);
        AssertComponent(Array.Empty<BigInteger>(), Value.Numeric);
        // An enum goes through its underlying type, and Nullable<enum> through the enum.
        AssertComponent(Array.Empty<BigColor>(), Value.Bigint);
        AssertComponent(Array.Empty<Color?>(), Value.Smallint);
        // A byte-backed enum array stands for the same bytes under the CLR's array
        // compatibility, so it is binary data too (as ClrArraysGoInAndOut pins for a
        // filled one).
        Assert.Equal(Value.Varbinary, H2ValueMapper.ToValue(Array.Empty<Color>(), null).GetValueType());
        // A byte array is binary data, not an array of TINYINT: byte[] is the .NET binary
        // type, and the CLR lets sbyte[] (Java's signed byte) stand for the same bytes, so
        // both answer VARBINARY.
        Assert.Equal(Value.Varbinary, H2ValueMapper.ToValue(Array.Empty<byte>(), null).GetValueType());
        Assert.Equal(Value.Varbinary, H2ValueMapper.ToValue(Array.Empty<sbyte>(), null).GetValueType());
        // An array whose elements are byte arrays is an ARRAY of VARBINARY.
        AssertComponent(Array.Empty<byte[]>(), Value.Varbinary);
        AssertComponent(Array.Empty<int[]>(), Value.Array);
        // An element the mapper has no SQL type for stays NULL.
        AssertComponent(Array.Empty<object>(), Value.Null);
        AssertComponent(Array.Empty<IntPtr>(), Value.Null);
    }

    /// <summary>
    /// A binary stream whose length is unknown cannot be kept inline: without a data handler
    /// it is streamed into a temporary file, and the catch path of the length probe leaves the
    /// length unknown.
    /// </summary>
    [Fact]
    public void AnUnknownLengthStreamSpillsToATemporaryFile()
    {
        byte[] payload = new byte[200 * 1024];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i * 17);
        }

        // Forward-only: CanSeek is false, so the length is never asked.
        AssertSpilledBlob(new ForwardOnlyStream(payload), payload);
        // Seekable, but the length probe itself is refused (the NotSupportedException catch).
        AssertSpilledBlob(new ThrowingLengthStream(payload), payload);
    }

    private static void AssertComponent<T>(T[] array, int expectedType)
    {
        Value value = H2ValueMapper.ToValue(array, null);
        ValueArray arrayValue = Assert.IsType<ValueArray>(value);
        Assert.Equal(expectedType, arrayValue.GetComponentType().GetValueType());
    }

    private static void AssertSpilledBlob(Stream stream, byte[] payload)
    {
        ValueBlob blob = (ValueBlob)H2ValueMapper.ToValue(stream, null);
        try
        {
            Assert.Equal(payload.Length, blob.OctetLength());
            Assert.Equal(payload, blob.GetBytes()!);
        }
        finally
        {
            blob.Remove();
        }
    }

    /// <summary>A readable stream that answers no length and cannot seek.</summary>
    private sealed class ForwardOnlyStream(byte[] data) : Stream
    {
        private int position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int available = Math.Min(count, data.Length - position);
            if (available <= 0)
            {
                return 0;
            }

            Array.Copy(data, position, buffer, offset, available);
            position += available;
            return available;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A seekable stream whose length probe refuses, as some wrapped streams do.</summary>
    private sealed class ThrowingLengthStream(byte[] data) : Stream
    {
        private readonly MemoryStream inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
