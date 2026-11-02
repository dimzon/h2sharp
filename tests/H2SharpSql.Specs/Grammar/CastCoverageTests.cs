// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/grammar.html — CAST truncates to the declared length, while assignment
// into a shorter column refuses with value-too-long.
using System;
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Grammar;

public sealed class CastCoverageTests
{
    /// <summary>docs/grammar.html — CAST to a shorter character type truncates</summary>
    [Fact]
    public void Cast_ToShorterCharType_Truncates()
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("abc", connection.QuerySingle<string>("SELECT CAST('abcdef' AS VARCHAR(3))"));
        Assert.Equal("abc", connection.QuerySingle<string>("SELECT CAST('abcdef' AS CHAR(3))"));
        Assert.Equal("ab ", connection.QuerySingle<string>("SELECT CAST('ab' AS CHAR(3))"));
    }

    /// <summary>docs/grammar.html — assignment into a shorter column is rejected</summary>
    [Fact]
    public void Insert_IntoShorterColumn_IsRejected()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(V VARCHAR(3), C CHAR(3))");

        H2Sharp.H2SharpException varchar = Assert.Throws<H2Sharp.H2SharpException>(
            () => connection.Execute("INSERT INTO TEST(V) VALUES ('abcdef')"));
        Assert.Equal(22001, varchar.H2ErrorCode);

        H2Sharp.H2SharpException ch = Assert.Throws<H2Sharp.H2SharpException>(
            () => connection.Execute("INSERT INTO TEST(C) VALUES ('abcdef')"));
        Assert.Equal(22001, ch.H2ErrorCode);

        _ = connection.Execute("INSERT INTO TEST(V, C) VALUES ('abc', 'ab')");
        Assert.Equal("abc", connection.QuerySingle<string>("SELECT V FROM TEST"));
        Assert.Equal("ab ", connection.QuerySingle<string>("SELECT C FROM TEST"));
    }

    /// <summary>docs/grammar.html — CAST to a smaller numeric type refuses overflow</summary>
    [Fact]
    public void Cast_OverflowingNumeric_IsRejected()
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.ThrowsAny<Exception>(() => connection.QuerySingle<sbyte>("SELECT CAST(1000 AS TINYINT)"));
        Assert.ThrowsAny<Exception>(() => connection.QuerySingle<short>("SELECT CAST(100000 AS SMALLINT)"));
        Assert.Equal((sbyte)100, connection.QuerySingle<sbyte>("SELECT CAST(100 AS TINYINT)"));
    }
}
