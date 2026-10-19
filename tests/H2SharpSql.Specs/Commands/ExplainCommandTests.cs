// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — EXPLAIN command, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class ExplainCommandTests
{
    /// <summary>docs/commands.html:872 — EXPLAIN { [ PLAN FOR ] | ANALYZE }</summary>
    [Fact]
    public void Explain_PlanForQuery_ReturnsThePlan() // CMD-0872
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");
        _ = connection.Execute("INSERT INTO TEST(ID) VALUES (1)");

        string plan = connection.QuerySingle<string>("EXPLAIN PLAN FOR SELECT ID FROM TEST WHERE ID = 1");

        Assert.NotEmpty(plan);
    }

    /// <summary>docs/commands.html:873 — { query | insert | update | delete | mergeInto | mergeUsing }</summary>
    [Fact]
    public void Explain_Insert_ReturnsThePlan() // CMD-0873
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");

        string plan = connection.QuerySingle<string>("EXPLAIN INSERT INTO TEST(ID, NAME) VALUES (3, 'Grace')");

        Assert.NotEmpty(plan);
    }
}
