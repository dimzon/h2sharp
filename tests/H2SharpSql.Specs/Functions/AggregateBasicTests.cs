// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/functions-aggregate.html — basic aggregates, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Functions;

public sealed class AggregateBasicTests
{
    private static void GivenNumbers(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");
        _ = connection.Execute("INSERT INTO TEST(ID) VALUES (1), (2), (3)");
    }

    /// <summary>docs/functions-aggregate.html:407 — AVG ( [ DISTINCT|ALL ] { numeric | interval } )</summary>
    [Fact]
    public void Avg_AveragesTheNumbers() // AGG-0407
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(2.0, connection.QuerySingle<double>("SELECT AVG(ID) FROM TEST"), 10);
        Assert.Equal(2.0, connection.QuerySingle<double>("SELECT AVG(DISTINCT ID) FROM TEST"), 10);
    }

    /// <summary>docs/functions-aggregate.html:408 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void Avg_WithFilterAndOver() // AGG-0408
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(2.5, connection.QuerySingle<double>("SELECT AVG(ID) FILTER (WHERE ID > 1) FROM TEST"), 10);
        Assert.Equal(2.0, connection.QuerySingle<double>("SELECT DISTINCT AVG(ID) OVER () FROM TEST"), 10);
    }

    /// <summary>docs/functions-aggregate.html:427 — MAX(value)</summary>
    [Fact]
    public void Max_ReturnsTheLargestValue() // AGG-0427
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(3, connection.QuerySingle<int>("SELECT MAX(ID) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:428 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void Max_WithFilterAndOver() // AGG-0428
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(2, connection.QuerySingle<int>("SELECT MAX(ID) FILTER (WHERE ID < 3) FROM TEST"));
        Assert.Equal(3, connection.QuerySingle<int>("SELECT DISTINCT MAX(ID) OVER () FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:447 — MIN(value)</summary>
    [Fact]
    public void Min_ReturnsTheSmallestValue() // AGG-0447
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(1, connection.QuerySingle<int>("SELECT MIN(ID) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:448 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void Min_WithFilterAndOver() // AGG-0448
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(2, connection.QuerySingle<int>("SELECT MIN(ID) FILTER (WHERE ID > 1) FROM TEST"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT DISTINCT MIN(ID) OVER () FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:467 — SUM( [ DISTINCT|ALL ] { numeric | interval | { boolean } } )</summary>
    [Fact]
    public void Sum_AddsTheNumbers() // AGG-0467
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(6, connection.QuerySingle<int>("SELECT SUM(ID) FROM TEST"));
        Assert.Equal(6, connection.QuerySingle<int>("SELECT SUM(DISTINCT ID) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:468 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void Sum_WithFilterAndOver() // AGG-0468
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(5, connection.QuerySingle<int>("SELECT SUM(ID) FILTER (WHERE ID > 1) FROM TEST"));
        Assert.Equal(6, connection.QuerySingle<int>("SELECT DISTINCT SUM(ID) OVER () FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:487 — {EVERY| {BOOL_AND}}(boolean)</summary>
    [Fact]
    public void Every_ChecksAllTheValues() // AGG-0487
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.True(connection.QuerySingle<bool>("SELECT EVERY(ID > 0) FROM TEST"));
        Assert.True(connection.QuerySingle<bool>("SELECT BOOL_AND(ID > 0) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:488 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void Every_WithFilterAndOver() // AGG-0488
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.True(connection.QuerySingle<bool>("SELECT EVERY(ID > 1) FILTER (WHERE ID > 1) FROM TEST"));
        Assert.True(connection.QuerySingle<bool>("SELECT DISTINCT EVERY(ID > 0) OVER () FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:507 — {ANY|SOME| {BOOL_OR}}(boolean)</summary>
    [Fact]
    public void Any_ChecksAnyValue() // AGG-0507
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.True(connection.QuerySingle<bool>("SELECT ANY(ID > 2) FROM TEST"));
        Assert.True(connection.QuerySingle<bool>("SELECT SOME(ID > 2) FROM TEST"));
        Assert.True(connection.QuerySingle<bool>("SELECT BOOL_OR(ID > 2) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:508 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void Any_WithFilterAndOver() // AGG-0508
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.False(connection.QuerySingle<bool>("SELECT ANY(ID > 2) FILTER (WHERE ID < 3) FROM TEST"));
        Assert.True(connection.QuerySingle<bool>("SELECT DISTINCT ANY(ID > 2) OVER () FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:527 — COUNT( { * | { [ DISTINCT|ALL ] expression } } )</summary>
    [Fact]
    public void Count_CountsTheRows() // AGG-0527
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(3L, connection.QuerySingle<long>("SELECT COUNT(*) FROM TEST"));
        Assert.Equal(3L, connection.QuerySingle<long>("SELECT COUNT(DISTINCT ID) FROM TEST"));
        Assert.Equal(3L, connection.QuerySingle<long>("SELECT COUNT(ALL ID) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:528 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void Count_WithFilterAndOver() // AGG-0528
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(2L, connection.QuerySingle<long>("SELECT COUNT(*) FILTER (WHERE ID > 1) FROM TEST"));
        Assert.Equal(3L, connection.QuerySingle<long>("SELECT DISTINCT COUNT(*) OVER () FROM TEST"));
    }
}
