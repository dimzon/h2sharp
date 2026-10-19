// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — INSERT command, one test per checklist row.
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class InsertCommandTests
{
    /// <summary>docs/commands.html:746 — INSERT INTO [schemaName.]tableName [ ( columnName [,...] ) ]</summary>
    [Fact]
    public void Insert_ColumnList_TakesValuesInTheGivenOrder() // CMD-0746
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");

        _ = connection.Execute("INSERT INTO TEST(NAME, ID) VALUES ('Ada', 1)");

        Assert.Equal("Ada", connection.QuerySingle<string>("SELECT NAME FROM TEST WHERE ID = 1"));
    }

    /// <summary>docs/commands.html:747 — { [ overrideClause ] { insertValues | [ DIRECT ] query } }</summary>
    [Fact]
    public void Insert_Query_AddsTheRowsTheQueryProduces() // CMD-0747
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("CREATE TABLE SOURCE(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO SOURCE(ID, NAME) VALUES (1, 'Ada')");
        _ = connection.Execute("INSERT INTO SOURCE(ID, NAME) VALUES (2, 'Alan')");

        _ = connection.Execute("INSERT INTO TEST SELECT ID, NAME FROM SOURCE");

        List<int> ids = connection.Query<int>("SELECT ID FROM TEST ORDER BY ID").AsList();
        Assert.Equal(new[] { 1, 2 }, ids);
    }

    /// <summary>docs/commands.html:748 — | DEFAULT VALUES</summary>
    [Fact]
    public void Insert_DefaultValues_UsesTheColumnDefaults() // CMD-0748
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT DEFAULT 5, NAME VARCHAR(50) DEFAULT 'n/a')");

        _ = connection.Execute("INSERT INTO TEST DEFAULT VALUES");

        Assert.Equal(5, connection.QuerySingle<int>("SELECT ID FROM TEST"));
        Assert.Equal("n/a", connection.QuerySingle<string>("SELECT NAME FROM TEST"));
    }
}
