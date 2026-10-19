// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — CREATE DOMAIN command, one test per checklist row.
using System;
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class CreateDomainCommandTests
{
    /// <summary>docs/commands.html:1675 — CREATE DOMAIN [ IF NOT EXISTS ] [schemaName.]domainName</summary>
    [Fact]
    public void CreateDomain_IfNotExists_RegistersItOnce() // CMD-1675
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE DOMAIN IF NOT EXISTS EMAIL AS VARCHAR(255)");
        _ = connection.Execute("CREATE DOMAIN IF NOT EXISTS EMAIL AS VARCHAR(255)");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.DOMAINS WHERE DOMAIN_NAME = 'EMAIL'"));
    }

    /// <summary>docs/commands.html:1676 — [ AS ] dataTypeOrDomain</summary>
    [Fact]
    public void CreateDomain_FromAnotherDomain_CanBeUsed() // CMD-1676
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE DOMAIN D1 AS INT");

        _ = connection.Execute("CREATE DOMAIN D2 D1");
        _ = connection.Execute("CREATE TABLE TEST(V D2)");
        _ = connection.Execute("INSERT INTO TEST(V) VALUES (5)");

        Assert.Equal(5, connection.QuerySingle<int>("SELECT V FROM TEST"));
    }

    /// <summary>docs/commands.html:1677 — [ DEFAULT expression ]</summary>
    [Fact]
    public void CreateDomain_Default_FillsNewColumns() // CMD-1677
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE DOMAIN D AS INT DEFAULT 7");
        _ = connection.Execute("CREATE TABLE TEST(V D)");

        _ = connection.Execute("INSERT INTO TEST DEFAULT VALUES");

        Assert.Equal(7, connection.QuerySingle<int>("SELECT V FROM TEST"));
    }

    /// <summary>docs/commands.html:1678 — [ ON UPDATE expression ]</summary>
    [Fact]
    public void CreateDomain_OnUpdate_SetsTheValueWhenTheRowChanges() // CMD-1678
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE DOMAIN D AS INT ON UPDATE 9");
        _ = connection.Execute("CREATE TABLE TEST(ID INT, V D)");
        _ = connection.Execute("INSERT INTO TEST(ID, V) VALUES (1, 1)");

        _ = connection.Execute("UPDATE TEST SET ID = 2");

        Assert.Equal(9, connection.QuerySingle<int>("SELECT V FROM TEST"));
    }

    /// <summary>docs/commands.html:1679 — [ COMMENT expression ]</summary>
    [Fact]
    public void CreateDomain_Comment_StoresTheRemark() // CMD-1679
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE DOMAIN D AS INT COMMENT 'my domain'");

        Assert.Equal("my domain", connection.QuerySingle<string>(
            "SELECT REMARKS FROM INFORMATION_SCHEMA.DOMAINS WHERE DOMAIN_NAME = 'D'"));
    }

    /// <summary>docs/commands.html:1680 — [ CHECK (condition) ] [...]</summary>
    [Fact]
    public void CreateDomain_Check_RejectsValuesThatFailIt() // CMD-1680
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE DOMAIN EMAIL AS VARCHAR(255) CHECK (POSITION('@', VALUE) > 1)");
        _ = connection.Execute("CREATE TABLE TEST(V EMAIL)");

        _ = Assert.ThrowsAny<Exception>(() => connection.Execute("INSERT INTO TEST(V) VALUES ('no-at-sign')"));
    }
}
