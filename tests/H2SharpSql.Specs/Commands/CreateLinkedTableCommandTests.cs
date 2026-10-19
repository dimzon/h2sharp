// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — CREATE LINKED TABLE command, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class CreateLinkedTableCommandTests
{
    /// <summary>docs/commands.html:1729 — CREATE [ FORCE ] [ [ GLOBAL | LOCAL ] TEMPORARY ]</summary>
    [Fact]
    public void CreateLinkedTable_Force_RegistersTheTableWithoutCheckingTheSource() // CMD-1729
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute(
            "CREATE FORCE LINKED TABLE LINK('org.h2.Driver', 'jdbc:h2:mem:src', 'sa', 'sa', 'SRC')");

        Assert.Equal(1, TableCount(connection, "LINK"));
    }

    /// <summary>docs/commands.html:1730 — LINKED TABLE [ IF NOT EXISTS ]</summary>
    [Fact]
    public void CreateLinkedTable_IfNotExists_RegistersItOnce() // CMD-1730
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute(
            "CREATE FORCE LINKED TABLE IF NOT EXISTS LINK('org.h2.Driver', 'jdbc:h2:mem:src', 'sa', 'sa', 'SRC')");
        _ = connection.Execute(
            "CREATE FORCE LINKED TABLE IF NOT EXISTS LINK('org.h2.Driver', 'jdbc:h2:mem:src', 'sa', 'sa', 'SRC')");

        Assert.Equal(1, TableCount(connection, "LINK"));
    }

    /// <summary>docs/commands.html:1731 — [schemaName.]tableName ( driverString, urlString, userString, passwordString,</summary>
    [Fact]
    public void CreateLinkedTable_DriverAndUrl_RegistersTheTable() // CMD-1731
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute(
            "CREATE FORCE LINKED TABLE LINK('', 'jdbc:h2:mem:src', 'sa', 'sa', 'SRC')");

        Assert.Equal(1, TableCount(connection, "LINK"));
    }

    /// <summary>docs/commands.html:1732 — [ originalSchemaString, ] originalTableString )</summary>
    [Fact]
    public void CreateLinkedTable_QualifiedSourceName_IsAccepted() // CMD-1732
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute(
            "CREATE FORCE LINKED TABLE LINK('', 'jdbc:h2:mem:src', 'sa', 'sa', 'PUBLIC', 'SRC')");

        Assert.Equal(1, TableCount(connection, "LINK"));
    }

    /// <summary>docs/commands.html:1733 — [ EMIT UPDATES | READONLY ] [ FETCH_SIZE sizeInt] [AUTOCOMMIT ON|OFF]</summary>
    [Fact]
    public void CreateLinkedTable_ReadonlyWithFetchSize_RegistersTheTable() // CMD-1733
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute(
            "CREATE FORCE LINKED TABLE LINK('', 'jdbc:h2:mem:src', 'sa', 'sa', 'SRC') READONLY FETCH_SIZE 100 AUTOCOMMIT OFF");

        Assert.Equal(1, TableCount(connection, "LINK"));
    }

    private static int TableCount(IDbConnection connection, string table) =>
        connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @Table",
            new { Table = table });
}
