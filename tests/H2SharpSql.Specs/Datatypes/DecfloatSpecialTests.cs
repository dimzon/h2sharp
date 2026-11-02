// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/datatypes.html — DECFLOAT special values: Infinity, -Infinity and NaN,
// and what the arithmetic answers for them.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Datatypes;

public sealed class DecfloatSpecialTests
{
    private const string Inf = "CAST('Infinity' AS DECFLOAT)";
    private const string NegInf = "CAST('-Infinity' AS DECFLOAT)";
    private const string Nan = "CAST('NaN' AS DECFLOAT)";

    /// <summary>docs/datatypes.html — DECFLOAT special values round-trip through CAST</summary>
    [Fact]
    public void Decfloat_SpecialValues_RoundTrip()
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("Infinity", connection.QuerySingle<string>($"SELECT CAST({Inf} AS VARCHAR)"));
        Assert.Equal("-Infinity", connection.QuerySingle<string>($"SELECT CAST({NegInf} AS VARCHAR)"));
        Assert.Equal("NaN", connection.QuerySingle<string>($"SELECT CAST({Nan} AS VARCHAR)"));
    }

    /// <summary>docs/datatypes.html — addition and subtraction with infinities</summary>
    [Fact]
    public void Decfloat_Infinity_AddAndSubtract()
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("Infinity", connection.QuerySingle<string>($"SELECT CAST({Inf} + 1 AS VARCHAR)"));
        Assert.Equal("Infinity", connection.QuerySingle<string>($"SELECT CAST({Inf} + {Inf} AS VARCHAR)"));
        Assert.Equal("NaN", connection.QuerySingle<string>($"SELECT CAST({Inf} + {NegInf} AS VARCHAR)"));
        Assert.Equal("NaN", connection.QuerySingle<string>($"SELECT CAST({Inf} - {Inf} AS VARCHAR)"));
        Assert.Equal("-Infinity", connection.QuerySingle<string>($"SELECT CAST(1 - {Inf} AS VARCHAR)"));
        Assert.Equal("Infinity", connection.QuerySingle<string>($"SELECT CAST(1 - {NegInf} AS VARCHAR)"));
    }

    /// <summary>docs/datatypes.html — negation and multiplication with infinities</summary>
    [Fact]
    public void Decfloat_Infinity_NegateAndMultiply()
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("-Infinity", connection.QuerySingle<string>($"SELECT CAST(-({Inf}) AS VARCHAR)"));
        Assert.Equal("Infinity", connection.QuerySingle<string>($"SELECT CAST(-({NegInf}) AS VARCHAR)"));
        Assert.Equal("NaN", connection.QuerySingle<string>($"SELECT CAST(-({Nan}) AS VARCHAR)"));
        Assert.Equal("Infinity", connection.QuerySingle<string>($"SELECT CAST(2 * {Inf} AS VARCHAR)"));
        Assert.Equal("-Infinity", connection.QuerySingle<string>($"SELECT CAST(-2 * {Inf} AS VARCHAR)"));
        Assert.Equal("NaN", connection.QuerySingle<string>($"SELECT CAST(0 * {Inf} AS VARCHAR)"));
        Assert.Equal("Infinity", connection.QuerySingle<string>($"SELECT CAST({Inf} * {Inf} AS VARCHAR)"));
        Assert.Equal("-Infinity", connection.QuerySingle<string>($"SELECT CAST({Inf} * {NegInf} AS VARCHAR)"));
    }

    /// <summary>docs/datatypes.html — division with infinities and division by zero</summary>
    [Fact]
    public void Decfloat_Infinity_DivideAndModulus()
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("Infinity", connection.QuerySingle<string>($"SELECT CAST({Inf} / 2 AS VARCHAR)"));
        Assert.Equal("-Infinity", connection.QuerySingle<string>($"SELECT CAST({Inf} / -2 AS VARCHAR)"));
        Assert.Equal(0, connection.QuerySingle<int>($"SELECT 1 / {Inf}"));
        Assert.Equal("NaN", connection.QuerySingle<string>($"SELECT CAST({Inf} / {Inf} AS VARCHAR)"));
        Assert.Equal("NaN", connection.QuerySingle<string>($"SELECT CAST({Inf} / {Nan} AS VARCHAR)"));
        Assert.Equal(5, connection.QuerySingle<int>($"SELECT 5 % {Inf}"));

        H2Sharp.H2SharpException byZero = Assert.Throws<H2Sharp.H2SharpException>(
            () => connection.QuerySingle<int>("SELECT 1 / CAST(0 AS DECFLOAT)"));
        Assert.Equal(22012, byZero.H2ErrorCode);
    }

    /// <summary>docs/datatypes.html — infinities compare above and below every number</summary>
    [Fact]
    public void Decfloat_Infinity_Compares()
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>($"SELECT {Inf} > 1E+999"));
        Assert.True(connection.QuerySingle<bool>($"SELECT {NegInf} < -1E+999"));
        Assert.True(connection.QuerySingle<bool>($"SELECT {Inf} = {Inf}"));
        // The special values are singletons, so NaN is equal to itself here,
        // exactly as the reference's compareTypeSafe answers for `this == o`.
        Assert.True(connection.QuerySingle<bool>($"SELECT {Nan} = {Nan}"));
        Assert.True(connection.QuerySingle<bool>($"SELECT {Nan} <> {Inf}"));
    }
}
