// Copyright 2026 H2Sharp contributors.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// The row format itself: every scalar tag compact form, temporal/time-zone encodings,
// collections and small in-memory LOBs must survive a write and read back equal.
using System;
using System.Linq;
using System.Numerics;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Db;
using H2Sharp.Util;
using H2Sharp.Values;
using H2Sharp.Values.Lobs;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class ValueDataTypeTests : StoreTestBase
{
    [Fact]
    public void ScalarsRoundTripThroughTheirCompactForms()
    {
        AssertRoundTrip(ValueNull.Instance, r => Assert.Same(ValueNull.Instance, r));
        AssertRoundTrip(ValueBoolean.True, r => Assert.True(r.GetBoolean()));
        AssertRoundTrip(ValueBoolean.False, r => Assert.False(r.GetBoolean()));
        AssertRoundTrip(ValueTinyint.Get(sbyte.MinValue), r => Assert.Equal(sbyte.MinValue, r.GetByte()));
        AssertRoundTrip(ValueTinyint.Get(sbyte.MaxValue), r => Assert.Equal(sbyte.MaxValue, r.GetByte()));
        AssertRoundTrip(ValueSmallint.Get(short.MinValue), r => Assert.Equal(short.MinValue, r.GetShort()));
        AssertRoundTrip(ValueSmallint.Get(short.MaxValue), r => Assert.Equal(short.MaxValue, r.GetShort()));
        AssertRoundTrip(ValueInteger.Get(-1), r => Assert.Equal(-1, r.GetInt()));
        AssertRoundTrip(ValueInteger.Get(0), r => Assert.Equal(0, r.GetInt()));
        AssertRoundTrip(ValueInteger.Get(15), r => Assert.Equal(15, r.GetInt()));
        AssertRoundTrip(ValueInteger.Get(16), r => Assert.Equal(16, r.GetInt()));
        AssertRoundTrip(ValueBigint.Get(-1L), r => Assert.Equal(-1L, r.GetLong()));
        AssertRoundTrip(ValueBigint.Get(7L), r => Assert.Equal(7L, r.GetLong()));
        AssertRoundTrip(ValueBigint.Get(8L), r => Assert.Equal(8L, r.GetLong()));
        AssertRoundTrip(ValueBigint.Get(long.MinValue), r => Assert.Equal(long.MinValue, r.GetLong()));
        AssertRoundTrip(ValueNumeric.Get(new BigDecimal("0")), r => Assert.Equal("0", r.GetBigDecimal().ToString()));
        AssertRoundTrip(ValueNumeric.Get(new BigDecimal("1")), r => Assert.Equal("1", r.GetBigDecimal().ToString()));
        AssertRoundTrip(ValueNumeric.Get(new BigDecimal("1.25")), r => Assert.Equal("1.25", r.GetBigDecimal().ToString()));
        AssertRoundTrip(
            ValueNumeric.Get(new BigDecimal("123456789012345678901234567890.12")),
            r => Assert.Equal("123456789012345678901234567890.12", r.GetBigDecimal().ToString()));
        AssertRoundTrip(ValueReal.Get(0f), r => Assert.Equal(0f, r.GetFloat()));
        AssertRoundTrip(ValueReal.Get(1f), r => Assert.Equal(1f, r.GetFloat()));
        AssertRoundTrip(ValueReal.Get(1.5f), r => Assert.Equal(1.5f, r.GetFloat()));
        AssertRoundTrip(ValueDouble.Get(0d), r => Assert.Equal(0d, r.GetDouble()));
        AssertRoundTrip(ValueDouble.Get(1d), r => Assert.Equal(1d, r.GetDouble()));
        AssertRoundTrip(ValueDouble.Get(1.5d), r => Assert.Equal(1.5d, r.GetDouble()));
        AssertRoundTrip(ValueChar.Get("a"), r => Assert.Equal("a", r.GetString()));
        AssertRoundTrip(ValueVarchar.Get(string.Empty), r => Assert.Equal(string.Empty, r.GetString()));
        AssertRoundTrip(ValueVarchar.Get(new string('x', 31)), r => Assert.Equal(new string('x', 31), r.GetString()));
        AssertRoundTrip(ValueVarchar.Get(new string('x', 32)), r => Assert.Equal(new string('x', 32), r.GetString()));
        AssertRoundTrip(
            ValueVarcharIgnoreCase.Get(new string('y', 40)),
            r => Assert.Equal(new string('y', 40), r.GetString()));
        AssertRoundTrip(
            ValueBinary.GetNoCopy(new byte[40]),
            r => Assert.True(new byte[40].SequenceEqual(r.GetBytesNoCopy()!)));
        AssertRoundTrip(
            ValueVarbinary.Get(Enumerable.Range(0, 31).Select(i => (byte)i).ToArray()),
            r => Assert.Equal(31, r.GetBytesNoCopy()!.Length));
        AssertRoundTrip(
            ValueVarbinary.Get(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()),
            r => Assert.Equal(32, r.GetBytesNoCopy()!.Length));
        AssertRoundTrip(ValueUuid.Get(Guid.Parse("12345678-1234-1234-1234-1234567890ab")),
            r => Assert.Equal(
                Guid.Parse("12345678-1234-1234-1234-1234567890ab"), ((ValueUuid)r).GetUuid()));
    }

    [Fact]
    public void TemporalValuesRoundTripWithTimeZones()
    {
        AssertRoundTrip(ValueDate.Parse("2024-05-06"), r => Assert.Equal("2024-05-06", r.GetString()));
        AssertRoundTrip(
            ValueTime.FromNanos(3723000000000L), r => Assert.Equal(3723000000000L, ((ValueTime)r).GetNanos()));
        ValueTimestamp timestamp = ValueTimestamp.Parse("2024-05-06 10:20:30.123", null);
        AssertRoundTrip(timestamp, r =>
        {
            ValueTimestamp read = (ValueTimestamp)r;
            Assert.Equal(timestamp.GetDateValue(), read.GetDateValue());
            Assert.Equal(timestamp.GetTimeNanos(), read.GetTimeNanos());
        });
        AssertRoundTrip(
            ValueTimeTimeZone.FromNanos(3661000000123L, 3600),
            r => Assert.Equal(new DateTimeOffset(1, 1, 1, 1, 1, 1, TimeSpan.FromHours(1)).AddTicks(1), ToDateTimeOffset(r)));
        AssertRoundTrip(
            ValueTimeTimeZone.FromNanos(3661000000123L, 3661),
            r => Assert.Equal(3661, ((ValueTimeTimeZone)r).GetTimeZoneOffsetSeconds()));
        AssertRoundTrip(
            ValueTimeTimeZone.FromNanos(3661000000123L, -3661),
            r => Assert.Equal(-3661, ((ValueTimeTimeZone)r).GetTimeZoneOffsetSeconds()));
        AssertRoundTrip(
            ValueTimestampTimeZone.Parse("2024-05-06 10:20:30+02:00", null),
            r => Assert.Equal(7200, ((ValueTimestampTimeZone)r).GetTimeZoneOffsetSeconds()));
    }

    [Fact]
    public void CollectionsGeometryAndSmallLobsRoundTrip()
    {
        AssertRoundTrip(
            ValueArray.Get([ValueInteger.Get(1), ValueInteger.Get(2)], null),
            r =>
            {
                Value[] list = ((ValueArray)r).GetList();
                Assert.Equal(2, list.Length);
                Assert.Equal(1, list[0].GetInt());
            });
        AssertRoundTrip(
            ValueArray.Get([ValueVarchar.Get("a"), ValueVarchar.Get("b")], null),
            r => Assert.Equal(2, ((ValueArray)r).GetList().Length));
        AssertRoundTrip(
            ValueRow.Get([ValueInteger.Get(1), ValueNull.Instance]),
            r => Assert.Equal(2, ((ValueRow)r).GetList().Length));
        ValueGeometry geometry = (ValueGeometry)ValueGeometry.Get("POINT(1 2)");
        AssertRoundTrip(
            geometry,
            r => Assert.True(geometry.GetBytesNoCopy()!.SequenceEqual(
                ((ValueGeometry)r).GetBytesNoCopy()!)));
        AssertRoundTrip(
            ValueBlob.CreateSmall([1, 2, 3]),
            r => Assert.True(new byte[] { 1, 2, 3 }.SequenceEqual(r.GetBytesNoCopy()!)));
        AssertRoundTrip(
            ValueClob.CreateSmall("hello"),
            r => Assert.Equal("hello", r.GetString()));
        AssertRoundTrip(
            new ValueBlob(new LobDataFile("missing", 10), 10),
            r => Assert.Equal(10L, r.OctetLength()));
        AssertRoundTrip(
            new ValueClob(new LobDataFile("missing", 10), 10, 5),
            r =>
            {
                // The file reference carries no char length: the reader answers -1, as in H2.
                Assert.Equal(10L, r.OctetLength());
                Assert.Equal(-1L, r.CharLength());
            });
    }

    [Fact]
    public void LegacyAndErrorTagsAreHandled()
    {
        ValueDataType dataType = new(null, CompareMode.GetInstance(null, 0), null, null);

        WriteBuffer buffer = new();
        buffer.Put((byte)24).PutVarLong(1L).PutVarLong(1000L).PutVarInt(2).PutVarInt(120);
        byte[] bytes = buffer.ToArray();
        int position = 0;
        ValueTimestampTimeZone read = (ValueTimestampTimeZone)dataType.Read(bytes, ref position);
        Assert.Equal(bytes.Length, position);
        Assert.Equal(1L, read.GetDateValue());
        Assert.Equal(7200, read.GetTimeZoneOffsetSeconds());

        WriteBuffer unknown = new();
        unknown.Put((byte)250);
        byte[] unknownBytes = unknown.ToArray();
        int unknownPosition = 0;
        Assert.ThrowsAny<Exception>(() => dataType.Read(unknownBytes, ref unknownPosition));

        Assert.ThrowsAny<Exception>(() =>
        {
            WriteBuffer java = new();
            dataType.Write(java, ValueJavaObject.Get([1]));
        });
        WriteBuffer javaRead = new();
        javaRead.Put((byte)19);
        byte[] javaBytes = javaRead.ToArray();
        int javaPosition = 0;
        Assert.ThrowsAny<Exception>(() => dataType.Read(javaBytes, ref javaPosition));

        WriteBuffer rowOrdinal = new();
        rowOrdinal.Put((byte)25).PutVarInt(3);
        byte[] rowBytes = rowOrdinal.ToArray();
        int rowPosition = 0;
        Assert.Equal(3, ((ValueInteger)dataType.Read(rowBytes, ref rowPosition)).GetInt());
    }

    [Fact]
    public void CompareHandlesNullsRowsAndDescendingOrder()
    {
        ValueDataType ascending = new(null, CompareMode.GetInstance(null, 0), null, null);
        Assert.True(ascending.CompareValues(ValueNull.Instance, ValueInteger.Get(1), 0) < 0);
        Assert.True(ascending.CompareValues(ValueInteger.Get(1), ValueNull.Instance, 0) > 0);
        Assert.True(ascending.CompareValues(null, null, 0) == 0);

        ValueDataType descending = new(
            null, CompareMode.GetInstance(null, 0), null, [H2Sharp.Result.SortOrder.Descending]);
        Assert.True(descending.Compare(
            ValueRow.Get([ValueInteger.Get(1)]), ValueRow.Get([ValueInteger.Get(2)])) > 0);

        Assert.True(ascending.GetMemory(ValueInteger.Get(1)) > 0);
        Assert.ThrowsAny<Exception>(() => ascending.GetMemory(null!));
        Assert.Equal(3, ascending.CreateStorage(3).Length);
    }

    private static void AssertRoundTrip(Value value, Action<Value> check)
    {
        ValueDataType dataType = new(null, CompareMode.GetInstance(null, 0), null, null);
        WriteBuffer buffer = new();
        dataType.Write(buffer, value);
        byte[] bytes = buffer.ToArray();
        int position = 0;
        Value read = dataType.Read(bytes, ref position);
        Assert.Equal(bytes.Length, position);
        Assert.Equal(value.GetValueType(), read.GetValueType());
        check(read);
    }

    private static DateTimeOffset ToDateTimeOffset(Value value)
    {
        ValueTimeTimeZone time = (ValueTimeTimeZone)value;
        long totalTicks = time.GetNanos() / 100L;
        return new DateTimeOffset(
            new DateTime(1, 1, 1).AddTicks(totalTicks),
            TimeSpan.FromSeconds(time.GetTimeZoneOffsetSeconds()));
    }
}
