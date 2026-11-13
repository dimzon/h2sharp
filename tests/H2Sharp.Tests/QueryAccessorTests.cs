/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The query accessors and SQL renderings a plain SELECT never asks for: the lazy and
 * in-predicate flags, the read-only and FOR UPDATE answers, the cache switch, the sort
 * order, the ORDER BY text a view definition keeps, and the OFFSET/FETCH evaluation that
 * refuses a negative or NULL offset.
 */
using System;
using H2Sharp.Command;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Result;
using Xunit;

namespace H2Sharp.Tests;

public sealed class QueryAccessorTests
{
    private readonly SessionLocal session;
    private readonly Database database;

    public QueryAccessorTests()
    {
        database = new Database(
            ConnectionInfo.Memory("query_accessors_" + Guid.NewGuid().ToString("N")));
        session = new SessionLocal(database, "sa");
        Execute("CREATE TABLE T(ID INT PRIMARY KEY, A INT)");
        Execute("INSERT INTO T VALUES (1, 10), (2, 20), (3, 30)");
    }

    private void Execute(string sql)
    {
        CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
        _ = container.ExecuteUpdate(false);
    }

    private string QuerySingle(string sql)
    {
        CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
        using ResultInterface result = container.ExecuteQuery(10, 0, false);
        _ = result.Next();
        return result.CurrentRow()![0]!.GetString()!;
    }

    [Fact]
    public void TheFlagsOfAPlainSelectAnswer()
    {
        CommandContainer container = Assert.IsType<CommandContainer>(
            session.PrepareCommand("SELECT A FROM T"));
        Select query = Assert.IsType<Select>(container.GetPreparedCommand());
        Assert.False(query.IsNeverLazy());
        Assert.False(query.IsInPredicateResult());
        Assert.True(query.IsReadOnly());
        Assert.Null(query.GetForUpdate());
        Assert.Null(query.GetSortOrder());
        query.DisableCache();
    }

    [Fact]
    public void AViewKeepsItsOrderByText()
    {
        Execute("CREATE VIEW V AS SELECT A FROM T ORDER BY A");
        string sql = QuerySingle(
            "SELECT VIEW_DEFINITION FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_NAME = 'V'");
        Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void AViewKeepsItsFetchText()
    {
        Execute("CREATE VIEW VF AS SELECT A FROM T ORDER BY A FETCH FIRST 10 PERCENT ROWS WITH TIES");
        string sql = QuerySingle(
            "SELECT VIEW_DEFINITION FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_NAME = 'VF'");
        Assert.Contains("PERCENT", sql, StringComparison.Ordinal);
        Assert.Contains("WITH TIES", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void ANegativeOffsetIsRefused()
    {
        CommandContainer container = Assert.IsType<CommandContainer>(
            session.PrepareCommand("SELECT * FROM T OFFSET -1"));
        H2SharpException e = Assert.Throws<H2SharpException>(
            () => container.ExecuteQuery(10, 0, false));
        Assert.Contains("OFFSET", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANullOffsetIsRefused()
    {
        CommandContainer container = Assert.IsType<CommandContainer>(
            session.PrepareCommand("SELECT * FROM T OFFSET NULL"));
        H2SharpException e = Assert.Throws<H2SharpException>(
            () => container.ExecuteQuery(10, 0, false));
        Assert.Contains("OFFSET", e.Message, StringComparison.Ordinal);
    }
}
