// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — UPDATE command, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class UpdateCommandTests
{
    /// <summary>docs/commands.html:769 — UPDATE [schemaName.]tableName [ [ AS ] newTableAlias ] SET setClauseList</summary>
    [Fact]
    public void Update_TableAlias_CanQualifyTheSetExpressions() // CMD-0769
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("UPDATE TEST T SET NAME = UPPER(T.NAME) WHERE T.ID = 1");

        Assert.Equal("ADA", connection.QuerySingle<string>("SELECT NAME FROM TEST WHERE ID = 1"));
    }

    /// <summary>docs/commands.html:770 — [ WHERE expression ] [ ORDER BY sortSpecificationList ]</summary>
    [Fact]
    public void Update_WhereAndOrderBy_TouchOnlyTheMatchingRows() // CMD-0770
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("UPDATE TEST SET NAME = 'X' WHERE ID > 0 ORDER BY ID");

        Assert.Equal("X", connection.QuerySingle<string>("SELECT NAME FROM TEST WHERE ID = 1"));
        Assert.Equal("X", connection.QuerySingle<string>("SELECT NAME FROM TEST WHERE ID = 2"));
    }

    /// <summary>docs/commands.html:771 — FETCH { FIRST | NEXT } [ expression ] { ROW | ROWS } ONLY</summary>
    [Fact]
    public void Update_FetchFirst_UpdatesOnlyTheFirstRow() // CMD-0771
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("UPDATE TEST SET NAME = 'X' ORDER BY ID FETCH FIRST 1 ROW ONLY");

        Assert.Equal("X", connection.QuerySingle<string>("SELECT NAME FROM TEST WHERE ID = 1"));
        Assert.Equal("Alan", connection.QuerySingle<string>("SELECT NAME FROM TEST WHERE ID = 2"));
    }

    private static void GivenTestTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada')");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (2, 'Alan')");
    }
}
