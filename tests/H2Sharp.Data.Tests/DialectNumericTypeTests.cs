/* Copyright 2026 H2Sharp contributors. MPL 2.0 or EPL 1.0.
 * The common dialect facade must keep SQL / CLR numeric types, even for the value 1. */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Numerics;
using System.Text;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class DialectNumericTypeTests
{
    public static IEnumerable<object[]> NumericValues()
    {
        yield return ["TINYINT", (sbyte)1];
        yield return ["SMALLINT", (short)1];
        yield return ["INTEGER", 1];
        yield return ["BIGINT", 1L];
        yield return ["REAL", 1f];
        yield return ["DOUBLE PRECISION", 1d];
        yield return ["NUMERIC", 1m];
        yield return ["NUMERIC", 1.000m];
        yield return ["NUMERIC", 0.5m];
        yield return ["NUMERIC", 0.00m];
        yield return ["NUMERIC", -0.5m];
    }

    [Theory]
    [MemberData(nameof(NumericValues))]
    public void NumericLiteralsKeepTheirCallerType(string sqlType, object value)
    {
        string literal = Render(value);
        using H2Connection connection = new("jdbc:h2:mem:dialect_type_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT " + literal;
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(sqlType, reader.GetDataTypeName(0));
        Assert.Equal(value.GetType(), reader.GetFieldType(0));
        object actual = reader.GetValue(0);
        Assert.Equal(value.GetType(), actual.GetType());
        Assert.Equal(value, actual);
        if (value is decimal number)
            Assert.Equal((decimal.GetBits(number)[3] >> 16) & 255,
                (decimal.GetBits((decimal)actual)[3] >> 16) & 255);
    }

    [Theory]
    [InlineData("TINYINT")]
    [InlineData("SMALLINT")]
    [InlineData("INTEGER")]
    [InlineData("BIGINT")]
    [InlineData("REAL")]
    [InlineData("DOUBLE PRECISION")]
    [InlineData("NUMERIC")]
    public void TypedNullRetainsColumnType(string sqlType)
    {
        StringBuilder sql = new("SELECT ");
        Type type;
        switch (sqlType)
        {
            case "TINYINT": Dialect.AppendTinyint(sql, null); type = typeof(sbyte); break;
            case "SMALLINT": Dialect.AppendSmallint(sql, null); type = typeof(short); break;
            case "INTEGER": Dialect.AppendInteger(sql, null); type = typeof(int); break;
            case "BIGINT": Dialect.AppendBigint(sql, null); type = typeof(long); break;
            case "REAL": Dialect.AppendReal(sql, null); type = typeof(float); break;
            case "DOUBLE PRECISION": Dialect.AppendDouble(sql, null); type = typeof(double); break;
            default: Dialect.AppendNumeric(sql, null); type = typeof(decimal); break;
        }
        using H2Connection connection = new("jdbc:h2:mem:dialect_null_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql.ToString();
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(type, reader.GetFieldType(0));
        Assert.Equal(sqlType, reader.GetDataTypeName(0));
        Assert.Equal(DBNull.Value, reader.GetValue(0));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("-1")]
    [InlineData("2147483648")]
    [InlineData("-9223372036854775808")]
    [InlineData("9223372036854775808")]
    public void BigIntegerLiteralsKeepSqlNumericInsteadOfNarrowing(string text)
    {
        BigInteger value = BigInteger.Parse(text, CultureInfo.InvariantCulture);
        string literal = Dialect.AppendBigInteger(new StringBuilder(), value).ToString();
        using H2Connection connection = new("jdbc:h2:mem:dialect_big_integer_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT " + literal;
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("NUMERIC", reader.GetDataTypeName(0));
        // The established ADO.NET default mapping for SQL NUMERIC is decimal.
        Assert.Equal(typeof(decimal), reader.GetFieldType(0));
        Assert.IsType<decimal>(reader.GetValue(0));
        Assert.Equal((decimal)value, reader.GetDecimal(0));
    }

    private static string Render(object value)
    {
        StringBuilder builder = new();
        return (value switch
        {
            sbyte v => Dialect.AppendTinyint(builder, v),
            short v => Dialect.AppendSmallint(builder, v),
            int v => Dialect.AppendInteger(builder, v),
            long v => Dialect.AppendBigint(builder, v),
            float v => Dialect.AppendReal(builder, v),
            double v => Dialect.AppendDouble(builder, v),
            decimal v => Dialect.AppendNumeric(builder, v),
            _ => throw new ArgumentException("Unexpected numeric type"),
        }).ToString();
    }
}
