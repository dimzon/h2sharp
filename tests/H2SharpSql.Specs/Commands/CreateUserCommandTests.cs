// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — CREATE USER and CREATE VIEW commands, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

[Trait("Category", "BlockedByPort")]
public sealed class CreateUserCommandTests
{
    /// <summary>docs/commands.html:1879 — CREATE USER [ IF NOT EXISTS ] newUserName</summary>
    [Fact]
    public void CreateUser_IfNotExists_RegistersItOnce() // CMD-1879
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE USER IF NOT EXISTS GUEST PASSWORD 'abc'");
        _ = connection.Execute("CREATE USER IF NOT EXISTS GUEST PASSWORD 'abc'");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.USERS WHERE USER_NAME = 'GUEST'"));
    }

    /// <summary>docs/commands.html:1880 — { PASSWORD string | SALT bytes HASH bytes } [ ADMIN ]</summary>
    [Fact]
    public void CreateUser_Admin_MarksTheUserAsAdministrator() // CMD-1880
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE USER ADMIN2 PASSWORD 'abc' ADMIN");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.USERS WHERE USER_NAME = 'ADMIN2' AND IS_ADMIN = 'YES'"));
    }
}

public sealed class CreateViewCommandTests
{
    /// <summary>docs/commands.html:1900 — CREATE [ OR REPLACE ] [ FORCE ]</summary>
    [Fact]
    public void CreateView_OrReplace_ReplacesTheDefinition() // CMD-1900
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);
        _ = connection.Execute("CREATE VIEW TEST_VIEW AS SELECT ID FROM TEST WHERE ID < 100");

        _ = connection.Execute("CREATE OR REPLACE VIEW TEST_VIEW AS SELECT ID FROM TEST WHERE ID > 1");

        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST_VIEW"));
    }

    /// <summary>docs/commands.html:1901 — VIEW [ IF NOT EXISTS ] [schemaName.]viewName</summary>
    [Fact]
    public void CreateView_IfNotExists_CreatesItOnce() // CMD-1901
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("CREATE VIEW IF NOT EXISTS TEST_VIEW AS SELECT ID FROM TEST");
        _ = connection.Execute("CREATE VIEW IF NOT EXISTS TEST_VIEW AS SELECT ID FROM TEST");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_NAME = 'TEST_VIEW'"));
    }

    /// <summary>docs/commands.html:1902 — [ ( columnName [,...] ) ] AS query</summary>
    [Fact]
    public void CreateView_ColumnList_NamesTheResultColumns() // CMD-1902
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("CREATE VIEW TEST_VIEW(ID, NAME) AS SELECT ID, NAME FROM TEST WHERE ID < 100");

        Assert.Equal("Ada", connection.QuerySingle<string>("SELECT NAME FROM TEST_VIEW WHERE ID = 1"));
    }

    private static void GivenTestTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada')");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (2, 'Alan')");
    }
}
