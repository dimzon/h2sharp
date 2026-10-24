/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The dialect facade: the literal a .NET value becomes is exactly the literal the engine
 * prints for it, and a statement built from these literals reads back into the same values.
 */
using System;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class DialectTests
{
    [Theory]
    [InlineData(null, "NULL")]
    [InlineData(true, "TRUE")]
    [InlineData(false, "FALSE")]
    public void BooleanLiteral(bool? value, string expected)
        => Assert.Equal(expected, Dialect.AppendBoolean(new StringBuilder(), value).ToString());

    [Fact]
    public void IntegerLiterals()
    {
        Assert.Equal("NULL", Dialect.AppendTinyint(new StringBuilder(), null).ToString());
        Assert.Equal("CAST(-1 AS TINYINT)", Dialect.AppendTinyint(new StringBuilder(), -1).ToString());
        Assert.Equal("CAST(5 AS SMALLINT)", Dialect.AppendSmallint(new StringBuilder(), (short)5).ToString());
        Assert.Equal("5", Dialect.AppendInteger(new StringBuilder(), 5).ToString());
        Assert.Equal("CAST(5 AS BIGINT)", Dialect.AppendBigint(new StringBuilder(), 5L).ToString());
        Assert.Equal("5000000000", Dialect.AppendBigint(new StringBuilder(), 5_000_000_000L).ToString());
    }

    [Fact]
    public void NumericLiterals()
    {
        Assert.Equal("123.45", Dialect.AppendNumeric(new StringBuilder(), 123.45m).ToString());
        Assert.Equal("0.5", Dialect.AppendNumeric(new StringBuilder(), 0.5m).ToString());
        Assert.Equal("CAST(123 AS NUMERIC(3))", Dialect.AppendNumeric(new StringBuilder(), 123m).ToString());
        Assert.Equal(
            "CAST(-9223372036854775808 AS NUMERIC(19))",
            Dialect.AppendNumeric(new StringBuilder(), long.MinValue).ToString());
        Assert.Equal(
            "12345678901234567890123456789",
            Dialect.AppendNumeric(new StringBuilder(), 12345678901234567890123456789m).ToString());
        Assert.Equal("NULL", Dialect.AppendNumeric(new StringBuilder(), null).ToString());
    }

    [Fact]
    public void FloatingPointLiterals()
    {
        Assert.Equal("CAST(1.5 AS REAL)", Dialect.AppendReal(new StringBuilder(), 1.5f).ToString());
        Assert.Equal(
            "CAST('Infinity' AS REAL)",
            Dialect.AppendReal(new StringBuilder(), float.PositiveInfinity).ToString());
        Assert.Equal("CAST('NaN' AS REAL)", Dialect.AppendReal(new StringBuilder(), float.NaN).ToString());
        Assert.Equal(
            "CAST(1.5 AS DOUBLE PRECISION)", Dialect.AppendDouble(new StringBuilder(), 1.5).ToString());
        Assert.Equal(
            "CAST(1.0E7 AS DOUBLE PRECISION)", Dialect.AppendDouble(new StringBuilder(), 1e7).ToString());
        Assert.Equal(
            "CAST('-Infinity' AS DOUBLE PRECISION)",
            Dialect.AppendDouble(new StringBuilder(), double.NegativeInfinity).ToString());
    }

    [Fact]
    public void StringAndCharLiterals()
    {
        Assert.Equal("'O''Brien'", Dialect.AppendString(new StringBuilder(), "O'Brien").ToString());
        Assert.Equal("NULL", Dialect.AppendString(new StringBuilder(), null).ToString());
        Assert.Equal("CAST('x' AS CHAR(1))", Dialect.AppendChar(new StringBuilder(), 'x').ToString());
    }

    [Fact]
    public void BytesAndUuidLiterals()
    {
        Assert.Equal("X'cafe'", Dialect.AppendBytes(new StringBuilder(), [0xCA, 0xFE]).ToString());
        Assert.Equal("NULL", Dialect.AppendBytes(new StringBuilder(), null).ToString());
        Assert.Equal(
            "UUID '00112233-4455-6677-8899-aabbccddeeff'",
            Dialect.AppendUuid(new StringBuilder(), new Guid("00112233-4455-6677-8899-aabbccddeeff")).ToString());
        Assert.Equal("NULL", Dialect.AppendUuid(new StringBuilder(), null).ToString());
    }

    [Fact]
    public void DateTimeLiterals()
    {
        Assert.Equal("DATE '2024-01-02'", Dialect.AppendDate(new StringBuilder(), new DateOnly(2024, 1, 2)).ToString());
        Assert.Equal("TIME '10:00:00'", Dialect.AppendTime(new StringBuilder(), new TimeOnly(10, 0, 0)).ToString());
        Assert.Equal(
            "TIME '10:00:00.123'",
            Dialect.AppendTime(new StringBuilder(), new TimeOnly(10, 0, 0, 123)).ToString());
        Assert.Equal("TIME '10:00:00'", Dialect.AppendTime(new StringBuilder(), new TimeSpan(10, 0, 0)).ToString());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Dialect.AppendTime(new StringBuilder(), new TimeSpan(25, 0, 0)));
        Assert.Equal(
            "TIMESTAMP '2024-01-02 10:00:00.123'",
            Dialect.AppendTimestamp(new StringBuilder(), new DateTime(2024, 1, 2, 10, 0, 0, 123)).ToString());
        Assert.Equal(
            "TIMESTAMP WITH TIME ZONE '2024-01-02 10:00:00+03'",
            Dialect.AppendTimestampWithTimeZone(
                new StringBuilder(),
                new DateTimeOffset(new DateTime(2024, 1, 2, 10, 0, 0), TimeSpan.FromHours(3))).ToString());
        Assert.Equal(
            "TIMESTAMP WITH TIME ZONE '2024-01-02 10:00:00.5+03:30'",
            Dialect.AppendTimestampWithTimeZone(
                new StringBuilder(),
                new DateTimeOffset(
                    new DateTime(2024, 1, 2, 10, 0, 0, 500), TimeSpan.FromHours(3.5))).ToString());
        Assert.Equal(
            "INTERVAL '1 02:03:04' DAY TO SECOND",
            Dialect.AppendInterval(new StringBuilder(), new TimeSpan(1, 2, 3, 4)).ToString());
        Assert.Equal(
            "INTERVAL '-1 02:03:04' DAY TO SECOND",
            Dialect.AppendInterval(new StringBuilder(), new TimeSpan(1, 2, 3, 4).Negate()).ToString());
        Assert.Equal("NULL", Dialect.AppendTimestamp(new StringBuilder(), null).ToString());
        // The document is normalized before it is a literal, which is what the engine's
        // ValueJson.FromJson does: the spaces of the source do not survive it.
        Assert.Equal(
            "JSON '{\"a\":1}'",
            Dialect.AppendJson(new StringBuilder(), JsonDocument.Parse("{\"a\": 1}")).ToString());
    }

    [Fact]
    public void LiteralsReadBackAsTheSameValues()
    {
        DateTime timestamp = new(2024, 1, 2, 10, 0, 0, 123);
        DateTimeOffset timestampTz = new(timestamp, TimeSpan.FromHours(3));
        Guid uuid = new("00112233-4455-6677-8899-aabbccddeeff");

        StringBuilder sql = new("SELECT ");
        _ = Dialect.AppendTimestamp(sql, timestamp);
        _ = sql.Append(", ");
        _ = Dialect.AppendDate(sql, new DateOnly(2024, 1, 2));
        _ = sql.Append(", ");
        _ = Dialect.AppendTime(sql, new TimeOnly(10, 0, 0, 123));
        _ = sql.Append(", ");
        _ = Dialect.AppendInterval(sql, new TimeSpan(1, 2, 3, 4));
        _ = sql.Append(", ");
        _ = Dialect.AppendString(sql, "O'Brien");
        _ = sql.Append(", ");
        _ = Dialect.AppendBytes(sql, [0xCA, 0xFE]);
        _ = sql.Append(", ");
        _ = Dialect.AppendUuid(sql, uuid);
        _ = sql.Append(", ");
        _ = Dialect.AppendNumeric(sql, 123.45m);
        _ = sql.Append(", ");
        _ = Dialect.AppendBigint(sql, 5_000_000_000L);
        _ = sql.Append(", ");
        _ = Dialect.AppendBoolean(sql, true);
        _ = sql.Append(", ");
        _ = Dialect.AppendDouble(sql, 1.5);
        _ = sql.Append(", ");
        _ = Dialect.AppendNull(sql);
        _ = sql.Append(", ");
        _ = Dialect.AppendTimestampWithTimeZone(sql, timestampTz);

        using H2Connection connection = new("jdbc:h2:mem:dialect_literals");
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql.ToString();
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(timestamp, reader.GetDateTime(0));
        Assert.Equal(new DateTime(2024, 1, 2), reader.GetDateTime(1));
        Assert.Equal(new TimeSpan(0, 10, 0, 0, 123), reader.GetValue(2));
        Assert.Equal(new TimeSpan(1, 2, 3, 4), reader.GetValue(3));
        Assert.Equal("O'Brien", reader.GetString(4));
        Assert.Equal(new byte[] { 0xCA, 0xFE }, reader.GetValue(5));
        Assert.Equal(uuid, reader.GetGuid(6));
        Assert.Equal(123.45m, reader.GetDecimal(7));
        Assert.Equal(5_000_000_000L, (long)reader.GetValue(8)!);
        Assert.True(reader.GetBoolean(9));
        Assert.Equal(1.5, reader.GetDouble(10));
        Assert.Equal(DBNull.Value, reader.GetValue(11));
        Assert.Equal(timestampTz, (DateTimeOffset)reader.GetValue(12)!);
    }
}
