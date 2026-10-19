// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/functions.html — bit functions, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Functions;

public sealed class BitFunctionTests
{
    /// <summary>docs/functions.html:1167 — BITAND(expression, expression)</summary>
    [Fact]
    public void BitAnd_CombinesTheCommonBits() // FUN-1167
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>("SELECT BITAND(3, 5)"));
    }

    /// <summary>docs/functions.html:1185 — BITOR(expression, expression)</summary>
    [Fact]
    public void BitOr_CombinesAllSetBits() // FUN-1185
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(7, connection.QuerySingle<int>("SELECT BITOR(3, 5)"));
    }

    /// <summary>docs/functions.html:1203 — BITXOR(expression, expression)</summary>
    [Fact]
    public void BitXor_KeepsTheDifferentBits() // FUN-1203
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(6, connection.QuerySingle<int>("SELECT BITXOR(3, 5)"));
    }

    /// <summary>docs/functions.html:1221 — BITNOT(expression)</summary>
    [Fact]
    public void BitNot_InvertsTheValue() // FUN-1221
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(-4, connection.QuerySingle<int>("SELECT BITNOT(3)"));
    }

    /// <summary>docs/functions.html:1239 — BITNAND(expression, expression)</summary>
    [Fact]
    public void BitNand_NegatesTheAnd() // FUN-1239
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(-2, connection.QuerySingle<int>("SELECT BITNAND(3, 5)"));
    }

    /// <summary>docs/functions.html:1257 — BITNOR(expression, expression)</summary>
    [Fact]
    public void BitNor_NegatesTheOr() // FUN-1257
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(-8, connection.QuerySingle<int>("SELECT BITNOR(3, 5)"));
    }

    /// <summary>docs/functions.html:1275 — BITXNOR(expression, expression)</summary>
    [Fact]
    public void BitXnor_NegatesTheXor() // FUN-1275
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(-7, connection.QuerySingle<int>("SELECT BITXNOR(3, 5)"));
    }

    /// <summary>docs/functions.html:1293 — BITGET(expression, long)</summary>
    [Fact]
    public void BitGet_ReadsTheGivenBit() // FUN-1293
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT BITGET(3, 0) = BITGET(1, 0)"));
        Assert.False(connection.QuerySingle<bool>("SELECT BITGET(2, 0) = BITGET(1, 0)"));
    }

    /// <summary>docs/functions.html:1311 — BITCOUNT(expression)</summary>
    [Fact]
    public void BitCount_CountsTheSetBits() // FUN-1311
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3, connection.QuerySingle<int>("SELECT BITCOUNT(7)"));
    }

    /// <summary>docs/functions.html:1329 — LSHIFT(expression, long)</summary>
    [Fact]
    public void LShift_ShiftsLeft() // FUN-1329
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(4, connection.QuerySingle<int>("SELECT LSHIFT(1, 2)"));
    }

    /// <summary>docs/functions.html:1347 — RSHIFT(expression, long)</summary>
    [Fact]
    public void RShift_ShiftsRight() // FUN-1347
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>("SELECT RSHIFT(4, 2)"));
    }

    /// <summary>docs/functions.html:1365 — ULSHIFT(expression, long)</summary>
    [Fact]
    public void UlShift_ShiftsLeftWithoutSign() // FUN-1365
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(4, connection.QuerySingle<int>("SELECT ULSHIFT(1, 2)"));
    }

    /// <summary>docs/functions.html:1383 — URSHIFT(expression, long)</summary>
    [Fact]
    public void UrShift_ShiftsRightWithoutSign() // FUN-1383
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>("SELECT URSHIFT(4, 2)"));
    }

    /// <summary>docs/functions.html:1401 — ROTATELEFT(expression, long)</summary>
    [Fact]
    public void RotateLeft_MovesTheHighestBitAround() // FUN-1401
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>("SELECT ROTATELEFT(1, 1)"));
    }

    /// <summary>docs/functions.html:1419 — ROTATERIGHT(expression, long)</summary>
    [Fact]
    public void RotateRight_MovesTheLowestBitAround() // FUN-1419
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>("SELECT ROTATERIGHT(2, 1)"));
    }
}
