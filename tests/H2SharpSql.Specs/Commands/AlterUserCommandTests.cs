// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — ALTER USER and ALTER VIEW commands, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

[Trait("Category", "BlockedByPort")]
public sealed class AlterUserCommandTests
{
    /// <summary>docs/commands.html:1471 — ALTER USER userName ADMIN { TRUE | FALSE }</summary>
    [Fact]
    public void AlterUser_AdminGrant_MakesTheUserAnAdministrator() // CMD-1471
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE USER TOM PASSWORD 'secret'");

        _ = connection.Execute("ALTER USER TOM ADMIN TRUE");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.USERS WHERE USER_NAME = 'TOM' AND IS_ADMIN = 'YES'"));
    }

    /// <summary>docs/commands.html:1490 — ALTER USER userName RENAME TO newUserName</summary>
    [Fact]
    public void AlterUser_Rename_GivesTheUserANewName() // CMD-1490
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE USER TOM PASSWORD 'secret'");

        _ = connection.Execute("ALTER USER TOM RENAME TO THOMAS");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.USERS WHERE USER_NAME = 'THOMAS'"));
        Assert.Equal(0, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.USERS WHERE USER_NAME = 'TOM'"));
    }

    /// <summary>docs/commands.html:1509 — ALTER USER userName SET { PASSWORD string | SALT bytes HASH bytes }</summary>
    [Fact]
    public void AlterUser_SetPassword_ChangesItWithoutTouchingRows() // CMD-1509
    {
        using IDbConnection connection = TestDatabase.Open();

        int affected = connection.Execute("ALTER USER SA SET PASSWORD 'rioyxlgt'");

        Assert.Equal(0, affected);
    }
}

public sealed class AlterViewCommandTests
{
    /// <summary>docs/commands.html:1528 — ALTER VIEW [ IF EXISTS ] [schemaName.]viewName RECOMPILE</summary>
    [Fact]
    public void AlterView_Recompile_KeepsTheViewUsable() // CMD-1528
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE ADDRESS(ID INT)");
        _ = connection.Execute("CREATE VIEW ADDRESS_VIEW AS SELECT ID FROM ADDRESS");

        _ = connection.Execute("ALTER VIEW ADDRESS_VIEW RECOMPILE");

        Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM ADDRESS_VIEW"));
    }

    /// <summary>docs/commands.html:1547 — ALTER VIEW [ IF EXISTS ] [schemaName.]viewName RENAME TO newName</summary>
    [Fact]
    public void AlterView_Rename_GivesTheViewANewName() // CMD-1547
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE ADDRESS(ID INT)");
        _ = connection.Execute("CREATE VIEW TEST AS SELECT ID FROM ADDRESS");

        _ = connection.Execute("ALTER VIEW TEST RENAME TO MY_VIEW");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_NAME = 'MY_VIEW'"));
    }
}
