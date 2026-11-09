/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 106: the wire form of every SQL value. One round trip per value type and per
 * compact/full tag form: booleans, the integer widths with their negative and small
 * forms, REAL/DOUBLE 0/1/other, short and long strings and binary strings, CHAR,
 * date/time/timestamp with and without zone, UUID, NUMERIC small/scaled/large, ARRAY,
 * ROW, INTERVAL (both signs), JSON and the LOBs.
 */
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Db;
using H2Sharp.Store;
using H2Sharp.Util;
using H2Sharp.Values;
using System;
using System.IO;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ValueDataTypeWireTests
{
    private static ValueDataType NewType()
        => new(new TestCastDataProvider(), CompareMode.GetInstance(null, 0), null, null);

    private static void RoundTrip(ValueDataType type, Value? value)
    {
        WriteBuffer buffer = new(256);
        type.Write(buffer, value!);
        byte[] bytes = buffer.ToArray();

        int position = 0;
        Value? back = type.Read(bytes, ref position);
        Assert.Equal(bytes.Length, position);
        if (value is null || value is ValueNull)
        {
            Assert.True(back is null || back is ValueNull);
            return;
        }

        Assert.NotNull(back);
        Assert.Equal(0, type.Compare(value, back));
        Assert.Equal(value.GetString(), back!.GetString());
    }

    [Fact]
    public void EveryValueTypeKeepsItsWireForm()
    {
        ValueDataType type = NewType();
        long date = DateTimeUtils.DateValue(2024, 1, 2);
        long nanos = System.TimeSpan.FromHours(10.5).Ticks * 100L;

        Value?[] values =
        [
            ValueNull.Instance,
            ValueBoolean.True,
            ValueBoolean.False,
            ValueTinyint.Get(0),
            ValueTinyint.Get(-1),
            ValueTinyint.Get(sbyte.MinValue),
            ValueSmallint.Get(0),
            ValueSmallint.Get(-2),
            ValueSmallint.Get(short.MinValue),
            ValueInteger.Get(0),
            ValueInteger.Get(5),
            ValueInteger.Get(16),
            ValueInteger.Get(-7),
            ValueInteger.Get(int.MinValue),
            ValueBigint.Get(0),
            ValueBigint.Get(-7),
            ValueBigint.Get(5_000_000_000L),
            ValueReal.Get(0f),
            ValueReal.Get(1f),
            ValueReal.Get(1.5f),
            ValueReal.Get(-2.25f),
            ValueDouble.Get(0d),
            ValueDouble.Get(1d),
            ValueDouble.Get(2.5d),
            ValueDouble.Get(-3.5d),
            ValueVarchar.Get(""),
            ValueVarchar.Get("abc"),
            ValueVarchar.Get(new string('x', 40)),
            ValueVarcharIgnoreCase.Get("AbC"),
            ValueVarcharIgnoreCase.Get(new string('y', 40)),
            ValueVarbinary.Get([0, 1, 255]),
            ValueVarbinary.Get(new byte[40]),
            ValueBinary.Get([1, 2, 3]),
            ValueChar.Get("z"),
            ValueDate.FromDateValue(date),
            ValueTime.FromNanos(nanos),
            ValueTimeTimeZone.FromNanos(nanos, 3 * 3_600),
            ValueTimestamp.FromDateValueAndNanos(date, nanos),
            ValueTimestampTimeZone.FromDateValueAndNanos(date, nanos, 3 * 3_600),
            ValueUuid.Get(0x0011223344556677L, unchecked((long)0x8899aabbccddeeffUL)),
            ValueNumeric.Get(BigDecimal.Zero),
            ValueNumeric.Get(BigDecimal.One),
            ValueNumeric.Get(BigDecimal.FromLong(12)),
            ValueNumeric.Get(new BigDecimal(12_345, 2)),
            ValueNumeric.Get(new BigDecimal(
                System.Numerics.BigInteger.Parse("123456789012345678901234567890"), 0)),
            ValueArray.Get([ValueInteger.Get(1), ValueNull.Instance], null),
            ValueRow.Get([ValueInteger.Get(1), ValueVarchar.Get("a")]),
            ValueInterval.From(IntervalQualifier.DayToSecond, false, 1, (2L * 3_600 + 3L * 60 + 4L) * 1_000_000_000L),
            ValueInterval.From(IntervalQualifier.YearToMonth, true, 1, 2),
            ValueJson.FromJson("{\"a\": 1}"),
            ValueGeometry.Get("POINT (1 1)"),
            ValueBlob.CreateSmall([1, 2, 3]),
            ValueClob.CreateSmall("clob"),
        ];

        foreach (Value? value in values)
        {
            RoundTrip(type, value);
        }
    }

    [Fact]
    public void StoredLobsGoThroughTheHandler()
    {
        Database database = new(ConnectionInfo.Memory("lob_wire_" + Guid.NewGuid().ToString("N")));
        LobStorageMap storage = (LobStorageMap)database.GetLobStorage();
        try
        {
            byte[] payload = new byte[300];
            new Random(7).NextBytes(payload);
            ValueBlob blob = storage.CreateBlob(new MemoryStream(payload), payload.Length);
            ValueClob clob = storage.CreateClob(new StringReader("stored clob"), 11);

            ValueDataType type = new(database, CompareMode.GetInstance(null, 0), database, null);
            RoundTrip(type, blob);
            RoundTrip(type, clob);

            // A stored LOB whose metadata is gone is a corrupt file.
            WriteBuffer missing = new(16);
            _ = missing.Put((byte)15).PutVarInt(-2).PutVarLong(987_654L).PutVarInt(0);
            byte[] missingBytes = missing.ToArray();
            int missingPosition = 0;
            _ = Assert.ThrowsAny<MvStoreException>(
                () => type.Read(missingBytes, ref missingPosition));

            // Without a handler the stored LOB cannot be resolved either.
            ValueDataType handlerless = NewType();
            WriteBuffer written = new(64);
            handlerless.Write(written, blob);
            byte[] storedBytes = written.ToArray();
            int storedPosition = 0;
            _ = Assert.ThrowsAny<TODOException>(
                () => handlerless.Read(storedBytes, ref storedPosition));

            storage.RemoveLob(blob);
            storage.RemoveLob(clob);
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void LegacyBigintTagsAreRead()
    {
        ValueDataType type = NewType();

        WriteBuffer negative = new(16);
        _ = negative.Put((byte)67).PutVarLong(5L);
        byte[] negativeBytes = negative.ToArray();
        int negativePosition = 0;
        Assert.Equal(-5L, type.Read(negativeBytes, ref negativePosition)!.GetLong());
        Assert.Equal(negativeBytes.Length, negativePosition);

        WriteBuffer small = new(8);
        _ = small.Put((byte)(48 + 3));
        byte[] smallBytes = small.ToArray();
        int smallPosition = 0;
        Assert.Equal(3L, type.Read(smallBytes, ref smallPosition)!.GetLong());
        Assert.Equal(smallBytes.Length, smallPosition);

        WriteBuffer full = new(16);
        _ = full.Put((byte)5).PutVarLong(123_456_789L);
        byte[] fullBytes = full.ToArray();
        int fullPosition = 0;
        Assert.Equal(123_456_789L, type.Read(fullBytes, ref fullPosition)!.GetLong());
        Assert.Equal(fullBytes.Length, fullPosition);
    }
}
