// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — ALTER INDEX, ALTER SCHEMA and ALTER SEQUENCE commands,
// one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class AlterObjectCommandTests
{
    /// <summary>docs/commands.html:1206 — ALTER INDEX [ IF EXISTS ] [schemaName.]indexName RENAME TO newIndexName</summary>
    [Fact]
    public void AlterIndexRename_GivesTheIndexANewName() // CMD-1206
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("CREATE INDEX IDXNAME ON TEST(NAME)");

        _ = connection.Execute("ALTER INDEX IDXNAME RENAME TO IDX_TEST_NAME");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.INDEXES WHERE INDEX_NAME = 'IDX_TEST_NAME'"));
    }

    /// <summary>docs/commands.html:1225 — ALTER SCHEMA [ IF EXISTS ] schemaName RENAME TO newSchemaName</summary>
    [Fact]
    public void AlterSchemaRename_GivesTheSchemaANewName() // CMD-1225
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SCHEMA TEST");

        _ = connection.Execute("ALTER SCHEMA TEST RENAME TO PRODUCTION");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = 'PRODUCTION'"));
    }

    /// <summary>docs/commands.html:1244 — ALTER SEQUENCE [ IF EXISTS ] [schemaName.]sequenceName alterSequenceOption [...]</summary>
    [Fact]
    public void AlterSequence_RestartWith_ChangesTheNextValue() // CMD-1244
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SEQUENCE SEQ_ID");

        _ = connection.Execute("ALTER SEQUENCE SEQ_ID RESTART WITH 1000");

        Assert.Equal(1000L, connection.QuerySingle<long>("SELECT NEXT VALUE FOR SEQ_ID"));
    }

    /// <summary>docs/commands.html:2252 — ALTER TYPE [schemaName.]enumName ADD VALUE string</summary>
    [Fact]
    public void AlterType_AddValue_ExtendsTheEnum() // CMD-2252
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TYPE CARD_SUIT AS ENUM ('hearts', 'spades')");

        _ = connection.Execute("ALTER TYPE CARD_SUIT ADD VALUE 'clubs'");

        Assert.Equal(3, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.ENUM_VALUES WHERE OBJECT_NAME = 'CARD_SUIT'"));
    }
}
