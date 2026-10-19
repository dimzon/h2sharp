// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — COMMENT ON command, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class CommentCommandTests
{
    /// <summary>docs/commands.html:1585 — COMMENT ON</summary>
    [Fact]
    public void Comment_OnTable_StoresTheRemark() // CMD-1585
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("COMMENT ON TABLE TEST IS 'Table used for testing'");

        Assert.Equal("Table used for testing", connection.QuerySingle<string>(
            "SELECT REMARKS FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TEST'"));
    }

    /// <summary>docs/commands.html:1586 — { { COLUMN [schemaName.]tableName.columnName }</summary>
    [Fact]
    public void Comment_OnColumn_StoresTheRemark() // CMD-1586
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("COMMENT ON COLUMN TEST.NAME IS 'person name'");

        Assert.Equal("person name", connection.QuerySingle<string>(
            "SELECT REMARKS FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TEST' AND COLUMN_NAME = 'NAME'"));
    }

    /// <summary>docs/commands.html:1587 — | { { TABLE | VIEW | CONSTANT | CONSTRAINT | ALIAS | INDEX | ROLE</summary>
    [Fact]
    public void Comment_OnView_StoresTheRemark() // CMD-1587
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);
        _ = connection.Execute("CREATE VIEW V AS SELECT ID FROM TEST");

        _ = connection.Execute("COMMENT ON VIEW V IS 'view comment'");

        Assert.Equal("view comment", connection.QuerySingle<string>(
            "SELECT REMARKS FROM INFORMATION_SCHEMA.VIEWS WHERE VIEW_NAME = 'V'"));
    }

    /// <summary>docs/commands.html:1588 — | SCHEMA | SEQUENCE | TRIGGER | USER | DOMAIN } [schemaName.]objectName } }</summary>
    [Fact]
    public void Comment_OnSequence_StoresTheRemark() // CMD-1588
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SEQUENCE SEQ_ID");

        _ = connection.Execute("COMMENT ON SEQUENCE SEQ_ID IS 'seq comment'");

        Assert.Equal("seq comment", connection.QuerySingle<string>(
            "SELECT REMARKS FROM INFORMATION_SCHEMA.SEQUENCES WHERE SEQUENCE_NAME = 'SEQ_ID'"));
    }

    /// <summary>docs/commands.html:1589 — IS expression</summary>
    [Fact]
    public void Comment_Expression_IsEvaluated() // CMD-1589
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("COMMENT ON TABLE TEST IS 'a' || 'b'");

        Assert.Equal("ab", connection.QuerySingle<string>(
            "SELECT REMARKS FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TEST'"));
    }

    private static void GivenTestTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
    }
}
