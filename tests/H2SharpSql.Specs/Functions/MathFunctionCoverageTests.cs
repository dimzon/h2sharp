// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// Extra MathFunction cases: execute the type-specific and rounding branches on columns,
// where the optimizer cannot fold the call to a literal.
using System;
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Functions;

public sealed class MathFunctionCoverageTests
{
    private static IDbConnection GivenTable()
    {
        IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE T(I INT, B BIGINT, N NUMERIC(10, 2), R REAL, D DOUBLE, DF DECFLOAT)");
        _ = connection.Execute("INSERT INTO T VALUES (1234, 1234, 12.34, 1.5, 1.5, 1.5)");
        return connection;
    }

    [Fact]
    public void RoundCoversIntegerNumericRealDoubleAndDecfloat()
    {
        using IDbConnection connection = GivenTable();

        Assert.Equal(1230, connection.QuerySingle<int>("SELECT ROUND(I, -1) FROM T"));
        Assert.Equal(0L, connection.QuerySingle<long>("SELECT ROUND(B, -20) FROM T"));
        Assert.Equal(12.30m, connection.QuerySingle<decimal>("SELECT ROUND(N, 1) FROM T"));
        Assert.Equal(12.34m, connection.QuerySingle<decimal>("SELECT ROUND(N, 3) FROM T"));
        Assert.Equal(10.00m, connection.QuerySingle<decimal>("SELECT ROUND(N, -1) FROM T"));
        Assert.Equal(1.5f, connection.QuerySingle<float>("SELECT ROUND(R, 1) FROM T"));
        Assert.Equal(1.5, connection.QuerySingle<double>("SELECT ROUND(D, 1) FROM T"));
        Assert.Equal(1.5m, connection.QuerySingle<decimal>("SELECT ROUND(DF, 1) FROM T"));
    }

    [Fact]
    public void FloorCeilAndTruncUseTheDoubleRoundingPaths()
    {
        using IDbConnection connection = GivenTable();

        Assert.Equal(1.0, connection.QuerySingle<double>("SELECT FLOOR(R) FROM T"));
        Assert.Equal(2.0, connection.QuerySingle<double>("SELECT CEIL(R) FROM T"));
        Assert.Equal(1.0, connection.QuerySingle<double>("SELECT TRUNC(R) FROM T"));
        Assert.Equal(-2.0, connection.QuerySingle<double>("SELECT FLOOR(-D) FROM T"));
        Assert.Equal(-1.0, connection.QuerySingle<double>("SELECT CEIL(-D) FROM T"));
        Assert.Equal(-1.0, connection.QuerySingle<double>("SELECT TRUNC(-D) FROM T"));
    }

    [Fact]
    public void ModAbsAndSignWorkOnTypedColumns()
    {
        using IDbConnection connection = GivenTable();

        Assert.Equal(0m, connection.QuerySingle<decimal>("SELECT MOD(N, 4) FROM T"));
        Assert.Equal(2, connection.QuerySingle<int>("SELECT MOD(D, 4) FROM T"));
        Assert.Equal(1234, connection.QuerySingle<int>("SELECT ABS(-I) FROM T"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT SIGN(R) FROM T"));
        Assert.Null(connection.QuerySingle<decimal?>("SELECT MOD(N, NULL) FROM T"));
    }

    [Fact]
    public void RoundmagicCoversItsBoundaries()
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(0.0, connection.QuerySingle<double>("SELECT ROUNDMAGIC(1e-20)"));
        Assert.Equal(1e12, connection.QuerySingle<double>("SELECT ROUNDMAGIC(1e12)"));
        Assert.Equal(1e20, connection.QuerySingle<double>("SELECT ROUNDMAGIC(1e20)"));
        _ = connection.QuerySingle<double>("SELECT ROUNDMAGIC(1.2345678901234567)");
        _ = connection.QuerySingle<double>("SELECT ROUNDMAGIC(-1.9876543210987654)");
    }

    [Fact]
    public void NullScaleAndInvalidArgumentsAreHandled()
    {
        using IDbConnection connection = GivenTable();

        Assert.Null(connection.QuerySingle<decimal?>("SELECT ROUND(N, NULL) FROM T"));
        Assert.ThrowsAny<Exception>(() => connection.Execute("SELECT ROUND(N, 1000000000) FROM T"));
        Assert.ThrowsAny<Exception>(() =>
            connection.Execute("SELECT TRUNC(TIMESTAMP '2024-01-01 00:00:00', 1)"));
    }
}
