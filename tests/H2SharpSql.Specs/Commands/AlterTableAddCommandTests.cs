// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — ALTER TABLE ... ADD and ADD CONSTRAINT, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class AlterTableAddCommandTests
{
    /// <summary>docs/commands.html:1263 — ALTER TABLE [ IF EXISTS ] [schemaName.]tableName ADD [ COLUMN ]</summary>
    [Fact]
    public void AlterTableAdd_Column_AddsItToTheTable() // CMD-1263
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("ALTER TABLE IF EXISTS TEST ADD COLUMN CREATEDATE TIMESTAMP");

        Assert.Equal(1, ColumnCount(connection, "CREATEDATE"));
    }

    /// <summary>docs/commands.html:1264 — { [ IF NOT EXISTS ] columnName columnDefinition [ USING initialValueExpression ]</summary>
    [Fact]
    public void AlterTableAdd_Using_FillsExistingRows() // CMD-1264
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("ALTER TABLE TEST ADD CREATEDATE TIMESTAMP USING CURRENT_TIMESTAMP");

        Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE CREATEDATE IS NULL"));
    }

    /// <summary>docs/commands.html:1265 — | { ( { columnName columnDefinition | tableConstraintDefinition } [,...] ) } }</summary>
    [Fact]
    public void AlterTableAdd_ColumnList_AddsThemAllAtOnce() // CMD-1265
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("ALTER TABLE TEST ADD (A INT, B INT)");

        Assert.Equal(1, ColumnCount(connection, "A"));
        Assert.Equal(1, ColumnCount(connection, "B"));
    }

    /// <summary>docs/commands.html:1266 — [ { { BEFORE | AFTER } columnName } | FIRST ]</summary>
    [Fact]
    public void AlterTableAdd_Before_PutsTheColumnAtTheGivenPlace() // CMD-1266
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("ALTER TABLE TEST ADD A INT BEFORE ID");

        int position = connection.QuerySingle<int>(
            "SELECT ORDINAL_POSITION FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TEST' AND COLUMN_NAME = 'A'");
        Assert.Equal(1, position);
    }

    /// <summary>docs/commands.html:1288 — ALTER TABLE [ IF EXISTS ] tableName ADD tableConstraintDefinition [ CHECK | NOCHECK ]</summary>
    [Fact]
    public void AlterTableAddConstraint_AddsTheNamedTableConstraint() // CMD-1288
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("ALTER TABLE TEST ADD CONSTRAINT NAME_UNIQUE UNIQUE(NAME)");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE CONSTRAINT_NAME = 'NAME_UNIQUE'"));
    }

    private static int ColumnCount(IDbConnection connection, string column) =>
        connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'PUBLIC' AND TABLE_NAME = 'TEST' AND COLUMN_NAME = $Column",
            new { Column = column });

    private static void GivenTestTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada')");
    }
}
