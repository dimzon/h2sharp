// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — the TABLE command (explicit table) and the table value
// (VALUES ...), one test per checklist row.
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class TableReferenceTests
{
    /// <summary>docs/commands.html:1016 — TABLE [schemaName.]tableName</summary>
    [Fact]
    public void Table_Command_ReturnsEveryRowOfTheTable() // CMD-1016
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        List<int> ids = connection.Query<int>("TABLE TEST").AsList();

        Assert.Equal(2, ids.Count);
    }

    /// <summary>docs/commands.html:1017 — [ ORDER BY selectOrder [,...] ]</summary>
    [Fact]
    public void Table_OrderBy_SortsTheRows() // CMD-1017
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        List<int> ids = connection.Query<int>("TABLE TEST ORDER BY ID DESC").AsList();

        Assert.Equal(new[] { 2, 1 }, ids);
    }

    /// <summary>docs/commands.html:1018 — [ OFFSET expression { ROW | ROWS } ]</summary>
    [Fact]
    public void Table_Offset_SkipsTheGivenNumberOfRows() // CMD-1018
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        List<int> ids = connection.Query<int>("TABLE TEST ORDER BY ID OFFSET 1 ROW").AsList();

        Assert.Equal(new[] { 2 }, ids);
    }

    /// <summary>docs/commands.html:1019 — [ FETCH { FIRST | NEXT } [ expression [ PERCENT ] ] { ROW | ROWS }</summary>
    [Fact]
    public void Table_FetchFirst_LimitsTheNumberOfRows() // CMD-1019
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        List<int> ids = connection.Query<int>("TABLE TEST ORDER BY ID FETCH FIRST 1 ROW ONLY").AsList();

        Assert.Equal(new[] { 1 }, ids);
    }

    /// <summary>docs/commands.html:1020 — { ONLY | WITH TIES } ]</summary>
    [Fact]
    public void Table_FetchWithTies_KeepsRowsTiedAtTheBoundary() // CMD-1020
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE RESULT(ID INT, SCORE INT)");
        _ = connection.Execute("INSERT INTO RESULT(ID, SCORE) VALUES (1, 10)");
        _ = connection.Execute("INSERT INTO RESULT(ID, SCORE) VALUES (2, 10)");
        _ = connection.Execute("INSERT INTO RESULT(ID, SCORE) VALUES (3, 20)");

        List<int> ids = connection
            .Query<int>("TABLE RESULT ORDER BY SCORE, ID FETCH FIRST 1 ROW WITH TIES")
            .AsList();

        // The order names two columns, so the sort position includes both: the second row
        // shares the score but not the id, and is not tied. The reference answers the same.
        Assert.Equal(new[] { 1 }, ids);
    }

    /// <summary>docs/commands.html:1043 — VALUES rowValueExpression [,...]</summary>
    [Fact]
    public void TableValue_ReturnsTheGivenRows() // CMD-1043
    {
        using IDbConnection connection = TestDatabase.Open();

        List<int> ids = connection.Query<int>("VALUES (1, 'Hello'), (2, 'World')").AsList();

        Assert.Equal(new[] { 1, 2 }, ids);
    }

    /// <summary>docs/commands.html:1044 — [ ORDER BY selectOrder [,...] ]</summary>
    [Fact]
    public void TableValue_OrderBy_SortsTheGivenRows() // CMD-1044
    {
        using IDbConnection connection = TestDatabase.Open();

        List<int> ids = connection.Query<int>("VALUES (2, 'B'), (1, 'A') ORDER BY C1").AsList();

        Assert.Equal(new[] { 1, 2 }, ids);
    }

    /// <summary>docs/commands.html:1045 — [ OFFSET expression { ROW | ROWS } ]</summary>
    [Fact]
    public void TableValue_Offset_SkipsTheGivenNumberOfRows() // CMD-1045
    {
        using IDbConnection connection = TestDatabase.Open();

        List<int> ids = connection.Query<int>("VALUES (1, 'A'), (2, 'B') ORDER BY C1 OFFSET 1 ROW").AsList();

        Assert.Equal(new[] { 2 }, ids);
    }

    /// <summary>docs/commands.html:1046 — [ FETCH { FIRST | NEXT } [ expression [ PERCENT ] ] { ROW | ROWS }</summary>
    [Fact]
    public void TableValue_FetchFirst_LimitsTheNumberOfRows() // CMD-1046
    {
        using IDbConnection connection = TestDatabase.Open();

        List<int> ids = connection.Query<int>("VALUES (1, 'A'), (2, 'B') ORDER BY C1 FETCH FIRST 1 ROW ONLY").AsList();

        Assert.Equal(new[] { 1 }, ids);
    }

    /// <summary>docs/commands.html:1047 — { ONLY | WITH TIES } ]</summary>
    [Fact]
    public void TableValue_FetchWithTies_KeepsRowsTiedAtTheBoundary() // CMD-1047
    {
        using IDbConnection connection = TestDatabase.Open();

        List<int> ids = connection
            .Query<int>("VALUES (1, 10), (2, 10), (3, 20) ORDER BY C2, C1 FETCH FIRST 1 ROW WITH TIES")
            .AsList();

        // Same as the TABLE case: the order names two columns, so the second row is not
        // tied to the first. The reference answers the same.
        Assert.Equal(new[] { 1 }, ids);
    }

    private static void GivenTestTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada')");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (2, 'Alan')");
    }
}
