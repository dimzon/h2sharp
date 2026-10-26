// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — ALTER TABLE ... RENAME and RENAME CONSTRAINT,
// one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class AlterTableRenameCommandTests
{
    /// <summary>docs/commands.html:1307 — ALTER TABLE [ IF EXISTS ] [schemaName.]tableName</summary>
    [Fact]
    public void AlterTableRenameConstraint_RenamesIt() // CMD-1307
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenConstrainedTable(connection);

        _ = connection.Execute("ALTER TABLE TEST RENAME CONSTRAINT FOO TO BAR");

        Assert.Equal(1, ConstraintCount(connection, "BAR"));
    }

    /// <summary>docs/commands.html:1308 — RENAME CONSTRAINT [schemaName.]oldConstraintName</summary>
    [Fact]
    public void AlterTableRenameConstraint_QualifiedOldName_IsAccepted() // CMD-1308
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenConstrainedTable(connection);

        _ = connection.Execute("ALTER TABLE TEST RENAME CONSTRAINT PUBLIC.FOO TO BAR");

        Assert.Equal(1, ConstraintCount(connection, "BAR"));
    }

    /// <summary>docs/commands.html:1309 — TO newConstraintName</summary>
    [Fact]
    public void AlterTableRenameConstraint_OldNameIsGone() // CMD-1309
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenConstrainedTable(connection);

        _ = connection.Execute("ALTER TABLE TEST RENAME CONSTRAINT FOO TO BAR");

        Assert.Equal(0, ConstraintCount(connection, "FOO"));
    }

    /// <summary>docs/commands.html:1452 — ALTER TABLE [ IF EXISTS ] [schemaName.]tableName RENAME TO newName</summary>
    [Fact]
    public void AlterTableRename_GivesTheTableANewName() // CMD-1452
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenConstrainedTable(connection);

        _ = connection.Execute("ALTER TABLE TEST RENAME TO MY_DATA");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'MY_DATA'"));
    }

    private static int ConstraintCount(IDbConnection connection, string name) =>
        connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE CONSTRAINT_NAME = $Name",
            new { Name = name });

    private static void GivenConstrainedTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50), CONSTRAINT FOO UNIQUE(NAME))");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada')");
    }
}
