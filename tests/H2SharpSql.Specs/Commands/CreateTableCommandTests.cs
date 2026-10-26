// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — CREATE TABLE command, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class CreateTableCommandTests
{
    /// <summary>docs/commands.html:1819 — CREATE [ CACHED | MEMORY ] [ { TEMP } | [ GLOBAL | LOCAL ] TEMPORARY ]</summary>
    [Fact]
    public void CreateTable_Temporary_RegistersTheTable() // CMD-1819
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE MEMORY TEMP TABLE TEST(ID INT)");

        Assert.Equal(1, TableCount(connection, "TEST"));
    }

    /// <summary>docs/commands.html:1820 — TABLE [ IF NOT EXISTS ] [schemaName.]tableName</summary>
    [Fact]
    public void CreateTable_IfNotExists_CreatesItOnce() // CMD-1820
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE TABLE IF NOT EXISTS TEST(ID INT)");
        _ = connection.Execute("CREATE TABLE IF NOT EXISTS TEST(ID INT)");

        Assert.Equal(1, TableCount(connection, "TEST"));
    }

    /// <summary>docs/commands.html:1821 — [ ( { columnName [columnDefinition] | tableConstraintDefinition } [,...] ) ]</summary>
    [Fact]
    public void CreateTable_TableConstraints_RegisterBothOfThem() // CMD-1821
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute(
            "CREATE TABLE TEST(ID INT, NAME VARCHAR(50), CONSTRAINT PK_TEST PRIMARY KEY(ID), CONSTRAINT NAME_UNIQUE UNIQUE(NAME))");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE CONSTRAINT_NAME = 'PK_TEST'"));
        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE CONSTRAINT_NAME = 'NAME_UNIQUE'"));
    }

    /// <summary>docs/commands.html:1822 — [ ENGINE tableEngineName ]</summary>
    [Fact]
    public void CreateTable_Engine_RegistersTheTable() // CMD-1822
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE TABLE TEST(ID INT) ENGINE \"org.h2.mvstore.db.MVTableEngine\"");

        Assert.Equal(1, TableCount(connection, "TEST"));
    }

    /// <summary>docs/commands.html:1823 — [ WITH tableEngineParamName [,...] ]</summary>
    [Fact]
    public void CreateTable_WithEngineParameter_RegistersTheTable() // CMD-1823
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute(
            "CREATE TABLE TEST(ID INT) ENGINE \"org.h2.mvstore.db.MVTableEngine\" WITH \"compress\"");

        Assert.Equal(1, TableCount(connection, "TEST"));
    }

    /// <summary>docs/commands.html:1824 — [ NOT PERSISTENT ] [ TRANSACTIONAL ]</summary>
    [Fact]
    public void CreateTable_NotPersistentTransactional_RegistersTheTable() // CMD-1824
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE TABLE TEST(ID INT) NOT PERSISTENT TRANSACTIONAL");

        Assert.Equal(1, TableCount(connection, "TEST"));
    }

    /// <summary>docs/commands.html:1825 — [ AS ( query ) [ WITH [ NO ] DATA ] ]</summary>
    [Fact]
    public void CreateTable_AsQueryWithData_CopiesTheRows() // CMD-1825
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE TABLE TEST AS (SELECT 1 AS ID) WITH DATA");

        Assert.Equal(1, connection.QuerySingle<int>("SELECT ID FROM TEST"));
    }

    private static int TableCount(IDbConnection connection, string table) =>
        connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = $Table",
            new { Table = table });
}
