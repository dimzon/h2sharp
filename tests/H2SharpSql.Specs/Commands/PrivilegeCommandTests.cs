// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — GRANT and REVOKE statements, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class PrivilegeCommandTests
{
    /// <summary>docs/commands.html:2349 — GRANT { { SELECT | INSERT | UPDATE | DELETE } [,..] | ALL [ PRIVILEGES ] } ON</summary>
    [Fact]
    public void GrantRight_SelectOnTable_GivesTheRight() // CMD-2349
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenGuardedTable(connection);

        _ = connection.Execute("GRANT SELECT ON TEST TO READONLY");

        Assert.Equal(1, RightCount(connection, "READONLY"));
    }

    /// <summary>docs/commands.html:2350 — { { SCHEMA schemaName } | { [ TABLE ] [schemaName.]tableName [,...] } }</summary>
    [Fact]
    public void GrantRight_OnSchema_GivesTheRight() // CMD-2350
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenGuardedTable(connection);

        _ = connection.Execute("GRANT SELECT ON SCHEMA PUBLIC TO READONLY");

        Assert.Equal(1, RightCount(connection, "READONLY"));
    }

    /// <summary>docs/commands.html:2351 — TO { PUBLIC | userName | roleName }</summary>
    [Fact]
    public void GrantRight_ToPublic_GivesTheRightToEveryone() // CMD-2351
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenGuardedTable(connection);

        _ = connection.Execute("GRANT SELECT ON TEST TO PUBLIC");

        Assert.Equal(1, RightCount(connection, "PUBLIC"));
    }

    /// <summary>docs/commands.html:2372 — GRANT ALTER ANY SCHEMA TO userName</summary>
    [Fact]
    public void GrantAlterAnySchema_GivesTheRight() // CMD-2372
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenGuardedTable(connection);

        _ = connection.Execute("GRANT ALTER ANY SCHEMA TO BOB");

        Assert.Equal(1, RightCount(connection, "BOB"));
    }

    /// <summary>docs/commands.html:2391 — GRANT { roleName [,...] } TO { PUBLIC | userName | roleName }</summary>
    [Fact]
    public void GrantRole_ToUser_GivesTheRole() // CMD-2391
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenGuardedTable(connection);

        _ = connection.Execute("GRANT READONLY TO TOM");

        Assert.Equal(1, RightCount(connection, "TOM"));
    }

    /// <summary>docs/commands.html:2448 — REVOKE { { SELECT | INSERT | UPDATE | DELETE } [,..] | ALL [ PRIVILEGES ] } ON</summary>
    [Fact]
    public void RevokeRight_SelectOnTable_RemovesTheRight() // CMD-2448
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenGuardedTable(connection);
        _ = connection.Execute("GRANT SELECT ON TEST TO READONLY");

        _ = connection.Execute("REVOKE SELECT ON TEST FROM READONLY");

        Assert.Equal(0, RightCount(connection, "READONLY"));
    }

    /// <summary>docs/commands.html:2449 — { { SCHEMA schemaName } | { [ TABLE ] [schemaName.]tableName [,...] } }</summary>
    [Fact]
    public void RevokeRight_OnSchema_RemovesTheRight() // CMD-2449
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenGuardedTable(connection);
        _ = connection.Execute("GRANT SELECT ON SCHEMA PUBLIC TO READONLY");

        _ = connection.Execute("REVOKE SELECT ON SCHEMA PUBLIC FROM READONLY");

        Assert.Equal(0, RightCount(connection, "READONLY"));
    }

    /// <summary>docs/commands.html:2450 — FROM { PUBLIC | userName | roleName }</summary>
    [Fact]
    public void RevokeRight_FromPublic_RemovesTheRight() // CMD-2450
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenGuardedTable(connection);
        _ = connection.Execute("GRANT SELECT ON TEST TO PUBLIC");

        _ = connection.Execute("REVOKE SELECT ON TEST FROM PUBLIC");

        Assert.Equal(0, RightCount(connection, "PUBLIC"));
    }

    /// <summary>docs/commands.html:2471 — REVOKE ALTER ANY SCHEMA FROM userName</summary>
    [Fact]
    public void RevokeAlterAnySchema_RemovesTheRight() // CMD-2471
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenGuardedTable(connection);
        _ = connection.Execute("GRANT ALTER ANY SCHEMA TO BOB");

        _ = connection.Execute("REVOKE ALTER ANY SCHEMA FROM BOB");

        Assert.Equal(0, RightCount(connection, "BOB"));
    }

    /// <summary>docs/commands.html:2490 — REVOKE { roleName [,...] } FROM { PUBLIC | userName | roleName }</summary>
    [Fact]
    public void RevokeRole_FromUser_RemovesTheRole() // CMD-2490
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenGuardedTable(connection);
        _ = connection.Execute("GRANT READONLY TO TOM");

        _ = connection.Execute("REVOKE READONLY FROM TOM");

        Assert.Equal(0, RightCount(connection, "TOM"));
    }

    private static int RightCount(IDbConnection connection, string grantee) =>
        connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.RIGHTS WHERE GRANTEE = @Grantee",
            new { Grantee = grantee });

    private static void GivenGuardedTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");
        _ = connection.Execute("CREATE ROLE READONLY");
        _ = connection.Execute("CREATE USER BOB PASSWORD 'secret'");
        _ = connection.Execute("CREATE USER TOM PASSWORD 'secret'");
    }
}
