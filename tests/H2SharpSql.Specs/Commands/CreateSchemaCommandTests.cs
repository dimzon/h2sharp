// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — CREATE SCHEMA and CREATE SEQUENCE commands, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class CreateSchemaCommandTests
{
    /// <summary>docs/commands.html:1775 — CREATE SCHEMA [ IF NOT EXISTS ]</summary>
    [Fact]
    public void CreateSchema_IfNotExists_CreatesItOnce() // CMD-1775
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE SCHEMA IF NOT EXISTS TEST_SCHEMA");
        _ = connection.Execute("CREATE SCHEMA IF NOT EXISTS TEST_SCHEMA");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = 'TEST_SCHEMA'"));
    }

    /// <summary>docs/commands.html:1776 — { name [ AUTHORIZATION ownerName ] | [ AUTHORIZATION ownerName ] }</summary>
    [Fact]
    public void CreateSchema_Authorization_NamesTheOwner() // CMD-1776
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE SCHEMA TEST_SCHEMA AUTHORIZATION SA");

        Assert.Equal("SA", connection.QuerySingle<string>(
            "SELECT SCHEMA_OWNER FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = 'TEST_SCHEMA'"));
    }

    /// <summary>docs/commands.html:1777 — [ WITH tableEngineParamName [,...] ]</summary>
    [Fact]
    public void CreateSchema_WithEngineParameters_IsAccepted() // CMD-1777
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE SCHEMA TEST_SCHEMA WITH \"compress\"");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = 'TEST_SCHEMA'"));
    }
}

public sealed class CreateSequenceCommandTests
{
    /// <summary>docs/commands.html:1798 — CREATE SEQUENCE [ IF NOT EXISTS ] [schemaName.]sequenceName</summary>
    [Fact]
    public void CreateSequence_IfNotExists_RegistersItOnce() // CMD-1798
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE SEQUENCE IF NOT EXISTS SEQ_ID");
        _ = connection.Execute("CREATE SEQUENCE IF NOT EXISTS SEQ_ID");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SEQUENCES WHERE SEQUENCE_NAME = 'SEQ_ID'"));
    }

    /// <summary>docs/commands.html:1799 — [ { AS dataType | sequenceOption } [...] ]</summary>
    [Fact]
    public void CreateSequence_AsIntegerStartWith_SetsTheFirstValue() // CMD-1799
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE SEQUENCE SEQ2 AS INTEGER START WITH 10");

        Assert.Equal(10, connection.QuerySingle<int>("SELECT NEXT VALUE FOR SEQ2"));
    }
}
