// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — ALTER TABLE ... DROP COLUMN and DROP CONSTRAINT,
// one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class AlterTableDropCommandTests
{
    /// <summary>docs/commands.html:1385 — ALTER TABLE [ IF EXISTS ] [schemaName.]tableName</summary>
    [Fact]
    public void AlterTableDropColumn_IfExistsOnTable_DropsTheColumn() // CMD-1385
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("ALTER TABLE IF EXISTS TEST DROP COLUMN NAME");

        Assert.Equal(0, ColumnCount(connection, "NAME"));
    }

    /// <summary>docs/commands.html:1386 — DROP [ COLUMN ] [ IF EXISTS ]</summary>
    [Fact]
    public void AlterTableDropColumn_IfExists_DropsTheColumn() // CMD-1386
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("ALTER TABLE TEST DROP COLUMN IF EXISTS NAME");

        Assert.Equal(0, ColumnCount(connection, "NAME"));
    }

    /// <summary>docs/commands.html:1387 — { ( columnName [,...] ) } | columnName [,...]</summary>
    [Fact]
    public void AlterTableDropColumn_ColumnList_DropsThemAllAtOnce() // CMD-1387
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME1 VARCHAR(50), NAME2 VARCHAR(50))");

        _ = connection.Execute("ALTER TABLE TEST DROP (NAME1, NAME2)");

        Assert.Equal(0, ColumnCount(connection, "NAME1"));
        Assert.Equal(0, ColumnCount(connection, "NAME2"));
    }

    /// <summary>docs/commands.html:1408 — ALTER TABLE [ IF EXISTS ] [schemaName.]tableName DROP</summary>
    [Fact]
    public void AlterTableDropConstraint_Restrict_RemovesTheConstraint() // CMD-1408
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenConstrainedTable(connection);

        _ = connection.Execute("ALTER TABLE TEST DROP CONSTRAINT NAME_UNIQUE RESTRICT");

        Assert.Equal(0, ConstraintCount(connection, "NAME_UNIQUE"));
    }

    /// <summary>docs/commands.html:1409 — CONSTRAINT [ IF EXISTS ] [schemaName.]constraintName [ RESTRICT | CASCADE ] | { PRIMARY KEY }</summary>
    [Fact]
    public void AlterTableDropConstraint_PrimaryKey_RemovesIt() // CMD-1409
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR(50))");

        _ = connection.Execute("ALTER TABLE TEST DROP PRIMARY KEY");

        Assert.Equal(0, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE TABLE_NAME = 'TEST' AND CONSTRAINT_TYPE = 'PRIMARY KEY'"));
    }

    private static int ColumnCount(IDbConnection connection, string column) =>
        connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'PUBLIC' AND TABLE_NAME = 'TEST' AND COLUMN_NAME = $Column",
            new { Column = column });

    private static int ConstraintCount(IDbConnection connection, string name) =>
        connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE CONSTRAINT_NAME = $Name",
            new { Name = name });

    private static void GivenTestTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada')");
    }

    private static void GivenConstrainedTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50), CONSTRAINT NAME_UNIQUE UNIQUE(NAME))");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada')");
    }
}
