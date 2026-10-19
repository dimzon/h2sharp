// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — DROP statements for objects, one test per checklist row.
using System;
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class DropObjectCommandTests
{
    /// <summary>docs/commands.html:1946 — DROP AGGREGATE [ IF EXISTS ] aggregateName</summary>
    [Fact]
    public void DropAggregate_RemovesIt() // CMD-1946
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE AGGREGATE SIMPLE_MEDIAN FOR 'com.acme.db.Median'");

        _ = connection.Execute("DROP AGGREGATE SIMPLE_MEDIAN");

        Assert.Equal(0, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_NAME = 'SIMPLE_MEDIAN'"));
    }

    /// <summary>docs/commands.html:1965 — DROP ALIAS [ IF EXISTS ] [schemaName.]aliasName</summary>
    [Fact]
    public void DropAlias_RemovesIt() // CMD-1965
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE ALIAS MY_SQRT FOR 'java.lang.Math.sqrt'");

        _ = connection.Execute("DROP ALIAS MY_SQRT");

        _ = Assert.ThrowsAny<Exception>(() => connection.ExecuteScalar("SELECT MY_SQRT(4)"));
    }

    /// <summary>docs/commands.html:2003 — DROP CONSTANT [ IF EXISTS ] [schemaName.]constantName</summary>
    [Fact]
    public void DropConstant_RemovesIt() // CMD-2003
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE CONSTANT ONE VALUE 1");

        _ = connection.Execute("DROP CONSTANT ONE");

        Assert.Equal(0, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.CONSTANTS WHERE CONSTANT_NAME = 'ONE'"));
    }

    /// <summary>docs/commands.html:2041 — DROP INDEX [ IF EXISTS ] [schemaName.]indexName</summary>
    [Fact]
    public void DropIndex_RemovesIt() // CMD-2041
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("CREATE INDEX IDXNAME ON TEST(NAME)");

        _ = connection.Execute("DROP INDEX IF EXISTS IDXNAME");

        Assert.Equal(0, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.INDEXES WHERE INDEX_NAME = 'IDXNAME'"));
    }

    /// <summary>docs/commands.html:2060 — DROP ROLE [ IF EXISTS ] roleName</summary>
    [Fact]
    public void DropRole_RemovesIt() // CMD-2060
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE ROLE READONLY");

        _ = connection.Execute("DROP ROLE READONLY");

        Assert.Equal(0, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.ROLES WHERE ROLE_NAME = 'READONLY'"));
    }

    /// <summary>docs/commands.html:2098 — DROP SEQUENCE [ IF EXISTS ] [schemaName.]sequenceName</summary>
    [Fact]
    public void DropSequence_RemovesIt() // CMD-2098
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SEQUENCE SEQ_ID");

        _ = connection.Execute("DROP SEQUENCE SEQ_ID");

        Assert.Equal(0, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SEQUENCES WHERE SEQUENCE_NAME = 'SEQ_ID'"));
    }

    /// <summary>docs/commands.html:2138 — DROP TRIGGER [ IF EXISTS ] [schemaName.]triggerName</summary>
    [Fact]
    public void DropTrigger_RemovesIt() // CMD-2138
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");
        _ = connection.Execute("CREATE TRIGGER TRIG_INS BEFORE INSERT ON TEST FOR EACH ROW CALL 'MyTrigger'");

        _ = connection.Execute("DROP TRIGGER TRIG_INS");

        Assert.Equal(0, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TRIGGERS WHERE TRIGGER_NAME = 'TRIG_INS'"));
    }

    /// <summary>docs/commands.html:2157 — DROP USER [ IF EXISTS ] userName</summary>
    [Fact]
    public void DropUser_RemovesIt() // CMD-2157
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE USER TOM PASSWORD 'secret'");

        _ = connection.Execute("DROP USER TOM");

        Assert.Equal(0, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.USERS WHERE USER_NAME = 'TOM'"));
    }

    /// <summary>docs/commands.html:2022 — DROP DOMAIN [ IF EXISTS ] [schemaName.]domainName [ RESTRICT | CASCADE ]</summary>
    [Fact]
    public void DropDomain_RemovesIt() // CMD-2022
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE DOMAIN EMAIL AS VARCHAR(255)");

        _ = connection.Execute("DROP DOMAIN EMAIL RESTRICT");

        Assert.Equal(0, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.DOMAINS WHERE DOMAIN_NAME = 'EMAIL'"));
    }

    /// <summary>docs/commands.html:2079 — DROP SCHEMA [ IF EXISTS ] schemaName [ RESTRICT | CASCADE ]</summary>
    [Fact]
    public void DropSchema_Cascade_RemovesIt() // CMD-2079
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SCHEMA TEST_SCHEMA");

        _ = connection.Execute("DROP SCHEMA TEST_SCHEMA CASCADE");

        Assert.Equal(0, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = 'TEST_SCHEMA'"));
    }

    /// <summary>docs/commands.html:2117 — DROP TABLE [ IF EXISTS ] [schemaName.]tableName [,...]</summary>
    [Fact]
    public void DropTable_IfExists_RemovesTheTable() // CMD-2117
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");

        _ = connection.Execute("DROP TABLE IF EXISTS TEST");

        Assert.Equal(0, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TEST'"));
    }

    /// <summary>docs/commands.html:2118 — [ RESTRICT | CASCADE ]</summary>
    [Fact]
    public void DropTable_Cascade_AlsoRemovesTheDependentView() // CMD-2118
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");
        _ = connection.Execute("CREATE VIEW TEST_VIEW AS SELECT ID FROM TEST");

        _ = connection.Execute("DROP TABLE TEST CASCADE");

        Assert.Equal(0, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.VIEWS WHERE VIEW_NAME = 'TEST_VIEW'"));
    }

    /// <summary>docs/commands.html:2176 — DROP VIEW [ IF EXISTS ] [schemaName.]viewName [ RESTRICT | CASCADE ]</summary>
    [Fact]
    public void DropView_RemovesIt() // CMD-2176
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");
        _ = connection.Execute("CREATE VIEW TEST_VIEW AS SELECT ID FROM TEST");

        _ = connection.Execute("DROP VIEW TEST_VIEW");

        Assert.Equal(0, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.VIEWS WHERE VIEW_NAME = 'TEST_VIEW'"));
    }

    /// <summary>docs/commands.html:2195 — DROP MATERIALIZED VIEW [ IF EXISTS ] [schemaName.]viewName</summary>
    [Fact]
    public void DropMaterializedView_RemovesIt() // CMD-2195
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");
        _ = connection.Execute("CREATE MATERIALIZED VIEW TEST_VIEW AS SELECT ID FROM TEST");

        _ = connection.Execute("DROP MATERIALIZED VIEW TEST_VIEW");

        Assert.Equal(0, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TEST_VIEW'"));
    }
}
