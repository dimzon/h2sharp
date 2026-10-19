// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — SHOW command, one test per checklist row.
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class ShowCommandTests
{
    /// <summary>docs/commands.html:995 — SHOW { SCHEMAS | TABLES [ FROM schemaName ] |</summary>
    [Fact]
    public void Show_Tables_ListsTheTablesOfTheSchema() // CMD-0995
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");

        List<string> tables = connection.Query<string>("SHOW TABLES").AsList();

        Assert.Contains("TEST", tables);
    }

    /// <summary>docs/commands.html:996 — COLUMNS FROM tableName [ FROM schemaName ] }</summary>
    [Fact]
    public void Show_Columns_ListsTheColumnsOfTheTable() // CMD-0996
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");

        List<string> columns = connection.Query<string>("SHOW COLUMNS FROM TEST").AsList();

        Assert.Contains("ID", columns);
        Assert.Contains("NAME", columns);
    }
}
