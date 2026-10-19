// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/grammar.html — window specifications, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Grammar;

public sealed class WindowGrammarTests
{
    /// <summary>docs/grammar.html:3533 — windowName | windowSpecification</summary>
    [Fact]
    public void WindowNameOrSpecification_BothForms_AreAccepted() // GRM-3533
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(6, connection.QuerySingle<int>(
            "SELECT MAX(S) FROM (SELECT SUM(ID) OVER W AS S FROM TEST WINDOW W AS (ORDER BY ID))"));
        Assert.Equal(6, connection.QuerySingle<int>(
            "SELECT MAX(S) FROM (SELECT SUM(ID) OVER (ORDER BY ID) AS S FROM TEST)"));
    }

    /// <summary>docs/grammar.html:3551 — ([existingWindowName]</summary>
    [Fact]
    public void WindowSpecification_ExistingWindowName_IsInherited() // GRM-3551
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(3, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM (SELECT SUM(ID) OVER W2 AS S FROM TEST WINDOW W1 AS (PARTITION BY NAME), W2 AS (W1 ORDER BY ID))"));
    }

    /// <summary>docs/grammar.html:3552 — [PARTITION BY expression [,...]] [ORDER BY sortSpecificationList]</summary>
    [Fact]
    public void WindowSpecification_PartitionAndOrder_AreAccepted() // GRM-3552
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(3, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM (SELECT SUM(ID) OVER (PARTITION BY NAME ORDER BY ID) AS S FROM TEST)"));
    }

    /// <summary>docs/grammar.html:3553 — [windowFrame])</summary>
    [Fact]
    public void WindowSpecification_Frame_IsAccepted() // GRM-3553
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(6, connection.QuerySingle<int>(
            "SELECT MAX(S) FROM (SELECT SUM(ID) OVER (ORDER BY ID ROWS UNBOUNDED PRECEDING) AS S FROM TEST)"));
    }

    /// <summary>docs/grammar.html:3573 — ROWS|RANGE|GROUP</summary>
    [Fact]
    public void WindowFrame_RowsAndRange_AreAccepted() // GRM-3573
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(6, connection.QuerySingle<int>(
            "SELECT MAX(S) FROM (SELECT SUM(ID) OVER (ORDER BY ID ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS S FROM TEST)"));
        Assert.Equal(6, connection.QuerySingle<int>(
            "SELECT MAX(S) FROM (SELECT SUM(ID) OVER (ORDER BY ID RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS S FROM TEST)"));
    }

    /// <summary>docs/grammar.html:3574 — {windowFramePreceding|BETWEEN windowFrameBound AND windowFrameBound}</summary>
    [Fact]
    public void WindowFrame_BetweenTwoBounds_IsAccepted() // GRM-3574
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(6, connection.QuerySingle<int>(
            "SELECT MAX(S) FROM (SELECT SUM(ID) OVER (ORDER BY ID ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING) AS S FROM TEST)"));
    }

    /// <summary>docs/grammar.html:3575 — [EXCLUDE {CURRENT ROW|GROUP|TIES|NO OTHERS}]</summary>
    [Fact]
    public void WindowFrame_ExcludeCurrentRow_IsHonoured() // GRM-3575
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(5, connection.QuerySingle<int>(
            "SELECT S FROM (SELECT ID, SUM(ID) OVER (ORDER BY ID ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING EXCLUDE CURRENT ROW) AS S FROM TEST) WHERE ID = 1"));
    }

    /// <summary>docs/grammar.html:3595 — UNBOUNDED PRECEDING|value PRECEDING|CURRENT ROW</summary>
    [Fact]
    public void WindowFramePreceding_AllForms_AreAccepted() // GRM-3595
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(6, connection.QuerySingle<int>(
            "SELECT MAX(S) FROM (SELECT SUM(ID) OVER (ORDER BY ID ROWS UNBOUNDED PRECEDING) AS S FROM TEST)"));
        Assert.Equal(5, connection.QuerySingle<int>(
            "SELECT S FROM (SELECT ID, SUM(ID) OVER (ORDER BY ID ROWS 1 PRECEDING) AS S FROM TEST) WHERE ID = 3"));
        Assert.Equal(2, connection.QuerySingle<int>(
            "SELECT S FROM (SELECT ID, SUM(ID) OVER (ORDER BY ID ROWS CURRENT ROW) AS S FROM TEST) WHERE ID = 2"));
    }

    /// <summary>docs/grammar.html:3613 — UNBOUNDED PRECEDING|value PRECEDING|CURRENT ROW</summary>
    [Fact]
    public void WindowFrameBound_EndingAtCurrentRow_IsAccepted() // GRM-3613
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(6, connection.QuerySingle<int>(
            "SELECT MAX(S) FROM (SELECT SUM(ID) OVER (ORDER BY ID RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS S FROM TEST)"));
    }

    /// <summary>docs/grammar.html:3614 — |value FOLLOWING|UNBOUNDED FOLLOWING</summary>
    [Fact]
    public void WindowFrameBound_Following_IsAccepted() // GRM-3614
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(6, connection.QuerySingle<int>(
            "SELECT S FROM (SELECT ID, SUM(ID) OVER (ORDER BY ID RANGE BETWEEN CURRENT ROW AND UNBOUNDED FOLLOWING) AS S FROM TEST) WHERE ID = 1"));
    }

    private static void GivenTestTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada')");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (2, 'Alan')");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (3, 'Grace')");
    }
}
