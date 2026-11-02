// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// Additional bit-function cases: binary operands, signed/unsigned shifts and fixed-width
// integer types.
using System;
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Functions;

public sealed class BitFunctionCoverageTests
{
    [Fact]
    public void BitGetAndCountSupportBinaryAndFixedWidthIntegers()
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT BITGET(X'01', 0)"));
        Assert.True(connection.QuerySingle<bool>("SELECT BITGET(X'80', 7)"));
        Assert.False(connection.QuerySingle<bool>("SELECT BITGET(X'', 0)"));
        Assert.Equal(4, connection.QuerySingle<int>("SELECT BITCOUNT(X'0F')"));
        Assert.Equal(13, connection.QuerySingle<int>("SELECT BITCOUNT(X'0102030405060708')"));
        Assert.Equal(8, connection.QuerySingle<int>("SELECT BITCOUNT(X'FF')"));

        Assert.False(connection.QuerySingle<bool>("SELECT BITGET(CAST(1 AS TINYINT), 7)"));
        Assert.True(connection.QuerySingle<bool>("SELECT BITGET(CAST(-32768 AS SMALLINT), 15)"));
        Assert.False(connection.QuerySingle<bool>("SELECT BITGET(CAST(1 AS BIGINT), 63)"));
        Assert.Equal(8, connection.QuerySingle<int>("SELECT BITCOUNT(CAST(-1 AS TINYINT))"));
        Assert.Equal(16, connection.QuerySingle<int>("SELECT BITCOUNT(CAST(-1 AS SMALLINT))"));
        Assert.Equal(64, connection.QuerySingle<int>("SELECT BITCOUNT(CAST(-1 AS BIGINT))"));
    }

    [Fact]
    public void ShiftsAndRotationsSupportBinaryOperands()
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("02", Hex(connection.QuerySingle<byte[]>("SELECT LSHIFT(X'01', 1)")));
        Assert.Equal("01", Hex(connection.QuerySingle<byte[]>("SELECT RSHIFT(X'02', 1)")));
        Assert.Equal("00", Hex(connection.QuerySingle<byte[]>("SELECT ULSHIFT(X'01', 8)")));
        Assert.Equal("01", Hex(connection.QuerySingle<byte[]>("SELECT URSHIFT(X'80', 7)")));
        Assert.Equal("02", Hex(connection.QuerySingle<byte[]>("SELECT ROTATELEFT(X'01', 1)")));
        Assert.Equal("01", Hex(connection.QuerySingle<byte[]>("SELECT ROTATERIGHT(X'02', 1)")));
    }

    [Fact]
    public void ShiftExtremesUseTheOperandWidth()
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(0L, connection.QuerySingle<long>("SELECT LSHIFT(CAST(1 AS BIGINT), 64)"));
        Assert.Equal(-1L, connection.QuerySingle<long>("SELECT RSHIFT(CAST(-1 AS BIGINT), 1)"));
        Assert.Equal(long.MaxValue, connection.QuerySingle<long>("SELECT URSHIFT(CAST(-1 AS BIGINT), 1)"));
        Assert.Equal(0, connection.QuerySingle<int>("SELECT LSHIFT(CAST(1 AS TINYINT), 8)"));
        Assert.Equal(-1, connection.QuerySingle<int>("SELECT RSHIFT(CAST(-128 AS TINYINT), 7)"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT URSHIFT(CAST(-128 AS TINYINT), 7)"));
    }

    private static string Hex(byte[] value) => Convert.ToHexString(value);
}
