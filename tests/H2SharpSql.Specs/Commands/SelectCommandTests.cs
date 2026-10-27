// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — SELECT clauses that SelectClauseTests does not cover. One test per
// checklist row, each naming its row in the marker comment. The two tests for FROM and
// WHERE live in tests/H2SharpSql.Specs/Dml/SelectClauseTests.cs, which is the example.
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class SelectCommandTests
{
    /// <summary>docs/commands.html:703 — SELECT [ DISTINCT [ ON ( expression [,...] ) ] | ALL ]</summary>
    [Fact]
    public void Select_Distinct_RemovesDuplicateRows() // CMD-0703
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(V INT)");
        _ = connection.Execute("INSERT INTO TEST(V) VALUES (10)");
        _ = connection.Execute("INSERT INTO TEST(V) VALUES (10)");
        _ = connection.Execute("INSERT INTO TEST(V) VALUES (20)");

        List<int> distinct = connection.Query<int>("SELECT DISTINCT V FROM TEST ORDER BY V").AsList();

        Assert.Equal(new[] { 10, 20 }, distinct);
    }

    /// <summary>docs/commands.html:704 — selectExpression [,...]</summary>
    [Fact]
    public void Select_ExpressionList_ReturnsEveryExpression() // CMD-0704
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        List<TestRow> rows = connection.Query<TestRow>("SELECT ID, NAME FROM TEST ORDER BY ID").AsList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0].Id);
        Assert.Equal("Ada", rows[0].Name);
        Assert.Equal(2, rows[1].Id);
        Assert.Equal("Alan", rows[1].Name);
    }

    /// <summary>docs/commands.html:707 — [ GROUP BY groupingElement [,...] ] [ HAVING expression ]</summary>
    [Fact]
    public void Select_GroupByAndHaving_KeepsOnlyGroupsThatMatch() // CMD-0707
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE CITY(NAME VARCHAR(20), COUNTRY VARCHAR(20))");
        _ = connection.Execute("INSERT INTO CITY(NAME, COUNTRY) VALUES ('Berlin', 'DE')");
        _ = connection.Execute("INSERT INTO CITY(NAME, COUNTRY) VALUES ('Munich', 'DE')");
        _ = connection.Execute("INSERT INTO CITY(NAME, COUNTRY) VALUES ('Vienna', 'AT')");

        List<string> countries = connection
            .Query<string>("SELECT COUNTRY FROM CITY GROUP BY COUNTRY HAVING COUNT(*) > 1")
            .AsList();

        Assert.Equal(new[] { "DE" }, countries);
    }

    /// <summary>docs/commands.html:708 — [ WINDOW { { windowName AS windowSpecification } [,...] } ]</summary>
    [Fact]
    public void Select_WindowClause_NamesAWindowThatOverCanReference() // CMD-0708
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        List<int> numbers = connection
            .Query<int>("SELECT ROW_NUMBER() OVER W AS RN FROM TEST WINDOW W AS (ORDER BY ID) ORDER BY ID")
            .AsList();

        Assert.Equal(new[] { 1, 2 }, numbers);
    }

    /// <summary>docs/commands.html:709 — [ QUALIFY expression ]</summary>
    [Fact]
    public void Select_Qualify_FiltersRowsAfterWindowFunctions() // CMD-0709
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        List<int> ids = connection
            .Query<int>("SELECT ID FROM TEST QUALIFY ROW_NUMBER() OVER (ORDER BY ID) > 1 ORDER BY ID")
            .AsList();

        Assert.Equal(new[] { 2 }, ids);
    }

    /// <summary>docs/commands.html:710 — [ { UNION [ ALL ] | EXCEPT | INTERSECT } query ]</summary>
    [Fact]
    public void Select_Union_CombinesTheResultsOfTwoQueries() // CMD-0710
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        List<int> ids = connection
            .Query<int>("SELECT ID FROM TEST WHERE NAME = 'Ada' UNION SELECT ID FROM TEST WHERE NAME = 'Alan' ORDER BY ID")
            .AsList();

        Assert.Equal(new[] { 1, 2 }, ids);
    }

    /// <summary>docs/commands.html:711 — [ ORDER BY selectOrder [,...] ]</summary>
    [Fact]
    public void Select_OrderBy_SortsRowsByTheGivenExpression() // CMD-0711
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        List<int> ids = connection.Query<int>("SELECT ID FROM TEST ORDER BY NAME DESC").AsList();

        Assert.Equal(new[] { 2, 1 }, ids);
    }

    /// <summary>docs/commands.html:712 — [ OFFSET expression { ROW | ROWS } ]</summary>
    [Fact]
    public void Select_Offset_SkipsTheGivenNumberOfRows() // CMD-0712
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        List<int> ids = connection.Query<int>("SELECT ID FROM TEST ORDER BY ID OFFSET 1 ROW").AsList();

        Assert.Equal(new[] { 2 }, ids);
    }

    /// <summary>docs/commands.html:713 — [ FETCH { FIRST | NEXT } [ expression [ PERCENT ] ] { ROW | ROWS }</summary>
    [Fact]
    public void Select_FetchFirst_LimitsTheNumberOfRows() // CMD-0713
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        List<int> ids = connection.Query<int>("SELECT ID FROM TEST ORDER BY ID FETCH FIRST 1 ROW ONLY").AsList();

        Assert.Equal(new[] { 1 }, ids);
    }

    /// <summary>docs/commands.html:714 — { ONLY | WITH TIES } ]</summary>
    [Fact]
    public void Select_FetchWithTies_KeepsRowsTiedAtTheBoundary() // CMD-0714
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE RESULT(ID INT, SCORE INT)");
        _ = connection.Execute("INSERT INTO RESULT(ID, SCORE) VALUES (1, 10)");
        _ = connection.Execute("INSERT INTO RESULT(ID, SCORE) VALUES (2, 10)");
        _ = connection.Execute("INSERT INTO RESULT(ID, SCORE) VALUES (3, 20)");

        List<int> noTies = connection
            .Query<int>("SELECT ID FROM RESULT ORDER BY SCORE, ID FETCH FIRST 1 ROW WITH TIES")
            .AsList();
        List<int> withTies = connection
            .Query<int>("SELECT ID FROM RESULT ORDER BY SCORE FETCH FIRST 1 ROW WITH TIES")
            .AsList();

        Assert.Equal(new[] { 1 }, noTies);
        Assert.Equal(new[] { 1, 2 }, withTies);
    }

    /// <summary>docs/commands.html:715 — [ FOR UPDATE [ NOWAIT | WAIT secondsNumeric | SKIP LOCKED ] ]</summary>
    [Fact]
    public void Select_ForUpdate_LocksTheSelectedRows() // CMD-0715
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        List<int> ids = connection.Query<int>("SELECT ID FROM TEST ORDER BY ID FOR UPDATE").AsList();

        Assert.Equal(new[] { 1, 2 }, ids);
    }

    private static void GivenTestTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada')");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (2, 'Alan')");
    }

    private sealed class TestRow
    {
        public int Id { get; set; }

        public string? Name { get; set; }
    }
}
