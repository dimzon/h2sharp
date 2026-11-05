/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of the ADO value mapper: every CLR input of ToValue, every engine type of
 * ToObject, the array and row projections, field types, parameter DbTypes and the decimal
 * edge. Read-only look at the provider conversion surface: the dialect rule is that findings
 * here are reported, not fixed.
 */
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Numerics;
using System.Text.Json;
using H2Sharp;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Api;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2ValueMapperBranchTests
{
    private enum SampleEnum
    {
        First = 7,
    }

    // ToValue: every accepted CLR shape.
    [Fact]
    public void ToValueNullAndDBNullGivesNull()
    {
        Assert.IsType<ValueNull>(H2ValueMapper.ToValue(null, null));
        Assert.IsType<ValueNull>(H2ValueMapper.ToValue(DBNull.Value, null));
    }

    [Fact]
    public void ToValueEnumConvertsThroughTheUnderlyingType()
    {
        Value value = H2ValueMapper.ToValue(SampleEnum.First, null);
        Assert.Equal(7, ((ValueInteger)value).GetInt());
    }

    [Fact]
    public void ToValueBigIntegerGivesNumeric()
    {
        Value value = H2ValueMapper.ToValue(new BigInteger(12345), null);
        Assert.Equal(12345m, H2ValueMapper.ToDecimal(value));
    }

    [Fact]
    public void ToValueCharAndJsonAndDateShapes()
    {
        Assert.Equal("x", ((ValueChar)H2ValueMapper.ToValue('x', null)).GetString());

        JsonDocument document = JsonDocument.Parse("{\"a\":1}");
        Assert.Equal("{\"a\":1}", ((ValueJson)H2ValueMapper.ToValue(document, null)).GetString());
        Assert.Equal("{\"a\":1}", ((ValueJson)H2ValueMapper.ToValue(document.RootElement, null)).GetString());

        Value date = H2ValueMapper.ToValue(new DateOnly(2024, 3, 5), null);
        Assert.Equal("2024-03-05", ((ValueDate)date).GetString());

        Value time = H2ValueMapper.ToValue(new TimeOnly(12, 34, 56), null);
        Assert.Equal(45296000000000L, ((ValueTime)time).GetNanos());
    }

    [Fact]
    public void ToValueTimeSpanDependsOnTheRequestedDbType()
    {
        TimeSpan span = new(1, 2, 3);
        Value asTime = H2ValueMapper.ToValue(span, DbType.Time);
        Assert.IsType<ValueTime>(asTime);

        Value asInterval = H2ValueMapper.ToValue(span, null);
        Assert.IsType<ValueInterval>(asInterval);
    }

    [Fact]
    public void ToValueOffsetDateTimeAndStreams()
    {
        DateTimeOffset dto = new(2024, 3, 5, 12, 0, 0, TimeSpan.FromHours(2));
        Value timestampTz = H2ValueMapper.ToValue(dto, null);
        Assert.IsType<ValueTimestampTimeZone>(timestampTz);

        using MemoryStream stream = new([1, 2, 3]);
        Value binary = H2ValueMapper.ToValue(stream, null);
        // A byte stream parameter is a BLOB, like JDBC setBinaryStream.
        Assert.Equal([1, 2, 3], ((ValueBlob)binary).GetBytes());

        Value clob = H2ValueMapper.ToValue(new StringReader("text"), null);
        Assert.Equal("text", ((ValueClob)clob).GetString());
    }

    [Fact]
    public void ToValueListsBecomeArrays()
    {
        Value array = H2ValueMapper.ToValue(new List<int> { 1, 2 }, null);
        Assert.Equal(new[] { 1, 2 }, H2ValueMapper.ToArrayObject((ValueArray)array, typeof(int[])));

        Value strings = H2ValueMapper.ToValue(new[] { "a", "b" }, null);
        Assert.Equal(new[] { "a", "b" }, H2ValueMapper.ToArrayObject((ValueArray)strings, typeof(string[])));
    }

    [Fact]
    public void ToValueRejectsMultidimensionalArraysAndUnknownTypes()
    {
        int[,] square = new int[2, 2];
        Assert.Throws<NotSupportedException>(() => H2ValueMapper.ToValue(square, null));
        Assert.Throws<InvalidOperationException>(() => H2ValueMapper.ToValue(new Random(), null));
    }

    [Fact]
    public void ToValueRejectsArraysPastTheCardinalityLimit()
    {
        List<int> items = new(65_537);
        for (int i = 0; i < 65_537; i++)
        {
            items.Add(i);
        }

        Assert.Throws<H2SharpException>(
            () => H2ValueMapper.ToValue(items, null));
    }

    [Fact]
    public void ToValueAllNullTypedFallsBackToTheElementType()
    {
        Value array = H2ValueMapper.ToValue(new List<DateOnly?> { null, null }, null);
        Value[] elements = ((ValueArray)array).GetList();
        Assert.All(elements, e => Assert.IsType<ValueNull>(e));
        // The component type falls back to the CLR element type (DATE), not UNKNOWN.
        Assert.Equal("DATE", ((ValueArray)array).GetComponentType().GetTraceSql());
    }

    // ToObject: every engine type and the unknown-type refusal.
    [Fact]
    public void ToObjectNumericAndCharacterFamilies()
    {
        Assert.Equal(true, H2ValueMapper.ToObject(ValueBoolean.True));
        Assert.Equal((sbyte)-5, H2ValueMapper.ToObject(ValueTinyint.Get(-5)));
        Assert.Equal((short)300, H2ValueMapper.ToObject(ValueSmallint.Get(300)));
        Assert.Equal(7, H2ValueMapper.ToObject(ValueInteger.Get(7)));
        Assert.Equal(9L, H2ValueMapper.ToObject(ValueBigint.Get(9L)));
        Assert.Equal(1.5f, H2ValueMapper.ToObject(ValueReal.Get(1.5f)));
        Assert.Equal(2.25d, H2ValueMapper.ToObject(ValueDouble.Get(2.25d)));
        Assert.Equal(1.25m, H2ValueMapper.ToObject(ValueNumeric.Get(new H2Sharp.Util.BigDecimal("1.25"))));
        Assert.Equal("text", H2ValueMapper.ToObject(ValueVarchar.Get("text", null)));
        Assert.Equal("clob", H2ValueMapper.ToObject(ValueClob.CreateSmall("clob")));
    }

    [Fact]
    public void ToObjectBinaryDateAndTimeFamilies()
    {
        byte[] bytes = [1, 2, 3];
        Assert.Equal(bytes, H2ValueMapper.ToObject(ValueVarbinary.Get(bytes)));
        Assert.Equal(bytes, H2ValueMapper.ToObject(ValueBlob.CreateSmall(bytes)));
        // JSON reads as the UTF-8 bytes of the document, like the reference's valueToDefaultObject.
        byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes("{\"a\":1}");
        Assert.Equal(jsonBytes, H2ValueMapper.ToObject(ValueJson.FromJson("{\"a\":1}")));

        Value date = ValueDate.FromDateValue(DateTimeUtils.DateValue(2024, 3, 5));
        Assert.Equal(new DateTime(2024, 3, 5), H2ValueMapper.ToObject(date));

        Value time = ValueTime.FromNanos((12L * 3600 + 34 * 60 + 56) * 1_000_000_000L);
        Assert.Equal(new TimeSpan(12, 34, 56), H2ValueMapper.ToObject(time));

        Value timestamp = ValueTimestamp.FromDateValueAndNanos(
            DateTimeUtils.DateValue(2024, 3, 5), 12L * 3600 * 1_000_000_000L);
        Assert.Equal(new DateTime(2024, 3, 5, 12, 0, 0), H2ValueMapper.ToObject(timestamp));

        Guid uuid = Guid.NewGuid();
        Assert.Equal(uuid, H2ValueMapper.ToObject(ValueUuid.Get(uuid)));
    }

    [Fact]
    public void ToObjectOffsetAndIntervalAndUnknown()
    {
        Value timeTz = ValueTimeTimeZone.FromNanos(12L * 3600 * 1_000_000_000L, 2 * 3600);
        DateTimeOffset offset = Assert.IsType<DateTimeOffset>(H2ValueMapper.ToObject(timeTz));
        Assert.Equal(TimeSpan.FromHours(2), offset.Offset);

        Value timestampTz = ValueTimestampTimeZone.FromDateValueAndNanos(
            DateTimeUtils.DateValue(2024, 3, 5), 0, -3 * 3600);
        DateTimeOffset stampOffset = Assert.IsType<DateTimeOffset>(H2ValueMapper.ToObject(timestampTz));
        Assert.Equal(TimeSpan.FromHours(-3), stampOffset.Offset);

        Value interval = ValueInterval.From(
            IntervalQualifier.DayToSecond, false, 1L, 2L * 3_600_000_000_000L);
        TimeSpan span = Assert.IsType<TimeSpan>(H2ValueMapper.ToObject(interval));
        Assert.Equal(new TimeSpan(1, 2, 0, 0), span);

        Value geometry = ValueGeometry.Get("POINT (1 2)");
        Assert.Equal("POINT (1 2)", H2ValueMapper.ToObject(geometry));

        Assert.Throws<TODOException>(
            () => H2ValueMapper.ToObject(ValueJavaObject.Get([1, 2])));
    }

    [Fact]
    public void ToObjectYearMonthIntervalRefusesTimeSpan()
    {
        Value interval = ValueInterval.From(IntervalQualifier.YearToMonth, false, 1L, 0L);
        Assert.Throws<TODOException>(() => H2ValueMapper.ToObject(interval));
    }

    [Fact]
    public void ToObjectRowAndArray()
    {
        ValueRow row = ValueRow.Get([
            ValueInteger.Get(1), ValueNull.Instance, ValueVarchar.Get("x", null)]);
        object?[] mapped = H2ValueMapper.ToRowObject(row);
        Assert.Equal(1, mapped[0]);
        Assert.Equal(DBNull.Value, mapped[1]);
        Assert.Equal("x", mapped[2]);

        ValueArray array = ValueArray.Get(
            TypeInfo.TypeInteger, [ValueInteger.Get(1), ValueInteger.Get(2)], null);
        Assert.Equal(new object[] { 1, 2 }, H2ValueMapper.ToArrayObject(array));
    }

    [Fact]
    public void ToArrayObjectWidensAndRefusesNullsInValueTypes()
    {
        ValueArray array = ValueArray.Get(
            TypeInfo.TypeInteger, [ValueInteger.Get(1), ValueInteger.Get(2)], null);
        Assert.Equal(new long[] { 1, 2 }, H2ValueMapper.ToArrayObject(array, typeof(long[])));

        ValueArray withNull = ValueArray.Get(
            TypeInfo.TypeInteger, [ValueInteger.Get(1), ValueNull.Instance], null);
        Assert.Throws<InvalidCastException>(
            () => H2ValueMapper.ToArrayObject(withNull, typeof(int[])));

        int?[] nullable = (int?[])H2ValueMapper.ToArrayObject(withNull, typeof(int?[]));
        Assert.Equal(1, nullable[0]);
        Assert.Null(nullable[1]);
    }

    [Fact]
    public void ToArrayObjectRecursesIntoNestedArrays()
    {
        ValueArray first = ValueArray.Get(
            TypeInfo.TypeInteger, [ValueInteger.Get(1)], null);
        ValueArray second = ValueArray.Get(
            TypeInfo.TypeInteger, [ValueInteger.Get(2), ValueInteger.Get(3)], null);
        ValueArray nested = ValueArray.Get(
            TypeInfo.GetHigherType(first.GetTypeInfo(), second.GetTypeInfo()),
            [first, second], null);

        int[][] result = (int[][])H2ValueMapper.ToArrayObject(nested, typeof(int[][]));
        Assert.Equal([1], result[0]);
        Assert.Equal([2, 3], result[1]);
    }

    // GetFieldType: one per engine family.
    [Theory]
    [InlineData(Value.Boolean, typeof(bool))]
    [InlineData(Value.Tinyint, typeof(sbyte))]
    [InlineData(Value.Smallint, typeof(short))]
    [InlineData(Value.Integer, typeof(int))]
    [InlineData(Value.Bigint, typeof(long))]
    [InlineData(Value.Numeric, typeof(decimal))]
    [InlineData(Value.Decfloat, typeof(decimal))]
    [InlineData(Value.Real, typeof(float))]
    [InlineData(Value.Double, typeof(double))]
    [InlineData(Value.Char, typeof(string))]
    [InlineData(Value.Varchar, typeof(string))]
    [InlineData(Value.Clob, typeof(string))]
    [InlineData(Value.Enum, typeof(string))]
    [InlineData(Value.Null, typeof(string))]
    [InlineData(Value.Binary, typeof(byte[]))]
    [InlineData(Value.Varbinary, typeof(byte[]))]
    [InlineData(Value.Blob, typeof(byte[]))]
    [InlineData(Value.Json, typeof(byte[]))]
    [InlineData(Value.Date, typeof(DateTime))]
    [InlineData(Value.Timestamp, typeof(DateTime))]
    [InlineData(Value.Time, typeof(TimeSpan))]
    [InlineData(Value.TimeTz, typeof(DateTimeOffset))]
    [InlineData(Value.TimestampTz, typeof(DateTimeOffset))]
    [InlineData(Value.Uuid, typeof(Guid))]
    [InlineData(Value.IntervalYearToMonth, typeof(TimeSpan))]
    [InlineData(Value.IntervalDayToSecond, typeof(TimeSpan))]
    public void GetFieldTypeMapsEveryFamily(int valueType, Type expected)
    {
        Assert.Equal(expected, H2ValueMapper.GetFieldType(TypeInfosByValueType[valueType]));
    }

    private static IReadOnlyDictionary<int, TypeInfo> TypeInfosByValueType { get; } =
        BuildTypeInfos();

    private static Dictionary<int, TypeInfo> BuildTypeInfos()
    {
        Dictionary<int, TypeInfo> map = new()
        {
            [Value.Boolean] = TypeInfo.TypeBoolean,
            [Value.Tinyint] = TypeInfo.TypeTinyint,
            [Value.Smallint] = TypeInfo.TypeSmallint,
            [Value.Integer] = TypeInfo.TypeInteger,
            [Value.Bigint] = TypeInfo.TypeBigint,
            [Value.Numeric] = TypeInfo.TypeNumericScale0,
            [Value.Decfloat] = TypeInfo.TypeDecfloat,
            [Value.Real] = TypeInfo.TypeReal,
            [Value.Double] = TypeInfo.TypeDouble,
            [Value.Char] = TypeInfo.TypeChar,
            [Value.Varchar] = TypeInfo.TypeVarchar,
            [Value.Clob] = TypeInfo.TypeClob,
            [Value.Enum] = TypeInfo.TypeEnumUndefined,
            [Value.Null] = TypeInfo.TypeNull,
            [Value.Binary] = TypeInfo.TypeBinary,
            [Value.Varbinary] = TypeInfo.TypeVarbinary,
            [Value.Blob] = TypeInfo.TypeBlob,
            [Value.Json] = TypeInfo.TypeJson,
            [Value.Date] = TypeInfo.TypeDate,
            [Value.Timestamp] = TypeInfo.TypeTimestamp,
            [Value.Time] = TypeInfo.TypeTime,
            [Value.TimeTz] = TypeInfo.TypeTimeTz,
            [Value.TimestampTz] = TypeInfo.TypeTimestampTz,
            [Value.Uuid] = TypeInfo.TypeUuid,
            [Value.IntervalYearToMonth] = TypeInfo.TypeIntervalYearToMonth,
            [Value.IntervalDayToSecond] = TypeInfo.TypeIntervalDayToSecond,
        };
        return map;
    }

    // GetDbType: one per branch plus the enum unwrap.
    [Fact]
    public void GetDbTypeCoversEveryClrShape()
    {
        Assert.Equal(DbType.Object, H2ValueMapper.GetDbType(null));
        Assert.Equal(DbType.Object, H2ValueMapper.GetDbType(DBNull.Value));
        Assert.Equal(DbType.Boolean, H2ValueMapper.GetDbType(true));
        Assert.Equal(DbType.SByte, H2ValueMapper.GetDbType((byte)1));
        Assert.Equal(DbType.SByte, H2ValueMapper.GetDbType((sbyte)1));
        Assert.Equal(DbType.Int16, H2ValueMapper.GetDbType((short)1));
        Assert.Equal(DbType.Int32, H2ValueMapper.GetDbType(1));
        Assert.Equal(DbType.Int64, H2ValueMapper.GetDbType(1L));
        Assert.Equal(DbType.VarNumeric, H2ValueMapper.GetDbType(new BigInteger(1)));
        Assert.Equal(DbType.Single, H2ValueMapper.GetDbType(1f));
        Assert.Equal(DbType.Double, H2ValueMapper.GetDbType(1d));
        Assert.Equal(DbType.Decimal, H2ValueMapper.GetDbType(1m));
        Assert.Equal(DbType.String, H2ValueMapper.GetDbType('c'));
        Assert.Equal(DbType.String, H2ValueMapper.GetDbType("s"));
        using (StringReader text = new("s"))
        {
            Assert.Equal(DbType.String, H2ValueMapper.GetDbType(text));
        }

        Assert.Equal(DbType.Binary, H2ValueMapper.GetDbType(new byte[1]));
        using (MemoryStream binary = new())
        {
            Assert.Equal(DbType.Binary, H2ValueMapper.GetDbType(binary));
        }
        Assert.Equal(DbType.DateTime2, H2ValueMapper.GetDbType(DateTime.Now));
        Assert.Equal(DbType.Date, H2ValueMapper.GetDbType(DateOnly.MinValue));
        Assert.Equal(DbType.Time, H2ValueMapper.GetDbType(TimeOnly.MinValue));
        Assert.Equal(DbType.Object, H2ValueMapper.GetDbType(TimeSpan.Zero));
        Assert.Equal(DbType.DateTimeOffset, H2ValueMapper.GetDbType(DateTimeOffset.Now));
        Assert.Equal(DbType.Guid, H2ValueMapper.GetDbType(Guid.NewGuid()));
        Assert.Equal(DbType.Object, H2ValueMapper.GetDbType(new Random()));
        Assert.Equal(DbType.Int32, H2ValueMapper.GetDbType(SampleEnum.First));
    }

    // ToDecimal: a non-finite DECFLOAT refuses.
    [Fact]
    public void ToDecimalRefusesNonFiniteDecfloat()
    {
        Assert.Throws<InvalidCastException>(
            () => H2ValueMapper.ToDecimal(ValueDecfloat.PositiveInfinity));
    }
}
