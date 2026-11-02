// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/grammar.html — LIKE optimizations: the '%' match-anything rewrite, the
// full-match equality rewrite, NULL handling, and the index prefix walk.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Grammar;

public sealed class LikeOptimizationTests
{
    private static IDbConnection GivenNames()
    {
        IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR(50))");
        _ = connection.Execute("CREATE INDEX TEST_NAME ON TEST(NAME)");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada')");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (2, 'Alan')");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (3, 'Bob')");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (4, NULL)");
        return connection;
    }

    /// <summary>docs/grammar.html:2569 — X LIKE '%' matches every non-null row</summary>
    [Fact]
    public void Like_PercentOnly_MatchesNonNullRows()
    {
        using IDbConnection connection = GivenNames();

        Assert.Equal(3, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE NAME LIKE '%'"));
        Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE NAME NOT LIKE '%'"));
    }

    /// <summary>docs/grammar.html:2569 — a full-match pattern answers like equality</summary>
    [Fact]
    public void Like_FullMatch_ActsAsEquality()
    {
        using IDbConnection connection = GivenNames();

        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE NAME LIKE 'Ada'"));
        Assert.Equal(2, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE NAME NOT LIKE 'Ada'"));
    }

    /// <summary>docs/grammar.html:2569 — a constant prefix walks the index</summary>
    [Fact]
    public void Like_PrefixPattern_WalksTheIndex()
    {
        using IDbConnection connection = GivenNames();

        Assert.Equal(
            new[] { "Ada", "Alan" },
            connection.Query<string>("SELECT NAME FROM TEST WHERE NAME LIKE 'A%' ORDER BY NAME").AsList());
        string plan = connection.QuerySingle<string>(
            "EXPLAIN PLAN FOR SELECT NAME FROM TEST WHERE NAME LIKE 'A%'");
        Assert.Contains("TEST_NAME", plan);
    }

    /// <summary>docs/grammar.html:2569 — NULL on either side answers NULL</summary>
    [Fact]
    public void Like_NullOnEitherSide_MatchesNothing()
    {
        using IDbConnection connection = GivenNames();

        Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE NAME LIKE NULL"));
        Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE NULL LIKE 'A%'"));
        Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE NAME LIKE 'A%' ESCAPE NULL"));
    }

    /// <summary>docs/grammar.html:2569 — a multi-character escape is rejected</summary>
    [Fact]
    public void Like_MultiCharacterEscape_IsRejected()
    {
        using IDbConnection connection = GivenNames();

        H2Sharp.H2SharpException e = Assert.Throws<H2Sharp.H2SharpException>(
            () => connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE NAME LIKE 'A%' ESCAPE 'ab'"));
        Assert.Equal(22025, e.H2ErrorCode);
    }

    /// <summary>docs/grammar.html:2587 — REGEXP looks for the pattern inside the value</summary>
    [Fact]
    public void Regexp_FindsThePatternInsideTheValue()
    {
        using IDbConnection connection = GivenNames();

        Assert.Equal(2, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE NAME REGEXP 'A.*'"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE NAME NOT REGEXP 'A.*'"));
    }
}
