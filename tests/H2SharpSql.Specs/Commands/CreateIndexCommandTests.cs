// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — CREATE INDEX command, one test per checklist row.
using System;
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class CreateIndexCommandTests
{
    /// <summary>docs/commands.html:1704 — CREATE [ UNIQUE [ nullsDistinct ] | SPATIAL ] INDEX</summary>
    [Fact]
    public void CreateIndex_Unique_RejectsDuplicateValues() // CMD-1704
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);
        _ = connection.Execute("CREATE UNIQUE INDEX IDXU ON TEST(NAME)");

        _ = Assert.ThrowsAny<Exception>(() => connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (3, 'Ada')"));
    }

    /// <summary>docs/commands.html:1705 — [ [ IF NOT EXISTS ] [schemaName.]indexName ]</summary>
    [Fact]
    public void CreateIndex_IfNotExists_CreatesItOnce() // CMD-1705
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("CREATE INDEX IF NOT EXISTS IDXNAME ON TEST(NAME)");
        _ = connection.Execute("CREATE INDEX IF NOT EXISTS IDXNAME ON TEST(NAME)");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.INDEXES WHERE INDEX_NAME = 'IDXNAME'"));
    }

    /// <summary>docs/commands.html:1706 — ON [schemaName.]tableName ( indexColumn [,...] )</summary>
    [Fact]
    public void CreateIndex_TwoColumns_IndexesBoth() // CMD-1706
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("CREATE INDEX IDXTWO ON TEST(NAME, ID)");

        Assert.Equal(2, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.INDEX_COLUMNS WHERE INDEX_NAME = 'IDXTWO'"));
    }

    /// <summary>docs/commands.html:1707 — [ INCLUDE ( indexColumn [,...] ) ]</summary>
    [Fact]
    public void CreateIndex_Include_KeepsTheExtraColumnInTheIndex() // CMD-1707
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("CREATE UNIQUE INDEX IDXINC ON TEST(NAME) INCLUDE (ID)");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.INDEXES WHERE INDEX_NAME = 'IDXINC'"));
    }

    private static void GivenTestTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada')");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (2, 'Alan')");
    }
}
