// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — DELETE command, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class DeleteCommandTests
{
    /// <summary>docs/commands.html:792 — DELETE FROM [schemaName.]tableName</summary>
    [Fact]
    public void Delete_WithoutWhere_EmptiesTheTable() // CMD-0792
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("DELETE FROM TEST");

        Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST"));
    }

    /// <summary>docs/commands.html:793 — [ WHERE expression ]</summary>
    [Fact]
    public void Delete_Where_RemovesOnlyTheMatchingRows() // CMD-0793
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("DELETE FROM TEST WHERE ID = 1");

        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST"));
        Assert.Equal("Alan", connection.QuerySingle<string>("SELECT NAME FROM TEST"));
    }

    /// <summary>docs/commands.html:794 — FETCH { FIRST | NEXT } [ expression ] { ROW | ROWS } ONLY</summary>
    [Fact]
    public void Delete_FetchFirst_RemovesOnlyTheFirstRow() // CMD-0794
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("DELETE FROM TEST ORDER BY ID FETCH FIRST 1 ROW ONLY");

        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST"));
        Assert.Equal("Alan", connection.QuerySingle<string>("SELECT NAME FROM TEST"));
    }

    private static void GivenTestTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada')");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (2, 'Alan')");
    }
}
