// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// THE EXAMPLE. Every test in this suite is shaped like this one, and this file is the one
// AGENTS.md points at. Read it before writing a new test.
//
// The rule: one checklist row, one test, and the test names its row in the comment above
// the method. CMD-0705 is the FROM clause of SELECT and CMD-0706 is its WHERE clause,
// both taken from CHECKLIST.csv; docs/commands.html:705 and :706 are the lines they came
// from. A test that does not name its row cannot be reconciled against the checklist.
//
// TestDatabase.Open() hands back a new, empty database, so there is nothing to read until
// the test puts something there. That is why the setup sits right here in GivenTestTable
// rather than anywhere shared: each test builds the state it needs and no state survives.
//
// The setup is a private method because the two tests below need the same three statements,
// not because there are nine hundred tests. While there are two, it is three lines either
// way, and this way the reader sees the whole test at once.
//
// Dapper is used as a caller uses it, on IDbConnection, and never against the provider's
// own members. Execute returns the number of rows a statement touched, which is why the
// setup calls are assigned to _ — the count is not what these tests are about.
//
// ORDER BY is in both statements so the expectation is a fixed list rather than whatever
// order the engine happened to produce.
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Dml;

public sealed class SelectClauseTests
{
    /// <summary>docs/commands.html:705 — SELECT [ FROM tableExpression [,...] ]</summary>
    [Fact]
    public void Select_FromClause_ReadsTheGivenTable() // CMD-0705
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        List<int> ids = connection.Query<int>("SELECT ID FROM TEST ORDER BY ID").AsList();

        Assert.Equal(new[] { 1, 2 }, ids);
    }

    /// <summary>docs/commands.html:706 — [ WHERE expression ]</summary>
    [Fact]
    public void Select_WhereClause_ReturnsOnlyTheMatchingRows() // CMD-0706
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        List<int> ids = connection.Query<int>(
            "SELECT ID FROM TEST WHERE NAME = @Name ORDER BY ID",
            new { Name = "Alan" }).AsList();

        Assert.Equal(new[] { 2 }, ids);
    }

    /// <summary>
    /// Puts TEST and two rows into a database that was empty a moment ago.
    /// </summary>
    private static void GivenTestTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada')");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (2, 'Alan')");
    }
}
