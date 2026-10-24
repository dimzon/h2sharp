/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The dialect's engine layer is the home of the literal bodies the value classes' GetSql
 * overrides delegate to. For every value, under both flag sets, the override and the dialect
 * method have to spell the same SQL — this is the test that fails the day one of them drifts.
 */
using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Data;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class DialectEngineTests
{
    public static TheoryData<Value> Values => new()
    {
        ValueNull.Instance,
        ValueBoolean.True,
        ValueBoolean.False,
        ValueTinyint.Get(-1),
        ValueSmallint.Get(-2),
        ValueInteger.Get(-3),
        ValueBigint.Get(5),
        ValueBigint.Get(5_000_000_000L),
        ValueNumeric.Get(new BigDecimal(12345, 2)),
        ValueNumeric.Get(BigDecimal.FromLong(123)),
        ValueNumeric.Get(new BigDecimal(
            BigInteger.Parse("12345678901234567890123456789", CultureInfo.InvariantCulture), 0)),
        ValueReal.Get(1.5f),
        ValueReal.Get(float.PositiveInfinity),
        ValueReal.Get(float.NaN),
        ValueDouble.Get(1e7),
        ValueDouble.Get(double.NegativeInfinity),
        ValueDecfloat.Get(new BigDecimal(15, 1)),
        ValueChar.Get("x"),
        ValueVarchar.Get("O'Brien"),
        ValueVarcharIgnoreCase.Get("AbC"),
        ValueVarbinary.Get([0xCA, 0xFE]),
        ValueBinary.Get([0xCA, 0xFE]),
        ValueUuid.Get(0x0011223344556677L, unchecked((long)0x8899aabbccddeeffUL)),
        ValueDate.FromDateValue(DateTimeUtils.DateValue(2024, 1, 2)),
        ValueTime.FromNanos(TimeSpan.FromHours(10).Ticks * 100L),
        ValueTimeTimeZone.FromNanos(TimeSpan.FromHours(10).Ticks * 100L, 3 * 3_600),
        ValueTimestamp.FromDateValueAndNanos(
            DateTimeUtils.DateValue(2024, 1, 2), TimeSpan.FromHours(10.5).Ticks * 100L),
        ValueTimestampTimeZone.FromDateValueAndNanos(
            DateTimeUtils.DateValue(2024, 1, 2), TimeSpan.FromHours(10.5).Ticks * 100L, 3 * 3_600),
        ValueInterval.From(IntervalQualifier.DayToSecond, false, 1, (2L * 3_600 + 3L * 60 + 4L) * 1_000_000_000L),
        ValueInterval.From(IntervalQualifier.DayToSecond, true, 1, (2L * 3_600 + 3L * 60 + 4L) * 1_000_000_000L),
        ValueJson.FromJson("{\"a\": 1}"),
        ValueArray.Get([ValueInteger.Get(1), ValueNull.Instance], null),
        ValueRow.Get([ValueInteger.Get(1), ValueVarchar.Get("a")]),
        ValueJavaObject.Get([1, 2, 3]),
        ValueGeometry.Get("POINT (1 1)"),
    };

    [Theory]
    [MemberData(nameof(Values))]
    public void DialectWritesWhatGetSqlWrites(Value value)
    {
        foreach (int sqlFlags in new[] { HasSql.DefaultSqlFlags, HasSql.NoCasts })
        {
            Assert.Equal(
                value.GetSql(new StringBuilder(), sqlFlags).ToString(),
                DialectSql(value, sqlFlags));
        }
    }

    private static string DialectSql(Value value, int sqlFlags)
    {
        StringBuilder builder = new();
        _ = value switch
        {
            ValueNull v => Dialect.AppendNull(builder, v, sqlFlags),
            ValueBoolean v => Dialect.AppendBoolean(builder, v, sqlFlags),
            ValueTinyint v => Dialect.AppendTinyint(builder, v, sqlFlags),
            ValueSmallint v => Dialect.AppendSmallint(builder, v, sqlFlags),
            ValueInteger v => Dialect.AppendInteger(builder, v, sqlFlags),
            ValueBigint v => Dialect.AppendBigint(builder, v, sqlFlags),
            ValueNumeric v => Dialect.AppendNumeric(builder, v, sqlFlags),
            ValueReal v => Dialect.AppendReal(builder, v, sqlFlags),
            ValueDouble v => Dialect.AppendDouble(builder, v, sqlFlags),
            ValueDecfloat v => Dialect.AppendDecfloat(builder, v, sqlFlags),
            ValueChar v => Dialect.AppendChar(builder, v, sqlFlags),
            ValueVarchar v => Dialect.AppendVarchar(builder, v, sqlFlags),
            ValueVarcharIgnoreCase v => Dialect.AppendVarcharIgnoreCase(builder, v, sqlFlags),
            ValueVarbinary v => Dialect.AppendBytes(builder, v, sqlFlags),
            ValueBinary v => Dialect.AppendBinary(builder, v, sqlFlags),
            ValueUuid v => Dialect.AppendUuid(builder, v, sqlFlags),
            ValueDate v => Dialect.AppendDate(builder, v, sqlFlags),
            ValueTime v => Dialect.AppendTime(builder, v, sqlFlags),
            ValueTimeTimeZone v => Dialect.AppendTimeWithTimeZone(builder, v, sqlFlags),
            ValueTimestamp v => Dialect.AppendTimestamp(builder, v, sqlFlags),
            ValueTimestampTimeZone v => Dialect.AppendTimestampWithTimeZone(builder, v, sqlFlags),
            ValueInterval v => Dialect.AppendInterval(builder, v, sqlFlags),
            ValueJson v => Dialect.AppendJson(builder, v, sqlFlags),
            ValueArray v => Dialect.AppendArray(builder, v, sqlFlags),
            ValueRow v => Dialect.AppendRow(builder, v, sqlFlags),
            ValueJavaObject v => Dialect.AppendJavaObject(builder, v, sqlFlags),
            ValueGeometry v => Dialect.AppendGeometry(builder, v, sqlFlags),
            _ => throw new Xunit.Sdk.XunitException(
                "No dialect method writes the SQL of " + value.GetType().Name),
        };
        return builder.ToString();
    }
}
