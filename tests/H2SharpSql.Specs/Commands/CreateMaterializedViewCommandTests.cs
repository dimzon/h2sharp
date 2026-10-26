// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — CREATE MATERIALIZED VIEW and CREATE ROLE commands,
// one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class CreateMaterializedViewCommandTests
{
    /// <summary>docs/commands.html:1923 — CREATE [ OR REPLACE ]</summary>
    [Fact]
    public void CreateMaterializedView_OrReplace_ReplacesTheDefinition() // CMD-1923
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);
        _ = connection.Execute("CREATE MATERIALIZED VIEW TEST_VIEW AS SELECT ID FROM TEST WHERE ID < 100");

        _ = connection.Execute("CREATE OR REPLACE MATERIALIZED VIEW TEST_VIEW AS SELECT ID FROM TEST WHERE ID > 1");

        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST_VIEW"));
    }

    /// <summary>docs/commands.html:1924 — MATERIALIZED VIEW [ IF NOT EXISTS ] [schemaName.]viewName</summary>
    [Fact]
    public void CreateMaterializedView_IfNotExists_CreatesItOnce() // CMD-1924
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("CREATE MATERIALIZED VIEW IF NOT EXISTS TEST_VIEW AS SELECT ID FROM TEST");
        _ = connection.Execute("CREATE MATERIALIZED VIEW IF NOT EXISTS TEST_VIEW AS SELECT ID FROM TEST");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TEST_VIEW'"));
    }

    /// <summary>docs/commands.html:1925 — [ ( columnName [,...] ) ] AS query</summary>
    [Fact]
    public void CreateMaterializedView_ColumnList_NamesTheResultColumns() // CMD-1925
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("CREATE MATERIALIZED VIEW TEST_VIEW(ID, NAME) AS SELECT ID, NAME FROM TEST WHERE ID < 100");

        Assert.Equal("Ada", connection.QuerySingle<string>("SELECT NAME FROM TEST_VIEW WHERE ID = 1"));
    }

    private static void GivenTestTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada')");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (2, 'Alan')");
    }
}

[Trait("Category", "BlockedByPort")]
public sealed class CreateRoleCommandTests
{
    /// <summary>docs/commands.html:1756 — CREATE ROLE [ IF NOT EXISTS ] newRoleName</summary>
    [Fact]
    public void CreateRole_IfNotExists_RegistersItOnce() // CMD-1756
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE ROLE IF NOT EXISTS READONLY");
        _ = connection.Execute("CREATE ROLE IF NOT EXISTS READONLY");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.ROLES WHERE ROLE_NAME = 'READONLY'"));
    }
}
