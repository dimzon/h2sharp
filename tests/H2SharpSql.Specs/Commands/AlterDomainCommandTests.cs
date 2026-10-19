// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — ALTER DOMAIN command, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class AlterDomainCommandTests
{
    /// <summary>docs/commands.html:1093 — ALTER DOMAIN [ IF EXISTS ] [schemaName.]domainName</summary>
    [Fact]
    public void AlterDomain_IfExists_ChangesTheExistingDomain() // CMD-1093
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE DOMAIN D AS INT");
        _ = connection.Execute("CREATE TABLE T(V D)");

        _ = connection.Execute("ALTER DOMAIN IF EXISTS D SET DEFAULT 42");
        _ = connection.Execute("INSERT INTO T DEFAULT VALUES");

        Assert.Equal(42, connection.QuerySingle<int>("SELECT V FROM T"));
    }

    /// <summary>docs/commands.html:1094 — { SET DEFAULT expression }</summary>
    [Fact]
    public void AlterDomain_SetDefault_AppliesToNewRows() // CMD-1094
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE DOMAIN D AS INT");
        _ = connection.Execute("CREATE TABLE T(V D)");

        _ = connection.Execute("ALTER DOMAIN D SET DEFAULT 42");
        _ = connection.Execute("INSERT INTO T DEFAULT VALUES");

        Assert.Equal(42, connection.QuerySingle<int>("SELECT V FROM T"));
    }

    /// <summary>docs/commands.html:1095 — | { DROP DEFAULT }</summary>
    [Fact]
    public void AlterDomain_DropDefault_LeavesTheColumnNull() // CMD-1095
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE DOMAIN D AS INT DEFAULT 7");
        _ = connection.Execute("CREATE TABLE T(V D)");

        _ = connection.Execute("ALTER DOMAIN D DROP DEFAULT");
        _ = connection.Execute("INSERT INTO T DEFAULT VALUES");

        Assert.True(connection.QuerySingle<bool>("SELECT V IS NULL FROM T"));
    }

    /// <summary>docs/commands.html:1096 — | { SET ON UPDATE expression }</summary>
    [Fact]
    public void AlterDomain_SetOnUpdate_SetsTheValueWhenTheRowChanges() // CMD-1096
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE DOMAIN D AS INT");
        _ = connection.Execute("CREATE TABLE T(ID INT, V D)");
        _ = connection.Execute("INSERT INTO T(ID, V) VALUES (1, 1)");

        _ = connection.Execute("ALTER DOMAIN D SET ON UPDATE 7");
        _ = connection.Execute("UPDATE T SET ID = 2");

        Assert.Equal(7, connection.QuerySingle<int>("SELECT V FROM T"));
    }

    /// <summary>docs/commands.html:1097 — | { DROP ON UPDATE }</summary>
    [Fact]
    public void AlterDomain_DropOnUpdate_LeavesTheValueAlone() // CMD-1097
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE DOMAIN D AS INT");
        _ = connection.Execute("CREATE TABLE T(ID INT, V D)");
        _ = connection.Execute("INSERT INTO T(ID, V) VALUES (1, 1)");

        _ = connection.Execute("ALTER DOMAIN D SET ON UPDATE 7");
        _ = connection.Execute("ALTER DOMAIN D DROP ON UPDATE");
        _ = connection.Execute("UPDATE T SET ID = 2");

        Assert.Equal(1, connection.QuerySingle<int>("SELECT V FROM T"));
    }

    /// <summary>docs/commands.html:1120 — ALTER DOMAIN [ IF EXISTS ] [schemaName.]domainName</summary>
    [Fact]
    public void AlterDomainAddConstraint_AddsTheConstraint() // CMD-1120
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE DOMAIN D AS INT");

        _ = connection.Execute("ALTER DOMAIN D ADD CONSTRAINT D_POSITIVE CHECK (VALUE > 0)");

        Assert.Equal(1, CountConstraints(connection, "D_POSITIVE"));
    }

    /// <summary>docs/commands.html:1121 — ADD [ constraintNameDefinition ]</summary>
    [Fact]
    public void AlterDomainAddConstraint_WithoutName_GetsAGeneratedOne() // CMD-1121
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE DOMAIN D AS INT");

        _ = connection.Execute("ALTER DOMAIN D ADD CHECK (VALUE > 0)");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.DOMAIN_CONSTRAINTS WHERE DOMAIN_NAME = 'D'"));
    }

    /// <summary>docs/commands.html:1122 — CHECK (condition) [ CHECK | NOCHECK ]</summary>
    [Fact]
    public void AlterDomainAddConstraint_NoCheck_DoesNotEnforceIt() // CMD-1122
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE DOMAIN D AS INT");
        _ = connection.Execute("CREATE TABLE T(V D)");

        _ = connection.Execute("ALTER DOMAIN D ADD CONSTRAINT D_POSITIVE CHECK (VALUE > 0) NOCHECK");
        _ = connection.Execute("INSERT INTO T(V) VALUES (-1)");

        Assert.Equal(-1, connection.QuerySingle<int>("SELECT V FROM T"));
    }

    /// <summary>docs/commands.html:1143 — ALTER DOMAIN [ IF EXISTS ] [schemaName.]domainName</summary>
    [Fact]
    public void AlterDomainDropConstraint_RemovesTheConstraint() // CMD-1143
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE DOMAIN D AS INT");
        _ = connection.Execute("ALTER DOMAIN D ADD CONSTRAINT D_POSITIVE CHECK (VALUE > 0)");

        _ = connection.Execute("ALTER DOMAIN D DROP CONSTRAINT D_POSITIVE");

        Assert.Equal(0, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.DOMAIN_CONSTRAINTS WHERE DOMAIN_NAME = 'D'"));
    }

    /// <summary>docs/commands.html:1144 — DROP CONSTRAINT [ IF EXISTS ] [schemaName.]constraintName</summary>
    [Fact]
    public void AlterDomainDropConstraint_IfExists_RemovesTheConstraint() // CMD-1144
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE DOMAIN D AS INT");
        _ = connection.Execute("ALTER DOMAIN D ADD CONSTRAINT D_POSITIVE CHECK (VALUE > 0)");

        _ = connection.Execute("ALTER DOMAIN D DROP CONSTRAINT IF EXISTS D_POSITIVE");

        Assert.Equal(0, CountConstraints(connection, "D_POSITIVE"));
    }

    /// <summary>docs/commands.html:1164 — ALTER DOMAIN [ IF EXISTS ] [schemaName.]domainName RENAME TO newName</summary>
    [Fact]
    public void AlterDomainRename_GivesTheDomainANewName() // CMD-1164
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE DOMAIN D AS INT");

        _ = connection.Execute("ALTER DOMAIN D RENAME TO D2");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.DOMAINS WHERE DOMAIN_NAME = 'D2'"));
    }

    /// <summary>docs/commands.html:1183 — ALTER DOMAIN [ IF EXISTS ] [schemaName.]domainName</summary>
    [Fact]
    public void AlterDomainRenameConstraint_RenamesTheConstraint() // CMD-1183
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE DOMAIN D AS INT");
        _ = connection.Execute("ALTER DOMAIN D ADD CONSTRAINT FOO CHECK (VALUE > 0)");

        _ = connection.Execute("ALTER DOMAIN D RENAME CONSTRAINT FOO TO BAR");

        Assert.Equal(1, CountConstraints(connection, "BAR"));
    }

    /// <summary>docs/commands.html:1184 — RENAME CONSTRAINT [schemaName.]oldConstraintName</summary>
    [Fact]
    public void AlterDomainRenameConstraint_QualifiedOldName_IsAccepted() // CMD-1184
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE DOMAIN D AS INT");
        _ = connection.Execute("ALTER DOMAIN D ADD CONSTRAINT FOO CHECK (VALUE > 0)");

        _ = connection.Execute("ALTER DOMAIN D RENAME CONSTRAINT PUBLIC.FOO TO BAR");

        Assert.Equal(1, CountConstraints(connection, "BAR"));
    }

    /// <summary>docs/commands.html:1185 — TO newConstraintName</summary>
    [Fact]
    public void AlterDomainRenameConstraint_OldNameIsGone() // CMD-1185
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE DOMAIN D AS INT");
        _ = connection.Execute("ALTER DOMAIN D ADD CONSTRAINT FOO CHECK (VALUE > 0)");

        _ = connection.Execute("ALTER DOMAIN D RENAME CONSTRAINT FOO TO BAR");

        Assert.Equal(0, CountConstraints(connection, "FOO"));
    }

    private static int CountConstraints(IDbConnection connection, string name) =>
        connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.DOMAIN_CONSTRAINTS WHERE CONSTRAINT_NAME = @Name",
            new { Name = name });
}
